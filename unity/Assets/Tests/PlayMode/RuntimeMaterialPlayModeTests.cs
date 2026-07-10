using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace TrainSurvival.Game.PlayModeTests
{
    public sealed class RuntimeMaterialPlayModeTests
    {
        [UnityTest]
        public IEnumerator GeneratedCarUsesUrpShadersForEveryRenderer()
        {
            var root = new GameObject("RuntimeMaterialTestCar");
            CarBuilder.OverrideNextCoffeeSpawn(false);
            root.AddComponent<CarBuilder>();
            yield return null;

            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            Assert.Greater(renderers.Length, 0, "車内Rendererが生成されていません。");
            foreach (Renderer renderer in renderers)
            {
                foreach (Material material in renderer.sharedMaterials)
                {
                    Assert.IsNotNull(material, $"{renderer.name} のMaterialがありません。");
                    Assert.IsNotNull(material.shader, $"{renderer.name} のShaderがありません。");
                    StringAssert.StartsWith("Universal Render Pipeline/", material.shader.name,
                        $"{renderer.name} がURP以外のShaderを使用しています。");
                    Assert.IsTrue(material.shader.isSupported,
                        $"{renderer.name} のShader {material.shader.name} が非対応です。");
                }
            }

            Object.Destroy(root);
            yield return null;
        }
    }
}
