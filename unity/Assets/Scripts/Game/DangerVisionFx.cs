using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace TrainSurvival.Game
{
    /// <summary>
    /// 体力の危険度に連動する視界演出（描画専用）。心音と同じ強度（<see cref="StaminaSystem.Danger01"/>）で、
    /// 視界の縁が狭まり（ビネット）・色がにじみ（色収差）・遠くがぼやけ（ガウスDoF）・血の気が引く
    /// （彩度低下）。Volume の weight を 0..1 で振るだけなので、危険でない間はコストゼロに近い。
    /// カメラのポストプロセスは必要な間だけ有効化し、元の設定へ戻す。
    /// </summary>
    public sealed class DangerVisionFx : MonoBehaviour
    {
        [SerializeField] private float _fadeInPerSecond = 0.7f;  // 効果の立ち上がり速度
        [SerializeField] private float _fadeOutPerSecond = 1.4f; // 回復時は速めに晴れる

        private StaminaSystem _stamina;
        private Volume _volume;
        private Camera _camera;
        private UniversalAdditionalCameraData _cameraData;
        private bool _postWasEnabled;
        private bool _postForced;
        private float _weight;

        private void Start()
        {
            _stamina = FindFirstObjectByType<StaminaSystem>();

            // プロファイルはコードで組む（アセット不要・数値はここが唯一の置き場）
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();

            var vignette = profile.Add<Vignette>();
            vignette.active = true;
            vignette.intensity.Override(0.45f);
            vignette.smoothness.Override(0.65f);
            vignette.color.Override(new Color(0.08f, 0f, 0f)); // ほんのり血の色

            var chroma = profile.Add<ChromaticAberration>();
            chroma.active = true;
            chroma.intensity.Override(0.65f);

            var dof = profile.Add<DepthOfField>();
            dof.active = true;
            dof.mode.Override(DepthOfFieldMode.Gaussian);
            dof.gaussianStart.Override(1.2f);  // 手元は読める
            dof.gaussianEnd.Override(9f);      // 車内の奥がぼやける
            dof.highQualitySampling.Override(false);

            var color = profile.Add<ColorAdjustments>();
            color.active = true;
            color.saturation.Override(-45f); // 血の気が引く

            _volume = gameObject.AddComponent<Volume>();
            _volume.isGlobal = true;
            _volume.priority = 100f;
            _volume.profile = profile;
            _volume.weight = 0f;
        }

        private void LateUpdate()
        {
            float target = _stamina != null ? _stamina.Danger01 : 0f;
            float speed = target > _weight ? _fadeInPerSecond : _fadeOutPerSecond;
            _weight = Mathf.MoveTowards(_weight, target, Time.unscaledDeltaTime * speed);
            _volume.weight = _weight;

            EnsureCamera();
            if (_cameraData == null)
            {
                return;
            }

            // 効いている間だけポストプロセスをON（切った瞬間は元の設定へ戻す）
            bool need = _weight > 0.001f;
            if (need && !_postForced)
            {
                _postWasEnabled = _cameraData.renderPostProcessing;
                _cameraData.renderPostProcessing = true;
                _postForced = true;
            }
            else if (!need && _postForced)
            {
                _cameraData.renderPostProcessing = _postWasEnabled;
                _postForced = false;
            }
        }

        private void EnsureCamera()
        {
            if (_camera != null && _camera.isActiveAndEnabled)
            {
                return;
            }
            _camera = Camera.main;
            _cameraData = _camera != null ? _camera.GetUniversalAdditionalCameraData() : null;
            _postForced = false; // カメラが替わったら状態を取り直す
        }
    }
}
