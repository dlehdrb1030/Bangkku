using System.Collections.Generic;
using UnityEngine;

namespace Bangkku.FloorPlan
{
    /// <summary>
    /// 평면도에서 자주 쓰는 위치를 계산한다. 평면도마다 시작 위치·카메라를 손으로 맞추지 않도록 (JSON 교체만으로 동작).
    /// - 현관: 한쪽에만 바닥이 있는 문 (바깥과 이어진 문). 그 문 안쪽에서 방을 바라보는 위치
    /// - 범위: 모든 벽 바깥면을 감싸는 사각형
    /// 좌표는 평면도 mm (X 오른쪽, Y 아래쪽). 월드 변환은 Units.PlanToWorld 를 쓴다.
    /// </summary>
    public static class FloorPlanSpots
    {
        /// <summary>문 양쪽에 바닥이 있는지 볼 때 벽 면에서 떨어진 거리 (mm)</summary>
        const float ProbeMm = 300f;

        /// <summary>
        /// 현관문 안쪽 insideMm 지점과 그 방향(Y축 회전, 도)을 구한다.
        /// 한쪽에만 바닥이 있는 문을 현관으로 보고, 여러 개면 openings 순서상 처음 것. 없으면 false.
        /// </summary>
        public static bool TryGetEntrance(FloorPlanData plan, float insideMm, out Vector2 startMm, out float yawDegrees)
        {
            startMm = default;
            yawDegrees = 0f;
            var doors = ExteriorDoors(plan);
            if (doors.Count == 0) return false;

            var door = doors[0];
            var inward = -door.Outward;
            startMm = door.CenterMm + inward * (door.HalfThicknessMm + insideMm);
            // 현관이 insideMm 보다 얕으면 바닥이 있는 곳까지 당긴다
            if (!IsOnFloor(plan, startMm)) startMm = door.CenterMm + inward * (door.HalfThicknessMm + ProbeMm);
            yawDegrees = YawFromPlanDirection(inward);
            return true;
        }

        /// <summary>바깥과 이어진 문 하나 (한쪽에만 바닥이 있음)</summary>
        public struct ExteriorDoor
        {
            public OpeningData Opening;
            public WallData Wall;

            /// <summary>문 가운데 (벽 중심선 위, mm)</summary>
            public Vector2 CenterMm;

            /// <summary>바닥이 없는 쪽(바깥) 방향 단위 벡터 (평면도 좌표)</summary>
            public Vector2 Outward;

            public float HalfThicknessMm;
        }

        /// <summary>
        /// 한쪽에만 바닥이 있는 문 (현관문처럼 집 바깥과 이어진 문). openings 순서대로.
        /// 양쪽 다 바닥이 있는 문(방·욕실·발코니 문)과 양쪽 다 없는 문은 빠진다
        /// </summary>
        public static List<ExteriorDoor> ExteriorDoors(FloorPlanData plan)
        {
            var result = new List<ExteriorDoor>();
            if (plan?.Openings == null || plan.Walls == null) return result;

            foreach (var opening in plan.Openings)
            {
                if (opening == null || opening.Type != OpeningType.Door) continue;
                var wall = plan.Walls.Find(w => w != null && w.Id == opening.WallId);
                if (wall?.Start == null || wall.End == null) continue;

                var a = new Vector2(wall.Start[0], wall.Start[1]);
                var b = new Vector2(wall.End[0], wall.End[1]);
                float length = Vector2.Distance(a, b);
                if (length <= 0f) continue;

                var along = (b - a) / length;
                var normal = new Vector2(-along.y, along.x);
                var center = a + along * (opening.Offset + opening.Width * 0.5f);
                float half = wall.ThicknessOr(plan.DefaultWallThickness) * 0.5f;

                bool plus = IsOnFloor(plan, center + normal * (half + ProbeMm));
                bool minus = IsOnFloor(plan, center - normal * (half + ProbeMm));
                if (plus == minus) continue; // 양쪽 다 방(욕실·발코니 문)이거나 양쪽 다 바깥

                result.Add(new ExteriorDoor
                {
                    Opening = opening, Wall = wall, CenterMm = center,
                    Outward = plus ? -normal : normal, HalfThicknessMm = half,
                });
            }

            return result;
        }

        /// <summary>평면도 방향 (mm 좌표계) → Unity Y축 회전 (도). 평면도 Y 는 Unity -Z 이므로 (0, 1) 은 180°</summary>
        public static float YawFromPlanDirection(Vector2 planDirection)
        {
            float yaw = Mathf.Atan2(planDirection.x, -planDirection.y) * Mathf.Rad2Deg;
            return yaw < 0f ? yaw + 360f : yaw;
        }

        /// <summary>점이 어느 바닥 다각형 안에 있는지</summary>
        public static bool IsOnFloor(FloorPlanData plan, Vector2 pointMm)
        {
            if (plan?.Floors == null) return false;
            foreach (var floor in plan.Floors)
                if (floor?.Polygon != null && Contains(floor.Polygon, pointMm)) return true;
            return false;
        }

        /// <summary>다각형 안에 있는지 (짝홀 규칙). 변 위의 점은 어느 쪽이든 될 수 있다</summary>
        public static bool Contains(IReadOnlyList<int[]> polygon, Vector2 p)
        {
            bool inside = false;
            for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
            {
                float xi = polygon[i][0], yi = polygon[i][1], xj = polygon[j][0], yj = polygon[j][1];
                if ((yi > p.y) != (yj > p.y) && p.x < (xj - xi) * (p.y - yi) / (yj - yi) + xi)
                    inside = !inside;
            }

            return inside;
        }

        /// <summary>모든 벽 바깥면을 감싸는 사각형 (mm). 벽이 없으면 false</summary>
        public static bool TryGetBounds(FloorPlanData plan, out Vector2 minMm, out Vector2 maxMm)
        {
            minMm = new Vector2(float.MaxValue, float.MaxValue);
            maxMm = new Vector2(float.MinValue, float.MinValue);
            bool any = false;
            if (plan?.Walls == null) return false;

            foreach (var wall in plan.Walls)
            {
                if (wall?.Start == null || wall.End == null) continue;
                float half = wall.ThicknessOr(plan.DefaultWallThickness) * 0.5f;
                foreach (var p in new[] { wall.Start, wall.End })
                {
                    minMm = Vector2.Min(minMm, new Vector2(p[0] - half, p[1] - half));
                    maxMm = Vector2.Max(maxMm, new Vector2(p[0] + half, p[1] + half));
                    any = true;
                }
            }

            return any;
        }

        /// <summary>넓이가 가장 큰 바닥 다각형. 없으면 null</summary>
        public static FloorData LargestFloor(FloorPlanData plan)
        {
            FloorData best = null;
            long bestArea = 0;
            if (plan?.Floors == null) return null;
            foreach (var floor in plan.Floors)
            {
                if (floor?.Polygon == null || floor.Polygon.Count < 3) continue;
                long area = System.Math.Abs(PolygonMesh.SignedArea2(floor.Polygon));
                if (area > bestArea)
                {
                    bestArea = area;
                    best = floor;
                }
            }

            return best;
        }
    }
}
