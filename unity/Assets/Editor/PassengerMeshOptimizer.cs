using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace TrainSurvival.Editor
{
    /// <summary>
    /// ベタ塗りマテリアルごとに分かれた乗客のサブメッシュを、パレットテクスチャ付きの
    /// 1サブメッシュへ変換する。ボーン、ウェイト、BlendShape はそのまま複製する。
    /// </summary>
    public static class PassengerMeshOptimizer
    {
        private const string PrefabDirectory = "Assets/Resources/Passengers";
        private const string ModelDirectory = "Assets/Floreswa/Models";
        private const string OutputDirectory = "Assets/Resources/PassengerOptimized";
        private const string LegacyOutputDirectory = "Assets/Generated/PassengerOptimized";

        private static readonly string[] PassengerNames =
        {
            "male01_1", "male01_2", "male01_3",
            "male02_1", "male02_2", "male02_3",
            "male03_1", "male03_2", "male03_3",
        };

        private readonly struct MaterialSample
        {
            public readonly Color Albedo;
            public readonly float Metallic;
            public readonly float Smoothness;

            public MaterialSample(Color albedo, float metallic, float smoothness)
            {
                Albedo = albedo;
                Metallic = metallic;
                Smoothness = smoothness;
            }
        }

        [MenuItem("Tools/Train Survival/Optimize Passenger Meshes")]
        public static void RunFromMenu()
        {
            OptimizeAll();
        }

        // -executeMethod TrainSurvival.Editor.PassengerMeshOptimizer.RunBatch 用。
        public static void RunBatch()
        {
            OptimizeAll();
        }

        private static void OptimizeAll()
        {
            DeleteLegacyOutput();
            EnsureOutputDirectory();

            var originalReadableStates = new Dictionary<string, bool>();
            try
            {
                foreach (string passengerName in PassengerNames)
                {
                    string modelPath = ModelPath(passengerName);
                    var importer = AssetImporter.GetAtPath(modelPath) as ModelImporter;
                    if (importer == null)
                    {
                        throw new InvalidOperationException($"ModelImporter が見つかりません: {modelPath}");
                    }

                    originalReadableStates[modelPath] = importer.isReadable;
                    if (!importer.isReadable)
                    {
                        importer.isReadable = true;
                        importer.SaveAndReimport();
                    }
                }

                foreach (string passengerName in PassengerNames)
                {
                    OptimizePassenger(passengerName);
                }

                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                ValidateAll();
                Debug.Log("[PassengerMeshOptimizer] 9種類の乗客を1サブメッシュ・1マテリアルへ変換しました。");
            }
            finally
            {
                foreach (KeyValuePair<string, bool> pair in originalReadableStates)
                {
                    var importer = AssetImporter.GetAtPath(pair.Key) as ModelImporter;
                    if (importer != null && importer.isReadable != pair.Value)
                    {
                        importer.isReadable = pair.Value;
                        importer.SaveAndReimport();
                    }
                }

                AssetDatabase.SaveAssets();
            }
        }

        private static void OptimizePassenger(string passengerName)
        {
            string modelPath = ModelPath(passengerName);
            GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
            SkinnedMeshRenderer sourceRenderer = model != null
                ? model.GetComponentInChildren<SkinnedMeshRenderer>(true)
                : null;
            if (sourceRenderer == null || sourceRenderer.sharedMesh == null)
            {
                throw new InvalidOperationException($"SkinnedMeshRenderer が見つかりません: {modelPath}");
            }

            Mesh sourceMesh = sourceRenderer.sharedMesh;
            Material[] sourceMaterials = sourceRenderer.sharedMaterials;
            if (sourceMesh.subMeshCount != sourceMaterials.Length)
            {
                throw new InvalidOperationException(
                    $"{passengerName}: subMesh={sourceMesh.subMeshCount}, material={sourceMaterials.Length} が一致しません。");
            }

            MaterialSample[] samples = ReadMaterialSamples(passengerName, sourceMaterials);
            Mesh optimizedMesh = MergeSubMeshes(sourceMesh, passengerName, samples.Length);
            Texture2D albedoPalette = CreateAlbedoPalette(passengerName, samples);
            Texture2D maskPalette = CreateMaskPalette(passengerName, samples);
            Material optimizedMaterial = CreateMaterial(passengerName, albedoPalette, maskPalette);

            ReplaceAsset(optimizedMesh, $"{OutputDirectory}/{passengerName}_Mesh.asset");
            ReplaceAsset(albedoPalette, $"{OutputDirectory}/{passengerName}_AlbedoPalette.asset");
            ReplaceAsset(maskPalette, $"{OutputDirectory}/{passengerName}_MaskPalette.asset");
            ReplaceAsset(optimizedMaterial, $"{OutputDirectory}/{passengerName}_Material.mat");

            // Resources/Passengers は外部アセットとしてGit管理外なので、プレハブ自体は元参照へ戻す。
            // 実行時に PassengerActor が Resources/PassengerOptimized の生成物へ差し替える。
            RestoreSourcePrefab(passengerName, sourceRenderer);

            Debug.Log(
                $"[PassengerMeshOptimizer] {passengerName}: " +
                $"subMesh {sourceMesh.subMeshCount}->1, vertex {sourceMesh.vertexCount}->{optimizedMesh.vertexCount}");
        }

        private static MaterialSample[] ReadMaterialSamples(string passengerName, Material[] materials)
        {
            var samples = new MaterialSample[materials.Length];
            for (int i = 0; i < materials.Length; i++)
            {
                Material material = materials[i];
                if (material == null)
                {
                    throw new InvalidOperationException($"{passengerName}: material[{i}] が null です。");
                }

                Texture baseTexture = material.HasProperty("_BaseMap")
                    ? material.GetTexture("_BaseMap")
                    : material.HasProperty("_MainTex") ? material.GetTexture("_MainTex") : null;
                if (baseTexture != null)
                {
                    throw new InvalidOperationException(
                        $"{passengerName}/{material.name}: テクスチャ付き素材はパレット変換できません。");
                }

                Color color = material.HasProperty("_BaseColor")
                    ? material.GetColor("_BaseColor")
                    : material.HasProperty("_Color") ? material.GetColor("_Color") : Color.white;
                if (color.a < 0.999f)
                {
                    throw new InvalidOperationException(
                        $"{passengerName}/{material.name}: 半透明素材は1マテリアルへ統合できません。");
                }

                float metallic = material.HasProperty("_Metallic") ? material.GetFloat("_Metallic") : 0f;
                float smoothness = material.HasProperty("_Smoothness")
                    ? material.GetFloat("_Smoothness")
                    : material.HasProperty("_Glossiness") ? material.GetFloat("_Glossiness") : 0.5f;
                samples[i] = new MaterialSample(color, metallic, smoothness);
            }

            return samples;
        }

        private static Mesh MergeSubMeshes(Mesh source, string passengerName, int paletteSize)
        {
            Vector3[] sourceVertices = source.vertices;
            Vector3[] sourceNormals = source.normals;
            Vector4[] sourceTangents = source.tangents;
            BoneWeight[] sourceWeights = source.boneWeights;

            var vertices = new List<Vector3>(source.vertexCount);
            var normals = new List<Vector3>(source.vertexCount);
            var tangents = new List<Vector4>(source.vertexCount);
            var boneWeights = new List<BoneWeight>(source.vertexCount);
            var paletteUvs = new List<Vector2>(source.vertexCount);
            var triangles = new List<int>();
            var newToOld = new List<int>(source.vertexCount);
            var remap = new Dictionary<ulong, int>(source.vertexCount);

            bool hasNormals = sourceNormals.Length == sourceVertices.Length;
            bool hasTangents = sourceTangents.Length == sourceVertices.Length;
            bool hasWeights = sourceWeights.Length == sourceVertices.Length;

            for (int subMesh = 0; subMesh < source.subMeshCount; subMesh++)
            {
                int[] sourceTriangles = source.GetTriangles(subMesh);
                float paletteU = (subMesh + 0.5f) / paletteSize;
                for (int i = 0; i < sourceTriangles.Length; i++)
                {
                    int sourceIndex = sourceTriangles[i];
                    ulong key = ((ulong)(uint)subMesh << 32) | (uint)sourceIndex;
                    if (!remap.TryGetValue(key, out int targetIndex))
                    {
                        targetIndex = vertices.Count;
                        remap.Add(key, targetIndex);
                        newToOld.Add(sourceIndex);
                        vertices.Add(sourceVertices[sourceIndex]);
                        paletteUvs.Add(new Vector2(paletteU, 0.5f));
                        if (hasNormals) normals.Add(sourceNormals[sourceIndex]);
                        if (hasTangents) tangents.Add(sourceTangents[sourceIndex]);
                        if (hasWeights) boneWeights.Add(sourceWeights[sourceIndex]);
                    }

                    triangles.Add(targetIndex);
                }
            }

            var result = new Mesh
            {
                name = $"{passengerName}_Optimized",
                indexFormat = vertices.Count > ushort.MaxValue ? IndexFormat.UInt32 : IndexFormat.UInt16,
            };
            result.SetVertices(vertices);
            result.SetUVs(0, paletteUvs);
            if (hasNormals) result.SetNormals(normals);
            if (hasTangents) result.SetTangents(tangents);
            if (hasWeights) result.boneWeights = boneWeights.ToArray();
            result.bindposes = source.bindposes;
            result.SetTriangles(triangles, 0, false);
            result.bounds = source.bounds;
            CopyBlendShapes(source, result, newToOld);
            return result;
        }

        private static void CopyBlendShapes(Mesh source, Mesh target, IReadOnlyList<int> newToOld)
        {
            if (source.blendShapeCount == 0)
            {
                return;
            }

            int sourceVertexCount = source.vertexCount;
            var sourceDeltaVertices = new Vector3[sourceVertexCount];
            var sourceDeltaNormals = new Vector3[sourceVertexCount];
            var sourceDeltaTangents = new Vector3[sourceVertexCount];

            for (int shape = 0; shape < source.blendShapeCount; shape++)
            {
                string shapeName = source.GetBlendShapeName(shape);
                int frameCount = source.GetBlendShapeFrameCount(shape);
                for (int frame = 0; frame < frameCount; frame++)
                {
                    source.GetBlendShapeFrameVertices(
                        shape, frame, sourceDeltaVertices, sourceDeltaNormals, sourceDeltaTangents);
                    var deltaVertices = new Vector3[newToOld.Count];
                    var deltaNormals = new Vector3[newToOld.Count];
                    var deltaTangents = new Vector3[newToOld.Count];
                    for (int i = 0; i < newToOld.Count; i++)
                    {
                        int sourceIndex = newToOld[i];
                        deltaVertices[i] = sourceDeltaVertices[sourceIndex];
                        deltaNormals[i] = sourceDeltaNormals[sourceIndex];
                        deltaTangents[i] = sourceDeltaTangents[sourceIndex];
                    }

                    target.AddBlendShapeFrame(
                        shapeName,
                        source.GetBlendShapeFrameWeight(shape, frame),
                        deltaVertices,
                        deltaNormals,
                        deltaTangents);
                }
            }
        }

        private static Texture2D CreateAlbedoPalette(string passengerName, MaterialSample[] samples)
        {
            var texture = NewPaletteTexture($"{passengerName}_AlbedoPalette", samples.Length, false);
            var colors = new Color[samples.Length];
            for (int i = 0; i < samples.Length; i++) colors[i] = samples[i].Albedo;
            texture.SetPixels(colors);
            texture.Apply(false, true);
            return texture;
        }

        private static Texture2D CreateMaskPalette(string passengerName, MaterialSample[] samples)
        {
            var texture = NewPaletteTexture($"{passengerName}_MaskPalette", samples.Length, true);
            var colors = new Color[samples.Length];
            for (int i = 0; i < samples.Length; i++)
            {
                colors[i] = new Color(samples[i].Metallic, 0f, 0f, samples[i].Smoothness);
            }
            texture.SetPixels(colors);
            texture.Apply(false, true);
            return texture;
        }

        private static Texture2D NewPaletteTexture(string textureName, int width, bool linear)
        {
            return new Texture2D(width, 1, TextureFormat.RGBA32, false, linear)
            {
                name = textureName,
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                anisoLevel = 0,
            };
        }

        private static Material CreateMaterial(
            string passengerName, Texture2D albedoPalette, Texture2D maskPalette)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
            {
                throw new InvalidOperationException("Universal Render Pipeline/Lit shader が見つかりません。");
            }

            var material = new Material(shader) { name = $"{passengerName}_Optimized" };
            material.SetTexture("_BaseMap", albedoPalette);
            material.SetColor("_BaseColor", Color.white);
            material.SetTexture("_MetallicGlossMap", maskPalette);
            material.SetFloat("_Metallic", 1f);
            material.SetFloat("_Smoothness", 1f);
            material.EnableKeyword("_METALLICSPECGLOSSMAP");
            return material;
        }

        private static void ValidateAll()
        {
            foreach (string passengerName in PassengerNames)
            {
                string prefabPath = $"{PrefabDirectory}/{passengerName}.prefab";
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                SkinnedMeshRenderer renderer = prefab != null
                    ? prefab.GetComponentInChildren<SkinnedMeshRenderer>(true)
                    : null;
                Animator animator = prefab != null ? prefab.GetComponent<Animator>() : null;
                Mesh optimizedMesh = AssetDatabase.LoadAssetAtPath<Mesh>(
                    $"{OutputDirectory}/{passengerName}_Mesh.asset");
                Material optimizedMaterial = AssetDatabase.LoadAssetAtPath<Material>(
                    $"{OutputDirectory}/{passengerName}_Material.mat");

                if (optimizedMesh == null || optimizedMesh.subMeshCount != 1 || optimizedMaterial == null)
                {
                    throw new InvalidOperationException($"{passengerName}: 1サブメッシュ化の検証に失敗しました。");
                }
                if (renderer == null || renderer.bones.Length == 0
                    || optimizedMesh.bindposes.Length != renderer.bones.Length)
                {
                    throw new InvalidOperationException($"{passengerName}: ボーン参照の検証に失敗しました。");
                }
                if (animator == null || animator.runtimeAnimatorController == null)
                {
                    throw new InvalidOperationException($"{passengerName}: Animator の検証に失敗しました。");
                }
            }
        }

        private static void ReplaceAsset(UnityEngine.Object asset, string path)
        {
            AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(asset, path);
        }

        private static void EnsureOutputDirectory()
        {
            if (!AssetDatabase.IsValidFolder(OutputDirectory))
            {
                AssetDatabase.CreateFolder("Assets/Resources", "PassengerOptimized");
            }
        }

        private static void RestoreSourcePrefab(string passengerName, SkinnedMeshRenderer sourceRenderer)
        {
            string prefabPath = $"{PrefabDirectory}/{passengerName}.prefab";
            GameObject prefabRoot = PrefabUtility.LoadPrefabContents(prefabPath);
            try
            {
                SkinnedMeshRenderer targetRenderer = prefabRoot.GetComponentInChildren<SkinnedMeshRenderer>(true);
                if (targetRenderer == null)
                {
                    throw new InvalidOperationException($"Prefab に SkinnedMeshRenderer がありません: {prefabPath}");
                }

                targetRenderer.sharedMesh = sourceRenderer.sharedMesh;
                targetRenderer.sharedMaterials = sourceRenderer.sharedMaterials;
                PrefabUtility.SaveAsPrefabAsset(prefabRoot, prefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(prefabRoot);
            }
        }

        private static void DeleteLegacyOutput()
        {
            if (AssetDatabase.IsValidFolder(LegacyOutputDirectory))
            {
                AssetDatabase.DeleteAsset(LegacyOutputDirectory);
            }
            if (AssetDatabase.IsValidFolder("Assets/Generated")
                && AssetDatabase.FindAssets(string.Empty, new[] { "Assets/Generated" }).Length == 0)
            {
                AssetDatabase.DeleteAsset("Assets/Generated");
            }
        }

        private static string ModelPath(string passengerName)
        {
            return $"{ModelDirectory}/{passengerName}.fbx";
        }
    }
}
