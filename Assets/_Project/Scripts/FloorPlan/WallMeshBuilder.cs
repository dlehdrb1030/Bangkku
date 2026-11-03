using System.Collections.Generic;
using Bangkku.Core;
using UnityEngine;

namespace Bangkku.FloorPlan
{
    /// <summary>
    /// 벽 조각 하나. Unity 좌표(m). 큐브를 Size 로 늘리고 Y축으로 YawDegrees 만큼 돌린 모양이다.
    /// </summary>
    public readonly struct WallPiece
    {
        /// <summary>GameObject 이름 (벽 id)</summary>
        public readonly string Name;

        /// <summary>조각 중심 (m). y = 높이 / 2</summary>
        public readonly Vector3 Center;

        /// <summary>x = 벽 방향 길이, y = 높이, z = 두께 (m)</summary>
        public readonly Vector3 Size;

        /// <summary>Y축 회전 (도). 큐브의 로컬 +X 가 벽 start → end 방향이 된다</summary>
        public readonly float YawDegrees;

        public WallPiece(string name, Vector3 center, Vector3 size, float yawDegrees)
        {
            Name = name;
            Center = center;
            Size = size;
            YawDegrees = yawDegrees;
        }
    }

    /// <summary>
    /// 벽 중심선(mm) → 벽 조각(m) (팀장 가이드 4장 "벽 생성").
    /// 각 벽을 양 끝으로 두께/2 씩 늘려서 모서리에 빈틈이 생기지 않게 한다.
    /// 지금은 벽 하나 = 조각 하나. 개구부(문·창)로 벽을 나누는 것은 다음 단계에서 추가한다.
    /// </summary>
    public static class WallMeshBuilder
    {
        /// <summary>평면도의 모든 벽을 조각으로 계산한다. GameObject 는 만들지 않는다.</summary>
        public static List<WallPiece> ComputePieces(FloorPlanData plan)
        {
            var pieces = new List<WallPiece>(plan.Walls.Count);
            foreach (var wall in plan.Walls)
                pieces.Add(ComputeSolidPiece(wall, plan.DefaultWallThickness, plan.CeilingHeight.Value));
            return pieces;
        }

        /// <summary>개구부 없이 벽 전체를 조각 하나로 계산한다.</summary>
        public static WallPiece ComputeSolidPiece(WallData wall, int defaultThicknessMm, int ceilingHeightMm)
        {
            Vector3 start = Units.PlanToWorld(wall.Start);
            Vector3 end = Units.PlanToWorld(wall.End);
            Vector3 dir = end - start;

            float thickness = Units.MmToM(wall.ThicknessOr(defaultThicknessMm));
            float height = Units.MmToM(ceilingHeightMm);

            Vector3 center = (start + end) * 0.5f;
            center.y = height * 0.5f;

            // Unity 에서 Y축으로 θ 돌리면 로컬 +X 는 (cos θ, 0, -sin θ) 가 된다
            float yaw = Mathf.Atan2(-dir.z, dir.x) * Mathf.Rad2Deg;

            return new WallPiece(wall.Id, center, new Vector3(dir.magnitude + thickness, height, thickness), yaw);
        }

        /// <summary>조각 하나를 큐브(MeshRenderer + BoxCollider, Layer = Wall)로 만든다.</summary>
        /// <param name="material">null 이면 큐브 기본 재질</param>
        public static GameObject CreatePiece(WallPiece piece, Transform parent, Material material)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = piece.Name;
            go.layer = Layers.Wall;

            var t = go.transform;
            t.SetParent(parent, false);
            t.localPosition = piece.Center;
            t.localRotation = Quaternion.Euler(0f, piece.YawDegrees, 0f);
            t.localScale = piece.Size;

            if (material != null) go.GetComponent<MeshRenderer>().sharedMaterial = material;
            return go;
        }
    }
}
