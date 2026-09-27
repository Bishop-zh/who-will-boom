using BombTavern.Core;
using UnityEngine;

namespace BombTavern.Runtime
{
    /// <summary>
    /// 音频总管：订阅对局事件播对应音效，并集中管理各路音量。
    ///
    /// 两条重要规则：
    ///   1) **保密事件不出声**。如果对手偷偷用了道具却传来音效，那音效本身就成了情报，
    ///      和「使用道具看不到」的设定直接冲突。所以 secret 且不是自己座位的事件一律静音。
    ///   2) 音源用固定小池子轮转，而不是每次都新建 AudioSource ——
    ///      短时间内连续翻格时不会因为对象创建产生卡顿。
    /// </summary>
    [DisallowMultipleComponent]
    public class AudioDirector : MonoBehaviour
    {
        public static AudioDirector Instance { get; private set; }

        [Header("绑定")]
        [Tooltip("对局驱动。留空则自动查找。")]
        public GameDirector director;

        [Header("音量")]
        [Range(0f, 1f)] public float masterVolume = 0.85f;
        [Range(0f, 1f)] public float sfxVolume = 1.0f;
        [Range(0f, 1f)] public float musicVolume = 0.55f;
        [Range(0f, 1f)] public float voiceVolume = 1.0f;

        [Header("混音")]
        [Tooltip("同时可播放的音源数量。翻格很快时靠它叠出层次感。")]
        [Range(2, 16)] public int voices = 8;

        [Tooltip("同一个音效在这个时间内重复触发会被忽略（秒），避免连续翻格时糊成一片。")]
        [Range(0f, 0.2f)] public float minRepeatInterval = 0.035f;

        AudioSource[] _pool;
        AudioSource _musicSource;
        AudioClip _musicClip;
        bool _musicLoadWarned;
        int _next;
        readonly float[] _lastPlayed = new float[System.Enum.GetValues(typeof(Sfx)).Length];

        int _lastTurnSeat = int.MinValue;
        int _lastRoundSeen = -1;
        int _safeStreak;
        bool _reported;

        /// <summary>实际生效的音量（供设置界面显示）。</summary>
        public float EffectiveSfx => Mathf.Clamp01(masterVolume) * Mathf.Clamp01(sfxVolume);

        void Awake()
        {
            Instance = this;
            if (director == null) director = FindObjectOfType<GameDirector>();

            _pool = new AudioSource[Mathf.Max(2, voices)];
            for (int i = 0; i < _pool.Length; i++)
            {
                var go = new GameObject($"SfxVoice_{i}");
                go.transform.SetParent(transform, false);

                var src = go.AddComponent<AudioSource>();
                src.playOnAwake = false;
                src.spatialBlend = 0f;          // 2D：HUD 反馈不需要方位感
                src.bypassEffects = true;
                src.bypassListenerEffects = true;
                _pool[i] = src;
            }

            for (int i = 0; i < _lastPlayed.Length; i++) _lastPlayed[i] = -99f;
            BuildAmbientMusic();
        }

        void BuildAmbientMusic()
        {
            musicVolume = PlayerPrefs.GetFloat("BombTavern.MusicVolume", 0.65f);
            sfxVolume = PlayerPrefs.GetFloat("BombTavern.SfxVolume", 1f);
            _musicClip = Resources.Load<AudioClip>("Audio/TavernWaltzOriginal");
            if (_musicClip == null)
            {
                Debug.LogError("[音乐] 酒馆背景音乐资源缺失：Audio/TavernWaltzOriginal。");
                return;
            }
            // 资源没有预载时，Awake 中直接 Play 可能在 PCM 载入前执行而没有开始播放。
            _musicClip.LoadAudioData();
            _musicSource = gameObject.AddComponent<AudioSource>();
            _musicSource.playOnAwake = false;
            _musicSource.loop = true;
            _musicSource.priority = 64;
            _musicSource.spatialBlend = 0f;
            _musicSource.clip = _musicClip;
            _musicSource.volume = Mathf.Clamp01(masterVolume * musicVolume);
            EnsureMusicPlayback();
        }

        void EnsureMusicPlayback()
        {
            if (_musicSource == null || _musicClip == null || _musicSource.isPlaying) return;
            if (_musicClip.loadState == AudioDataLoadState.Unloaded) _musicClip.LoadAudioData();
            if (_musicClip.loadState == AudioDataLoadState.Loaded)
            {
                _musicSource.Play();
                return;
            }
            if (_musicClip.loadState == AudioDataLoadState.Failed && !_musicLoadWarned)
            {
                _musicLoadWarned = true;
                Debug.LogError("[音乐] 背景音乐加载失败，请检查音频资源导入。 ");
            }
        }

