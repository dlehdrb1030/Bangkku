using System.Collections.Generic;
using Bangkku.Core;
using UnityEngine;

namespace Bangkku.FloorPlan
{
    /// <summary>
    /// 평면도 다각형(mm) → 바닥·천장 메쉬. 오목 다각형(ㄱ자 거실 등)도 귀 자르기(ear clipping)로 삼각분할한다.
    /// 꼭짓점 순서는 시계·반시계 어느 쪽이어도 되지만, 변이 교차하면 안 된다 (FloorPlanLoader 가 검사).
    /// </summary>
    public static class PolygonMesh
    {
        /// <summary>
        /// 삼각분할. 결과는 꼭짓점 인덱스 3개씩이며, 모든 삼각형이 평면도 좌표에서 넓이가 양수인 방향이다.
        /// (평면도 Y 는 아래로 커지므로, 이 방향은 Unity 에서 위를 보는 면이 된다)
        /// 실패하면 null. 이때는 JSON 에서 바닥을 직사각형 여러 개로 나눈다 (팀장 가이드 6장).
        /// </summary>
        public static int[] Triangulate(IReadOnlyList<int[]> polygonMm)
        {
            int n = polygonMm.Count;
            if (n < 3) return null;

            var remaining = new List<int>(n);
            for (int i = 0; i < n; i++) remaining.Add(i);
            if (SignedArea2(polygonMm) < 0) remaining.Reverse();

            var triangles = new List<int>((n - 2) * 3);
            while (remaining.Count > 3)
            {
                bool clipped = false;
                for (int i = 0; i < remaining.Count; i++)
                {
                    int prev = remaining[(i - 1 + remaining.Count) % remaining.Count];
                    int cur = remaining[i];
                    int next = remaining[(i + 1) % remaining.Count];
                    long cross = Cross(polygonMm[prev], polygonMm[cur], polygonMm[next]);

                    // 한 줄 위의 꼭짓점은 삼각형 없이 뺀다
                    if (cross == 0)
                    {
                        remaining.RemoveAt(i);
                        clipped = true;
                        break;
                    }

                    if (cross < 0 || ContainsOtherVertex(polygonMm, remaining, prev, cur, next)) continue;

                    triangles.Add(prev);
                    triangles.Add(cur);
                    triangles.Add(next);
                    remaining.RemoveAt(i);
                    clipped = true;
                    break;
                }

                if (!clipped) return null;
            }

            if (Cross(polygonMm[remaining[0]], polygonMm[remaining[1]], polygonMm[remaining[2]]) > 0)
            {
                triangles.Add(remaining[0]);
                triangles.Add(remaining[1]);
                triangles.Add(remaining[2]);
            }

            return triangles.Count > 0 ? triangles.ToArray() : null;
        }

        /// <summary>
        /// 다각형을 높이 heightM 의 평평한 메쉬로 만든다. UV 는 월드 좌표(m)라서 재질이 1m 단위로 반복된다.
        /// 실패하면 null.
        /// </summary>
        /// <param name="faceUp">true = 위를 보는 면 (바닥), false = 아래를 보는 면 (천장)</param>
        public static Mesh BuildMesh(string meshName, IReadOnlyList<int[]> polygonMm, float heightM, bool faceUp)
        {
            int[] triangles = Triangulate(polygonMm);
            if (triangles == null) return null;

            if (!faceUp)
            {
                for (int i = 0; i < triangles.Length; i += 3)
                    (triangles[i + 1], triangles[i + 2]) = (triangles[i + 2], triangles[i + 1]);
            }

            var vertices = new Vector3[polygonMm.Count];
            var normals = new Vector3[polygonMm.Count];
            var uvs = new Vector2[polygonMm.Count];
            for (int i = 0; i < polygonMm.Count; i++)
            {
                vertices[i] = Units.PlanToWorld(polygonMm[i], heightM);
                normals[i] = faceUp ? Vector3.up : Vector3.down;
                uvs[i] = new Vector2(vertices[i].x, vertices[i].z);
            }

            var mesh = new Mesh { name = meshName };
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>신발끈 공식 넓이 × 2 (mm²)</summary>
        public static long SignedArea2(IReadOnlyList<int[]> polygonMm)
        {
            long sum = 0;
            for (int i = 0; i < polygonMm.Count; i++)
            {
                var a = polygonMm[i];
                var b = polygonMm[(i + 1) % polygonMm.Count];
                sum += (long)a[0] * b[1] - (long)b[0] * a[1];
            }

            return sum;
        }

        /// <summary>(b - a) × (c - b). 양수면 a → b → c 가 넓이 양수 방향으로 꺾인다</summary>
        static long Cross(int[] a, int[] b, int[] c) =>
            (long)(b[0] - a[0]) * (c[1] - b[1]) - (long)(b[1] - a[1]) * (c[0] - b[0]);

        /// <summary>삼각형 (a, b, c) 안이나 변 위에 다른 꼭짓점이 있으면 귀가 아니다</summary>
        static bool ContainsOtherVertex(IReadOnlyList<int[]> pts, List<int> remaining, int a, int b, int c)
        {
            foreach (int i in remaining)
            {
                if (i == a || i == b || i == c) continue;
                var p = pts[i];
                if (SamePoint(p, pts[a]) || SamePoint(p, pts[b]) || SamePoint(p, pts[c])) continue;
                if (Cross(pts[a], pts[b], p) >= 0 && Cross(pts[b], pts[c], p) >= 0 && Cross(pts[c], pts[a], p) >= 0)
                    return true;
            }

            return false;
        }

        static bool SamePoint(int[] p, int[] q) => p[0] == q[0] && p[1] == q[1];
    }
}
