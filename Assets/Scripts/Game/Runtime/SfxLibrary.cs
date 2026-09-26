using System.Collections.Generic;
using UnityEngine;

namespace BombTavern.Runtime
{
    /// <summary>游戏里用到的音效。</summary>
    public enum Sfx
    {
        Hover,          // 悬停到格子
        RevealSafe,     // 翻开安全格
        RevealDanger,   // 翻开危险格（没炸）
        Hit,            // 踩雷
        Flag,           // 插旗
        Button,         // 按钮
        ItemUse,        // 使用道具
        ItemGain,       // 获得道具
        ShieldBlock,    // 护盾挡下
        Combo,          // 连续安全
        YourTurn,       // 轮到你
        RoundAdvance,   // 回合推进
        Eliminated,     // 出局
        Victory,        // 胜利
        Defeat,         // 失败
    }

    /// <summary>
    /// 程序化音效库。
    ///
    /// 为什么不放音频文件：项目里目前**一个音频资产都没有**，而二进制资源既不好评审、
    /// 也不好进版本库。这里用正弦/噪声 + 包络在运行时合成，
    /// 和项目已有的「程序化网格 / 程序化特效」保持一致的做法：
    /// **资源缺失也不会变成「点了没反应」**。
    ///
    /// 每个音都短（几十到几百毫秒），只在首次使用时合成一次并缓存。
    /// 正式版应换成真正的录音/采样资源，那时只要替换 <see cref="Get"/> 的实现即可。
    /// </summary>
    public static class SfxLibrary
    {
        const int SampleRate = 44100;

        static readonly Dictionary<Sfx, AudioClip> Cache = new Dictionary<Sfx, AudioClip>();

        public static AudioClip Get(Sfx id)
        {
            if (Cache.TryGetValue(id, out var cached) && cached != null) return cached;

            var clip = Build(id);
            Cache[id] = clip;
            return clip;
        }

        /// <summary>换音频资源时用：丢掉缓存，下次重新合成。</summary>
        public static void ClearCache() => Cache.Clear();

        // ============================================================ 合成
        static AudioClip Build(Sfx id)
        {
            float[] buf;
            string name = "sfx_" + id;

            switch (id)
            {
                case Sfx.Hover:
                    buf = C(0.035f);
                    Tone(buf, 0f, 0.035f, 2100f, 2100f, 0.10f, 0.002f, 6f);
                    break;

                case Sfx.RevealSafe:
                    buf = C(0.13f);
                    Tone(buf, 0f, 0.13f, 620f, 1150f, 0.22f, 0.004f, 7f);
                    Tone(buf, 0f, 0.13f, 930f, 1725f, 0.10f, 0.004f, 7f);
                    break;

                case Sfx.RevealDanger:
                    buf = C(0.16f);
                    Tone(buf, 0f, 0.16f, 300f, 170f, 0.26f, 0.003f, 6f);
                    break;

                case Sfx.Hit:
                    buf = C(0.75f);
                    Noise(buf, 0f, 0.42f, 0.55f, 0.30f);
                    Tone(buf, 0f, 0.55f, 150f, 42f, 0.45f, 0.002f, 4f);
                    break;

                case Sfx.Flag:
                    buf = C(0.06f);
                    Tone(buf, 0f, 0.06f, 1500f, 900f, 0.18f, 0.001f, 12f);
                    break;

                case Sfx.Button:
                    buf = C(0.07f);
                    Tone(buf, 0f, 0.07f, 880f, 1180f, 0.16f, 0.002f, 10f);
                    break;

                case Sfx.ItemUse:
                    buf = C(0.42f);
                    Tone(buf, 0f, 0.40f, 520f, 1560f, 0.20f, 0.006f, 5f);
                    Tone(buf, 0.05f, 0.34f, 780f, 2080f, 0.12f, 0.006f, 5f);
                    break;

                case Sfx.ItemGain:
                    buf = C(0.45f);
                    Tone(buf, 0f, 0.16f, 784f, 784f, 0.16f, 0.004f, 6f);     // G
                    Tone(buf, 0.11f, 0.16f, 988f, 988f, 0.16f, 0.004f, 6f);   // B
                    Tone(buf, 0.22f, 0.22f, 1319f, 1319f, 0.17f, 0.004f, 5f); // E
                    break;

                case Sfx.ShieldBlock:
                    buf = C(0.35f);
                    Tone(buf, 0f, 0.35f, 1760f, 1320f, 0.20f, 0.001f, 9f);
                    Tone(buf, 0f, 0.30f, 2640f, 1980f, 0.09f, 0.001f, 11f);
                    break;

                case Sfx.Combo:
                    buf = C(0.30f);
                    Tone(buf, 0f, 0.28f, 1046f, 1568f, 0.18f, 0.004f, 6f);
                    break;

                case Sfx.YourTurn:
                    buf = C(0.40f);
                    Tone(buf, 0f, 0.18f, 660f, 660f, 0.20f, 0.005f, 5f);
                    Tone(buf, 0.13f, 0.26f, 990f, 990f, 0.20f, 0.005f, 4f);
                    break;

                case Sfx.RoundAdvance:
                    buf = C(0.50f);
                    Tone(buf, 0f, 0.48f, 320f, 200f, 0.22f, 0.01f, 3.5f);
                    Tone(buf, 0f, 0.30f, 160f, 120f, 0.18f, 0.01f, 3f);
                    break;

                case Sfx.Eliminated:
                    buf = C(0.95f);
                    Tone(buf, 0f, 0.30f, 440f, 415f, 0.20f, 0.006f, 4f);
                    Tone(buf, 0.24f, 0.34f, 349f, 330f, 0.20f, 0.006f, 4f);
                    Tone(buf, 0.50f, 0.44f, 233f, 175f, 0.22f, 0.006f, 3f);
                    break;

                case Sfx.Victory:
                    buf = C(1.10f);
                    Tone(buf, 0f, 0.20f, 523f, 523f, 0.18f, 0.004f, 5f);      // C
                    Tone(buf, 0.15f, 0.20f, 659f, 659f, 0.18f, 0.004f, 5f);   // E
                    Tone(buf, 0.30f, 0.22f, 784f, 784f, 0.18f, 0.004f, 5f);   // G
                    Tone(buf, 0.47f, 0.60f, 1046f, 1046f, 0.20f, 0.005f, 3f); // C
                    break;

                default: // Defeat
                    buf = C(1.00f);
                    Tone(buf, 0f, 0.28f, 392f, 370f, 0.20f, 0.006f, 4f);      // G
                    Tone(buf, 0.22f, 0.30f, 330f, 311f, 0.20f, 0.006f, 4f);   // E
                    Tone(buf, 0.48f, 0.50f, 262f, 196f, 0.22f, 0.006f, 3f);   // C
                    break;
            }

            SoftClip(buf);
            var clip = AudioClip.Create(name, buf.Length, 1, SampleRate, false);
            clip.SetData(buf, 0);
            return clip;
        }

