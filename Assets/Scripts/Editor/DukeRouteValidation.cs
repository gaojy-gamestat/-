using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using GameNet.Gameplay;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
[InitializeOnLoad]
public static class DukeRouteValidation
{
 static List<string> lines = new List<string>(); static int arrivals, errors, portals; static Vector2 last; static bool sampled, checkedPlanner; static float lastTime; static double start;
 static BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
 static DukeRouteValidation() { if(SessionState.GetBool("DukeRouteTest",false)) { start=EditorApplication.timeSinceStartup; EditorApplication.update+=Tick; Application.logMessageReceived+=Log; } }
 public static void Run() { EditorSceneManager.OpenScene("Assets/Scenes/第一关.unity"); SessionState.SetBool("DukeRouteTest",true); EditorWindow.GetWindow(typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView")).Show(); EditorApplication.isPlaying=true; }
 static void Log(string m,string t,LogType k) { if(m.Contains("到达「")) { arrivals++;lines.Add(Time.time+" "+m); } if(k==LogType.Exception || k==LogType.Assert) { errors++;lines.Add(m); } if(m.Contains("暂无安全通路")) {errors++;lines.Add(m);} }
 static void Check(bool ok,string msg) {lines.Add((ok?"PASS ":"FAIL ")+msg);if(!ok)errors++;}
 static void Tick() {
 if(!EditorApplication.isPlaying)return;
 var d=UnityEngine.Object.FindObjectOfType<DukePatroller>();
 if(d!=null && d.IsReady && Time.time>3) {
 if(!checkedPlanner) { checkedPlanner=true; TestPlanner(d); }
 float phase=(float)typeof(DukePatroller).GetField("_doorProgress",flags).GetValue(d);
 var rt=(RectTransform)typeof(DukePatroller).GetField("_mirrorRt",flags).GetValue(d);
 if(sampled && !d.IsActing && Vector2.Distance(last,d.ImageNorm)>.02f) {
 portals++; Check(phase>=.25f && phase<.4f,"Position transfer concealed by doorway transition");
 if(rt!=null) Check(rt.GetComponent<Image>().color.a<.35f,"Transfer opacity below 0.35");
 ScreenCapture.CaptureScreenshot("Library/duke-route-door-"+portals+".png");
 }
 last=d.ImageNorm;lastTime=Time.time;sampled=true;
 }
 if(arrivals>=7 || errors>0 || EditorApplication.timeSinceStartup-start>240) {
 Check(arrivals>=7,"Two normal-speed patrol loops"); Check(portals>=12,"Twelve doorway transitions");
 File.WriteAllText("Library/duke-route-result.txt", "TimeScale="+Time.timeScale+"\n"+string.Join("\n",lines)+"\nErrors="+errors);
 SessionState.SetBool("DukeRouteTest",false);EditorApplication.Exit(errors==0?0:1);
 }
 }
 static void TestPlanner(DukePatroller d) {
 var room=d.room;
 // 在厨房地板的空旷前沿增加临时障碍，验证绕行及每一段碰撞约束。
 Vector2 a=new Vector2(.72f,.82f), b=new Vector2(.82f,.82f);
 var go=new GameObject("RouteTestObstacle");go.transform.SetParent(room.transform);var o=go.AddComponent<VillaObstacle>();
 o.corners=new[]{new Vector2(.763f,.808f),new Vector2(.777f,.808f),new Vector2(.777f,.832f),new Vector2(.763f,.832f)};room.RefreshZones();
 var path=DukeRoutePlanner.Find(room,a,b,d.clearanceNorm);
 Check(path!=null && path.Count>0,"Temporary kitchen obstacle has a detour");
 if(path!=null) { foreach(var p in path) {Check(DukeRoutePlanner.Clear(room,a,p,d.clearanceNorm),"Detour stays on floors and outside blockers");a=p;}Check(DukeRoutePlanner.Clear(room,a,b,d.clearanceNorm),"Detour reaches original target"); }
 go.SetActive(false);room.RefreshZones();UnityEngine.Object.Destroy(go);
 }
}
