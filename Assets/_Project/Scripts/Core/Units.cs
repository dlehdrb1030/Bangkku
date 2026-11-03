using UnityEngine;

namespace Bangkku.Core
{
    /// <summary>
    /// 단위·좌표 변환 (전체개발계획 5장).
    /// - Unity 1 unit = 1m, JSON = mm 정수
    /// - Y-up. 평면도 X → Unity X, 평면도 Y → Unity -Z
    ///   평면도 이미지는 Y가 아래로 커지고 Unity Top 뷰는 +Z가 위쪽이라, 부호를 뒤집어야
    ///   Top 뷰가 평면도와 같은 모양이 된다 (그대로 두면 방이 거울상이 됨).
    /// mm↔m 변환은 반드시 이 클래스를 거친다. 코드에 0.001 이나 1000 을 직접 쓰지 않는다.
    /// </summary>
    public static class Units
    {
        public const float MM_TO_M = 0.001f;
        public const float M_TO_MM = 1000f;

        public static float MmToM(int mm) => mm * MM_TO_M;

        public static float MmToM(float mm) => mm * MM_TO_M;

        /// <summary>저장용. JSON 규칙(정수 mm)에 맞게 반올림한다.</summary>
        public static int MToMm(float m) => Mathf.RoundToInt(m * M_TO_MM);

        /// <summary>
        /// 평면도 좌표(mm) → Unity 월드 좌표(m). Unity Z = -평면도 Y.
        /// </summary>
        /// <param name="planXmm">평면도 X (mm)</param>
        /// <param name="planYmm">평면도 Y (mm)</param>
        /// <param name="heightM">Unity Y (m). 바닥이면 0</param>
        public static Vector3 PlanToWorld(float planXmm, float planYmm, float heightM = 0f)
        {
            return new Vector3(planXmm * MM_TO_M, heightM, -planYmm * MM_TO_M);
        }

        /// <summary>JSON 의 [x, y] 배열(mm) → Unity 월드 좌표(m).</summary>
        public static Vector3 PlanToWorld(int[] planMm, float heightM = 0f)
        {
            return PlanToWorld(planMm[0], planMm[1], heightM);
        }

        /// <summary>
        /// Unity 월드 좌표(m) → 평면도 좌표(mm 정수). Y(높이)는 버린다.
        /// PlanToWorld 의 역변환이다.
        /// </summary>
        public static Vector2Int WorldToPlan(Vector3 worldM)
        {
            return new Vector2Int(MToMm(worldM.x), MToMm(-worldM.z));
        }
    }
}
