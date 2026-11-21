using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using GameNet.Gameplay;
using GameNet.UI;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

[InitializeOnLoad]
public static class Level01DukeGameplayValidation
{
    static readonly List<string> results = new List<string>();
    static int stage;
    static float mark;
    static double started, wallMark;
    static int oldScene;
    static Level01DukeGameplay game;
    static VillaCharacterWalker player;
    static Level01DukeGameplayValidation()
    {
        if (!SessionState.GetBool("DukeGameplayValidation", false)) return;
        started = EditorApplication.timeSinceStartup;
        EditorApplication.update += Tick;
    }
    public static void Run()
    {
        results.Clear();
        stage = 0;
        started = EditorApplication.timeSinceStartup;
        EditorApplication.update -= Tick;
        EditorApplication.update += Tick;
        SessionState.SetBool("DukeGameplayValidation", true);
        EditorSceneManager.OpenScene("Assets/Scenes/第一关.unity");
        var view = EditorWindow.GetWindow(typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView"));
        view.Show(); view.Focus();
        EditorApplication.isPlaying = true;
    }
    static void Check(bool condition, string name)
    {
        results.Add((condition ? "PASS " : "FAIL ") + name);
        if (!condition) throw new Exception(name);
    }
    static void MovePlayer(Vector2 p)
    {
        player.transform.position = (Vector3)typeof(DukePatroller).GetMethod("ToGround", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(game.duke, new object[] { p });
    }
    static Button ButtonNamed(string name) => GameObject.Find("Btn_" + name).GetComponent<Button>();
    static bool TitleContains(string text)
    {
        foreach (var label in UnityEngine.Object.FindObjectsOfType<TMP_Text>())
            if (label.text.Contains(text)) return true;
        return false;
    }
    static void Tick()
    {
        if (!EditorApplication.isPlaying) return;
        try
        {
            if (EditorApplication.timeSinceStartup - started > 100) throw new Exception("Timed out at stage " + stage);
            if (stage == 0)
            {
                game = UnityEngine.Object.FindObjectOfType<Level01DukeGameplay>();
                if (game == null || !game.duke.IsReady || Time.time < .4f) return;
                player = UnityEngine.Object.FindObjectOfType<VillaCharacterWalker>();
                player.enabled = false;
                Check(game.anger != null && game.tasks != null && game.countdown.IsRunning, "Scene systems wired and timer running");
                Check(!game.TryKitchenPrank(), "Cannot prank from the exterior spawn");
                MovePlayer(new Vector2(.847f, .815f));
                Time.timeScale = 0;
                Check(!game.TryKitchenPrank() && !game.ReportPrankCompleted("paused"), "Paused game cannot interact or award anger");
                Time.timeScale = 1;
                Check(game.TryKitchenPrank(), "Kitchen proximity interaction succeeds");
                Check(Mathf.Abs(game.anger.CurrentAnger - 35f) < .01f, "Prank adds 35 anger");
                Check(!game.TryKitchenPrank() && Mathf.Abs(game.anger.CurrentAnger - 35f) < .01f, "Repeated interaction cannot farm anger");
                Check(Mathf.Abs(GameObject.Find("AngerFill").GetComponent<Image>().fillAmount - .35f) < .01f, "Existing anger HUD updates");
                game.tasks.CompleteTask("find_key");
                Check(Mathf.Abs(game.anger.CurrentAnger - 35f) < .01f, "Key task is not treated as a prank");
                MovePlayer(new Vector2(.30f, .815f));
                game.anger.SetAnger(50);
                game.duke.JumpToStop(2);
                mark = Time.time; stage = 1;
            }
            else if (stage == 1 && Time.time - mark >= 2f)
            {
                Check(Mathf.Abs(game.anger.CurrentAnger - (50f - (Time.time - mark))) < .2f, "Sleeping calms at 1 point per second");
                game.duke.JumpToStop(1); game.anger.SetAnger(50);
                mark = Time.time; stage = 2;
            }
            else if (stage == 2 && Time.time - mark >= 2f)
            {
                Check(Mathf.Abs(game.anger.CurrentAnger - (50f - .5f * (Time.time - mark))) < .2f, "Eating calms at 0.5 points per second");
                Vector2 p = game.duke.EncounterImageNorm;
                Check(game.CanCatchAt(p), "Same-room body contact is catchable");
                Vector2 radius = game.contactRadius;
                game.contactRadius = Vector2.one;
                Check(!game.CanCatchAt(new Vector2(.69f, .51f)), "Cross-floor contact rejected even with exaggerated radius");
                Check(!game.CanCatchAt(new Vector2(.595f, .79f)), "Other room rejected even with exaggerated radius");
                game.contactRadius = radius;
                var wallGo = new GameObject("VerificationWall");
                var wall = wallGo.AddComponent<VillaWall>();
                wall.corners = new[] { p + new Vector2(.005f, -.01f), p + new Vector2(.007f, -.01f), p + new Vector2(.007f, .01f), p + new Vector2(.005f, .01f) };
                var wallsField = typeof(Level01DukeGameplay).GetField("walls", BindingFlags.NonPublic | BindingFlags.Instance);
                var oldWalls = wallsField.GetValue(game);
                wallsField.SetValue(game, new[] {wall});
                Check(!game.CanCatchAt(p + new Vector2(.014f, 0)), "Wall blocks nearby contact");
                wallsField.SetValue(game, oldWalls); UnityEngine.Object.DestroyImmediate(wallGo);
                MovePlayer(p); player.enabled = true; stage = 3; wallMark = EditorApplication.timeSinceStartup;
            }
            else if (stage == 3)
            {
                if (!game.HasFailed)
                {
                    if (EditorApplication.timeSinceStartup - wallMark > 3) throw new Exception("Real player contact did not fail level");
                    return;
                }
                Check(Time.timeScale == 0 && !game.countdown.IsRunning && player.IsMovementSuspended, "Caught freezes movement and timer");
                Check(TitleContains("被 Duke 抓住"), "Caught result panel has correct reason");
                Check(!game.duke.room.showProjectedWireframe, "Debug wireframe does not cover result panel");
                Check(Resources.Load<AudioClip>("Audio/BGM/defeat_sting") != null, "Defeat audio asset available");
                Check(UnityEngine.Object.FindObjectsOfType<TimeUpPanelRuntime>().Length == 1, "Only one result panel controller exists");
                ScreenCapture.CaptureScreenshot("Library/duke-caught-result.png", 2);
                oldScene = SceneManager.GetActiveScene().handle;
                stage = 4; wallMark = EditorApplication.timeSinceStartup;
            }
            else if (stage == 4 && EditorApplication.timeSinceStartup - wallMark > 1)
            {
                ButtonNamed("重新挑战").onClick.Invoke(); stage = 5;
            }
            else if (stage == 5)
            {
                if (SceneManager.GetActiveScene().handle == oldScene) return;
                game = UnityEngine.Object.FindObjectOfType<Level01DukeGameplay>();
                if (game == null || !game.duke.IsReady || Time.time < .2f) return;
                Check(!game.HasFailed && !game.KitchenPrankCompleted && game.anger.CurrentAnger == 0 && Time.timeScale == 1, "Retry resets failure, prank, anger and pause");
                Check(game.countdown.IsRunning, "Retry restarts countdown");
                game.countdown.SetDuration(.15f); game.countdown.StartTimer(); stage = 6;
            }
            else if (stage == 6 && Time.timeScale == 0)
            {
                Check(!game.HasFailed && TitleContains("时间到"), "Timeout retains its own result reason");
                Check(!game.ReportPrankCompleted("late_prank"), "Pranks cannot award anger after timeout");
                Check(Application.CanStreamedLevelBeLoaded("关卡选择"), "Level select registered for return");
                ButtonNamed("返回选关").onClick.Invoke(); stage = 7;
            }
            else if (stage == 7 && SceneManager.GetActiveScene().name == "关卡选择")
            {
                Check(Time.timeScale == 1, "Return to level select restores time scale"); Finish(0);
            }
        }
        catch (Exception ex) { results.Add("FAIL " + ex); Finish(1); }
    }
    static void Finish(int code)
    {
        File.WriteAllLines("Library/duke-gameplay-validation.txt", results);
        SessionState.SetBool("DukeGameplayValidation", false);
        EditorApplication.Exit(code);
    }
}
