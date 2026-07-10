using UnityEngine;

namespace TrainSurvival.Game
{
    /// <summary>
    /// 実行時マテリアルの供給元。Resources のテンプレート .mat を複製して返す。
    /// Shader.Find で直接組むとビルドで URP シェーダーの必要バリアント（透明・加算・エミッション）が
    /// 同梱されず真っピンクになるため、必ずアセット化されたテンプレート経由で作る（＝ビルドに
    /// 正しいバリアントが入る）。テンプレートが無い場合だけ Shader.Find へフォールバック（エディタ用保険）。
    /// </summary>
    public static class RuntimeMaterials
    {
        /// <summary>不透明の URP/Lit（吊り革・モデルのURP変換など）。</summary>
        public static Material Lit() => Clone("RuntimeLit", "Universal Render Pipeline/Lit");

        /// <summary>透明アルファブレンドの URP/Lit（窓・ドアのガラス）。</summary>
        public static Material Glass() => Clone("RuntimeLitGlass", "Universal Render Pipeline/Lit");

        /// <summary>エミッション有効の URP/Lit（車窓の灯り・アイテムの発光）。</summary>
        public static Material LitEmissive() => Clone("RuntimeLitEmissive", "Universal Render Pipeline/Lit");

        /// <summary>加算ブレンドの URP/Unlit（ビーコン・席ビームなどの光もの）。</summary>
        public static Material UnlitAdditive() => Clone("RuntimeUnlitAdditive", "Universal Render Pipeline/Unlit");

        /// <summary>URP/Lit の Shader 参照（「既にURPか」の判定用）。</summary>
        public static Shader LitShader
        {
            get
            {
                var template = Resources.Load<Material>("Materials/RuntimeLit");
                return template != null ? template.shader : Shader.Find("Universal Render Pipeline/Lit");
            }
        }

        private static Material Clone(string templateName, string shaderFallback)
        {
            var template = Resources.Load<Material>("Materials/" + templateName);
            if (template != null)
            {
                return new Material(template);
            }
            Debug.LogWarning($"[RuntimeMaterials] テンプレート {templateName} が見つからないため Shader.Find で代用（ビルドではピンクになる恐れ）");
            return new Material(Shader.Find(shaderFallback));
        }
    }
}
