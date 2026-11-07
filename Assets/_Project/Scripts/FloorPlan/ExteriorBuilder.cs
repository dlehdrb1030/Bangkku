using System.Collections.Generic;
using Bangkku.Core;
using UnityEngine;

namespace Bangkku.FloorPlan
{
    /// <summary>
    /// 창밖 풍경. 집을 둘러싼 원통(반지름 80m) 안쪽 면에 파노라마(하늘·먼 아파트·나무·땅)를 붙이고,
    /// 위는 하늘색 뚜껑, 아래는 땅(바닥 12m 아래 = 5층 높이)으로 막는다.
    /// - 조명을 받지 않는 Unlit 재질이라 계산이 거의 없고, 그림자를 만들지 않는다 (해를 가리지 않도록)
    /// - 콜라이더가 없다 (텔레포트·판정과 무관)
    /// 파노라마 그림은 이 클래스의 높이·반지름에 맞춰 그린다 (에디터 ExteriorPanorama). 원통의 V 0 = 땅(GroundY), V 1 = 위 끝(TopY)
    /// 사진 파노라마로 바꾸려면 Exterior 재질의 Base Map 만 바꾸면 된다 (가로 = 한 바퀴, 지평선이 V = HorizonV 높이).
    /// </summary>
    public static class ExteriorBuilder
    {
        /// <summary>원통 반지름 (m). 평면도가 아주 크면 더 키운다</summary>
        public const float RadiusM = 80f;

        /// <summary>집 바닥에서 땅까지 (m). 5층 정도</summary>
        public const float GroundBelowFloorM = 12f;

        /// <summary>원통 위 끝 높이 (집 바닥 기준, m). 창에서 올려다봐도 끝이 보이지 않을 높이</summary>
        public const float TopM = 50f;

        /// <summary>파노라마 지평선이 놓인 눈높이 (m). 서 있는 사람 기준</summary>
        public const float EyeHeightM = 1.5f;

        /// <summary>원통 둘레 조각 수</summary>
        public const int Segments = 96;

        /// <summary>뚜껑·땅이 파노라마 맨 위·아래 줄 색을 쓰도록 V 를 살짝 안쪽으로</summary>
        const float EdgeV = 0.002f;

        public static float GroundY => -GroundBelowFloorM;
        public static float TopY => TopM;

        /// <summary>파노라마에서 지평선(눈높이)의 V</summary>
        public static float HorizonV => (EyeHeightM - GroundY) / (TopY - GroundY);

        /// <summary>원통 중심 (평면도 범위 가운데, 높이 0)과 반지름</summary>
        public static void Placement(FloorPlanData plan, out Vector3 center, out float radius)
        {
            center = Vector3.zero;
            radius = RadiusM;
            if (!FloorPlanSpots.TryGetBounds(plan, out var min, out var max)) return;
            center = Units.PlanToWorld((min.x + max.x) * 0.5f, (min.y + max.y) * 0.5f);
            // 집이 원통에 너무 가까우면 풍경이 코앞에 보이므로 최소 40m 여유
            float halfDiagonal = Units.MmToM((max - min).magnitude) * 0.5f;
            radius = Mathf.Max(RadiusM, halfDiagonal + 40f);
        }

        /// <summary>
        /// 원통(안쪽 면) + 위 뚜껑(아래를 봄) + 땅(위를 봄)의 꼭짓점·UV·삼각형. 앞면은 모두 원통 안쪽을 향한다.
        /// 네이티브 호출이 없어 따로 시험할 수 있다
        /// </summary>
        public static void ComputeMesh(Vector3 center, float radius, List<Vector3> vertices, List<Vector2> uvs, List<int> triangles)
        {
            float bottom = center.y + GroundY, top = center.y + TopY;

            // 옆면: 각도 i/Segments 마다 아래·위 꼭짓점 (이음매에서 U 가 0→1 로 이어지도록 마지막 줄을 한 번 더)
            for (int i = 0; i <= Segments; i++)
            {
                float u = i / (float)Segments;
                var dir = Direction(u);
                vertices.Add(center + dir * radius + Vector3.up * (bottom - center.y));
                vertices.Add(center + dir * radius + Vector3.up * (top - center.y));
                uvs.Add(new Vector2(u, 0f));
                uvs.Add(new Vector2(u, 1f));
            }

            for (int i = 0; i < Segments; i++)
            {
                // 가운데에서 보면 각도가 커지는 쪽이 왼쪽. 0 = 오른쪽 아래, 1 = 오른쪽 위, 2 = 왼쪽 아래, 3 = 왼쪽 위
                int b0 = i * 2, t0 = b0 + 1, b1 = b0 + 2, t1 = b0 + 3;
                triangles.AddRange(new[] { b1, t1, t0, b1, t0, b0 });
            }

            AddDisc(center, radius, top, EdgeV, 1f - EdgeV, false, vertices, uvs, triangles);
            AddDisc(center, radius, bottom, EdgeV, EdgeV, true, vertices, uvs, triangles);
        }

        /// <summary>U(0~1, 한 바퀴) 방향의 수평 단위 벡터</summary>
        public static Vector3 Direction(float u)
        {
            float a = u * Mathf.PI * 2f;
            return new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
        }

        /// <summary>높이 y 의 원판. faceUp 이면 위를 보는 면(땅), 아니면 아래를 보는 면(뚜껑). UV 는 V 고정 (파노라마 맨 위·아래 줄 색)</summary>
        static void AddDisc(Vector3 center, float radius, float y, float uBase, float v, bool faceUp,
            List<Vector3> vertices, List<Vector2> uvs, List<int> triangles)
        {
            int hub = vertices.Count;
            vertices.Add(new Vector3(center.x, y, center.z));
            uvs.Add(new Vector2(uBase, v));
            for (int i = 0; i <= Segments; i++)
            {
                float u = i / (float)Segments;
                var p = center + Direction(u) * radius;
                vertices.Add(new Vector3(p.x, y, p.z));
                uvs.Add(new Vector2(u, v));
            }

            for (int i = 0; i < Segments; i++)
            {
                int a = hub + 1 + i, b = a + 1;
                // 위에서 보면 각도가 커지는 쪽이 반시계(+Z 쪽). 위를 보는 면은 위에서 볼 때 시계 방향
                if (faceUp) triangles.AddRange(new[] { hub, b, a });
                else triangles.AddRange(new[] { hub, a, b });
            }
        }

        public static Mesh CreateMesh(Vector3 center, float radius)
        {
            var vertices = new List<Vector3>();
            var uvs = new List<Vector2>();
            var triangles = new List<int>();
            ComputeMesh(center, radius, vertices, uvs, triangles);

            var mesh = new Mesh { name = "Exterior" };
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