        void OnDestroy()
        {
            if (director != null)
            {
                director.Event -= OnGameEvent;
                director.SessionReady -= OnSessionReady;
            }
            if (Instance == this) Instance = null;
        }

        void Start()
        {
            if (director == null) return;
            director.Event += OnGameEvent;
            // 新一局必须清掉跨局残留：去重计时、「已播报胜负」标记等
            director.SessionReady += OnSessionReady;
        }

        void OnSessionReady(GameSession _)
        {
            ResetForNewSession();
            EnsureMusicPlayback();
        }

        // ============================================================ 播放
        /// <summary>播一个音效。volumeScale 用来做轻重区分（例如远处的动作小一点）。</summary>
        public void Play(Sfx id, float volumeScale = 1f, float pitch = 1f)
        {
            if (_pool == null) return;

            float vol = EffectiveSfx * Mathf.Clamp01(volumeScale);
            if (vol <= 0.001f) return;

            float now = Time.unscaledTime;
            int key = (int)id;
            if (key >= 0 && key < _lastPlayed.Length)
            {
                if (now - _lastPlayed[key] < minRepeatInterval) return;
                _lastPlayed[key] = now;
            }

            var src = _pool[_next];
            _next = (_next + 1) % _pool.Length;

            src.pitch = Mathf.Clamp(pitch, 0.3f, 2.5f);
            src.PlayOneShot(SfxLibrary.Get(id), vol);
        }

        /// <summary>界面按钮音。UI 代码统一走这个，方便以后换音源。</summary>
        public void PlayUi() => Play(Sfx.Button, 0.7f);

        // ============================================================ 事件
        void OnGameEvent(GameEvent e)
        {
            // 保密事件不出声：音效会泄露「对手用了道具」
            bool isMine = director != null && e.seatIndex == director.localSeat;
            if (e.secret && !isMine) return;

            switch (e.type)
            {
                case GameEventType.CellRevealed:
                    Play(Sfx.RevealSafe, 0.9f);
                    _safeStreak++;
                    // 连续安全给一次上扬的音，形成「手感」上的正反馈
                    if (_safeStreak >= 3) Play(Sfx.Combo, 0.7f, 1f + Mathf.Min(_safeStreak, 8) * 0.05f);
                    break;

                case GameEventType.CellHit:
                    Play(Sfx.Hit, 1f);
                    _safeStreak = 0;
                    break;

                case GameEventType.CellFlagged:
                    Play(Sfx.Flag, 0.8f);
                    break;

                case GameEventType.CellProbed:
                case GameEventType.RowScanned:
                case GameEventType.AreaScanned:
                case GameEventType.CellRiskModified:
                case GameEventType.AreaRiskModified:
                    Play(Sfx.ItemUse, 0.75f);
                    break;

                case GameEventType.ItemUsed:
                    Play(Sfx.ItemUse, 1f);
                    break;

                case GameEventType.PlayerEliminated:
                    Play(Sfx.Eliminated, 1f);
                    break;

                case GameEventType.GameOver:
                    if (_reported) break;
                    _reported = true;
                    {
                        var s = director != null ? director.Session : null;
                        bool won = s != null && s.WinnerSeat == (director != null ? director.localSeat : -1);
                        Play(won ? Sfx.Victory : Sfx.Defeat, 1f);
                    }
                    break;
            }
        }

        /// <summary>
        /// 回合与行动权的变化没有对应事件（它们是状态而非事件），所以在 Update 里比较。
        /// 用 Session 的当前值而不是事件，才能保证「切到谁」一定会被察觉。
        /// </summary>
        void Update()
        {
            if (_musicSource != null)
            {
                _musicSource.volume = Mathf.Clamp01(masterVolume * musicVolume);
                EnsureMusicPlayback();
            }
            var s = director != null ? director.Session : null;
            if (s == null || director.IsPregame) return;

            if (_lastRoundSeen >= 0 && s.RoundIndex > _lastRoundSeen)
                Play(Sfx.RoundAdvance, 0.8f);
            _lastRoundSeen = s.RoundIndex;

            if (s.CurrentSeat != _lastTurnSeat)
            {
                _lastTurnSeat = s.CurrentSeat;
                if (!s.IsOver && s.CurrentSeat == director.localSeat)
                    Play(Sfx.YourTurn, 0.95f);
            }

            if (s.IsOver) _safeStreak = 0;
        }

        /// <summary>新一局开始：清掉跨局残留的状态与「已播报」标记。</summary>
        public void ResetForNewSession()
        {
            _reported = false;
            _safeStreak = 0;
            _lastRoundSeen = -1;
            _lastTurnSeat = int.MinValue;
            for (int i = 0; i < _lastPlayed.Length; i++) _lastPlayed[i] = -99f;
        }
    }
}
