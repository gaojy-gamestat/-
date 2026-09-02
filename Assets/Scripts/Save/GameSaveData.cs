using System;
using System.Collections.Generic;
using UnityEngine;

namespace GameNet
{
    /// <summary>
    /// 单个道具/机关状态记录。
    /// 注意：JsonUtility 不支持 Dictionary，因此存档结构必须使用 [Serializable] 列表，
    /// 不能出现 Dictionary&lt;string, bool&gt; 之类的字段。
    /// </summary>
    [Serializable]
    public class TrickItemRecord
    {
        public string itemId;
        public bool isUsed;
        public bool isPlaced;
    }

    /// <summary>房主（NPC）状态记录。</summary>
    [Serializable]
    public class NeighborStateRecord
    {
        public string stateId;   // 例如 "susicionLevel"
        public float value;
    }

    /// <summary>玩家记录。</summary>
    [Serializable]
    public class PlayerRecord
    {
        public string characterId;  // jitMin / laoShiRen
        public float posX;
        public float posY;
        public float posZ;
        public float rotY;
    }

    /// <summary>
    /// 一份完整存档。全部字段均可被 JsonUtility 稳定序列化。
    /// </summary>
    [Serializable]
    public class GameSaveData
    {
        public string saveName = "新存档";
        public string savedAtUtc = string.Empty;
        public string sceneName = "GamePlay";
        public int chapterIndex;

        public List<TrickItemRecord> trickItems = new List<TrickItemRecord>();
        public List<NeighborStateRecord> neighborStates = new List<NeighborStateRecord>();
        public List<PlayerRecord> players = new List<PlayerRecord>();

        public string ToJson()
        {
            return JsonUtility.ToJson(this, true);
        }

        public static GameSaveData FromJson(string json)
        {
            return JsonUtility.FromJson<GameSaveData>(json);
        }
    }
}
