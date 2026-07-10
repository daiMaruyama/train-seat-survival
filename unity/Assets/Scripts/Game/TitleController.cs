using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace TrainSurvival.Game
{
    /// <summary>
    /// タイトル画面「座れ！サラリーマン！」。
    /// ・背景は実際の車内＋車窓を流用し、席で力尽きて寝落ちしたサラリーマンを画面右手に主役として据える
    /// ・文字組みは駅サイン調の落ち着いた配色（紺／クリーム／差し色オレンジ）で、蛍光マーカー風の
    /// 　ハイライト帯を一度だけ横に走らせて“強さ”を出す。常時揺れる装飾は置かない
    /// ・スタート導線（ボタン＋ENTER/SPACE）、BGM/SE 音量スライダー、下段のLED運行案内
    /// ・倒れ込む本格アニメは削除済みモーキャップに依存するため、_dropClip を割り当てれば差し替わる。
    /// 　未割り当てのときは着席ポーズ＋首のうとうと運動でフォールバックする
    /// </summary>
    public sealed class TitleController : MonoBehaviour
    {
        private static readonly Color BgNavy = new Color(0.04f, 0.05f, 0.08f);
        private static readonly Color Vignette = new Color(0.02f, 0.025f, 0.04f, 0.55f);
        private static readonly Color CardBg = new Color(0.05f, 0.06f, 0.10f, 0.80f);
        private static readonly Color AccentOrange = new Color(0.95f, 0.45f, 0.15f);
        private static readonly Color Amber = new Color(1f, 0.72f, 0.34f);

        [SerializeField] private string _gameSceneName = "InGame";

        [Header("倒れ込みアニメ（再インポート後に割り当てると主役が差し替わる）")]
        [SerializeField] private AnimationClip _dropClip;
        [SerializeField] private AnimationClip _collapsedLoopClip;
        [SerializeField] private AnimationClip _getUpClip;

        [Header("音素材")]
        [SerializeField] private AudioClip _bgmClip;
        [SerializeField] private AudioClip _arriveClip;
        [SerializeField] private AudioClip _bellClip;
        [SerializeField] private AudioClip _trainDepartureClip;
        [SerializeField] private AudioClip _trainStopClip;
        [SerializeField] private AudioClip _hornClip;

        private Camera _camera;
        private Transform _cameraTransform;
        private Animator _commuterAnimator;
        private GameObject _commuterRoot;
        private Vector3 _commuterBasePosition;
        private Quaternion _commuterBaseRotation;
        private AnimatorOverrideController _commuterOverrideController;
        private AnimationClip _mainActionOriginalClip;
        private readonly List<KeyValuePair<AnimationClip, AnimationClip>> _clipOverrides = new List<KeyValuePair<AnimationClip, AnimationClip>>();
        private Transform _headBone;
        private Transform _spineBone;
        private Quaternion _headBaseLocal;
        private Quaternion _spineBaseLocal;
        private bool _hasDropClip;

        private CanvasGroup _titleGroup;
        private RectTransform _titleGroupRect;
        private CanvasGroup _startGroup;
        private RectTransform _highlight;
        private float _highlightWidth;
        private RectTransform _ticker;
        private float _tickerCycleWidth = 1600f;
        private RectTransform _startButton;
        private CanvasGroup _fade;

        private Vector3 _cameraBasePosition;
        private bool _loading;
        private bool _passengersPrewarmed;
        private bool _entranceReady;
        public bool EnteredWithFade { get; private set; }

        private void Start()
        {
            Time.timeScale = 1f;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;

            RegisterAudioClips();
            BuildUi();
            bool fadeInFromResult = RunStartContext.ConsumeTitleFadeInTransition();
            EnteredWithFade = fadeInFromResult;
            // 初回も復帰時も、背景だけが先に見えないよう黒で覆ってからUIごと表示する。
            if (_fade != null)
            {
                _fade.alpha = 1f;
                _fade.blocksRaycasts = true;
            }
            ConfigureCamera();
            // 車内＋ビル群の建設は重いので初回フレーム描画の後ろへ回す（黒カバー＋UIを先に出し、
            // シーン切替直後の「何も出ない待ち時間」を消す。建設のヒッチは黒の下に隠れる）

            GameAudio.Instance.Play(GameAudio.Sfx.Arrive, 0.98f);
            GameAudio.Instance.PlayBgm(_bgmClip); // タイトルBGMをループ再生
            StartCoroutine(TitleRevealRoutine());
            StartCoroutine(AmbientHornRoutine());
            StartCoroutine(PrewarmPassengersRoutine());
        }

        private void Update()
        {
            float t = Time.unscaledTime;

            // LED運行案内のスクロール
            if (_ticker != null)
            {
                _ticker.anchoredPosition = new Vector2(-Mathf.Repeat(t * 90f, _tickerCycleWidth), 0f);
            }

            // ごく浅いカメラの呼吸（酔わない範囲）
            if (_cameraTransform != null)
            {
                _cameraTransform.position = _cameraBasePosition
                    + new Vector3(Mathf.Sin(t * 0.7f) * 0.015f, Mathf.Sin(t * 1.1f) * 0.010f, 0f);
            }

            // スタートボタンをそっと脈打たせる（ホバーの色替えは UiKit.MakeButton 側が担当）
            if (_startButton != null)
            {
                float pulse = 1f + 0.024f * Mathf.Sin(t * 3.2f);
                _startButton.localScale = new Vector3(pulse, pulse, 1f);
            }

            Keyboard kb = Keyboard.current;
            if (!_loading && _entranceReady && kb != null &&
                (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame))
            {
                StartGame();
            }
        }

        /// <summary>着席ポーズの上に「うとうと」の首・背の揺れを重ねる（Animator の後に上書き）。</summary>
        private void LateUpdate()
        {
            if (_hasDropClip)
            {
                return; // 本物の倒れ込みアニメがあるならそちらに任せる
            }

            float t = Time.unscaledTime;
            if (_headBone != null)
            {
                // こくり…と落ちる周期的なうなずき＋わずかな横揺れ
                float nod = 14f + 10f * Mathf.Sin(t * 0.9f);
                float tilt = 5f * Mathf.Sin(t * 0.6f + 1f);
                _headBone.localRotation = _headBaseLocal * Quaternion.Euler(nod, tilt, tilt * 0.6f);
            }
            if (_spineBone != null)
            {
                float slump = 8f + 3f * Mathf.Sin(t * 0.9f); // 呼吸に合わせて前へ落ちる
                _spineBone.localRotation = _spineBaseLocal * Quaternion.Euler(slump, 0f, 0f);
            }
        }

        public void StartGame()
        {
            if (_loading || !_entranceReady)
            {
                return;
            }

            _loading = true;
            GameAudio.Instance.Play(GameAudio.Sfx.Bell);
            StartCoroutine(LoadRoutine());
        }

        private void ShowRankingPlaceholder()
        {
            GameAudio.Instance.Play(GameAudio.Sfx.Ding, 0.92f);
        }

        private IEnumerator LoadRoutine()
        {
            if (_fade != null)
            {
                _fade.blocksRaycasts = true;
                float t = 0f;
                while (t < 1f)
                {
                    t = Mathf.Min(1f, t + Time.unscaledDeltaTime / 0.45f);
                    _fade.alpha = t * t;
                    yield return null;
                }
            }
            if (!_passengersPrewarmed)
            {
                PassengerPool.Prewarm();
                _passengersPrewarmed = true;
                yield return null;
            }
            GameAudio.Instance.StopBgm(); // タイトルBGMをゲームへ持ち込まない
            RunStartContext.RequestOpeningDayTransition(); // InGame 開幕の日替わり演出を焚く
            yield return SceneManager.LoadSceneAsync(_gameSceneName);
        }

        private IEnumerator PrewarmPassengersRoutine()
        {
            // タイトル入場中の引っかかりを避け、操作可能になった直後に裏で先読みする。
            yield return null;
            while (!_entranceReady)
            {
                yield return null;
            }
            yield return null;
            PassengerPool.Prewarm();
            _passengersPrewarmed = true;
        }

        private IEnumerator TitleRevealRoutine()
        {
            // まず黒カバー＋UIだけの初回フレームを出してから、重い世界構築を黒の下で行う
            yield return null;
            BuildWorld();

            PrepareEntrance();
            StartCoroutine(FadeInRoutine());
            // 黒が大半晴れてからUIを動かし、入場アニメーションを見せる。
            yield return new WaitForSecondsRealtime(0.12f);
            yield return EntranceRoutine();
        }

        private IEnumerator FadeInRoutine()
        {
            yield return Animate(0.28f, t => _fade.alpha = 1f - EaseOutCubic(t));
            _fade.alpha = 0f;
            _fade.blocksRaycasts = false;
        }

        // ---- 入場演出：タイトルが決まり、マーカーが走る -------------------

        private void PrepareEntrance()
        {
            _entranceReady = false;
            _titleGroup.alpha = 0f;
            _startGroup.alpha = 0f;
            if (_highlight != null)
            {
                _highlight.sizeDelta = new Vector2(0f, _highlight.sizeDelta.y);
            }
        }

        private IEnumerator EntranceRoutine()
        {
            // 操作UIも並行して出し、背景だけが先行して見える時間を作らない。
            StartCoroutine(Animate(0.48f, p => _startGroup.alpha = EaseOutCubic(p)));

            // タイトルがスッと決まる（左からわずかに寄って止まる）
            yield return Animate(0.45f, p =>
            {
                _titleGroup.alpha = p;
                _titleGroupRect.anchoredPosition = new Vector2(Mathf.Lerp(-48f, 0f, EaseOutCubic(p)), _titleGroupRect.anchoredPosition.y);
            });
            _startGroup.alpha = 1f;
            _entranceReady = true;

            // 蛍光マーカーが横に一度だけ走る
            if (_highlight != null)
            {
                yield return Animate(0.25f, p =>
                    _highlight.sizeDelta = new Vector2(_highlightWidth * EaseOutCubic(p), _highlight.sizeDelta.y));
            }
        }

        private IEnumerator AmbientHornRoutine()
        {
            // 遠くのクラクションをたまに鳴らして駅の空気を出す
            while (!_loading)
            {
                yield return new WaitForSeconds(Random.Range(6f, 12f));
                if (!_loading)
                {
                    GameAudio.Instance.Play(GameAudio.Sfx.Horn, Random.Range(0.85f, 1.05f));
                }
            }
        }

        private void RegisterAudioClips()
        {
            GameAudio audio = GameAudio.Instance;
            audio.RegisterClip(GameAudio.Sfx.Arrive, _arriveClip);
            audio.RegisterClip(GameAudio.Sfx.Bell, _bellClip);
            audio.RegisterClip(GameAudio.Sfx.TrainDeparture, _trainDepartureClip);
            audio.RegisterClip(GameAudio.Sfx.TrainStop, _trainStopClip);
            audio.RegisterClip(GameAudio.Sfx.Horn, _hornClip);
        }

        // ---- 3D 背景（車内＋主役サラリーマン） -----------------------------

        private void BuildWorld()
        {
            var carGo = new GameObject("TitleCar");
            CarBuilder.OverrideNextCoffeeSpawn(false);
            carGo.AddComponent<CarBuilder>();

            var sceneryGo = new GameObject("TitleScenery");
            Scenery.OverrideNextBuildingCount(5);
            sceneryGo.AddComponent<Scenery>();

            SpawnCommuter();
        }

        private void SpawnCommuter()
        {
            bool useDropPerformance = _dropClip != null;
            Vector3 basePos = useDropPerformance ? new Vector3(0f, 0f, 0.08f) : new Vector3(1.4f, 0.30f, 0.15f);
            Quaternion baseRot = useDropPerformance ? Quaternion.Euler(0f, 0f, 0f) : Quaternion.Euler(0f, -90f, 0f);

            GameObject prefab = Resources.Load<GameObject>("Passengers/male02_1");
            if (prefab == null)
            {
                GameObject fallback = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                fallback.name = "TitleSalaryman_Fallback";
                fallback.transform.SetPositionAndRotation(basePos + Vector3.up * 0.35f, baseRot);
                fallback.transform.localScale = new Vector3(0.42f, 0.6f, 0.42f);
                return;
            }

            GameObject commuter = Instantiate(prefab);
            PassengerActor.ApplyOptimizedVisual(commuter, prefab.name);
            commuter.name = "TitleSalaryman";
            commuter.transform.SetPositionAndRotation(basePos, baseRot);
            commuter.transform.localScale = Vector3.one * 0.72f;
            _commuterRoot = commuter;
            _commuterBasePosition = basePos;
            _commuterBaseRotation = baseRot;

            _commuterAnimator = commuter.GetComponent<Animator>();
            if (_commuterAnimator == null)
            {
                return;
            }

            _commuterAnimator.applyRootMotion = false;
            _commuterAnimator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

            _hasDropClip = useDropPerformance;
            if (_hasDropClip)
            {
                OverrideCommuterClips(_commuterAnimator);
                StartCoroutine(CollapsePerformanceRoutine());
            }
            else
            {
                // 着席ポーズで固定し、うとうと運動は LateUpdate で重ねる
                _commuterAnimator.Play("Sit", 0, 0.5f);
                _headBone = _commuterAnimator.GetBoneTransform(HumanBodyBones.Head);
                _spineBone = _commuterAnimator.GetBoneTransform(HumanBodyBones.Spine);
                if (_headBone != null)
                {
                    _headBaseLocal = _headBone.localRotation;
                }
                if (_spineBone != null)
                {
                    _spineBaseLocal = _spineBone.localRotation;
                }
            }
        }

        private IEnumerator CollapsePerformanceRoutine()
        {
            yield return null;

            while (!_loading && _commuterAnimator != null)
            {
                if (_commuterRoot != null)
                {
                    _commuterRoot.transform.SetPositionAndRotation(_commuterBasePosition, _commuterBaseRotation);
                }

                SetMainActionClip(_dropClip);
                _commuterAnimator.Play("SitDown", 0, 0f);
                GameAudio.Instance.Play(GameAudio.Sfx.GameOver, 0.86f);
                yield return new WaitForSeconds(Mathf.Max(1.6f, ClipLength(_dropClip)));

                if (_collapsedLoopClip != null)
                {
                    _commuterAnimator.CrossFade("Walk", 0.18f, 0, 0f);
                    yield return new WaitForSeconds(1.35f);
                }

                if (_getUpClip != null)
                {
                    SetMainActionClip(_getUpClip);
                    _commuterAnimator.Play("SitDown", 0, 0f);
                    yield return new WaitForSeconds(Mathf.Max(1.4f, ClipLength(_getUpClip)));
                }
                else
                {
                    yield return new WaitForSeconds(0.85f);
                }
            }
        }

        private void OverrideCommuterClips(Animator animator)
        {
            RuntimeAnimatorController baseController = animator.runtimeAnimatorController;
            if (baseController == null || _dropClip == null)
            {
                return;
            }

            _commuterOverrideController = new AnimatorOverrideController(baseController);
            _commuterOverrideController.GetOverrides(_clipOverrides);
            for (int i = 0; i < _clipOverrides.Count; i++)
            {
                AnimationClip original = _clipOverrides[i].Key;
                if (original == null)
                {
                    continue;
                }

                AnimationClip replacement = null;
                if (original.name.Contains("Stand_Trans_SitPiano"))
                {
                    _mainActionOriginalClip = original;
                    replacement = _dropClip;
                }
                else if (original.name.Contains("OrcHammer") && _collapsedLoopClip != null)
                {
                    replacement = _collapsedLoopClip;
                }

                if (replacement != null)
                {
                    _clipOverrides[i] = new KeyValuePair<AnimationClip, AnimationClip>(original, replacement);
                }
            }
            _commuterOverrideController.ApplyOverrides(_clipOverrides);
            animator.runtimeAnimatorController = _commuterOverrideController;
        }

        private void SetMainActionClip(AnimationClip clip)
        {
            if (_commuterOverrideController == null || _mainActionOriginalClip == null || clip == null)
            {
                return;
            }

            _commuterOverrideController.GetOverrides(_clipOverrides);
            for (int i = 0; i < _clipOverrides.Count; i++)
            {
                if (_clipOverrides[i].Key == _mainActionOriginalClip)
                {
                    _clipOverrides[i] = new KeyValuePair<AnimationClip, AnimationClip>(_mainActionOriginalClip, clip);
                    break;
                }
            }
            _commuterOverrideController.ApplyOverrides(_clipOverrides);
        }

        private void ConfigureCamera()
        {
            _camera = Camera.main;
            if (_camera == null)
            {
                var cameraGo = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener));
                cameraGo.tag = "MainCamera";
                _camera = cameraGo.GetComponent<Camera>();
            }

            // 車両中央で倒れる主役が読めるように、少し引いた正面斜めから見る。
            _cameraTransform = _camera.transform;
            Vector3 camPos = new Vector3(-1.25f, 1.10f, -2.85f);
            Vector3 lookAt = new Vector3(0f, 0.82f, 0.08f);
            _cameraTransform.position = camPos;
            _cameraTransform.rotation = Quaternion.LookRotation((lookAt - camPos).normalized, Vector3.up);
            _cameraBasePosition = camPos;
            _camera.fieldOfView = 46f;
            _camera.clearFlags = CameraClearFlags.SolidColor;
            _camera.backgroundColor = BgNavy;

            // 主役の顔に暖色のキーライトを当てる
            Light light = FindFirstObjectByType<Light>();
            if (light != null)
            {
                light.transform.rotation = Quaternion.Euler(38f, -128f, 0f);
                light.intensity = 2.5f;
                light.color = new Color(1f, 0.94f, 0.82f);
            }
        }

        // ---- 2D タイトル UI ----------------------------------------------

        private void BuildUi()
        {
            EnsureEventSystem();

            var canvasGo = new GameObject("TitleCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasGo.transform.SetParent(transform, false);
            Canvas canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 10;
            CanvasScaler scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            RectTransform root = canvas.GetComponent<RectTransform>();

            // 画面全体を落ち着かせる暗幕。車内は見せつつ、文字を全面で読ませる。
            Image vignette = CreateImage("Vignette", root, Vignette);
            Stretch(vignette.rectTransform);
            Image fullShade = CreateImage("FullShade", root, new Color(0.02f, 0.025f, 0.04f, 0.32f));
            Stretch(fullShade.rectTransform);

            BuildTitleBlock(root);
            BuildStartBlock(root);
            BuildTicker(root);
            BuildVolumePanel(root);

            Image fadeImage = CreateImage("Fade", root, Color.black);
            Stretch(fadeImage.rectTransform);
            _fade = fadeImage.gameObject.AddComponent<CanvasGroup>();
            _fade.alpha = 0f;
            _fade.blocksRaycasts = false;
        }

        private void BuildTitleBlock(RectTransform root)
        {
            var groupGo = new GameObject("TitleBlock", typeof(RectTransform));
            groupGo.transform.SetParent(root, false);
            _titleGroupRect = groupGo.GetComponent<RectTransform>();
            Stretch(_titleGroupRect);
            _titleGroup = groupGo.AddComponent<CanvasGroup>();

            // キッカー：小さな見出し＋オレンジの下線
            Text kicker = CreateText("Kicker", _titleGroupRect, 30, TextAnchor.MiddleLeft);
            kicker.text = "満員電車サバイバル";
            kicker.color = new Color(1f, 1f, 1f, 0.82f);
            kicker.fontStyle = FontStyle.Bold;
            Anchor(kicker.rectTransform, new Vector2(0f, 1f), new Vector2(96f, -168f), new Vector2(520f, 44f));

            Image kickerLine = CreateImage("KickerLine", _titleGroupRect, AccentOrange);
            Anchor(kickerLine.rectTransform, new Vector2(0f, 1f), new Vector2(98f, -206f), new Vector2(190f, 6f));
            UiKit.Panelize(kickerLine, 3);

            // タイトル本体（クリアな一枚影＋2行目に蛍光マーカー帯）
            // 2行目のマーカー帯（テキストより先に生成して背面へ）
            Image highlightImg = CreateImage("Highlight", _titleGroupRect, new Color(AccentOrange.r, AccentOrange.g, AccentOrange.b, 0.9f));
            _highlight = highlightImg.rectTransform;
            Anchor(_highlight, new Vector2(0f, 1f), new Vector2(92f, -430f), new Vector2(660f, 96f));
            _highlight.pivot = new Vector2(0f, 0.5f);
            _highlightWidth = 660f;
            UiKit.Panelize(highlightImg, 8);

            CreateTitleText("TitleShadow", _titleGroupRect, new Vector2(106f, -234f), new Color(0f, 0f, 0f, 0.55f));
            Text titleMain = CreateTitleText("Title", _titleGroupRect, new Vector2(100f, -228f), Color.white);
            UiKit.Outline(titleMain, 2f, 0f);
        }

        private Text CreateTitleText(string name, RectTransform parent, Vector2 pos, Color color)
        {
            Text title = CreateText(name, parent, 118, TextAnchor.UpperLeft);
            title.text = "座れ！\nサラリーマン！";
            title.fontStyle = FontStyle.Bold;
            title.lineSpacing = 0.92f;
            title.color = color;
            Anchor(title.rectTransform, new Vector2(0f, 1f), pos, new Vector2(1000f, 320f));
            return title;
        }

        private void BuildStartBlock(RectTransform root)
        {
            var groupGo = new GameObject("StartBlock", typeof(RectTransform));
            groupGo.transform.SetParent(root, false);
            RectTransform groupRect = groupGo.GetComponent<RectTransform>();
            Stretch(groupRect);
            _startGroup = groupGo.AddComponent<CanvasGroup>();

            // 全シーン共通ボタン（暗い面→ホバーでオレンジ）。同じ見た目・同じ大きさに統一。
            // ※MakeButton は生成時の位置で影を焼くので、中心基準の最終位置で呼ぶ
            // 　（左上(0,1)基準の (340,-600) は 1920×1080 中心座標で約 (-620,-70)）。
            var btnSize = new Vector2(360f, 92f);
            Button startBtn = UiKit.MakeButton(groupRect, new Vector2(-620f, -70f), btnSize, "出勤する", 30, StartGame);
            _startButton = startBtn.GetComponent<RectTransform>();

            UiKit.MakeButton(groupRect, new Vector2(-620f, -176f), btnSize, "ランキング", 30, ShowRankingPlaceholder);

            Text hint = CreateText("Hint", groupRect, 22, TextAnchor.MiddleCenter);
            hint.text = "［ENTER / SPACE］で発車";
            hint.color = new Color(1f, 1f, 1f, 0.55f);
            hint.fontStyle = FontStyle.Bold;
            Anchor(hint.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(-620f, -252f), new Vector2(420f, 30f));
        }

        private void BuildTicker(RectTransform root)
        {
            // 下段いっぱいの LED 運行案内（濃紺の帯にアンバーの流れる文字）
            RectTransform strip = CreateImage("TickerStrip", root, new Color(0.03f, 0.04f, 0.06f, 0.92f)).rectTransform;
            strip.anchorMin = new Vector2(0f, 0f);
            strip.anchorMax = new Vector2(1f, 0f);
            strip.pivot = new Vector2(0.5f, 0f);
            strip.anchoredPosition = Vector2.zero;
            strip.sizeDelta = new Vector2(0f, 52f);

            Image topLine = CreateImage("TickerTop", strip, new Color(AccentOrange.r, AccentOrange.g, AccentOrange.b, 0.7f));
            topLine.rectTransform.anchorMin = new Vector2(0f, 1f);
            topLine.rectTransform.anchorMax = new Vector2(1f, 1f);
            topLine.rectTransform.pivot = new Vector2(0.5f, 1f);
            topLine.rectTransform.sizeDelta = new Vector2(0f, 3f);
            topLine.rectTransform.anchoredPosition = Vector2.zero;

            _ticker = new GameObject("Ticker", typeof(RectTransform)).GetComponent<RectTransform>();
            _ticker.SetParent(strip, false);
            _ticker.anchorMin = new Vector2(0f, 0.5f);
            _ticker.anchorMax = new Vector2(0f, 0.5f);
            _ticker.pivot = new Vector2(0f, 0.5f);
            _ticker.sizeDelta = new Vector2(6400f, 52f);

            const string unit = "◆ 空席を見つけて座り、社畜人生を生き延びろ　◆ 毎日お疲れ様です、ご自愛ください　";
            Text measure = CreateText("Measure", _ticker, 24, TextAnchor.MiddleLeft);
            measure.text = unit;
            measure.enabled = false;
            const float overlap = 18f;
            _tickerCycleWidth = Mathf.Max(1f, Mathf.Ceil(measure.preferredWidth) - overlap);
            Destroy(measure.gameObject);

            _ticker.sizeDelta = new Vector2(_tickerCycleWidth * 4f, 52f);
            for (int i = 0; i < 4; i++)
            {
                Text ticker = CreateText($"Text_{i}", _ticker, 24, TextAnchor.MiddleLeft);
                ticker.text = unit;
                ticker.color = Amber;
                ticker.rectTransform.anchorMin = new Vector2(0f, 0.5f);
                ticker.rectTransform.anchorMax = new Vector2(0f, 0.5f);
                ticker.rectTransform.pivot = new Vector2(0f, 0.5f);
                ticker.rectTransform.anchoredPosition = new Vector2(_tickerCycleWidth * i, 0f);
                ticker.rectTransform.sizeDelta = new Vector2(_tickerCycleWidth + overlap, 52f);
            }
        }

        private void BuildVolumePanel(RectTransform root)
        {
            GameAudio audio = GameAudio.Instance;

            Image panel = CreateImage("VolumePanel", root, CardBg);
            RectTransform rect = panel.rectTransform;
            Anchor(rect, new Vector2(1f, 1f), new Vector2(-36f, -36f), new Vector2(320f, 132f));
            rect.pivot = new Vector2(1f, 1f);
            UiKit.Panelize(panel, 16);
            UiKit.AddShadow(panel, 16, blur: 26, alpha: 0.42f, offset: new Vector2(0f, -8f));

            Text head = CreateText("Head", rect, 20, TextAnchor.MiddleLeft);
            head.text = "音量";
            head.color = new Color(1f, 1f, 1f, 0.7f);
            head.fontStyle = FontStyle.Bold;
            Anchor(head.rectTransform, new Vector2(0f, 1f), new Vector2(20f, -12f), new Vector2(120f, 26f));
            UiKit.Outline(head, 1.1f, 0.35f);

            BuildSlider(rect, "BGM", new Vector2(20f, -48f), audio.BgmVolume, v => audio.BgmVolume = v);
            BuildSlider(rect, "SE", new Vector2(20f, -90f), audio.SeVolume, v => audio.SeVolume = v);
        }

        /// <summary>ラベル付きの簡易スライダー（ハンドル無し・バーをドラッグで値変更）。</summary>
        private void BuildSlider(RectTransform parent, string label, Vector2 pos, float value, UnityEngine.Events.UnityAction<float> onChanged)
        {
            Text tag = CreateText($"{label}Label", parent, 18, TextAnchor.MiddleLeft);
            tag.text = label;
            tag.color = new Color(1f, 1f, 1f, 0.62f);
            Anchor(tag.rectTransform, new Vector2(0f, 1f), new Vector2(20f, pos.y), new Vector2(52f, 28f));

            var sliderGo = new GameObject($"{label}Slider", typeof(RectTransform), typeof(Image), typeof(Slider));
            sliderGo.transform.SetParent(parent, false);
            RectTransform sliderRect = sliderGo.GetComponent<RectTransform>();
            Anchor(sliderRect, new Vector2(0f, 1f), new Vector2(76f, pos.y), new Vector2(212f, 20f));
            sliderRect.pivot = new Vector2(0f, 0.5f);
            Image bg = sliderGo.GetComponent<Image>();
            bg.color = new Color(1f, 1f, 1f, 0.14f);
            UiKit.Panelize(bg, 10);

            // 塗り（Fill Area > Fill）
            var fillAreaGo = new GameObject("Fill Area", typeof(RectTransform));
            fillAreaGo.transform.SetParent(sliderRect, false);
            RectTransform fillArea = fillAreaGo.GetComponent<RectTransform>();
            fillArea.anchorMin = new Vector2(0f, 0f);
            fillArea.anchorMax = new Vector2(1f, 1f);
            fillArea.offsetMin = Vector2.zero;
            fillArea.offsetMax = Vector2.zero;

            Image fillImage = CreateImage("Fill", fillArea, AccentOrange);
            fillImage.raycastTarget = false;
            UiKit.Panelize(fillImage, 9);
            RectTransform fillRect = fillImage.rectTransform;
            fillRect.anchorMin = new Vector2(0f, 0f);
            fillRect.anchorMax = new Vector2(0f, 1f);
            fillRect.pivot = new Vector2(0f, 0.5f);
            fillRect.sizeDelta = new Vector2(0f, 0f);

            // つまみ（丸ノブ）で操作感を出す
            var handleAreaGo = new GameObject("Handle Slide Area", typeof(RectTransform));
            handleAreaGo.transform.SetParent(sliderRect, false);
            RectTransform handleArea = handleAreaGo.GetComponent<RectTransform>();
            handleArea.anchorMin = new Vector2(0f, 0f);
            handleArea.anchorMax = new Vector2(1f, 1f);
            handleArea.offsetMin = new Vector2(10f, 0f);
            handleArea.offsetMax = new Vector2(-10f, 0f);

            Image handleImage = CreateImage("Handle", handleArea, Color.white);
            handleImage.raycastTarget = true;
            UiKit.Panelize(handleImage, 10);
            RectTransform handleRect = handleImage.rectTransform;
            handleRect.sizeDelta = new Vector2(22f, 22f);

            Slider slider = sliderGo.GetComponent<Slider>();
            slider.transition = Selectable.Transition.None;
            slider.fillRect = fillRect;
            slider.handleRect = handleRect;
            slider.targetGraphic = handleImage;
            slider.direction = Slider.Direction.LeftToRight;
            slider.minValue = 0f;
            slider.maxValue = 1f;
            slider.value = value;
            slider.onValueChanged.AddListener(onChanged);
        }

        // ---- 補間ユーティリティ（すべて unscaled 時間） --------------------

        private static IEnumerator Animate(float duration, System.Action<float> step)
        {
            float t = 0f;
            while (t < 1f)
            {
                t = duration <= 0f ? 1f : Mathf.Min(1f, t + Time.unscaledDeltaTime / duration);
                step(t);
                yield return null;
            }
        }

        private static float EaseOutCubic(float x) => 1f - Mathf.Pow(1f - x, 3f);

        private static float ClipLength(AnimationClip clip) => clip != null ? clip.length : 0f;

        private static void EnsureEventSystem()
        {
            if (EventSystem.current != null)
            {
                return;
            }
            var go = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
            DontDestroyOnLoad(go);
        }

        private static void Anchor(RectTransform rt, Vector2 anchor, Vector2 position, Vector2 size)
        {
            rt.anchorMin = anchor;
            rt.anchorMax = anchor;
            rt.pivot = anchor;
            rt.anchoredPosition = position;
            rt.sizeDelta = size;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private static Image CreateImage(string objName, Transform parent, Color color)
        {
            var go = new GameObject(objName, typeof(Image));
            go.transform.SetParent(parent, false);
            Image image = go.GetComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        private static Text CreateText(string objName, Transform parent, int fontSize, TextAnchor alignment)
        {
            var go = new GameObject(objName, typeof(Text));
            go.transform.SetParent(parent, false);
            Text text = go.GetComponent<Text>();
            text.font = UiFont.Load();
            text.fontSize = fontSize;
            text.alignment = alignment;
            text.color = Color.white;
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            return text;
        }
    }
}
