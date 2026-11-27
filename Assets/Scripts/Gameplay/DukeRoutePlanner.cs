using System.Collections.Generic;
using UnityEngine;

namespace GameNet.Gameplay
{
    // 图片坐标上的八方向搜索；每条边都复用关卡碰撞判定，不能穿家具或离开地板。
    public static class DukeRoutePlanner
    {
        public static List<Vector2> Find(VillaRoomBuilder room, Vector2 start, Vector2 goal, float radius)
        {
            const int side = 126;
            const float step = 1f / (side - 1);
            var parents = new Dictionary<int, int>();
            var points = new Dictionary<int, Vector2>();
            var queue = new Queue<int>();
            int first = Mathf.RoundToInt(start.y / step) * side + Mathf.RoundToInt(start.x / step);
            parents[first] = first; points[first] = start; queue.Enqueue(first);
            int end = -1;
            while (queue.Count > 0)
            {
                int id = queue.Dequeue();
                Vector2 p = points[id];
                if (Vector2.Distance(p, goal) < step * 2 && Clear(room, p, goal, radius)) { end = id; break; }
                int x = id % side, y = id / side;
                for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int nx = x + dx, ny = y + dy, next = ny * side + nx;
                    if ((dx == 0 && dy == 0) || nx < 0 || nx >= side || ny < 0 || ny >= side || parents.ContainsKey(next)) continue;
                    Vector2 n = new Vector2(nx * step, ny * step);
                    if (!Clear(room, p, n, radius)) continue;
                    parents[next] = id; points[next] = n; queue.Enqueue(next);
                }
            }
            if (end < 0) return null;
            var path = new List<Vector2>();
            while (end != first) { path.Add(points[end]); end = parents[end]; }
            path.Reverse();
            // 合并可直达的路段，避免沿网格锯齿行走。
            Vector2 cursor = start;
            var result = new List<Vector2>();
            for (int i = 0; i < path.Count;)
            {
                int far = i;
                while (far + 1 < path.Count && Clear(room, cursor, path[far + 1], radius)) far++;
                result.Add(path[far]); cursor = path[far]; i = far + 1;
            }
            return result;
        }

        public static bool Clear(VillaRoomBuilder room, Vector2 a, Vector2 b, float radius)
        {
            if (room.IsBlockedPath(room.ImageNormToGround(a), room.ImageNormToGround(b), radius)) return false;
            int samples = Mathf.Max(1, Mathf.CeilToInt(Vector2.Distance(a, b) / .004f));
            for (int i = 1; i <= samples; i++)
            {
                Vector2 p = Vector2.Lerp(a, b, (float)i / samples);
                bool onFloor = false;
                foreach (var floor in room.Floors)
                    if (floor != null && floor.isActiveAndEnabled && floor.ContainsImageNorm(p)) { onFloor = true; break; }
                if (!onFloor) return false;
            }
            return true;
        }
    }
}
