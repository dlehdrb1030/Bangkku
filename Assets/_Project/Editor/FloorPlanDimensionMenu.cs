using System.Collections.Generic;
using System.Text;
using Bangkku.FloorPlan;
using UnityEditor;
using UnityEngine;

namespace Bangkku.EditorTools
{
    /// <summary>
    /// 평면도의 두 벽 사이 거리를 재서 도면·실측 치수와 비교한다 (팀장 가이드 2주차, 허용 오차 ±2cm).
    /// Project 창에서 *.dimensions.json 을 선택했으면 그 파일만, 아니면 Data/FloorPlans 의 *.dimensions.json 전부를 검사한다.
    /// 결과는 Console 에 표로 남고, 허용 오차를 넘으면 에러로 표시한다.
    /// </summary>
    public static class FloorPlanDimensionMenu
    {
        const string MenuPath = "Tools/방꾸/평면도 치수 검사";
        const string FloorPlanFolder = "Assets/_Project/Data/FloorPlans";
        const string Suffix = ".dimensions.json";

        [MenuItem(MenuPath)]
        static void Check()
        {
            var targets = new List<TextAsset>();
            foreach (var asset in Selection.GetFiltered<TextAsset>(SelectionMode.Assets))
            {
                if (AssetDatabase.GetAssetPath(asset).EndsWith(Suffix)) targets.Add(asset);
            }

            if (targets.Count == 0)
            {
                foreach (string guid in AssetDatabase.FindAssets("t:TextAsset", new[] { FloorPlanFolder }))
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    if (path.EndsWith(Suffix)) targets.Add(AssetDatabase.LoadAssetAtPath<TextAsset>(path));
                }
            }

            if (targets.Count == 0)
            {
                EditorUtility.DisplayDialog("방꾸", $"{FloorPlanFolder} 에 {Suffix} 파일이 없습니다.", "확인");
                return;
            }

            var summary = new StringBuilder();
            int problems = 0;
            foreach (var asset in targets)
                problems += CheckOne(asset, summary);

            string title = problems == 0 ? "평면도 치수 검사 통과" : $"평면도 치수 검사: 문제 {problems}개";
            EditorUtility.DisplayDialog(title, summary.ToString(), "확인");
        }

        /// <summary>파일 하나를 검사한다. 허용 오차를 넘었거나 잴 수 없는 항목 수를 돌려준다.</summary>
        static int CheckOne(TextAsset asset, StringBuilder summary)
        {
            var file = FloorPlanMeasure.ParseFile(asset.text, out string error);
            if (file == null)
            {
                Debug.LogError($"[방꾸] {asset.name} 을 읽지 못했습니다: {error}", asset);
                summary.AppendLine($"✗ {asset.name}: 읽기 실패 (Console 참고)");
                return 1;
            }

            var planAsset = AssetDatabase.LoadAssetAtPath<TextAsset>($"{FloorPlanFolder}/{file.Floorplan}.json");
            if (planAsset == null)
            {
                Debug.LogError($"[방꾸] {asset.name}: 평면도 {file.Floorplan}.json 이 {FloorPlanFolder} 에 없습니다.", asset);
                summary.AppendLine($"✗ {asset.name}: 평면도 {file.Floorplan} 를 찾을 수 없음");
                return 1;
            }

            var loaded = FloorPlanLoader.Parse(planAsset.text, planAsset.name);
            if (!loaded.Success)
            {
                Debug.LogError($"[방꾸] 평면도 {file.Floorplan} 이 올바르지 않아 치수를 잴 수 없습니다. " +
                               "Tools > 방꾸 > 평면도 JSON 검사 를 먼저 실행하세요.", planAsset);
                summary.AppendLine($"✗ {file.Floorplan}: 평면도 오류 (평면도 JSON 검사 먼저)");
                return 1;
            }

            var results = FloorPlanMeasure.Check(loaded.Data, file);
            int pass = 0, info = 0, bad = 0;
            var log = new StringBuilder();
            log.AppendLine($"[방꾸] 평면도 치수 검사 {file.Floorplan} (허용 오차 ±{file.ToleranceMm} mm)");
            foreach (var r in results)
            {
                switch (r.Status)
                {
                    case DimensionStatus.Pass:
                        pass++;
                        log.AppendLine($"  ✓ {r.Entry.Id}: 기준 {r.Entry.ExpectedMm} / 측정 {r.MeasuredMm:0.#} / 차 {r.DiffMm:+0.#;-0.#;0} mm  ({r.Entry.Label})");
                        break;
                    case DimensionStatus.Fail:
                        bad++;
                        log.AppendLine($"  ✗ {r.Entry.Id}: 기준 {r.Entry.ExpectedMm} / 측정 {r.MeasuredMm:0.#} / 차 {r.DiffMm:+0.#;-0.#;0} mm > ±{r.ToleranceMm}  ({r.Entry.Label})");
                        break;
                    case DimensionStatus.Info:
                        info++;
                        log.AppendLine($"  · {r.Entry.Id}: 측정 {r.MeasuredMm:0.#} mm  ({r.Entry.Label})");
                        break;
                    default:
                        bad++;
                        log.AppendLine($"  ✗ {r.Entry.Id}: 잴 수 없음 - {r.Message}");
                        break;
                }
            }

            if (bad == 0) Debug.Log(log.ToString(), asset);
            else Debug.LogError(log.ToString(), asset);

            summary.AppendLine($"{(bad == 0 ? "✓" : "✗")} {file.Floorplan}: 통과 {pass}, 참고 {info}, 문제 {bad}");
            return bad;
        }
    }
}
