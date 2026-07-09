using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace TrainSurvival.Game
{
    /// <summary>
    /// 依存ゼロの軽量オーディオサービス（自動生成のシングルトン）。
    /// ・SE は Assets/Resources/Audio/ に置いた指定名のファイルが自動で使われる（無ければ合成音で代用）
    /// 　　departure（発車メロディ）/ ding（停車チャイム）/ sit（着席）/ coffee（回復）/ gameover（倒れる）/ bgm
    /// ・シーン側から AudioClip を RegisterClip しておくと、Resources 外の素材も同じサービスから鳴らせる
    /// ・音量は BGM/SE 別に PlayerPrefs へ保存（タイトルのスライダーから触る想定）
    /// ・AudioSource は timeScale の影響を受けないので、スロー/停止中の演出でもそのまま鳴る
    /// </summary>
    public sealed class GameAudio : MonoBehaviour
    {
        public enum Sfx { Departure, Ding, Sit, Coffee, GameOver, Arrive, Bell, TrainDeparture, TrainStop, Horn, Heart }

        [System.Serializable]
        public struct CueTuning
        {
            [Min(0f)] public float startTime;
            [Min(0f)] public float duration;
            [Min(0f)] public float volumeScale;
            [Min(0f)] public float fadeOutSeconds;
        }

        private const string BgmVolumeKey = "BgmVolume";
        private const string SeVolumeKey = "SeVolume";

        private static GameAudio _instance;

        private AudioSource _bgmSource;
        private AudioSource _trainSource;
        private AudioSource _stationSource;
        private AudioSource _heartSource;
        private AudioSource[] _sePool;
        private int _seIndex;
        private float _bgmVolume;
        private float _seVolume;
        private float _trainTargetVolume;
        private float _trainPitch = 1f;
        private float _trainFadeSeconds = 0.45f;
        private float _heartIntensity;
        [SerializeField] private bool _debugLogging;
        private readonly Dictionary<Sfx, AudioClip> _clips = new Dictionary<Sfx, AudioClip>();
        private readonly Dictionary<Sfx, CueTuning> _cueTunings = new Dictionary<Sfx, CueTuning>();
        private readonly Dictionary<Sfx, float> _lastPlayedAt = new Dictionary<Sfx, float>();
        private readonly Dictionary<AudioSource, int> _sourceTokens = new Dictionary<AudioSource, int>();
        private readonly string[] _debugEvents = new string[8];
        private int _debugEventIndex;

        public static GameAudio Instance
        {
            get
            {
                if (_instance == null)
                {
                    var go = new GameObject("GameAudio");
                    DontDestroyOnLoad(go);
                    _instance = go.AddComponent<GameAudio>();
                    _instance.Setup();
                }
                return _instance;
            }
        }

        public static bool HasInstance => _instance != null;

        /// <summary>BGM 音量(0..1)。設定変更は即反映＆保存。</summary>
        public float BgmVolume
        {
            get => _bgmVolume;
            set
            {
                _bgmVolume = Mathf.Clamp01(value);
                ApplyBgmVolume();
                PlayerPrefs.SetFloat(BgmVolumeKey, _bgmVolume);
            }
        }

        /// <summary>SE 音量(0..1)。</summary>
        public float SeVolume
        {
            get => _seVolume;
            set
            {
                _seVolume = Mathf.Clamp01(value);
                ApplySeVolume();
                PlayerPrefs.SetFloat(SeVolumeKey, _seVolume);
            }
        }

        private void Setup()
        {
            _bgmVolume = PlayerPrefs.GetFloat(BgmVolumeKey, 0.7f);
            _seVolume = PlayerPrefs.GetFloat(SeVolumeKey, 0.9f);
            _debugLogging = PlayerPrefs.GetInt("AudioDebug", 0) != 0;

            _bgmSource = gameObject.AddComponent<AudioSource>();
            _bgmSource.loop = true;
            _bgmSource.playOnAwake = false;
            _bgmSource.volume = _bgmVolume;

            _trainSource = gameObject.AddComponent<AudioSource>();
            _trainSource.loop = true;
            _trainSource.playOnAwake = false;
            _trainSource.volume = 0f;

            _stationSource = gameObject.AddComponent<AudioSource>();
            _stationSource.loop = false;
            _stationSource.playOnAwake = false;
            _stationSource.volume = _seVolume;

            _heartSource = gameObject.AddComponent<AudioSource>();
            _heartSource.loop = true;
            _heartSource.playOnAwake = false;
            _heartSource.volume = 0f;

            _sePool = new AudioSource[6];
            for (int i = 0; i < _sePool.Length; i++)
            {
                _sePool[i] = gameObject.AddComponent<AudioSource>();
                _sePool[i].playOnAwake = false;
                _sePool[i].volume = _seVolume;
            }
        }

        /// <summary>SE 再生。Resources/Audio の実ファイル優先、無ければ合成音。</summary>
        public void Play(Sfx sfx, float pitch = 1f)
        {
            if (IsTransportCue(sfx))
            {
                PlayTransportCue(sfx, pitch);
                return;
            }

            AudioClip clip = GetClip(sfx);
            if (clip == null)
            {
                return;
            }

            float now = Time.unscaledTime;
            float interval = MinimumRepeatInterval(sfx);
            if (_lastPlayedAt.TryGetValue(sfx, out float last) && now - last < interval)
            {
                Log($"skip {sfx} ({now - last:0.00}s < {interval:0.00}s)");
                return;
            }
            _lastPlayedAt[sfx] = now;

            AudioSource src = _sePool[_seIndex];
            _seIndex = (_seIndex + 1) % _sePool.Length;
            CueTuning tuning = GetCueTuning(sfx);
            // 呼び出し側がピッチ指定なし（=1）のときだけ、軽い揺らぎで単調さを消す
            src.pitch = Mathf.Approximately(pitch, 1f) ? PitchJitter(sfx) : pitch;
            src.volume = _seVolume * Mathf.Max(0f, tuning.volumeScale);
            PlaySource(src, clip, tuning, false);
            Log($"play {sfx} clip={clip.name} pitch={pitch:0.00} vol={src.volume:0.00}");
        }

        /// <summary>シーンに直接置いた AudioClip を SE として登録する（Resources 外の素材用）。</summary>
        public void RegisterClip(Sfx sfx, AudioClip clip)
        {
            if (clip != null)
            {
                _clips[sfx] = clip;
                if (sfx == Sfx.Heart)
                {
                    _heartSource.clip = clip;
                }
                Log($"register {sfx} clip={clip.name} len={clip.length:0.00}s");
            }
        }

        /// <summary>SEごとの再生位置・再生時間・音量倍率。Play中に呼び直せる。</summary>
        public void ConfigureCue(Sfx sfx, CueTuning tuning)
        {
            tuning.startTime = Mathf.Max(0f, tuning.startTime);
            tuning.duration = Mathf.Max(0f, tuning.duration);
            tuning.volumeScale = Mathf.Max(0f, tuning.volumeScale <= 0f ? 1f : tuning.volumeScale);
            tuning.fadeOutSeconds = Mathf.Max(0f, tuning.fadeOutSeconds);
            _cueTunings[sfx] = tuning;
            if (sfx == Sfx.TrainDeparture && _trainSource != null && _trainSource.isPlaying && _trainTargetVolume > 0f)
            {
                _trainFadeSeconds = tuning.fadeOutSeconds > 0f ? tuning.fadeOutSeconds : 0.45f;
                _trainTargetVolume = _seVolume * Mathf.Max(0f, tuning.volumeScale);
            }
        }

        /// <summary>心音の強さ(0..1)。黄色ゲージから入り、倒れる直前へ向けてピークにする。</summary>
        public void SetHeartbeat(float intensity)
        {
            _heartIntensity = Mathf.Clamp01(intensity);
        }

        /// <summary>既存呼び出し用。true は最大強度として扱う。</summary>
        public void SetHeartbeat(bool active)
        {
            SetHeartbeat(active ? 1f : 0f);
        }

        /// <summary>電車の走行ループを状態として管理する。発車でON、停車入りでOFF。</summary>
        public void SetTrainMoving(bool moving, float pitch = 1f)
        {
            CueTuning tuning = GetCueTuning(Sfx.TrainDeparture);
            _trainPitch = pitch;
            _trainFadeSeconds = tuning.fadeOutSeconds > 0f ? tuning.fadeOutSeconds : 0.45f;
            _trainTargetVolume = moving ? _seVolume * Mathf.Max(0f, tuning.volumeScale) : 0f;

            if (!moving)
            {
                Log("train loop fade out");
                return;
            }

            AudioClip clip = GetClip(Sfx.TrainDeparture);
            if (clip == null || _trainSource == null)
            {
                return;
            }
            if (_trainSource.clip != clip)
            {
                _trainSource.clip = clip;
            }
            _trainSource.pitch = _trainPitch;
            if (!_trainSource.isPlaying)
            {
                _trainSource.volume = 0f;
                SetStartTime(_trainSource, clip, tuning.startTime);
                _trainSource.Play();
                Log($"train loop fade in clip={clip.name} pitch={pitch:0.00}");
            }
        }

        /// <summary>Play中にGameAudioを選んでInspectorからONにすると、SEの鳴り方をConsoleへ出す。</summary>
        public bool DebugLogging
        {
            get => _debugLogging;
            set
            {
                _debugLogging = value;
                PlayerPrefs.SetInt("AudioDebug", value ? 1 : 0);
            }
        }

        /// <summary>BGM 開始（Resources/Audio/bgm があれば）。既に同じ曲なら何もしない。</summary>
        public void PlayBgm()
        {
            var clip = Resources.Load<AudioClip>("Audio/bgm");
            if (clip == null || _bgmSource.clip == clip && _bgmSource.isPlaying)
            {
                return;
            }
            _bgmSource.clip = clip;
            _bgmSource.Play();
        }

        public void StopBgm() => _bgmSource.Stop();

        private void Update()
        {
            Keyboard kb = Keyboard.current;
            if (kb != null && kb.f9Key.wasPressedThisFrame)
            {
                DebugLogging = !DebugLogging;
                Log(_debugLogging ? "debug overlay on" : "debug overlay off");
            }
            UpdateTrainLoop();
            UpdateHeartbeat();
        }

        private void ApplyBgmVolume()
        {
            if (_bgmSource != null)
            {
                _bgmSource.volume = _bgmVolume;
            }
        }

        private void ApplySeVolume()
        {
            if (_sePool == null)
            {
                return;
            }

            for (int i = 0; i < _sePool.Length; i++)
            {
                if (_sePool[i] != null)
                {
                    _sePool[i].volume = _seVolume;
                }
            }
            if (_stationSource != null)
            {
                _stationSource.volume = _seVolume;
            }
            _trainTargetVolume = _trainSource != null && _trainSource.isPlaying && _trainTargetVolume > 0f
                ? _seVolume * Mathf.Max(0f, GetCueTuning(Sfx.TrainDeparture).volumeScale)
                : _trainTargetVolume;
        }

        private void UpdateTrainLoop()
        {
            if (_trainSource == null)
            {
                return;
            }

            _trainSource.pitch = _trainPitch;
            float fade = Mathf.Max(0.01f, _trainFadeSeconds);
            _trainSource.volume = Mathf.MoveTowards(_trainSource.volume, _trainTargetVolume, Time.unscaledDeltaTime / fade);
            if (_trainSource.isPlaying && _trainTargetVolume <= 0f && _trainSource.volume <= 0.001f)
            {
                _trainSource.Stop();
                Log("train loop stopped");
            }
        }

        private void UpdateHeartbeat()
        {
            AudioClip clip = _heartSource.clip != null ? _heartSource.clip : GetClip(Sfx.Heart);
            if (clip == null)
            {
                return;
            }
            if (_heartSource.clip != clip)
            {
                _heartSource.clip = clip;
            }

            float target = _heartIntensity > 0f ? _seVolume * Mathf.Lerp(0.18f, 0.92f, _heartIntensity) : 0f;
            float fadeSpeed = _heartIntensity > 0f ? Mathf.Lerp(0.35f, 1.35f, _heartIntensity) : 1.3f;
            _heartSource.volume = Mathf.MoveTowards(_heartSource.volume, target, Time.unscaledDeltaTime * fadeSpeed);
            _heartSource.pitch = 0.92f + 0.14f * _heartIntensity
                + 0.05f * Mathf.Sin(Time.unscaledTime * Mathf.Lerp(1.8f, 3.2f, _heartIntensity));

            if (_heartSource.volume > 0.001f)
            {
                if (!_heartSource.isPlaying)
                {
                    _heartSource.Play();
                    Log($"heartbeat fade in clip={clip.name}");
                }
            }
            else if (_heartSource.isPlaying)
            {
                _heartSource.Stop();
                Log("heartbeat fade out");
            }
        }

        private void PlayTransportCue(Sfx sfx, float pitch)
        {
            if (sfx == Sfx.TrainDeparture)
            {
                SetTrainMoving(true, pitch);
                return;
            }
            if (sfx == Sfx.TrainStop)
            {
                SetTrainMoving(false);
            }

            AudioClip clip = GetClip(sfx);
            if (clip == null || _stationSource == null)
            {
                return;
            }

            float now = Time.unscaledTime;
            float interval = MinimumRepeatInterval(sfx);
            if (_lastPlayedAt.TryGetValue(sfx, out float last) && now - last < interval)
            {
                Log($"skip {sfx} ({now - last:0.00}s < {interval:0.00}s)");
                return;
            }
            _lastPlayedAt[sfx] = now;

            _stationSource.Stop();
            _stationSource.clip = clip;
            CueTuning tuning = GetCueTuning(sfx);
            _stationSource.pitch = pitch;
            _stationSource.volume = _seVolume * Mathf.Max(0f, tuning.volumeScale);
            PlaySource(_stationSource, clip, tuning, false);
            Log($"transport {sfx} clip={clip.name} pitch={pitch:0.00} vol={_stationSource.volume:0.00}");
        }

        private static bool IsTransportCue(Sfx sfx)
        {
            return sfx == Sfx.Arrive
                || sfx == Sfx.Bell
                || sfx == Sfx.TrainDeparture
                || sfx == Sfx.TrainStop;
        }

        private CueTuning GetCueTuning(Sfx sfx)
        {
            if (_cueTunings.TryGetValue(sfx, out CueTuning tuning))
            {
                return tuning;
            }

            // 既定のミックス。頻繁に鳴る・環境的な音は控えめにし、決め所（発車ベル/着席/倒れる）を立てる
            float vol;
            switch (sfx)
            {
                case Sfx.TrainDeparture: vol = 0.5f; break;  // 走行ループは床鳴りとして薄く
                case Sfx.Horn: vol = 0.45f; break;           // クラクションは遠景として控えめ
                case Sfx.Ding: vol = 0.75f; break;
                case Sfx.Arrive: vol = 0.8f; break;
                case Sfx.TrainStop: vol = 0.8f; break;
                case Sfx.Bell: vol = 0.9f; break;            // 発車ベルは決め所なので前へ
                case Sfx.Coffee: vol = 0.9f; break;
                case Sfx.Sit: vol = 1f; break;               // 「座れた！」は一番気持ちよく
                case Sfx.GameOver: vol = 1f; break;
                default: vol = 1f; break;
            }

            return new CueTuning
            {
                startTime = 0f,
                duration = 0f,
                volumeScale = vol,
                fadeOutSeconds = 0.05f,
            };
        }

        /// <summary>連射しても機械的に聞こえないよう、繰り返し系の一発物へ軽いピッチ揺らぎを与える。</summary>
        private static float PitchJitter(Sfx sfx)
        {
            switch (sfx)
            {
                case Sfx.Sit:
                case Sfx.Coffee:
                case Sfx.Ding:
                case Sfx.Horn:
                    return 1f + Random.Range(-0.04f, 0.04f);
                default:
                    return 1f;
            }
        }

        private void PlaySource(AudioSource source, AudioClip clip, CueTuning tuning, bool loop)
        {
            source.Stop();
            source.clip = clip;
            source.loop = loop;
            SetStartTime(source, clip, tuning.startTime);
            source.Play();

            int token = NextSourceToken(source);
            if (tuning.duration > 0f)
            {
                StartCoroutine(StopSourceAfter(source, token, tuning.duration, tuning.fadeOutSeconds));
            }
        }

        private int NextSourceToken(AudioSource source)
        {
            _sourceTokens.TryGetValue(source, out int token);
            token++;
            _sourceTokens[source] = token;
            return token;
        }

        private IEnumerator StopSourceAfter(AudioSource source, int token, float delay, float fadeOut)
        {
            yield return new WaitForSecondsRealtime(delay);
            if (source == null
                || !_sourceTokens.TryGetValue(source, out int current)
                || current != token
                || !source.isPlaying)
            {
                yield break;
            }

            if (fadeOut > 0f)
            {
                float start = source.volume;
                float t = 0f;
                while (t < 1f && source != null && source.isPlaying)
                {
                    t = Mathf.Min(1f, t + Time.unscaledDeltaTime / fadeOut);
                    source.volume = Mathf.Lerp(start, 0f, t);
                    yield return null;
                }
            }
            if (source != null && _sourceTokens.TryGetValue(source, out current) && current == token)
            {
                source.Stop();
            }
        }

        private static void SetStartTime(AudioSource source, AudioClip clip, float startTime)
        {
            if (source == null || clip == null || startTime <= 0f)
            {
                return;
            }
            source.time = Mathf.Min(startTime, Mathf.Max(0f, clip.length - 0.01f));
        }

        private AudioClip GetClip(Sfx sfx)
        {
            if (_clips.TryGetValue(sfx, out AudioClip cached))
            {
                return cached;
            }

            var loaded = LoadResourceClip(sfx);
            AudioClip clip = loaded != null ? loaded : Synthesize(sfx);
            _clips[sfx] = clip;
            return clip;
        }

        private static AudioClip LoadResourceClip(Sfx sfx)
        {
            string[] names;
            switch (sfx)
            {
                case Sfx.Arrive:
                    names = new[] { "Arrive", "arrive", "ding" };
                    break;
                case Sfx.Bell:
                    names = new[] { "Bell", "bell", "departure" };
                    break;
                case Sfx.TrainDeparture:
                    names = new[] { "電車発車1", "train_departure", "departure" };
                    break;
                case Sfx.TrainStop:
                    names = new[] { "電車停車", "train_stop", "ding" };
                    break;
                case Sfx.Horn:
                    names = new[] { "電車のクラクション", "horn" };
                    break;
                case Sfx.Heart:
                    names = new[] { "Heart", "heart" };
                    break;
                default:
                    names = new[] { sfx.ToString().ToLowerInvariant() };
                    break;
            }

            foreach (string name in names)
            {
                var clip = Resources.Load<AudioClip>("Audio/" + name);
                if (clip != null)
                {
                    return clip;
                }
            }
#if UNITY_EDITOR
            foreach (string name in names)
            {
                var clip = UnityEditor.AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/" + name + ".mp3");
                if (clip != null)
                {
                    return clip;
                }
            }
#endif
            return null;
        }

        private static float MinimumRepeatInterval(Sfx sfx)
        {
            switch (sfx)
            {
                case Sfx.Arrive:
                case Sfx.Bell:
                case Sfx.TrainDeparture:
                case Sfx.TrainStop:
                    return 0.8f;
                case Sfx.Horn:
                    return 2.0f;
                case Sfx.Heart:
                    return 0f;
                case Sfx.GameOver:
                    return 0.6f;
                case Sfx.Sit:
                case Sfx.Coffee:
                    return 0.15f;
                default:
                    return 0.08f;
            }
        }

        private void Log(string message)
        {
            if (_debugLogging)
            {
                _debugEvents[_debugEventIndex % _debugEvents.Length] = $"{Time.unscaledTime:0.0}s  {message}";
                _debugEventIndex++;
                Debug.Log("[GameAudio] " + message, this);
            }
        }

        private void OnGUI()
        {
            if (!_debugLogging)
            {
                return;
            }

            GUILayout.BeginArea(new Rect(14f, 14f, 460f, 272f), "Audio Debug (F9)", GUI.skin.window);
            GUILayout.Label($"BGM {_bgmVolume:0.00}  SE {_seVolume:0.00}");
            GUILayout.Label(SourceLine("TrainLoop", _trainSource, _trainTargetVolume));
            GUILayout.Label(SourceLine("Station", _stationSource, 0f));
            GUILayout.Label(SourceLine("Heart", _heartSource, _heartIntensity > 0f ? _seVolume * Mathf.Lerp(0.18f, 0.92f, _heartIntensity) : 0f));
            GUILayout.Label($"Heart intensity {_heartIntensity:0.00}");
            GUILayout.Label($"SE Pool active {ActiveSeCount()}/{(_sePool != null ? _sePool.Length : 0)}");
            GUILayout.Space(4f);
            GUILayout.Label("Recent");
            int count = Mathf.Min(_debugEventIndex, _debugEvents.Length);
            for (int i = 0; i < count; i++)
            {
                int index = (_debugEventIndex - 1 - i + _debugEvents.Length) % _debugEvents.Length;
                string line = _debugEvents[index];
                if (!string.IsNullOrEmpty(line))
                {
                    GUILayout.Label(line);
                }
            }
            GUILayout.EndArea();
        }

        private int ActiveSeCount()
        {
            if (_sePool == null)
            {
                return 0;
            }

            int count = 0;
            for (int i = 0; i < _sePool.Length; i++)
            {
                if (_sePool[i] != null && _sePool[i].isPlaying)
                {
                    count++;
                }
            }
            return count;
        }

        private static string SourceLine(string label, AudioSource source, float target)
        {
            if (source == null)
            {
                return $"{label}: none";
            }
            string clip = source.clip != null ? source.clip.name : "(no clip)";
            string state = source.isPlaying ? "play" : "stop";
            return $"{label}: {state} {clip} vol {source.volume:0.00}->{target:0.00} pitch {source.pitch:0.00}";
        }

        // ---- 合成音（素材が届くまでの仮。それっぽさ優先の簡易シンセ） ----

        private static AudioClip Synthesize(Sfx sfx)
        {
            switch (sfx)
            {
                case Sfx.Departure:
                case Sfx.Bell:
                case Sfx.TrainDeparture:
                    // 発車メロディ風：明るいアルペジオを2回
                    return Melody("synth_departure", new[]
                    {
                        (784f, 0.18f), (988f, 0.18f), (1175f, 0.18f), (988f, 0.18f),
                        (784f, 0.18f), (988f, 0.18f), (1175f, 0.42f), (0f, 0.1f),
                        (1319f, 0.5f),
                    });
                case Sfx.Ding:
                case Sfx.Arrive:
                case Sfx.TrainStop:
                    // 停車チャイム：ピンポーン
                    return Melody("synth_ding", new[] { (988f, 0.22f), (784f, 0.4f) });
                case Sfx.Sit:
                    // 座れた！：上昇2音
                    return Melody("synth_sit", new[] { (659f, 0.1f), (988f, 0.22f) });
                case Sfx.Coffee:
                    // 回復：コロンとした3音
                    return Melody("synth_coffee", new[] { (523f, 0.09f), (659f, 0.09f), (784f, 0.18f) });
                case Sfx.GameOver:
                    // 倒れる：低く沈む2音
                    return Melody("synth_gameover", new[] { (220f, 0.5f), (147f, 0.9f) });
                case Sfx.Horn:
                    return Melody("synth_horn", new[] { (330f, 0.2f), (294f, 0.34f), (0f, 0.08f), (294f, 0.18f) });
                case Sfx.Heart:
                    return Melody("synth_heart", new[] { (74f, 0.12f), (0f, 0.06f), (58f, 0.18f), (0f, 0.54f) });
                default:
                    return null;
            }
        }

        /// <summary>(周波数Hz, 長さ秒) の列からサイン波メロディを生成（周波数0は休符）。</summary>
        private static AudioClip Melody(string clipName, (float freq, float dur)[] notes)
        {
            const int rate = 44100;
            float total = 0f;
            foreach ((float _, float dur) in notes)
            {
                total += dur;
            }

            var samples = new float[(int)(total * rate) + 1];
            int cursor = 0;
            foreach ((float freq, float dur) in notes)
            {
                int count = (int)(dur * rate);
                for (int i = 0; i < count; i++)
                {
                    if (freq <= 0f)
                    {
                        cursor++;
                        continue;
                    }
                    float t = (float)i / rate;
                    // 立ち上がりと余韻のエンベロープで「電子チャイム」らしく
                    float env = Mathf.Min(1f, t / 0.01f) * Mathf.Exp(-2.6f * t / dur);
                    float wave = Mathf.Sin(2f * Mathf.PI * freq * t) * 0.75f
                               + Mathf.Sin(4f * Mathf.PI * freq * t) * 0.20f; // 2倍音で少し明るく
                    samples[cursor++] = wave * env * 0.5f;
                }
            }

            var clip = AudioClip.Create(clipName, samples.Length, 1, rate, false);
            clip.SetData(samples, 0);
            return clip;
        }
    }
}
