using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace TrainSurvival.Game.PlayModeTests
{
    public sealed class SceneTransitionTests
    {
        [UnitySetUp]
        public IEnumerator SetUp()
        {
            Time.timeScale = 1f;
            RunStartContext.ConsumeOpeningDayTransition();
            RunStartContext.ConsumeTitleFadeInTransition();
            yield return null;
        }

        [UnityTest]
        public IEnumerator TitleStartLoadsInGameWithOpeningCutIn()
        {
            yield return LoadScene("Title");
            TitleController title = Object.FindFirstObjectByType<TitleController>();
            Assert.IsNotNull(title);
            GameObject titlePassenger = GameObject.Find("TitleSalaryman");
            Assert.IsNotNull(titlePassenger);
            AssertOptimizedRenderer(titlePassenger.GetComponentInChildren<SkinnedMeshRenderer>(), "TitleSalaryman");

            title.StartGame();
            yield return null;
            Assert.AreEqual("Title", SceneManager.GetActiveScene().name,
                "スタート時のフェードアウトを待たずにシーンが切り替わりました。");
            yield return WaitForScene("InGame", 8f);

            CutInView cutIn = Object.FindFirstObjectByType<CutInView>();
            Assert.IsNotNull(cutIn);
            Assert.IsTrue(cutIn.IsBusy, "Title→InGame で1日目カットインが開始されていません。");
            PassengerActor[] passengers = Object.FindObjectsByType<PassengerActor>(FindObjectsSortMode.None);
            Assert.Greater(passengers.Length, 50, "InGameの乗客を検証できません。");
            foreach (PassengerActor passenger in passengers)
            {
                AssertOptimizedRenderer(
                    passenger.GetComponentInChildren<SkinnedMeshRenderer>(), passenger.name);
            }
            yield return WaitUntilNotBusy(cutIn, 5f);
        }

        [UnityTest]
        public IEnumerator RetryReloadsInGameWithOpeningCutIn()
        {
            yield return LoadScene("InGame");
            RunController run = Object.FindFirstObjectByType<RunController>();
            CutInView previousCutIn = Object.FindFirstObjectByType<CutInView>();
            Assert.IsNotNull(run);
            Assert.IsNotNull(previousCutIn);

            run.Restart();
            yield return null;
            Assert.IsNotNull(previousCutIn, "リトライ時のフェードアウトを待たずに再ロードされました。");
            Assert.IsTrue(previousCutIn.IsSceneTransitioning, "リトライ時の暗転が開始されていません。");
            yield return WaitUntilDestroyed(previousCutIn, 8f);

            CutInView cutIn = Object.FindFirstObjectByType<CutInView>();
            Assert.IsNotNull(cutIn);
            Assert.IsTrue(cutIn.IsBusy, "リトライ後に1日目カットインが開始されていません。");
            yield return WaitUntilNotBusy(cutIn, 5f);
        }

        [UnityTest]
        public IEnumerator ResultReturnLoadsTitleAndConsumesFadeRequest()
        {
            yield return LoadScene("InGame");
            CutInView cutIn = Object.FindFirstObjectByType<CutInView>();
            Assert.IsNotNull(cutIn);

            cutIn.ReturnToTitle();
            yield return null;
            Assert.AreEqual("InGame", SceneManager.GetActiveScene().name,
                "タイトル復帰時のフェードアウトを待たずにシーンが切り替わりました。");
            yield return WaitForScene("Title", 8f);

            TitleController title = Object.FindFirstObjectByType<TitleController>();
            Assert.IsNotNull(title);
            Assert.IsTrue(title.EnteredWithFade, "タイトルが暗転復帰フローで開始されていません。");
            Assert.IsFalse(RunStartContext.ConsumeTitleFadeInTransition(),
                "タイトル側が復帰フェード要求を消費していません。");
            CanvasGroup titleFade = GameObject.Find("Fade")?.GetComponent<CanvasGroup>();
            Assert.IsNotNull(titleFade, "タイトルの復帰フェードがありません。");
            yield return new WaitForSecondsRealtime(0.6f);
            Assert.Less(titleFade.alpha, 0.05f, "タイトルのフェードインが完了していません。");
        }

        private static IEnumerator LoadScene(string sceneName)
        {
            AsyncOperation load = SceneManager.LoadSceneAsync(sceneName);
            while (load != null && !load.isDone)
            {
                yield return null;
            }
            yield return null;
        }

        private static IEnumerator WaitForScene(string sceneName, float timeout)
        {
            float deadline = Time.realtimeSinceStartup + timeout;
            while (SceneManager.GetActiveScene().name != sceneName
                   && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }
            Assert.AreEqual(sceneName, SceneManager.GetActiveScene().name);
            yield return null;
        }

        private static IEnumerator WaitUntilDestroyed(Object previousSceneObject, float timeout)
        {
            float deadline = Time.realtimeSinceStartup + timeout;
            while (previousSceneObject != null && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }
            Assert.IsTrue(previousSceneObject == null, "規定時間内にInGameが再ロードされませんでした。");
            Assert.AreEqual("InGame", SceneManager.GetActiveScene().name);
            yield return null;
        }

        private static IEnumerator WaitUntilNotBusy(CutInView cutIn, float timeout)
        {
            float deadline = Time.realtimeSinceStartup + timeout;
            while (cutIn != null && cutIn.IsBusy && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }
            Assert.IsNotNull(cutIn);
            Assert.IsFalse(cutIn.IsBusy, "カットインが規定時間内に完了しませんでした。");
        }

        private static void AssertOptimizedRenderer(SkinnedMeshRenderer renderer, string objectName)
        {
            Assert.IsNotNull(renderer, $"{objectName}: SkinnedMeshRendererがありません。");
            Assert.AreEqual(1, renderer.sharedMaterials.Length,
                $"{objectName}: 最適化マテリアルへ差し替わっていません。");
            Material material = renderer.sharedMaterial;
            Assert.IsNotNull(material, $"{objectName}: マテリアルがありません。");
            Assert.IsNotNull(material.shader, $"{objectName}: Shaderがありません。");
            Assert.AreEqual("Universal Render Pipeline/Lit", material.shader.name,
                $"{objectName}: URP/Litが割り当てられていません。");
            Assert.IsNotNull(material.GetTexture("_BaseMap"),
                $"{objectName}: パレットテクスチャがありません。");
        }
    }
}
