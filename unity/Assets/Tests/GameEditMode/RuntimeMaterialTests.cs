using NUnit.Framework;
using UnityEngine;

namespace TrainSurvival.Game.Tests
{
    public sealed class RuntimeMaterialTests
    {
        [TestCase("RuntimeLit", "Universal Render Pipeline/Lit")]
        [TestCase("RuntimeLitGlass", "Universal Render Pipeline/Lit")]
        [TestCase("RuntimeLitEmissive", "Universal Render Pipeline/Lit")]
        [TestCase("RuntimeUnlitAdditive", "Universal Render Pipeline/Unlit")]
        public void RuntimeTemplateUsesExpectedUrpShader(string templateName, string shaderName)
        {
            Material material = Resources.Load<Material>($"Materials/{templateName}");
            Assert.IsNotNull(material, $"{templateName}.mat がResourcesにありません。");
            Assert.IsNotNull(material.shader, $"{templateName}.mat のShader参照がありません。");
            Assert.AreEqual(shaderName, material.shader.name);
            Assert.IsTrue(material.shader.isSupported, $"{shaderName} が現在のRender Pipelineで非対応です。");
        }
    }
}
