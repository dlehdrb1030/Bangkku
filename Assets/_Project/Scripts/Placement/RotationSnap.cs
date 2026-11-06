using UnityEngine;

namespace Bangkku.Placement
{
    /// <summary>
    /// 가구 회전 규칙 (전체개발계획 5장): Y축만, 기본 90° 단위, 미세 조정 15° 단위.
    /// 데스크톱(Q/E)과 VR(스틱)이 같은 규칙을 쓰도록 한 곳에 둔다.
    /// </summary>
    public static class RotationSnap
    {
        public const float CoarseDegrees = 90f;
        public const float FineDegrees = 15f;

        /// <summary>
        /// 현재 Y 회전에 delta 를 더하고 0 이상 360 미만의 정수 도로 맞춘다.
        /// 90° 단위와 15° 단위를 오가도 소수점 오차가 쌓이지 않는다.
        /// </summary>
        public static float RotateYaw(float currentYawDegrees, float deltaDegrees)
        {
            float yaw = Mathf.Repeat(Mathf.Round(currentYawDegrees + deltaDegrees), 360f);
            return yaw >= 360f ? 0f : yaw;
        }

        /// <summary>임의의 각도를 step 의 배수 중 가장 가까운 값으로 맞춘다 (0 이상 360 미만). 스틱처럼 연속으로 들어오는 회전용</summary>
        public static float SnapYaw(float yawDegrees, float stepDegrees)
        {
            if (stepDegrees <= 0f) return Mathf.Repeat(yawDegrees, 360f);
            float yaw = Mathf.Repeat(Mathf.Round(yawDegrees / stepDegrees) * stepDegrees, 360f);
            return yaw >= 360f ? 0f : yaw;
        }

        /// <summary>가구를 Y축으로 delta 만큼 돌린다. 기울어져 있었더라도 Y 회전만 남긴다.</summary>
        public static void Rotate(Transform furniture, float deltaDegrees)
        {
            furniture.rotation = Quaternion.Euler(0f, RotateYaw(furniture.eulerAngles.y, deltaDegrees), 0f);
        }
    }
}
