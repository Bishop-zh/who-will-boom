using UnityEngine;

namespace BombTavern.Runtime
{
    /// <summary>
    /// 语音回放：把网络上收到的 16kHz PCM 喂进 AudioSource。
    ///
    /// 为什么需要重采样：
    ///   <c>OnAudioFilterRead</c> 回调里的 <c>data</c> 是**设备输出采样率**（通常 44100/48000），
    ///   而我们传的是 16kHz 的语音。直接把 16k 的样本按输出率吐出去，
    ///   声音会被加速约 2.75 倍（变成尖细的「花栗鼠音」）。
    ///   所以这里用线性插值把 16k 拉到输出率。
    ///
    /// 为什么用环形缓冲而不是 Queue：
    ///   OnAudioFilterRead 跑在**音频线程**上，而 Enqueue 来自**网络线程**，
    ///   逐样本进出 Queue 既慢又不好控制延迟；float[] 环形缓冲可以按整数指针整块消费。
    ///
    /// 抖动缓冲：攒够 prebufferMilliseconds 才开始出声，避免网络抖动造成断断续续。
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(AudioSource))]
    public class VoicePlayback : MonoBehaviour
    {
        [Tooltip("这个音源属于哪个座位。")]
        public int seatIndex = -1;

        [Tooltip("网络语音的采样率。必须与发送端一致，否则音调会不对。")]
        public int sourceSampleRate = 16000;

        [Tooltip("缓冲上限（毫秒）。超过就丢最旧的数据，防止延迟越积越大。")]
        [Range(100, 3000)] public int maxBufferMilliseconds = 400;

        [Tooltip("抖动缓冲目标深度（毫秒）。攒够这么多才开始出声，能明显减少断音。")]
        [Range(0, 300)] public int prebufferMilliseconds = 100;

        [Tooltip("输出音量。")]
        [Range(0f, 2f)] public float volume = 1f;

        int _lastPacketAt;
        float _fadeGain;
        float _lastOutput;
        float[] _ring;
        int _capacity;
        int _read;          // 读指针（环形下标）
        int _count;         // 可读样本数
        double _cursor;     // 当前读位置相对 _read 的小数偏移，用于重采样
        bool _primed;       // 是否已攒够预缓冲、进入出声状态

        AudioSource _source;
        int _outputSampleRate = 44100;

        public AudioSource Source => _source;

        /// <summary>当前缓冲里还有多少个源采样点（用于判断是否「正在说话」）。</summary>
        public int BufferedSamples { get { lock (this) return _count; } }

        /// <summary>是否已经攒够预缓冲、正在出声。</summary>
        public bool IsPrimed { get { lock (this) return _primed; } }

        void Awake()
        {
            EnsureCapacity();

            _source = GetComponent<AudioSource>();
            if (_source == null) return;

            _source.playOnAwake = false;
            _source.loop = true;
            _source.spatialBlend = 1f;      // 语音带方位感，听得出是谁在说
            _source.volume = 1f;

            // AudioSource 必须有一个正在播放的 clip，OnAudioFilterRead 才会被调用。
            // 这里只是「载波」，内容由滤波器回调实时写入。
            // AudioSettings 只能从主线程读取。缓存下来给 OnAudioFilterRead 使用，
            // 避免音频线程每个回调都抛 "GetSampleRate can only be called from the main thread"。
            _outputSampleRate = AudioSettings.outputSampleRate > 0 ? AudioSettings.outputSampleRate : 44100;
            int carrierRate = _outputSampleRate;
            var silent = AudioClip.Create("VoiceCarrier", carrierRate / 10, 1, carrierRate, false);
            silent.SetData(new float[carrierRate / 10], 0);

            _source.clip = silent;
            _source.Play();
        }

        void OnDestroy()
        {
            if (_source != null && _source.clip != null) Destroy(_source.clip);
        }

        void EnsureCapacity()
        {
            int rate = sourceSampleRate > 0 ? sourceSampleRate : 16000;
            int want = Mathf.Max(1, rate * Mathf.Max(100, maxBufferMilliseconds) / 1000);

            if (_ring != null && _capacity == want) return;

            _ring = new float[want];
            _capacity = want;
            _read = 0;
            _count = 0;
            _cursor = 0d;
            _primed = false;
        }

        /// <summary>把 16 位 PCM 样本放入播放缓冲（由网络线程调用）。</summary>
        public void Enqueue(short[] pcm)
        {
            if (pcm == null || pcm.Length == 0) return;

            lock (this)
            {
                EnsureCapacity();
                _lastPacketAt = System.Environment.TickCount;

                // 溢出就先丢最旧的，保证低延迟优先于完整性
                int overflow = _count + pcm.Length - _capacity;
                if (overflow > 0)
                {
                    int drop = Mathf.Min(overflow, _count);
                    _read = (_read + drop) % _capacity;
                    _count -= drop;
                }

                int skip = Mathf.Max(0, pcm.Length - _capacity);
                if (overflow > 0) _cursor = 0d;
                int write = (_read + _count) % _capacity;
                for (int i = skip; i < pcm.Length; i++)
                {
                    _ring[write] = pcm[i] * (1f / 32768f);
                    write++;
                    if (write >= _capacity) write = 0;
                }
                _count += pcm.Length - skip;
            }
        }

        public void Clear()
        {
            lock (this)
            {
                _read = 0;
                _count = 0;
                _cursor = 0d;
                _primed = false;
            }
        }

        /// <summary>取环形缓冲里第 i 个可读样本。</summary>
        float SampleAt(int i)
        {
            int idx = _read + i;
            if (idx >= _capacity) idx -= _capacity;
            return _ring[idx];
        }

        // ---- 注意：本方法运行在音频线程上
        void OnAudioFilterRead(float[] data, int channels)
        {
            lock (this)
            {
                if (_ring == null) { Fill(data, channels, 0f); return; }

                int outRate = _outputSampleRate > 0 ? _outputSampleRate : 44100;
                int srcRate = sourceSampleRate > 0 ? sourceSampleRate : 16000;

                // 每个输出帧要消耗多少源样本（16k → 44.1k 约 0.3628）
                double stride = (double)srcRate / outRate;

                int prebuffer = Mathf.Max(0, srcRate * prebufferMilliseconds / 1000);

                // 抖动缓冲：没攒够就先不出声；但已经在出声状态时不要因为一次欠载就重新预缓冲
                if (!_primed)
                {
                    if (_count == 0 || (_count < prebuffer && unchecked((uint)(System.Environment.TickCount - _lastPacketAt)) < 120u))
                    {
                        Fill(data, channels, 0f);
                        return;
                    }
                    _primed = true;
                    _fadeGain = 0f;
                }

                int frame = 0;
                for (; frame < data.Length; frame += channels)
                {
                    int i0 = (int)_cursor;
                    if (i0 >= _count)
                    {
                        // 欠载：这一帧剩余部分静音，并回到预缓冲状态
                        // Commit consumed samples even when a callback ends in underrun.
                        // Leaving the cursor pending here used to strand/replay the old buffer.
                        _read = (_read + _count) % _capacity;
                        _count = 0;
                        _cursor = 0d;
                        _primed = false;
                        for (int tail = frame; tail < data.Length; tail += channels)
                        {
                            _lastOutput *= .97f;
                            for (int c = 0; c < channels; c++) data[tail + c] = _lastOutput;
                        }
                        return;
                    }

                    float s;
                    if (i0 + 1 < _count)
                    {
                        float frac = (float)(_cursor - i0);
                        float a = SampleAt(i0);
                        float b = SampleAt(i0 + 1);
                        s = a + (b - a) * frac;
                    }
                    else
                    {
                        s = SampleAt(i0);
                    }

                    _fadeGain = Mathf.Min(1f, _fadeGain + 1f / (outRate * .005f));
                    s *= volume * _fadeGain;
                    _lastOutput = s;
                    for (int c = 0; c < channels; c++) data[frame + c] = s;

                    _cursor += stride;
                }

                // 整块处理完，统一推进读指针
                int consumed = (int)_cursor;
                if (consumed > 0)
                {
                    consumed = Mathf.Min(consumed, _count);
                    _read = (_read + consumed) % _capacity;
                    _count -= consumed;
                    _cursor -= consumed;
                }
            }
        }

        static void Fill(float[] data, int channels, float value) => FillFrom(data, channels, 0, value);

        static void FillFrom(float[] data, int channels, int from, float value)
        {
            for (int i = from; i < data.Length; i++) data[i] = value;
        }
    }
}
