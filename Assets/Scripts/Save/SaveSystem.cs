using System;
using System.Collections.Generic;
using System.IO;
using GameNet;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 存档系统：严格 Host-only。
/// - Host：可以 SaveGame / LoadOneSave / LoadAllSaveFiles；
/// - Client：任何读写都会被硬性拦截并记录日志；
/// - 单机（未联机）时按 Host 权限处理，方便本地开发。
/// 存档文件：Application.persistentDataPath/saves/*.json（JsonUtility 序列化）。
/// </summary>
public static class SaveSystem
{
    private const string SaveFolderName = "saves";
    private const string FileExtension = ".json";

    private static string SaveDirectory => Path.Combine(Application.persistentDataPath, SaveFolderName);

    public static GameSaveData CurrentSession { get; private set; }

    public static void SetCurrentSession(GameSaveData data)
    {
        if (!HasSavePermission("SetCurrentSession"))
        {
            return;
        }

        CurrentSession = data;
    }

    /// <summary>
    /// 硬性权限保护：只有 Host（或未联机时的本地开发模式）可以读写存档。
    /// Client 调用会直接拦截并留下明确日志。
    /// </summary>
    private static bool HasSavePermission(string operation)
    {
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening)
        {
            // 未联机：本地开发模式，允许（等价 Host）。
            return true;
        }

        if (!NetworkManager.Singleton.IsHost)
        {
            Debug.LogWarning($"[SaveSystem] 拦截：Client(OwnerId) 试图执行存档操作 {operation}，" +
                             "Client 无权读写 Host 存档，已拒绝。");
            return false;
        }

        return true;
    }

    /// <summary>保存一份存档，返回是否成功。</summary>
    public static bool SaveGame(string slotName, GameSaveData data)
    {
        if (!HasSavePermission($"SaveGame({slotName})"))
        {
            return false;
        }

        try
        {
            Directory.CreateDirectory(SaveDirectory);
            data.savedAtUtc = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
            string path = GetSavePath(slotName);
            File.WriteAllText(path, data.ToJson());
            Debug.Log($"[SaveSystem] Host 已保存存档：{path}");
            return true;
        }
        catch (Exception e)
        {
            Debug.LogError($"[SaveSystem] 保存存档失败：{e}");
            return false;
        }
    }

    /// <summary>读取指定存档；不存在返回 null。</summary>
    public static GameSaveData LoadOneSave(string slotName)
    {
        if (!HasSavePermission($"LoadOneSave({slotName})"))
        {
            return null;
        }

        string path = GetSavePath(slotName);
        if (!File.Exists(path))
        {
            Debug.LogWarning($"[SaveSystem] 存档不存在：{path}");
            return null;
        }

        try
        {
            var data = GameSaveData.FromJson(File.ReadAllText(path));
            Debug.Log($"[SaveSystem] Host 已读取存档：{path}（chapterIndex={data.chapterIndex}，道具数={data.trickItems.Count}）");
            return data;
        }
        catch (Exception e)
        {
            Debug.LogError($"[SaveSystem] 读取存档失败：{e}");
            return null;
        }
    }

    /// <summary>列出所有存档（文件名不含扩展名）。</summary>
    public static List<string> LoadAllSaveFiles()
    {
        var result = new List<string>();

        if (!HasSavePermission("LoadAllSaveFiles"))
        {
            return result;
        }

        if (!Directory.Exists(SaveDirectory))
        {
            return result;
        }

        foreach (string file in Directory.GetFiles(SaveDirectory, "*" + FileExtension))
        {
            result.Add(Path.GetFileNameWithoutExtension(file));
        }

        result.Sort(StringComparer.OrdinalIgnoreCase);
        string joined = string.Join(", ", result);
        Debug.Log($"[SaveSystem] Host 存档列表：[{joined}]");
        return result;
    }

    /// <summary>删除存档（Host only）。</summary>
    public static bool DeleteSave(string slotName)
    {
        if (!HasSavePermission($"DeleteSave({slotName})"))
        {
            return false;
        }

        string path = GetSavePath(slotName);
        if (!File.Exists(path))
        {
            return false;
        }

        File.Delete(path);
        Debug.Log($"[SaveSystem] Host 已删除存档：{path}");
        return true;
    }

    private static string GetSavePath(string slotName)
    {
        string safeName = string.Join("_", (slotName ?? "save").Split(Path.GetInvalidFileNameChars()));
        return Path.Combine(SaveDirectory, safeName + FileExtension);
    }
}
