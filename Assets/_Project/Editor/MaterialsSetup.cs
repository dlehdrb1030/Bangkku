using System.Collections.Generic;
using System.Text;
using Bangkku.FloorPlan;
using Bangkku.Placement;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Bangkku.EditorTools
{
    /// <summary>
    /// 전체개발계획 6장 Materials 폴더의 재질을 만들고, 열려 있는 씬의 FloorPlanBuilder 재질 칸을 채운다.
    /// - 재질 파일이 이미 있으면 만들지 않는다 (색을 직접 고친 재질을 덮어쓰지 않음).
    /// - 재질 칸이 이미 채워져 있으면 바꾸지 않는다.
    /// 여러 번 실행해도 결과가 같다. 씬은 바뀐 채로 두므로 Cmd+S 로 저장한다.
    /// </summary>
    public static class MaterialsSetup
    {
        const string MenuPath = "Tools/방꾸/재질 생성·연결";
        const string Folder = "Assets/_Project/Materials";

        struct Spec
        {
            public string Name;
            public Color Color;
            public float Smoothness;
            public float Metallic;
            public bool Transparent;
            public bool Unlit;

            /// <summary>FloorPlanBuilder 의 재질 칸 이름. 연결하지 않는 재질은 null</summary>
            public string BuilderField;
        }

        static readonly Spec[] Specs =
        {
            new() { Name = "Wall", Color = new Color(0.93f, 0.92f, 0.89f), Smoothness = 0.1f, BuilderField = nameof(FloorPlanBuilder.WallMaterial) },
            new() { Name = "Floor", Color = new Color(0.80f, 0.68f, 0.52f), Smoothness = 0.3f, BuilderField = nameof(FloorPlanBuilder.FloorMaterial) },
            new() { Name = "Tile", Color = new Color(0.80f, 0.82f, 0.83f), Smoothness = 0.55f, BuilderField = nameof(FloorPlanBuilder.TileMaterial) },
            new() { Name = "Ceiling", Color = new Color(0.97f, 0.97f, 0.96f), Smoothness = 0.05f, BuilderField = nameof(FloorPlanBuilder.CeilingMaterial) },
            new() { Name = "Fixture", Color = new Color(0.88f, 0.86f, 0.80f), Smoothness = 0.25f, BuilderField = nameof(FloorPlanBuilder.FixtureMaterial) },
            // 고정설비 모델 (FixtureModels): 도기·상판, 손잡이·수전, 어두운 부분
            new() { Name = "Porcelain", Color = new Color(0.95f, 0.95f, 0.94f), Smoothness = 0.85f, BuilderField = nameof(FloorPlanBuilder.PorcelainMaterial) },
            new() { Name = "Metal", Color = new Color(0.78f, 0.79f, 0.80f), Smoothness = 0.7f, Metallic = 0.6f, BuilderField = nameof(FloorPlanBuilder.MetalMaterial) },
            new() { Name = "Dark", Color = new Color(0.16f, 0.16f, 0.17f), Smoothness = 0.6f, BuilderField = nameof(FloorPlanBuilder.DarkMaterial) },
            new() { Name = "Glass", Color = new Color(0.75f, 0.88f, 0.95f, 0.25f), Smoothness = 0.9f, Transparent = true, BuilderField = nameof(FloorPlanBuilder.GlassMaterial) },
            // 걸레받이·문틀·창틀. 벽지보다 조금 밝고 매끈한 도장 (벽과 구분되도록)
            new() { Name = "Trim", Color = new Color(0.97f, 0.97f, 0.96f), Smoothness = 0.5f, BuilderField = nameof(FloorPlanBuilder.TrimMaterial) },
            // 창밖 풍경. 조명을 받지 않는 Unlit. 파노라마 텍스처는 방 텍스처 생성·적용 메뉴가 넣는다
            new() { Name = "Exterior", Color = Color.white, Unlit = true, BuilderField = nameof(FloorPlanBuilder.ExteriorMaterial) },

            // 판정 상태 색 (3주차 윤곽선·상자 표시용). 조명을 받지 않는 단색
            new() { Name = "StateFree", Color = PlacementStateView.FreeColor, Unlit = true },
            new() { Name = "StateTight", Color = PlacementStateView.TightColor, Unlit = true },
            new() { Name = "StateOverlap", Color = PlacementStateView.OverlapColor, Unlit = true },
            new() { Name = "StateOverlapFill", Color = PlacementStateView.OverlapFillColor, Unlit = true, Transparent = true },
        };

        [MenuItem(MenuPath)]
        static void Run()
        {
            if (EditorApplication.isPlaying)
            {
                EditorUtility.DisplayDialog("방꾸", "Play 를 멈춘 뒤 실행하세요. Play 중에 바꾼 씬 설정은 저장되지 않습니다.", "확인");
                return;
            }

            var created = new List<string>();
            var kept = new List<string>();
            var materials = new Dictionary<string, Material>();
            foreach (var spec in Specs)
            {
                string path = $"{Folder}/{spec.Name}.mat";
                var material = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (material != null)
                {
                    kept.Add(spec.Name);
                }
                else
                {
                    material = spec.Unlit
                        ? UrpMaterials.CreateUnlit(spec.Name, spec.Color, spec.Transparent)
                        : UrpMaterials.CreateLit(spec.Name, spec.Color, spec.Smoothness, spec.Transparent, spec.Metallic);
                    if (material == null)
                    {
                        EditorUtility.DisplayDialog("방꾸", "URP 셰이더를 찾지 못했습니다. Console 을 확인하세요.", "확인");
                        return;
                    }

                    AssetDatabase.CreateAsset(material, path);
                    created.Add(spec.Name);
                }

                materials[spec.Name] = material;
            }

            AssetDatabase.SaveAssets();

            int linked = 0;
            var scenes = new HashSet<string>();
            foreach (var builder in Object.FindObjectsByType<FloorPlanBuilder>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                var serialized = new SerializedObject(builder);
                bool changed = false;
                foreach (var spec in Specs)
                {
                    if (spec.BuilderField == null) continue;
                    var property = serialized.FindProperty(spec.BuilderField);
                    if (property == null || property.objectReferenceValue != null) continue;
                    property.objectReferenceValue = materials[spec.Name];
                    changed = true;
                    linked++;
                }

                if (!changed) continue;
                serialized.ApplyModifiedProperties();
                EditorSceneManager.MarkSceneDirty(builder.gameObject.scene);
                scenes.Add(builder.gameObject.scene.name);
            }

            var report = new StringBuilder();
            report.AppendLine($"재질 새로 만듦 {created.Count}개, 이미 있어서 유지 {kept.Count}개 ({Folder})");
            report.AppendLine(linked > 0
                ? $"열린 씬의 재질 칸 {linked}곳 연결 ({string.Join(", ", scenes)}). Cmd+S 로 씬을 저장하세요."
                : "연결할 재질 칸이 없습니다 (열린 씬에 FloorPlanBuilder 가 없거나 이미 채워져 있음).");
            Debug.Log("[방꾸] " + report);
            EditorUtility.DisplayDialog("재질 생성·연결", report.ToString(), "확인");
        }
    }
}
