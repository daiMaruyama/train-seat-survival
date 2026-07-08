using System.Collections.Generic;
using UnityEngine;

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

        private const string BgmVolumeKey = "BgmVolume";
        private const string SeVolumeKey = "SeVolume";

        private static GameAudio _instance;

        private AudioSource _bgmSource;
        private AudioSource _heartSource;
        private AudioSource[] _sePool;
        private int _seIndex;
        private float _bgmVolume;
        private float _seVolume;
        private bool _heartRequested;
        [SerializeField] private bool _debugLogging;
        private readonly Dictionary<Sfx, AudioClip> _clips = new Dictionary<Sfx, AudioClip>();
        private readonly Dictionary<Sfx, float> _lastPlayedAt = new Dictionary<Sfx, float>();

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
                _bgmSource.volume = _bgmVolume;
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

            _heartSource = gameObject.AddComponent<AudioSource>();
            _heartSource.loop = true;
            _heartSource.playOnAwake = false;
            _heartSource.volume = 0f;

            _sePool = new AudioSource[6];
            for (int i = 0; i < _sePool.Length; i++)
            {
                _sePool[i] = gameObject.AddComponent<AudioSource>();
                _sePool[i].playOnAwake = false;
            }
        }

        /// <summary>SE 再生。Resources/Audio の実ファイル優先、無ければ合成音。</summary>
        public void Play(Sfx sfx, float pitch = 1f)
        {
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
            src.pitch = pitch;
            src.PlayOneShot(clip, _seVolume);
            Log($"play {sfx} clip={clip.name} pitch={pitch:0.00} vol={_seVolume:0.00}");
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

        /// <summary>赤ゲージ中だけ鳴る心音ループ。音量は Update でなめらかに追従する。</summary>
        public void SetHeartbeat(bool active)
        {
            _heartRequested = active;
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
            UpdateHeartbeat();
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

            float target = _heartRequested ? _seVolume * 0.72f : 0f;
            _heartSource.volume = Mathf.MoveTowards(_heartSource.volume, target, Time.unscaledDeltaTime * 0.9f);
            _heartSource.pitch = 0.96f + 0.05f * Mathf.Sin(Time.unscaledTime * 2.2f);

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
                Debug.Log("[GameAudio] " + message, this);
            }
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
