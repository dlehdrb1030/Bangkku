using System.Collections.Generic;
using System.Text;
using Bangkku.FloorPlan;
using UnityEditor;
using UnityEngine;

namespace Bangkku.EditorTools
{
    /// <summary>
    /// 평면도 JSON 을 씬 없이 검사한다. Project 창에서 .json 을 선택했으면 그 파일만,
    /// 아니면 Data/FloorPlans 폴더의 .json 전부를 FloorPlanLoader 로 읽어 본다.
    /// </summary>
    public static class FloorPlanCheckMenu
    {
        const string MenuPath = "Tools/방꾸/평면도 JSON 검사";
        const string FloorPlanFolder = "Assets/_Project/Data/FloorPlans";

        [MenuItem(MenuPath)]
        static void Check()
        {
            var targets = new List<TextAsset>();
            foreach (var asset in Selection.GetFiltered<TextAsset>(SelectionMode.Assets))
            {
                if (AssetDatabase.GetAssetPath(asset).EndsWith(".json")) targets.Add(asset);
            }

            if (targets.Count == 0)
            {
                foreach (string guid in AssetDatabase.FindAssets("t:TextAsset", new[] { FloorPlanFolder }))
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    if (path.EndsWith(".json")) targets.Add(AssetDatabase.LoadAssetAtPath<TextAsset>(path));
                }
            }

            if (targets.Count == 0)
            {
                EditorUtility.DisplayDialog("방꾸", $"{FloorPlanFolder} 에 .json 파일이 없습니다.", "확인");
                return;
            }

            var report = new StringBuilder();
            int failed = 0;
            foreach (var asset in targets)
            {
                var result = FloorPlanLoader.Parse(asset.text, asset.name);
                foreach (string warning in result.Warnings)
                    Debug.LogWarning($"[방꾸] 평면도 {asset.name}: {warning}", asset);

                if (result.Success)
                {
                    Debug.Log($"[방꾸] 평면도 검사 통과 {result.Summary()}", asset);
                    report.AppendLine($"✓ {result.Summary()}");
                }
                else
                {
                    failed++;
                    Debug.LogError($"[방꾸] 평면도 {asset.name} 오류 {result.Errors.Count}개\n- " +
                                   string.Join("\n- ", result.Errors), asset);
                    report.AppendLine($"✗ {asset.name}: 오류 {result.Errors.Count}개 (Console 참고)");
                }

                if (result.Warnings.Count > 0)
                    report.AppendLine($"   경고 {result.Warnings.Count}개 (Console 참고)");
            }

            string title = failed == 0 ? "평면도 JSON 검사 통과" : $"평면도 JSON 오류 ({failed}개 파일)";
            EditorUtility.DisplayDialog(title, report.ToString(), "확인");
        }
    }
}
