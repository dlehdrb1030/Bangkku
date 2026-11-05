using System.Collections.Generic;
using Bangkku.FloorPlan;
using UnityEngine;

namespace Bangkku.Placement
{
    /// <summary>바닥에서 내려다본 벽 조각 하나 (월드 XZ, m). 가구가 붙을 수 있는 면을 가진다.</summary>
    public readonly struct WallRect
    {
        public readonly Vector2 Center;

        /// <summary>벽이 뻗은 방향 (단위 벡터)</summary>
        public readonly Vector2 Along;

        /// <summary>벽 면에 수직인 방향 (단위 벡터)</summary>
        public readonly Vector2 Normal;

        public readonly float HalfLength;
        public readonly float HalfThickness;

        public WallRect(Vector2 center, float yawDegrees, float length, float thickness)
        {
            // Unity 에서 Y축으로 θ 돌리면 로컬 X 는 (cos θ, -sin θ), 로컬 Z 는 (sin θ, cos θ) (x, z 순서)
            float r = yawDegrees * Mathf.Deg2Rad;
            Center = center;
            Along = new Vector2(Mathf.Cos(r), -Mathf.Sin(r));
            Normal = new Vector2(Mathf.Sin(r), Mathf.Cos(r));
            HalfLength = length * 0.5f;
            HalfThickness = thickness * 0.5f;
        }
    }

    /// <summary>
    /// 가구 위치 스냅 (팀장 가이드 2주차): 10mm 격자에 맞추고, 가구 가장자리가 벽에서 30mm 이내면 벽에 붙인다.
    /// 계산은 바닥에서 본 2D(월드 XZ, m)로 하며 회전한 가구도 처리한다.
    /// 겹침 판정은 하지 않는다: 이미 벽과 겹친 가구는 건드리지 않는다 (판정은 PlacementValidator).
    /// </summary>
    public static class PositionSnap
    {
        public const int GridMm = 10;
        public const int WallThresholdMm = 30;

        /// <summary>벽에 붙인 뒤 다시 다른 벽에 붙을 수 있는 횟수 (모서리에서 두 벽)</summary>
        const int MaxWallPasses = 4;

        /// <summary>이보다 작은 틈은 이미 붙어 있다고 본다 (m)</summary>
        const float TouchingM = 0.0001f;

        /// <summary>벽 길이 방향으로 이만큼은 겹쳐야 같은 벽 옆에 있다고 본다 (m)</summary>
        const float MinAlongOverlapM = 0.001f;

        /// <summary>
        /// 바닥에 놓인 벽 조각만 골라 WallRect 로 만든다. 문 위 벽(바닥에서 떠 있음)과 창 유리는 가구를 막지 않으므로 뺀다.
        /// 창 아래 벽(바닥~창턱)은 가구를 막으므로 넣는다.
        /// </summary>
        public static List<WallRect> WallsFromPieces(IEnumerable<WallPiece> pieces)
        {
            var walls = new List<WallRect>();
            foreach (var piece in pieces)
            {
                if (piece.Kind != WallPieceKind.Wall) continue;
                float bottom = piece.Center.y - piece.Size.y * 0.5f;
                if (bottom > 0.001f) continue;
                walls.Add(new WallRect(new Vector2(piece.Center.x, piece.Center.z), piece.YawDegrees, piece.Size.x, piece.Size.z));
            }

            return walls;
        }

        /// <summary>값을 격자(mm)의 배수로 맞춘다 (m). gridMm 이 0 이하면 그대로</summary>
        public static float SnapGrid(float meters, int gridMm)
        {
            if (gridMm <= 0) return meters;
            return Mathf.Round(meters * 1000f / gridMm) * gridMm * 0.001f;
        }

        public static Vector2 SnapGrid(Vector2 xz, int gridMm) => new(SnapGrid(xz.x, gridMm), SnapGrid(xz.y, gridMm));

        /// <summary>1mm 단위로 반올림 (layout.json 은 정수 mm 로 저장한다)</summary>
        public static Vector2 RoundMm(Vector2 xz) => new(Mathf.Round(xz.x * 1000f) * 0.001f, Mathf.Round(xz.y * 1000f) * 0.001f);

        /// <summary>격자와 벽 스냅을 차례로 적용한다. gridMm 또는 wallThresholdMm 이 0 이하면 그 스냅은 건너뛴다.</summary>
        /// <param name="center">가구 바닥면 중앙 (월드 XZ, m)</param>
        /// <param name="halfSize">가구 가로·세로 절반 (x = W/2, y = D/2, m)</param>
        /// <param name="yawDegrees">가구 Y 회전</param>
        public static Vector2 Apply(Vector2 center, Vector2 halfSize, float yawDegrees, IReadOnlyList<WallRect> walls,
            int gridMm, int wallThresholdMm)
        {
            if (gridMm > 0) center = SnapGrid(center, gridMm);
            if (wallThresholdMm > 0 && walls != null)
                center = SnapToWalls(center, halfSize, yawDegrees, walls, wallThresholdMm * 0.001f);
            return RoundMm(center);
        }

        /// <summary>
        /// 가구 가장자리와 벽 면 사이 틈이 0 초과 thresholdM 이하이면 틈이 0 이 되도록 벽 쪽으로 옮긴다.
        /// 모서리에서는 두 벽 모두에 붙는다.
        /// </summary>
        public static Vector2 SnapToWalls(Vector2 center, Vector2 halfSize, float yawDegrees, IReadOnlyList<WallRect> walls, float thresholdM)
        {
            float r = yawDegrees * Mathf.Deg2Rad;
            var furnitureX = new Vector2(Mathf.Cos(r), -Mathf.Sin(r)); // 가구 가로(W) 방향
            var furnitureZ = new Vector2(Mathf.Sin(r), Mathf.Cos(r)); // 가구 세로(D) 방향

            for (int pass = 0; pass < MaxWallPasses; pass++)
            {
                float bestGap = float.MaxValue;
                Vector2 bestMove = Vector2.zero;

                foreach (var wall in walls)
                {
                    var offset = center - wall.Center;

                    float distance = Vector2.Dot(offset, wall.Normal);
                    float gap = Mathf.Abs(distance) - (wall.HalfThickness + Extent(wall.Normal, halfSize, furnitureX, furnitureZ));
                    if (gap <= TouchingM || gap > thresholdM || gap >= bestGap) continue;

                    // 같은 벽 옆에 있어야 한다: 벽 길이 방향으로 가구와 벽이 겹쳐야 함
                    float along = Vector2.Dot(offset, wall.Along);
                    float alongRadius = Extent(wall.Along, halfSize, furnitureX, furnitureZ);
                    float overlap = Mathf.Min(along + alongRadius, wall.HalfLength) - Mathf.Max(along - alongRadius, -wall.HalfLength);
                    if (overlap < MinAlongOverlapM) continue;

                    bestGap = gap;
                    bestMove = -Mathf.Sign(distance) * gap * wall.Normal;
                }

                if (bestGap == float.MaxValue) break;
                center += bestMove;
            }

            return center;
        }

        /// <summary>가구(회전한 직사각형)를 axis 방향으로 투영했을 때 중심에서 끝까지의 길이</summary>
        static float Extent(Vector2 axis, Vector2 halfSize, Vector2 furnitureX, Vector2 furnitureZ) =>
            halfSize.x * Mathf.Abs(Vector2.Dot(axis, furnitureX)) + halfSize.y * Mathf.Abs(Vector2.Dot(axis, furnitureZ));
    }
}
