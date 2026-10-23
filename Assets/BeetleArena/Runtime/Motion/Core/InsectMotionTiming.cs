using UnityEngine;
namespace BeetleArena
{
    /// <summary>纯函数节奏工具：固定步时钟采样，停顿与有界变化，无状态、无逐帧随机调用。</summary>
    public static class InsectMotionTiming
    {
        // 整数散列只提供表现差异，不消耗比赛 RNG；同一时间多次采样结果完全相同。
        public static float Value(int n, int salt)
        {
            unchecked { uint x = (uint)n * 747796405u + (uint)salt * 2891336453u;
                x = ((x >> (int)((x >> 28) + 4)) ^ x) * 277803737u;
                x = (x >> 22) ^ x; return (x & 0xffffff) / 16777215f; }
        }
        // 每个时间窗最多一段短动作，起点、时长与幅度不同；窗边界静止，避免换窗跳变。
        public static float Bout(float time, float window, int salt)
        {
            int n = Mathf.FloorToInt(time / window);
            float t = time - n * window;
            float start = window * Mathf.Lerp(.08f, .22f, Value(n, salt));
            float span = window * Mathf.Lerp(.30f, .53f, Value(n, salt + 1));
            float p = Mathf.Clamp01((t - start) / span);
            return Mathf.Pow(Mathf.Sin(Mathf.PI * p), 2) * Mathf.Lerp(.6f, 1, Value(n, salt + 2));
        }
    }
}
