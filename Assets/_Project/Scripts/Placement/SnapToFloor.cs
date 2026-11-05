using UnityEngine;

namespace Bangkku.Placement
{
    /// <summary>
    /// 가구는 항상 바닥에 붙어 있다: 높이는 바닥(Y = 0)에 고정하고, Y축 회전만 남긴다.
    /// 가구 원점이 바닥면 중앙이므로 transform.position.y = 0 이면 바닥에 놓인 것이다.
    /// </summary>
    public static class SnapToFloor
    {
        /// <summary>평면도 바닥의 월드 높이. 모든 바닥은 Y = 0 에 만든다 (FloorPlanBuilder)</summary>
        public const float FloorY = 0f;

        /// <summary>위치의 높이만 바닥으로 맞춘 값</summary>
        public static Vector3 FloorPosition(Vector3 position, float floorY = FloorY) => new(position.x, floorY, position.z);

        /// <summary>가구를 바닥에 붙이고 기울어짐을 없앤다 (Y 회전은 유지)</summary>
        public static void Apply(Transform furniture, float floorY = FloorY)
        {
            furniture.position = FloorPosition(furniture.position, floorY);
            furniture.rotation = Quaternion.Euler(0f, furniture.eulerAngles.y, 0f);
        }
    }
}
