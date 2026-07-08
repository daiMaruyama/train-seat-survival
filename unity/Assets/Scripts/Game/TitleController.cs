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
    /// タイトル画面。InGame と同じ車内・フォント・落ち着いた配色を使いつつ、
    /// 斜め帯と強い文字組みで少しだけ派手にする。
    /// </summary>
    public sealed class TitleController : MonoBehaviour
    {
        private static readonly Color Cover = new Color(0.03f, 0.035f, 0.05f, 0.56f);
        private static readonly Color CardBg = new Color(0.05f, 0.06f, 0.10f, 0.72f);
        private static readonly Color AccentOrange = new Color(0.95f, 0.45f, 0.15f);
        private static readonly Color Paper = new Color(0.94f, 0.93f, 0.89f);
        private static readonly Color Ink = new Color(0.12f, 0.13f, 0.17f);

        [SerializeField] private string _gameSceneName = "InGame";
        [SerializeField] private AnimationClip _dropClip;
        [SerializeField] private AnimationClip _collapsedLoopClip;

        [Header("音素材")]
        [SerializeField] private AudioClip _arriveClip;
        [SerializeField] private AudioClip _bellClip;
        [SerializeField] private AudioClip _trainDepartureClip;
        [SerializeField] private AudioClip _trainStopClip;
        [SerializeField] private AudioClip _hornClip;

        private Camera _camera;
        private Transform _cameraTransform;
        private Animator _commuterAnimator;
        private RectTransform _slashA;
        private RectTransform _slashB;
        private RectTransform _trainBand;
        private CanvasGroup _fade;
        private Vector3 _cameraBasePosition;
        private bool _loading;

        private void Start()
        {
            Time.timeScale = 1f;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;

            RegisterAudioClips();
            BuildWorld();
            BuildUi();
            ConfigureCamera();

            GameAudio.Instance.Play(GameAudio.Sfx.Arrive, 0.98f);
            StartCoroutine(CommuterLoop());
        }

        private void Update()
        {
            float t = Time.unscaledTime;
            if (_slashA != null)
            {
                _slashA.anchoredPosition = new Vector2(Mathf.Sin(t * 1.8f) * 28f, 0f);
                _slashB.anchoredPosition = new Vector2(Mathf.Sin(t * 1.2f + 1.6f) * 36f, -40f);
            }
            if (_trainBand != null)
            {
                _trainBand.anchoredPosition = new Vector2(Mathf.Repeat(t * 120f, 420f) - 210f, 0f);
            }
            if (_cameraTransform != null)
            {
                _cameraTransform.position = _cameraBasePosition + new Vector3(Mathf.Sin(t * 0.9f) * 0.018f, Mathf.Sin(t * 1.4f) * 0.012f, 0f);
            }

            Keyboard kb = Keyboard.current;
            if (!_loading && kb != null && (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame))
            {
                StartGame();
            }
        }

        public void StartGame()
        {
            if (_loading)
            {
                return;
            }

            _loading = true;
            GameAudio.Instance.Play(GameAudio.Sfx.Bell);
            StartCoroutine(LoadRoutine());
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
            SceneManager.LoadScene(_gameSceneName);
        }

        private IEnumerator CommuterLoop()
        {
            if (_commuterAnimator == null)
            {
                yield break;
            }

            while (true)
            {
                _commuterAnimator.Play("SitDown", 0, 0f);
                GameAudio.Instance.Play(GameAudio.Sfx.GameOver, 0.9f);
                float dropDuration = _dropClip != null ? Mathf.Max(2.2f, _dropClip.length) : 2.8f;
                yield return new WaitForSeconds(dropDuration);
                _commuterAnimator.CrossFade("Walk", 0.25f, 0, 0f);
                yield return new WaitForSeconds(1.8f);
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

        private void BuildWorld()
        {
            var carGo = new GameObject("TitleCar");
            carGo.AddComponent<CarBuilder>();

            var sceneryGo = new GameObject("TitleScenery");
            sceneryGo.AddComponent<Scenery>();

            SpawnCommuter();
            SpawnBriefcase();
            SpawnFloorShadows();
        }

        private void SpawnCommuter()
        {
            GameObject prefab = Resources.Load<GameObject>("Passengers/male02_1");
            if (prefab == null)
            {
                GameObject fallback = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                fallback.name = "TitleSalaryman_Fallback";
                fallback.transform.position = new Vector3(0.08f, 0.65f, -0.75f);
                fallback.transform.localScale = new Vector3(0.42f, 0.78f, 0.42f);
                return;
            }

            GameObject commuter = Instantiate(prefab);
            commuter.name = "TitleSalaryman";
            commuter.transform.SetPositionAndRotation(new Vector3(0.12f, -0.02f, -0.72f), Quaternion.Euler(0f, 168f, 0f));
            commuter.transform.localScale = Vector3.one * 0.72f;

            _commuterAnimator = commuter.GetComponent<Animator>();
            if (_commuterAnimator == null)
            {
                return;
            }

            _commuterAnimator.applyRootMotion = false;
            _commuterAnimator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            OverrideCommuterClips(_commuterAnimator);
        }

        private void OverrideCommuterClips(Animator animator)
        {
            RuntimeAnimatorController baseController = animator.runtimeAnimatorController;
            if (baseController == null || _dropClip == null)
            {
                return;
            }

            var controller = new AnimatorOverrideController(baseController);
            var overrides = new List<KeyValuePair<AnimationClip, AnimationClip>>();
            controller.GetOverrides(overrides);
            for (int i = 0; i < overrides.Count; i++)
            {
                AnimationClip original = overrides[i].Key;
                if (original == null)
                {
                    continue;
                }

                AnimationClip replacement = null;
                if (original.name.Contains("Stand_Trans_SitPiano"))
                {
                    replacement = _dropClip;
                }
                else if (original.name.Contains("OrcHammer") && _collapsedLoopClip != null)
                {
                    replacement = _collapsedLoopClip;
                }

                if (replacement != null)
                {
                    overrides[i] = new KeyValuePair<AnimationClip, AnimationClip>(original, replacement);
                }
            }
            controller.ApplyOverrides(overrides);
            animator.runtimeAnimatorController = controller;
        }

        private void SpawnBriefcase()
        {
            GameObject bag = GameObject.CreatePrimitive(PrimitiveType.Cube);
            bag.name = "DroppedBriefcase";
            bag.transform.position = new Vector3(-0.38f, 0.12f, -0.48f);
            bag.transform.rotation = Quaternion.Euler(4f, 24f, -9f);
            bag.transform.localScale = new Vector3(0.46f, 0.22f, 0.12f);
            var renderer = bag.GetComponent<Renderer>();
            renderer.material.color = new Color(0.08f, 0.075f, 0.065f);
            Destroy(bag.GetComponent<Collider>());

            GameObject handle = GameObject.CreatePrimitive(PrimitiveType.Cube);
            handle.name = "BriefcaseHandle";
            handle.transform.SetParent(bag.transform, false);
            handle.transform.localPosition = new Vector3(0f, 0.68f, 0f);
            handle.transform.localScale = new Vector3(0.42f, 0.16f, 0.24f);
            handle.GetComponent<Renderer>().material.color = new Color(0.025f, 0.023f, 0.021f);
            Destroy(handle.GetComponent<Collider>());
        }

        private void SpawnFloorShadows()
        {
            for (int i = 0; i < 3; i++)
            {
                GameObject shadow = GameObject.CreatePrimitive(PrimitiveType.Cube);
                shadow.name = $"TitleShadow_{i}";
                shadow.transform.position = new Vector3(-0.08f + i * 0.14f, -0.086f, -0.74f + i * 0.06f);
                shadow.transform.localScale = new Vector3(0.72f - i * 0.1f, 0.008f, 0.22f);
                shadow.transform.rotation = Quaternion.Euler(0f, 12f + i * 11f, 0f);
                var renderer = shadow.GetComponent<Renderer>();
                renderer.material.color = new Color(0f, 0f, 0f, 0.25f);
                Destroy(shadow.GetComponent<Collider>());
            }
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

            _cameraTransform = _camera.transform;
            _cameraTransform.SetPositionAndRotation(new Vector3(2.15f, 1.24f, -4.35f), Quaternion.Euler(7f, -22f, 0f));
            _cameraBasePosition = _cameraTransform.position;
            _camera.fieldOfView = 54f;
            _camera.clearFlags = CameraClearFlags.SolidColor;
            _camera.backgroundColor = new Color(0.09f, 0.12f, 0.17f);

            Light light = FindFirstObjectByType<Light>();
            if (light != null)
            {
                light.transform.rotation = Quaternion.Euler(46f, -24f, 8f);
                light.intensity = 2.4f;
                light.color = new Color(1f, 0.95f, 0.84f);
            }
        }

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

            Image cover = CreateImage("Cover", root, Cover);
            Stretch(cover.rectTransform);

            _slashA = CreateSlash("SlashA", root, new Color(0f, 0f, 0f, 0.72f), new Vector2(1220f, 260f), new Vector2(590f, -80f), -13f);
            _slashB = CreateSlash("SlashB", root, new Color(0.95f, 0.45f, 0.15f, 0.92f), new Vector2(1020f, 40f), new Vector2(640f, 128f), -13f);

            BuildStationSign(root);
            BuildTitle(root);
            BuildStartCard(root);
            BuildMovingBand(root);

            Image fadeImage = CreateImage("Fade", root, Color.black);
            Stretch(fadeImage.rectTransform);
            _fade = fadeImage.gameObject.AddComponent<CanvasGroup>();
            _fade.alpha = 0f;
            _fade.blocksRaycasts = false;
        }

        private void BuildStationSign(RectTransform root)
        {
            RectTransform sign = CreateImage("StationSign", root, Paper).rectTransform;
            Anchor(sign, new Vector2(0f, 1f), new Vector2(34f, -32f), new Vector2(520f, 118f));
            sign.pivot = new Vector2(0f, 1f);

            Image line = CreateImage("Line", sign, AccentOrange);
            Anchor(line.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(520f, 10f));

            Text next = CreateText("Next", sign, 22, TextAnchor.MiddleLeft);
            next.text = "つぎは";
            next.color = new Color(Ink.r, Ink.g, Ink.b, 0.72f);
            Anchor(next.rectTransform, new Vector2(0f, 1f), new Vector2(26f, -16f), new Vector2(180f, 32f));

            Text station = CreateText("Station", sign, 42, TextAnchor.MiddleLeft);
            station.text = "空席";
            station.color = Ink;
            station.fontStyle = FontStyle.Bold;
            Anchor(station.rectTransform, new Vector2(0f, 1f), new Vector2(24f, -48f), new Vector2(190f, 54f));

            Text route = CreateText("Route", sign, 20, TextAnchor.MiddleRight);
            route.text = "7:42  通勤快速";
            route.color = new Color(Ink.r, Ink.g, Ink.b, 0.7f);
            Anchor(route.rectTransform, new Vector2(1f, 1f), new Vector2(-20f, -62f), new Vector2(250f, 34f));
        }

        private void BuildTitle(RectTransform root)
        {
            Text title = CreateText("Title", root, 100, TextAnchor.UpperLeft);
            title.text = "座れ！\nサラリーマン！";
            title.fontStyle = FontStyle.Bold;
            title.color = Color.white;
            Anchor(title.rectTransform, new Vector2(0f, 1f), new Vector2(52f, -204f), new Vector2(900f, 250f));

            Text titleShadow = CreateText("TitleShadow", root, 100, TextAnchor.UpperLeft);
            titleShadow.text = title.text;
            titleShadow.fontStyle = FontStyle.Bold;
            titleShadow.color = new Color(0f, 0f, 0f, 0.55f);
            Anchor(titleShadow.rectTransform, new Vector2(0f, 1f), new Vector2(64f, -194f), new Vector2(900f, 250f));
            titleShadow.transform.SetSiblingIndex(title.transform.GetSiblingIndex());

            Text tag = CreateText("Tag", root, 30, TextAnchor.MiddleLeft);
            tag.text = "満員電車サバイバル";
            tag.color = new Color(1f, 1f, 1f, 0.82f);
            Anchor(tag.rectTransform, new Vector2(0f, 1f), new Vector2(60f, -468f), new Vector2(460f, 48f));
        }

        private void BuildStartCard(RectTransform root)
        {
            Image card = CreateImage("StartCard", root, CardBg);
            RectTransform rect = card.rectTransform;
            Anchor(rect, new Vector2(0f, 0f), new Vector2(52f, 58f), new Vector2(560f, 142f));
            rect.pivot = new Vector2(0f, 0f);

            Image accent = CreateImage("Accent", rect, AccentOrange);
            accent.rectTransform.anchorMin = new Vector2(0f, 0f);
            accent.rectTransform.anchorMax = new Vector2(0f, 1f);
            accent.rectTransform.pivot = new Vector2(0f, 0.5f);
            accent.rectTransform.anchoredPosition = Vector2.zero;
            accent.rectTransform.sizeDelta = new Vector2(8f, 0f);

            Text copy = CreateText("Copy", rect, 22, TextAnchor.MiddleLeft);
            copy.text = "席が空いたら、朝はまだ続く。";
            copy.color = new Color(1f, 1f, 1f, 0.68f);
            Anchor(copy.rectTransform, new Vector2(0f, 1f), new Vector2(28f, -18f), new Vector2(360f, 36f));

            var buttonGo = new GameObject("StartButton", typeof(RectTransform), typeof(Image), typeof(Button));
            buttonGo.transform.SetParent(rect, false);
            RectTransform buttonRect = buttonGo.GetComponent<RectTransform>();
            Anchor(buttonRect, new Vector2(0f, 0f), new Vector2(28f, 22f), new Vector2(250f, 58f));
            Image image = buttonGo.GetComponent<Image>();
            image.color = AccentOrange;
            Button button = buttonGo.GetComponent<Button>();
            ColorBlock colors = button.colors;
            colors.highlightedColor = new Color(1f, 0.58f, 0.30f);
            colors.pressedColor = new Color(0.66f, 0.30f, 0.10f);
            button.colors = colors;
            button.onClick.AddListener(StartGame);

            Text buttonText = CreateText("Text", buttonRect, 27, TextAnchor.MiddleCenter);
            buttonText.text = "出勤する";
            buttonText.fontStyle = FontStyle.Bold;
            Stretch(buttonText.rectTransform);

            Text hint = CreateText("Hint", rect, 18, TextAnchor.MiddleLeft);
            hint.text = "ENTER / SPACE";
            hint.color = new Color(1f, 1f, 1f, 0.42f);
            Anchor(hint.rectTransform, new Vector2(0f, 0f), new Vector2(300f, 36f), new Vector2(190f, 30f));
        }

        private void BuildMovingBand(RectTransform root)
        {
            RectTransform mask = CreateImage("TickerMask", root, new Color(0f, 0f, 0f, 0.58f)).rectTransform;
            mask.anchorMin = new Vector2(0f, 1f);
            mask.anchorMax = new Vector2(1f, 1f);
            mask.pivot = new Vector2(0.5f, 1f);
            mask.anchoredPosition = new Vector2(0f, -154f);
            mask.sizeDelta = new Vector2(0f, 36f);

            _trainBand = new GameObject("Ticker", typeof(RectTransform)).GetComponent<RectTransform>();
            _trainBand.SetParent(mask, false);
            _trainBand.sizeDelta = new Vector2(2400f, 36f);
            Text ticker = CreateText("Text", _trainBand, 18, TextAnchor.MiddleCenter);
            ticker.text = "発車します  ドアが閉まります  本日も満員です  発車します  ドアが閉まります  本日も満員です";
            ticker.color = new Color(1f, 1f, 1f, 0.54f);
            ticker.rectTransform.sizeDelta = new Vector2(2400f, 36f);
        }

        private static RectTransform CreateSlash(string name, RectTransform parent, Color color, Vector2 size, Vector2 position, float zRot)
        {
            RectTransform rect = CreateImage(name, parent, color).rectTransform;
            rect.anchorMin = new Vector2(1f, 0.5f);
            rect.anchorMax = new Vector2(1f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            rect.localRotation = Quaternion.Euler(0f, 0f, zRot);
            return rect;
        }

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
