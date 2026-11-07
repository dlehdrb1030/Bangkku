using System.Collections.Generic;
using UnityEngine;

namespace Bangkku.FloorPlan
{
    /// <summary>
    /// 벽 조각·고정설비용 상자 메쉬. Unity 기본 큐브를 늘리면 텍스처도 같이 늘어나므로,
    /// 실제 크기(m)의 상자를 만들고 UV 를 m 단위로 준다 (UV 1 = 1m, 바닥·천장 PolygonMesh 와 같은 규칙).
    /// - 옆면: U = 그 면을 정면에서 볼 때 오른쪽 방향 거리, V = 높이
    /// - 윗면·아랫면: U = 로컬 X, V = 로컬 Z
    /// - uvOrigin: 상자 중심의 "벽 기준 좌표". 같은 벽의 조각들이 같은 기준을 쓰면 조각 사이에서 무늬가 끊기지 않는다
    ///   (= 회전의 역 * 위치. FrameOrigin 으로 계산)
    /// 재질 Tiling 으로 텍스처 한 장이 몇 m 인지 정한다 (예: 1.2m 짜리 마루 텍스처 → Tiling 1/1.2).
    /// </summary>
    public static class BoxMesh
    {
        /// <summary>면 순서: +X, -X, +Y, -Y, +Z, -Z</summary>
        static readonly Vector3[] Normals =
        {
            Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back,
        };

        /// <summary>
        /// 크기 size(m), 중심이 원점인 상자 메쉬 (꼭짓점 24개, 법선·탄젠트 포함).
        /// </summary>
        public static Mesh Create(string name, Vector3 size, Vector3 uvOrigin)
        {
            var parts = new Parts();
            AppendBox(parts, size, uvOrigin, Vector3.zero, Quaternion.identity);
            return parts.ToMesh(name);
        }

        /// <summary>여러 상자를 메쉬 하나로 합칠 때 꼭짓점을 모으는 곳 (걸레받이·문틀처럼 작은 상자가 많을 때)</summary>
        public class Parts
        {
            public readonly List<Vector3> Vertices = new();
            public readonly List<Vector3> Normals = new();
            public readonly List<Vector2> Uvs = new();
            public readonly List<int> Triangles = new();

            public Mesh ToMesh(string name)
            {
                var mesh = new Mesh { name = name };
                // 꼭짓점이 65535 개를 넘으면 32비트 인덱스가 필요하다
                if (Vertices.Count > ushort.MaxValue) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                mesh.SetVertices(Vertices);
                mesh.SetNormals(Normals);
                mesh.SetUVs(0, Uvs);
                mesh.SetTriangles(Triangles, 0);
                mesh.RecalculateTangents(); // 노멀맵용
                mesh.RecalculateBounds();
                return mesh;
            }
        }

        /// <summary>
        /// 크기 size(m) 상자를 position·rotation 에 놓은 꼭짓점을 parts 에 더한다. UV 는 상자 자신의 면 기준 (uvOrigin 참고)
        /// </summary>
        public static void AppendBox(Parts parts, Vector3 size, Vector3 uvOrigin, Vector3 position, Quaternion rotation)
        {
            var half = size * 0.5f;
            foreach (var n in Normals)
            {
                FaceAxes(n, out var right, out var up);
                // 면 중심 + 오른쪽·위쪽 반 크기. 꼭짓점 순서: 왼아래, 오른아래, 오른위, 왼위
                var center = Vector3.Scale(n, half);
                var r = Vector3.Scale(right, half);
                var u = Vector3.Scale(up, half);
                int start = parts.Vertices.Count;
                foreach (var corner in new[] { center - r - u, center + r - u, center + r + u, center - r + u })
                {
                    parts.Vertices.Add(position + rotation * corner);
                    parts.Normals.Add(rotation * n);
                    parts.Uvs.Add(FaceUV(corner, n, uvOrigin));
                }

                // 바깥(법선 쪽)에서 볼 때 시계 방향 (Unity 앞면)
                parts.Triangles.AddRange(new[] { start, start + 2, start + 1, start, start + 3, start + 2 });
            }
        }

        /// <summary>
        /// 면을 바깥에서 정면으로 볼 때의 오른쪽·위쪽 방향 (로컬). 옆면의 위쪽은 +Y, 윗면·아랫면은 오른쪽 +X
        /// </summary>
        public static void FaceAxes(Vector3 normal, out Vector3 right, out Vector3 up)
        {
            if (Mathf.Abs(normal.y) > 0.5f)
            {
                right = Vector3.right;
                up = normal.y > 0f ? Vector3.forward : Vector3.back;
                return;
            }

            up = Vector3.up;
            // 바라보는 방향 = -normal. Unity(왼손 좌표)에서 오른쪽 = up × forward
            right = Vector3.Cross(Vector3.up, -normal);
        }

        /// <summary>꼭짓점(로컬, m)의 UV. 기준 좌표 uvOrigin 을 더한 뒤 면의 오른쪽·위쪽 성분 (m)</summary>
        public static Vector2 FaceUV(Vector3 localPoint, Vector3 normal, Vector3 uvOrigin)
        {
            FaceAxes(normal, out var right, out var up);
            var p = localPoint + uvOrigin;
            return new Vector2(Vector3.Dot(p, right), Vector3.Dot(p, up));
        }

        /// <summary>
        /// 위치·회전으로 놓인 상자의 기준 좌표. 같은 회전(같은 벽)의 상자들은 이 좌표계에서 이어지므로 무늬가 맞는다
        /// </summary>
        public static Vector3 FrameOrigin(Vector3 position, Quaternion rotation) =>
            // 단위 쿼터니언의 역 = 켤레 (Quaternion.Inverse 와 같은 값)
            new Quaternion(-rotation.x, -rotation.y, -rotation.z, rotation.w) * position;

        /// <summary>
        /// 상자 오브젝트 (MeshFilter + MeshRenderer + BoxCollider). 스케일은 1 이고 크기는 메쉬·콜라이더가 갖는다.
        /// 만든 메쉬를 meshes 에 담는다 (평면도를 다시 만들 때 지우도록)
        /// </summary>
        public static GameObject CreateObject(string name, Transform parent, Vector3 localPosition, Quaternion localRotation,
            Vector3 size, Material material, List<Mesh> meshes)
        {
            var go = new GameObject(name);
            var t = go.transform;
            t.SetParent(parent, false);
            t.localPosition = localPosition;
            t.localRotation = localRotation;

            var mesh = Create(name, size, FrameOrigin(localPosition, localRotation));
            meshes?.Add(mesh);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = material;
            go.AddComponent<BoxCollider>().size = size;
            return go;
        }
    }
}
