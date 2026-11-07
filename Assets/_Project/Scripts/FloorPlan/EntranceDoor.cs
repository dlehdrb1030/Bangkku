using System.Collections.Generic;
using Bangkku.Core;
using UnityEngine;

namespace Bangkku.FloorPlan
{
    /// <summary>닫힌 현관문 문짝 하나와 안쪽 손잡이. Unity 좌표(m), Y축 회전(도). 상자 모양</summary>
    public readonly struct DoorLeaf
    {
        public readonly string Name;
        public readonly Vector3 Center;

        /// <summary>x = 문 폭, y = 높이, z = 두께 (m)</summary>
        public readonly Vector3 Size;

        public readonly float YawDegrees;
        public readonly Vector3 HandleCenter;
        public readonly Vector3 HandleSize;

        /// <summary>문이 있는 벽 (판정 신뢰도)</summary>
        public readonly WallData Wall;

        public DoorLeaf(string name, Vector3 center, Vector3 size, float yawDegrees, Vector3 handleCenter, Vector3 handleSize, WallData wall)
        {
            Name = name;
            Center = center;
            Size = size;
            YawDegrees = yawDegrees;
            HandleCenter = handleCenter;
            HandleSize = handleSize;
            Wall = wall;
        }
    }

    /// <summary>
    /// 바깥과 이어진 문(한쪽에만 바닥이 있는 문, 보통 현관문)을 닫힌 문짝으로 막는다.
    /// 문 밖에는 바닥이 없어 VR 에서 걸어 나가면 아래로 떨어지고, 창밖 풍경이 문으로 보이면 집이 공중에 뜬 것처럼 보이기 때문.
    /// - 문짝: 문틀 안쪽 폭 × (문 높이 - 문틀), 두께 40mm, 벽 바깥면에 붙음 (안에서 보면 벽 두께만큼 들어가 보임)
    /// - Layer Wall + 벽 신뢰도 → 벽처럼 막고, 가구 판정·거리 라벨도 벽으로 본다 (닫힌 문 앞에 가구를 두면 좁음·겹침)
    /// - 안쪽 면에 레버 손잡이 (보이기만)
    /// 방·욕실·발코니 문처럼 양쪽에 바닥이 있는 문은 지금처럼 열린 채로 둔다.
    /// </summary>
    public static class EntranceDoor
    {
        public const int LeafThicknessMm = 40;

        /// <summary>손잡이 높이 (바닥에서 mm)</summary>
        public const int HandleHeightMm = 1000;

        public static List<DoorLeaf> ComputeLeaves(FloorPlanData plan)
        {
            var result = new List<DoorLeaf>();
            foreach (var door in FloorPlanSpots.ExteriorDoors(plan))
            {
                var o = door.Opening;
                var w = door.Wall;
                var a = new Vector2(w.Start[0], w.Start[1]);
                var along = (new Vector2(w.End[0], w.End[1]) - a).normalized;
                var outward = door.Outward;
                float half = door.HalfThicknessMm;
                float lining = TrimBuilder.LiningMm;

                // 벽 방향 s (mm, 벽 start 기준), 바깥 방향 n (mm, 벽 중심선 기준)
                float s0 = o.Offset + lining, s1 = o.Offset + o.Width - lining;
                float top = o.Head - lining;
                if (s1 - s0 <= 0f || top <= 0f) continue;
                float n0 = half - LeafThicknessMm, n1 = half;

                var center = Point(a, along, outward, (s0 + s1) * 0.5f, (n0 + n1) * 0.5f, top * 0.5f);
                var size = new Vector3(Units.MmToM(s1 - s0), Units.MmToM(top), Units.MmToM(LeafThicknessMm));

                // 손잡이: 문 끝(s1) 쪽, 안쪽 면에서 50mm 튀어나온 레버
                const float leverLength = 140f, leverHeight = 20f, leverDepth = 50f;
                float hs = s1 - 60f - leverLength * 0.5f;
                float hy = Mathf.Min(HandleHeightMm, top - 100f);
                var handleCenter = Point(a, along, outward, hs, n0 - leverDepth * 0.5f, hy);
                var handleSize = new Vector3(Units.MmToM(leverLength), Units.MmToM(leverHeight), Units.MmToM(leverDepth));

                // WallMeshBuilder 와 같은 식: 로컬 +X 가 벽 start → end 방향
                float yaw = Mathf.Atan2(along.y, along.x) * Mathf.Rad2Deg;
                result.Add(new DoorLeaf(o.Id + "_leaf", center, size, yaw, handleCenter, handleSize, w));
            }

            return result;
        }

        /// <summary>벽 위 (s, 바깥쪽 n) 점, 높이 y (모두 mm) → Unity 좌표 (m)</summary>
        static Vector3 Point(Vector2 start, Vector2 along, Vector2 outward, float s, float n, float y)
        {
            var p = start + along * s + outward * n;
            return Units.PlanToWorld(p.x, p.y, Units.MmToM(y));
        }
    }
}
