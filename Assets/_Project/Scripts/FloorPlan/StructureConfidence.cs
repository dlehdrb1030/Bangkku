using Bangkku.Core;
using UnityEngine;

namespace Bangkku.FloorPlan
{
    /// <summary>
    /// 벽 조각·고정설비의 치수 신뢰도. FloorPlanBuilder 가 만들 때 붙인다.
    /// PlacementValidator 의 좁음(Tight) 판정이 맞은편 대상의 여유값(10 / 30 / 100mm)을 이것으로 읽는다.
    /// - 벽 조각, 유리판: 평면도 JSON 의 벽 confidence
    /// - 고정설비: JSON 에 신뢰도 칸이 없어서 estimated (가장 보수적)
    /// </summary>
    [DisallowMultipleComponent]
    public class StructureConfidence : MonoBehaviour
    {
        [Tooltip("이 조각이 나온 벽 id 또는 고정설비 id")]
        public string SourceId;

        public Confidence Confidence = ConfidenceExtensions.Default;

        public void Set(string sourceId, Confidence confidence)
        {
            SourceId = sourceId;
            Confidence = confidence;
        }
    }
}
