using System.Collections.Generic;
using UnityEngine;

namespace Bangkku.Placement
{
    /// <summary>좁음(Tight) 판정 방식. PlacementValidator 인스펙터에서 고른다. 색(상태)만 바뀌고 간격 숫자는 그대로다.</summary>
    public enum TightRule
    {
        /// <summary>
        /// A. 가이드 그대로: 4면 중 어느 면이든 간격 &lt; 기준이면 좁음.
        /// 벽에 딱 붙인 가구(간격 0)도 좁음이 된다.
        /// </summary>
        [InspectorName("A. 가이드 그대로 (면마다 간격)")]
        Guide,

        /// <summary>
        /// B. 맞닿은 면 빼기: A 와 같지만 이미 맞닿은 면(간격 ≤ 맞닿음 허용 오차)은 세지 않는다.
        /// 벽에 붙인 소파는 여유, 벽에서 2cm 띄운 소파는 좁음.
        /// </summary>
        [InspectorName("B. 맞닿은 면 빼기")]
        IgnoreContact,

        /// <summary>
        /// C. 축 방향 여유 (기본): 좌우(±X) 간격의 합, 앞뒤(±Z) 간격의 합이 기준보다 작으면 좁음.
        /// 한쪽이 범위(MaxGapMeters) 안에서 막혀 있지 않으면 그 축은 밀어서 맞출 수 있으므로 좁음이 아니다.
        /// 실제 가구가 신뢰도 여유값만큼 더 크더라도 그 축 안에 들어가는지를 본다.
        /// </summary>
        [InspectorName("C. 축 방향 여유 (좌우 합, 앞뒤 합)")]
        AxisClearance,
    }

    /// <summary>좁음 판정 결과. Value 와 Threshold 는 기준에 가장 가까운(또는 가장 많이 모자란) 면·축의 값이다.</summary>
    public struct TightResult
    {
        public bool IsTight;

        /// <summary>잰 값 (m): A·B 는 한 면의 간격, C 는 한 축의 간격 합. 비교할 것이 없으면 PositiveInfinity</summary>
        public float ValueMeters;

        /// <summary>기준 (m) = max(가구 여유값, 맞은편 대상 여유값, 최소 기준)</summary>
        public float ThresholdMeters;

        /// <summary>어느 면·축인지 (가구 기준 "오른쪽" "왼쪽" "앞" "뒤" / "좌우" "앞뒤"). 비교할 것이 없으면 빈 문자열</summary>
        public string Where;

        /// <summary>true 면 C 의 한 축 (First + Second 간격 합), false 면 한 면 (First 만)</summary>
        public bool IsAxis;

        /// <summary>비교에 쓴 면의 간격과 대상. 축이면 First = +쪽(오른쪽·앞), Second = -쪽(왼쪽·뒤)</summary>
        public PlacementValidator.GapResult First, Second;

        public bool HasValue => !float.IsPositiveInfinity(ValueMeters);

        public static TightResult None => new() { ValueMeters = float.PositiveInfinity, Where = string.Empty };
    }

    /// <summary>
    /// 4면 간격으로 좁음 여부를 정한다. 물리 검사를 하지 않는 순수 계산이라 테스트하기 쉽게 따로 둔다.
    /// 여유값 규칙 (팀장 가이드 4장): margin = max(가구 신뢰도 여유값, 맞은편 대상 신뢰도 여유값), 기준 = max(margin, 최소 기준).
    /// </summary>
    public static class TightJudge
    {
        /// <summary>faces 순서: 가구 로컬 +X, -X, +Z, -Z</summary>
        public const int Right = 0, Left = 1, Front = 2, Back = 3, FaceCount = 4;

        static readonly string[] FaceNames = { "오른쪽", "왼쪽", "앞", "뒤" };

        /// <param name="faces">PlacementValidator 가 잰 4면 간격. Meters 가 PositiveInfinity 면 그 면은 범위 안에 아무것도 없음</param>
        /// <param name="furnitureMarginM">가구 자신의 신뢰도 여유값 (m)</param>
        /// <param name="minThresholdM">최소 기준 (m). 가이드 기준 0.05</param>
        /// <param name="contactToleranceM">B 에서 이 이하 간격은 맞닿음으로 보고 뺀다 (m)</param>
        public static TightResult Evaluate(TightRule rule, IReadOnlyList<PlacementValidator.GapResult> faces,
            float furnitureMarginM, float minThresholdM, float contactToleranceM)
        {
            var result = TightResult.None;
            if (faces == null || faces.Count < FaceCount) return result;

            if (rule == TightRule.AxisClearance)
            {
                ConsiderAxis(faces[Right], faces[Left], "좌우", furnitureMarginM, minThresholdM, ref result);
                ConsiderAxis(faces[Front], faces[Back], "앞뒤", furnitureMarginM, minThresholdM, ref result);
                return result;
            }

            for (int i = 0; i < FaceCount; i++)
            {
                var face = faces[i];
                if (float.IsPositiveInfinity(face.Meters)) continue;
                if (rule == TightRule.IgnoreContact && face.Meters <= contactToleranceM) continue;

                float threshold = Threshold(furnitureMarginM, minThresholdM, face.TargetMarginMeters);
                if (Consider(face.Meters, threshold, FaceNames[i], ref result))
                {
                    result.IsAxis = false;
                    result.First = face;
                    result.Second = PlacementValidator.GapResult.None;
                }
            }

            return result;
        }

        /// <summary>C: 한 축의 양쪽이 모두 범위 안에서 막혀 있을 때만 간격 합을 기준과 비교한다</summary>
        static void ConsiderAxis(PlacementValidator.GapResult plus, PlacementValidator.GapResult minus, string where,
            float furnitureMarginM, float minThresholdM, ref TightResult result)
        {
            if (float.IsPositiveInfinity(plus.Meters) || float.IsPositiveInfinity(minus.Meters)) return;
            float threshold = Mathf.Max(Threshold(furnitureMarginM, minThresholdM, plus.TargetMarginMeters), minus.TargetMarginMeters);
            if (Consider(plus.Meters + minus.Meters, threshold, where, ref result))
            {
                result.IsAxis = true;
                result.First = plus;
                result.Second = minus;
            }
        }

        public static float Threshold(float furnitureMarginM, float minThresholdM, float targetMarginM) =>
            Mathf.Max(Mathf.Max(furnitureMarginM, targetMarginM), minThresholdM);

        /// <summary>기준 대비 여유(value - threshold)가 가장 작은 것을 남긴다. 하나라도 기준보다 작으면 좁음. 바꿨으면 true</summary>
        static bool Consider(float value, float threshold, string where, ref TightResult result)
        {
            bool closer = !result.HasValue || value - threshold < result.ValueMeters - result.ThresholdMeters;
            if (!closer) return false;
            result.ValueMeters = value;
            result.ThresholdMeters = threshold;
            result.Where = where;
            result.IsTight = value < threshold;
            return true;
        }
    }
}
