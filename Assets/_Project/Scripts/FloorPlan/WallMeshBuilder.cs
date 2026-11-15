using System.Collections.Generic;
using Bangkku.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace Bangkku.FloorPlan
{
    public enum WallPieceKind
    {
        Wall,

        /// <summary>창의 유리판 (두께 10mm)</summary>
        Glass,
    }

    /// <summary>
    /// 벽 조각 하나. Unity 좌표(m). 큐브를 Size 로 늘리고 Y축으로 YawDegrees 만큼 돌린 모양이다.
    /// </summary>
    public readonly struct WallPiece
    {
        /// <summary>GameObject 이름 (예: w0_1, win_balcony_glass)</summary>
        public readonly string Name;

        public readonly WallPieceKind Kind;

        /// <summary>조각 중심 (m)</summary>
        public readonly Vector3 Center;

        /// <summary>x = 벽 방향 길이, y = 높이, z = 두께 (m)</summary>
        public readonly Vector3 Size;

        /// <summary>Y축 회전 (도). 큐브의 로컬 +X 가 벽 start → end 방향이 된다</summary>
        public readonly float YawDegrees;

        /// <summary>이 조각이 나온 벽의 id (예: w0)</summary>
        public readonly string WallId;

        /// <summary>그 벽의 치수 신뢰도. 판정 여유값에 쓴다</summary>
        public readonly Confidence Confidence;

        public WallPiece(string name, WallPieceKind kind, Vector3 center, Vector3 size, float yawDegrees,
            string wallId = null, Confidence confidence = ConfidenceExtensions.Default)
        {
            Name = name;
            Kind = kind;
            Center = center;
            Size = size;
            YawDegrees = yawDegrees;
            WallId = wallId;
            Confidence = confidence;
        }
    }

    /// <summary>
    /// 벽 중심선(mm) → 벽 조각(m) (팀장 가이드 4장 "벽 생성").
    /// 1. 벽을 양 끝으로 두께/2 씩 늘려서 모서리에 빈틈이 생기지 않게 한다.
    /// 2. 개구부를 offset 순으로 지나가며 벽을 나눈다.
    ///    개구부 사이 = 전체 높이 조각, 개구부 자리 = 아래(0 ~ sill) + 위(head ~ 천장) 조각, 창이면 가운데에 유리판.
    /// </summary>
    public static class WallMeshBuilder
    {
        /// <summary>유리판 두께 (m)</summary>
        public const float GlassThicknessM = 0.01f;

        /// <summary>이보다 얇은 조각은 만들지 않는다 (개구부가 벽 끝이나 다른 개구부에 딱 붙은 경우)</summary>
        const float MinPieceM = 0.0005f;

        /// <summary>평면도의 모든 벽을 조각으로 계산한다. GameObject 는 만들지 않는다.</summary>
        public static List<WallPiece> ComputePieces(FloorPlanData plan)
        {
            var openingsByWall = new Dictionary<string, List<OpeningData>>();
            foreach (var opening in plan.Openings)
            {
                if (!openingsByWall.TryGetValue(opening.WallId, out var list))
                    openingsByWall[opening.WallId] = list = new List<OpeningData>();
                list.Add(opening);
            }

            var pieces = new List<WallPiece>();
            foreach (var wall in plan.Walls)
            {
                openingsByWall.TryGetValue(wall.Id, out var openings);
                pieces.AddRange(ComputeWallPieces(wall, openings, plan.DefaultWallThickness, plan.CeilingHeight.Value));
            }

            return pieces;
        }

        /// <summary>
        /// 벽 하나를 개구부로 나눈 조각들. 개구부가 범위 안에 있고 서로 겹치지 않는다고 가정한다 (FloorPlanLoader 가 검사).
        /// </summary>
        /// <param name="openings">이 벽의 개구부. null 이면 벽 전체가 조각 하나</param>
        public static List<WallPiece> ComputeWallPieces(WallData wall, IReadOnlyList<OpeningData> openings,
            int defaultThicknessMm, int ceilingHeightMm)
        {
            Vector3 start = Units.PlanToWorld(wall.Start);
            Vector3 dir = Units.PlanToWorld(wall.End) - start;
            float length = dir.magnitude;
            Vector3 axis = dir / length;

            float thickness = Units.MmToM(wall.ThicknessOr(defaultThicknessMm));
            float ceiling = Units.MmToM(ceilingHeightMm);

            // Unity 에서 Y축으로 θ 돌리면 로컬 +X 는 (cos θ, 0, -sin θ) 가 된다
            float yaw = Mathf.Atan2(-axis.z, axis.x) * Mathf.Rad2Deg;

            var pieces = new List<WallPiece>();

            // s0 ~ s1: 벽 start 에서 잰 거리 (m), y0 ~ y1: 바닥에서 높이 (m)
            void Add(string pieceName, WallPieceKind kind, float s0, float s1, float y0, float y1, float depth)
            {
                if (s1 - s0 < MinPieceM || y1 - y0 < MinPieceM) return;
                Vector3 center = start + axis * ((s0 + s1) * 0.5f);
                center.y = (y0 + y1) * 0.5f;
                pieces.Add(new WallPiece(pieceName, kind, center, new Vector3(s1 - s0, y1 - y0, depth), yaw, wall.Id, wall.Confidence));
            }

            void AddWall(float s0, float s1, float y0, float y1) =>
                Add($"{wall.Id}_{pieces.Count}", WallPieceKind.Wall, s0, s1, y0, y1, thickness);

            var sorted = new List<OpeningData>();
            if (openings != null) sorted.AddRange(openings);
            sorted.Sort((a, b) => a.Offset.CompareTo(b.Offset));

            float cursor = -thickness * 0.5f;
            foreach (var o in sorted)
            {
                float a = Units.MmToM(o.Offset);
                float b = Units.MmToM(o.Offset + o.Width);
                float sill = Units.MmToM(o.Sill);
                float head = Units.MmToM(o.Head);

                AddWall(cursor, a, 0f, ceiling);
                AddWall(a, b, 0f, sill);
                AddWall(a, b, head, ceiling);
                if (o.Type == OpeningType.Window)
                    Add($"{o.Id}_glass", WallPieceKind.Glass, a, b, sill, head, GlassThicknessM);

                cursor = b;
            }

            AddWall(cursor, length + thickness * 0.5f, 0f, ceiling);
            return pieces;
        }

        /// <summary>
        /// 문 자리의 벽 두께만큼 바닥 (문턱) 다각형 (mm). 바닥 다각형은 벽 안쪽 면까지라서 이것이 없으면 문 아래가 비어 보인다.
        /// </summary>
        public static List<int[]> ComputeThresholdPolygon(WallData wall, OpeningData door, int defaultThicknessMm)
        {
            double dx = wall.End[0] - wall.Start[0];
            double dy = wall.End[1] - wall.Start[1];
            double length = System.Math.Sqrt(dx * dx + dy * dy);
            double ux = dx / length, uy = dy / length;
            double half = wall.ThicknessOr(defaultThicknessMm) * 0.5;
            double nx = -uy * half, ny = ux * half;

            double ax = wall.Start[0] + ux * door.Offset, ay = wall.Start[1] + uy * door.Offset;
            double bx = wall.Start[0] + ux * (door.Offset + door.Width), by = wall.Start[1] + uy * (door.Offset + door.Width);

            int[] Point(double x, double y) => new[] { (int)System.Math.Round(x), (int)System.Math.Round(y) };
            return new List<int[]> { Point(ax + nx, ay + ny), Point(bx + nx, by + ny), Point(bx - nx, by - ny), Point(ax - nx, ay - ny) };
        }

        /// <summary>
        /// 조각 하나를 상자(MeshRenderer + BoxCollider, Layer = Wall)로 만든다.
        /// 스케일 1 의 실제 크기 메쉬에 m 단위 UV 를 줘서 벽지 텍스처가 늘어나지 않고, 같은 벽의 조각 사이에서 무늬가 이어진다 (BoxMesh).
        /// 유리판도 Layer = Wall 이다 (가구가 창을 뚫고 놓이지 않도록).
        /// 만든 메쉬를 meshes 에 담는다 (평면도를 다시 만들 때 지우도록).
        /// </summary>
        public static GameObject CreatePiece(WallPiece piece, Transform parent, Material material, List<Mesh> meshes = null)
        {
            var go = BoxMesh.CreateObject(piece.Name, parent, piece.Center, Quaternion.Euler(0f, piece.YawDegrees, 0f),
                piece.Size, material, meshes);
            go.layer = Layers.Wall;

            // 좁음 판정이 이 조각의 신뢰도 여유값을 읽는다 (유리판은 그 창이 있는 벽의 신뢰도)
            go.AddComponent<StructureConfidence>().Set(piece.WallId, piece.Confidence);

            if (piece.Kind == WallPieceKind.Glass) go.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
            return go;
        }
    }
}