        static float[] C(float seconds) => new float[Mathf.Max(1, (int)(seconds * SampleRate))];

        /// <summary>加一个带包络的音。f0→f1 做频率滑动，attack/decay 控制起落。</summary>
        static void Tone(float[] buf, float startSec, float durSec,
            float f0, float f1, float gain, float attackSec, float decayRate)
        {
            int start = (int)(startSec * SampleRate);
            int len = (int)(durSec * SampleRate);
            int attack = Mathf.Max(1, (int)(attackSec * SampleRate));

            float phase = 0f;
            for (int i = 0; i < len; i++)
            {
                int idx = start + i;
                if (idx < 0) continue;
                if (idx >= buf.Length) break;

                float t = i / (float)len;
                float freq = Mathf.Lerp(f0, f1, t);
                phase += 2f * Mathf.PI * freq / SampleRate;

                // 包络：极短 attack 防止爆音，之后指数衰减
                float env = (i < attack ? i / (float)attack : 1f) * Mathf.Exp(-decayRate * t);

                buf[idx] += Mathf.Sin(phase) * env * gain;
            }
        }

        /// <summary>加一段噪声（爆炸/撞击）。lowpass ∈ (0,1]，越小越闷。</summary>
        static void Noise(float[] buf, float startSec, float durSec, float gain, float lowpass)
        {
            int start = (int)(startSec * SampleRate);
            int len = (int)(durSec * SampleRate);

            // 固定种子：音效每次听起来要一样，不能每局都变
            var rng = new System.Random(12345);
            float prev = 0f;

            for (int i = 0; i < len; i++)
            {
                int idx = start + i;
                if (idx < 0) continue;
                if (idx >= buf.Length) break;

                float white = (float)(rng.NextDouble() * 2.0 - 1.0);
                prev = Mathf.Lerp(prev, white, lowpass);   // 一阶低通

                float t = i / (float)len;
                buf[idx] += prev * Mathf.Exp(-3.2f * t) * gain;
            }
        }

        /// <summary>软限幅，避免叠加后削顶产生刺耳的爆音。</summary>
        static void SoftClip(float[] buf)
        {
            for (int i = 0; i < buf.Length; i++)
                buf[i] = (float)System.Math.Tanh(buf[i] * 1.1f) * 0.92f;
        }
    }
}
