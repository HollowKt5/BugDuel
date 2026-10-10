using UnityEngine;

namespace BeetleArena
{
    // 运行时合成短促的甲壳/木面接触声，避免依赖外部音频文件。
    // 只消费战斗事件强度，不参与接触检测或胜负计算。
    public sealed class ArenaAudio : MonoBehaviour
    {
        // AudioSource 负责播放，contact 是本组件创建并在销毁时释放的音频资源。
        AudioSource source;
        AudioClip contact;
        // 首次初始化音源并生成 44100Hz 单声道、6615 个采样点（约 0.15 秒）的短音效。
        void Awake()
        {
            source = gameObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = .25f;
            source.volume = .22f;
            int count = 6615;
            float[] data = new float[count];
            // 固定噪声种子让每次生成的声音一致；与战斗随机源独立，不改变对局随机序列。
            var random = new System.Random(913);
            float filtered = 0;
            for (int i = 0; i < count; i++)
            {
                float t = i / 44100f;
                // 对随机噪声做一阶平滑，再混合 170Hz 正弦形成低频敲击感。
                // 指数衰减控制尾音，快速淡入减轻波形起点突变造成的爆音。
                filtered = Mathf.Lerp(filtered, (float)random.NextDouble() * 2 - 1, .35f);
                data[i] = (filtered * .55f + Mathf.Sin(t * 2 * Mathf.PI * 170) * .35f) * Mathf.Exp(-t * 33) * Mathf.Min(1, t * 800);
            }
            contact = AudioClip.Create("Dry chitin and wood contact", count, 1, 44100, false);
            contact.SetData(data, 0);
        }
        // 在事件选手附近播放；强度越高声音越大、音调越低。
        // 输入强度由事件约定在 0～1，当前方法不另做夹紧。
        public void Contact(int actor, float strength, BeetleActor[] actors)
        {
            if (!source || !contact) return;
            if (actor >= 0 && actor < actors.Length) transform.position = actors[actor].transform.position;
            source.pitch = Mathf.Lerp(1.15f, .75f, strength);
            source.PlayOneShot(contact, strength);
        }
        // 释放动态创建的 AudioClip，防止反复加载场景积累音频对象。
        void OnDestroy() { if (contact) Destroy(contact); }
    }
}
