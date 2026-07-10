using NUnit.Framework;
using UnityEngine;

namespace TrainSurvival.Game.Tests
{
    public sealed class PassengerOptimizationTests
    {
        [Test]
        public void EveryPassengerUsesOneSubMeshAndMaterialAfterBuildBody()
        {
            GameObject[] prefabs = Resources.LoadAll<GameObject>("Passengers");
            Assert.AreEqual(9, prefabs.Length, "乗客プレハブ9種類を検証できません。");

            foreach (GameObject prefab in prefabs)
            {
                var root = new GameObject($"Test_{prefab.name}");
                try
                {
                    var actor = root.AddComponent<PassengerActor>();
                    actor.BuildBody(prefab);

                    SkinnedMeshRenderer renderer = root.GetComponentInChildren<SkinnedMeshRenderer>();
                    Assert.IsNotNull(renderer, $"{prefab.name}: SkinnedMeshRenderer がありません。");
                    Assert.AreEqual(1, renderer.sharedMesh.subMeshCount,
                        $"{prefab.name}: サブメッシュが統合されていません。");
                    Assert.AreEqual(1, renderer.sharedMaterials.Length,
                        $"{prefab.name}: マテリアルが統合されていません。");
                    Assert.AreEqual(renderer.bones.Length, renderer.sharedMesh.bindposes.Length,
                        $"{prefab.name}: ボーンとBindPoseが一致しません。");

                    Animator animator = root.GetComponentInChildren<Animator>();
                    Assert.IsNotNull(animator, $"{prefab.name}: Animator がありません。");
                    Assert.IsNotNull(animator.runtimeAnimatorController,
                        $"{prefab.name}: Animator Controller が失われています。");
                }
                finally
                {
                    Object.DestroyImmediate(root);
                }
            }
        }
    }
}
