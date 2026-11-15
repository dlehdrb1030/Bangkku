using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Bangkku.EditorTools
{
    /// <summary>
    /// 메뉴 Tools > 방꾸 > 방 텍스처 생성·적용
    /// 1. ProceduralTextures 로 마루·타일·벽지·천장 텍스처(색 + 노멀맵)를 그려 Materials/Textures/ 에 PNG 로 저장한다 (같은 결과가 나오므로 여러 번 실행해도 됨)
    /// 2. Materials 의 Floor·Tile·Wall·Ceiling 재질에 Base Map·Normal Map 을 넣고 Tiling 을 텍스처 실제 크기(m)에 맞춘다 (UV 1 = 1m)
    /// 3. 창밖 풍경 파노라마(ExteriorPanorama)를 그려 Exterior 재질에 넣는다 (가로 = 한 바퀴)
    /// 재질의 Base Map 에 다른 텍스처(직접 넣은 사진 등)가 이미 있으면 그 재질은 건드리지 않는다.
    /// 재질이 없으면 먼저 Tools > 방꾸 > 재질 생성·연결 을 실행한다.
    /// </summary>
    public static class RoomTexturesMenu
    {
        const string MenuPath = "Tools/방꾸/방 텍스처 생성·적용";
        const string MaterialFolder = "Assets/_Project/Materials";
        const string TextureFolder = MaterialFolder + "/Textures";

        struct Spec
        {
            public string Material;
            public string Texture;
            public System.Func<ProceduralTextures.Result> Generate;
            public float NormalStrength;
            public float BumpScale;
            public float Smoothness;
        }

        static readonly Spec[] Specs =
        {
            new() { Material = "Floor", Texture = "WoodFloor", Generate = () => ProceduralTextures.Wood(1024), NormalStrength = 6f, BumpScale = 0.6f, Smoothness = 0.35f },
            new() { Material = "Tile", Texture = "Tile", Generate = () => ProceduralTextures.Tile(1024), NormalStrength = 8f, BumpScale = 0.8f, Smoothness = 0.6f },
            new() { Material = "Wall", Texture = "Wallpaper", Generate = () => ProceduralTextures.Wallpaper(512), NormalStrength = 2f, BumpScale = 0.3f, Smoothness = 0.15f },
            new() { Material = "Ceiling", Texture = "Ceiling", Generate = () => ProceduralTextures.Ceiling(512), NormalStrength = 1.5f, BumpScale = 0.2f, Smoothness = 0.05f },
        };

        [MenuItem(MenuPath)]
        static void Run()
        {
            if (EditorApplication.isPlaying)
            {
                EditorUtility.DisplayDialog("방꾸", "Play 를 멈춘 뒤 실행하세요.", "확인");
                return;
            }

            Directory.CreateDirectory(TextureFolder);
            var applied = new List<string>();
            var skipped = new List<string>();
            var missing = new List<string>();

            try
            {
                for (int i = 0; i < Specs.Length; i++)
                {
                    var spec = Specs[i];
                    EditorUtility.DisplayProgressBar("방 텍스처 생성", spec.Texture, i / (float)Specs.Length);

                    var result = spec.Generate();
                    string albedoPath = $"{TextureFolder}/{spec.Texture}_Albedo.png";
                    string normalPath = $"{TextureFolder}/{spec.Texture}_Normal.png";
                    WritePng(albedoPath, result.Albedo, result.Size, result.Size);
                    WritePng(normalPath, ProceduralTextures.NormalFromHeight(result.Height, result.Size, spec.NormalStrength), result.Size, result.Size);
                    ConfigureImporter(albedoPath, false);
                    ConfigureImporter(normalPath, true);

                    var material = AssetDatabase.LoadAssetAtPath<Material>($"{MaterialFolder}/{spec.Material}.mat");
                    if (material == null)
                    {
                        missing.Add(spec.Material);
                        continue;
                    }

                    var albedo = AssetDatabase.LoadAssetAtPath<Texture2D>(albedoPath);
                    var normal = AssetDatabase.LoadAssetAtPath<Texture2D>(normalPath);
                    var current = material.GetTexture("_BaseMap");
                    if (current != null && current != albedo)
                    {
                        skipped.Add($"{spec.Material} (Base Map 에 {current.name} 이 있음)");
                        continue;
                    }

                    Apply(material, albedo, normal, result.Meters, spec);
                    applied.Add($"{spec.Material} ← {spec.Texture} ({result.Meters}m 한 장)");
                }

                EditorUtility.DisplayProgressBar("방 텍스처 생성", "창밖 풍경", 0.95f);
                ApplyExterior(applied, skipped, missing);
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            AssetDatabase.SaveAssets();

            var report = new StringBuilder();
            report.AppendLine($"텍스처 저장: {TextureFolder}");
            if (applied.Count > 0) report.AppendLine("적용: " + string.Join(", ", applied));
            if (skipped.Count > 0) report.AppendLine("건너뜀: " + string.Join(", ", skipped));
            if (missing.Count > 0) report.AppendLine($"재질 없음: {string.Join(", ", missing)} → Tools > 방꾸 > 재질 생성·연결 을 먼저 실행하세요.");
            Debug.Log("[방꾸] 방 텍스처 " + report);
            EditorUtility.DisplayDialog("방 텍스처 생성·적용", report.ToString(), "확인");
        }

        /// <summary>
        /// 창밖 풍경 파노라마(ExteriorPanorama)를 그려 Exterior 재질(Unlit)에 넣는다.
        /// 가로는 한 바퀴라 반복(Repeat), 세로는 원통 위아래 끝 색이 번지지 않게 Clamp
        /// </summary>
        static void ApplyExterior(List<string> applied, List<string> skipped, List<string> missing)
        {
            var result = ExteriorPanorama.Generate(PanoramaWidth, PanoramaHeight);
            string path = $"{TextureFolder}/Exterior_Panorama.png";
            WritePng(path, result.Pixels, result.Width, result.Height);
            if (AssetImporter.GetAtPath(path) is TextureImporter importer)
            {
                importer.textureType = TextureImporterType.Default;
                importer.sRGBTexture = true;
                importer.wrapModeU = TextureWrapMode.Repeat;
                importer.wrapModeV = TextureWrapMode.Clamp;
                importer.mipmapEnabled = true;
                importer.filterMode = FilterMode.Trilinear;
                importer.anisoLevel = 4;
                importer.maxTextureSize = Mathf.Max(PanoramaWidth, PanoramaHeight); // 기본 2048 이면 가로가 줄어든다
                importer.alphaSource = TextureImporterAlphaSource.None;
                importer.SaveAndReimport();
            }

            var material = AssetDatabase.LoadAssetAtPath<Material>($"{MaterialFolder}/Exterior.mat");
            if (material == null)
            {
                missing.Add("Exterior");
                return;
            }

            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            var current = material.GetTexture("_BaseMap");
            if (current != null && current != texture)
            {
                skipped.Add($"Exterior (Base Map 에 {current.name} 이 있음)");
                return;
            }

            Undo.RecordObject(material, "창밖 풍경 적용");
            material.SetColor("_BaseColor", Color.white);
            material.SetTexture("_BaseMap", texture);
            material.SetTextureScale("_BaseMap", Vector2.one);
            if (material.HasProperty("_MainTex"))
            {
                material.SetTexture("_MainTex", texture);
                material.SetTextureScale("_MainTex", Vector2.one);
            }

            EditorUtility.SetDirty(material);
            applied.Add("Exterior ← 창밖 풍경 파노라마");
        }

        const int PanoramaWidth = 4096;
        const int PanoramaHeight = 512;

        static void WritePng(string path, Color32[] pixels, int width, int height)
        {
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            try
            {
                texture.SetPixels32(pixels);
                texture.Apply();
                File.WriteAllBytes(path, texture.EncodeToPNG());
            }
            finally
            {
                Object.DestroyImmediate(texture);
            }

            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        }

        /// <summary>반복(Repeat), 밉맵, 비스듬히 봐도 흐려지지 않게 Aniso 8 (VR 에서 바닥을 낮은 각도로 봄)</summary>
        static void ConfigureImporter(string path, bool normalMap)
        {
            if (AssetImporter.GetAtPath(path) is not TextureImporter importer) return;
            importer.textureType = normalMap ? TextureImporterType.NormalMap : TextureImporterType.Default;
            importer.sRGBTexture = !normalMap;
            importer.wrapMode = TextureWrapMode.Repeat;
            importer.mipmapEnabled = true;
            importer.filterMode = FilterMode.Trilinear;
            importer.anisoLevel = 8;
            importer.alphaSource = TextureImporterAlphaSource.None;
            importer.SaveAndReimport();
        }

        static void Apply(Material material, Texture2D albedo, Texture2D normal, float meters, Spec spec)
        {
            Undo.RecordObject(material, "방 텍스처 적용");
            var tiling = Vector2.one / meters;

            // 색은 텍스처가 정하므로 Base Color 는 흰색
            material.SetColor("_BaseColor", Color.white);
            material.SetTexture("_BaseMap", albedo);
            material.SetTextureScale("_BaseMap", tiling);
            if (material.HasProperty("_MainTex"))
            {
                material.SetTexture("_MainTex", albedo);
                material.SetTextureScale("_MainTex", tiling);
            }

            material.SetTexture("_BumpMap", normal);
            material.SetFloat("_BumpScale", spec.BumpScale);
            material.EnableKeyword("_NORMALMAP");
            material.SetFloat("_Smoothness", spec.Smoothness);
            EditorUtility.SetDirty(material);
        }
    }
}
