using System.Collections.Generic;
using UnityEngine;

namespace Bangkku.FloorPlan
{
    /// <summary>
    /// 상자·타원기둥·움푹한 그릇 모양을 모아 메쉬 하나로 만든다. 재질마다 서브메쉬가 하나씩 (slot 번호 = 재질 순서).
    /// 고정설비 모델(FixtureModels)용. 좌표는 m.
    /// 면 방향은 원하는 바깥 방향(법선)을 받아 삼각형 순서를 정하므로 앞뒤가 뒤집히지 않는다.
    /// 네이티브 Unity 호출은 ToMesh 에만 있어 모양 계산은 따로 시험할 수 있다.
    /// </summary>
    public class ShapeMesh
    {
        /// <summary>둥근 모양의 둘레 조각 수</summary>
        public const int RoundSegments = 32;

        public readonly BoxMesh.Parts[] Slots;

        public ShapeMesh(int slotCount)
        {
            Slots = new BoxMesh.Parts[slotCount];
            for (int i = 0; i < slotCount; i++) Slots[i] = new BoxMesh.Parts();
        }

        /// <summary>축에 나란한 상자 (min~max)</summary>
        public void Box(int slot, Vector3 min, Vector3 max)
        {
            var center = (min + max) * 0.5f;
            BoxMesh.AppendBox(Slots[slot], max - min, center, center, Quaternion.identity);
        }

        public void Box(int slot, float x0, float x1, float y0, float y1, float z0, float z1) =>
            Box(slot, new Vector3(Mathf.Min(x0, x1), Mathf.Min(y0, y1), Mathf.Min(z0, z1)),
                new Vector3(Mathf.Max(x0, x1), Mathf.Max(y0, y1), Mathf.Max(z0, z1)));

        /// <summary>세로 타원기둥 (가운데 cx, cz, 반지름 rx, rz, 높이 y0~y1). 위·아래 뚜껑은 선택</summary>
        public void EllipticCylinder(int slot, float cx, float cz, float rx, float rz, float y0, float y1, bool capTop, bool capBottom)
        {
            Wall(slot, cx, cz, rx, rz, y0, y1, true);
            if (capTop) Disc(slot, cx, cz, rx, rz, y1, true);
            if (capBottom) Disc(slot, cx, cz, rx, rz, y0, false);
        }

        /// <summary>
        /// 위가 움푹한 그릇 (변기·세면대). 바깥 벽 y0~y1, 윗면 테두리, 안쪽 벽 y1~(y1 - depth), 안쪽 바닥.
        /// 안쪽 타원은 바깥과 같은 가운데에 (innerRx, innerRz)
        /// </summary>
        public void Cup(int slot, float cx, float cz, float rx, float rz, float innerRx, float innerRz, float y0, float y1, float depth)
        {
            Wall(slot, cx, cz, rx, rz, y0, y1, true);
            Ring(slot, cx, cz, rx, rz, innerRx, innerRz, y1);
            Wall(slot, cx, cz, innerRx, innerRz, y1 - depth, y1, false);
            Disc(slot, cx, cz, innerRx, innerRz, y1 - depth, true);
        }

        /// <summary>
        /// 위가 뚫린 직사각형 개수대 (x0~x1, z0~z1, 윗면 yTop 에서 depth 만큼). 안쪽 벽 4개 + 바닥, 모두 안쪽을 향함
        /// </summary>
        public void Basin(int slot, float x0, float x1, float z0, float z1, float yTop, float depth)
        {
            float yb = yTop - depth;
            Quad(slot, new Vector3(x0, yb, z0), new Vector3(x0, yb, z1), new Vector3(x0, yTop, z1), new Vector3(x0, yTop, z0), Vector3.right);
            Quad(slot, new Vector3(x1, yb, z0), new Vector3(x1, yb, z1), new Vector3(x1, yTop, z1), new Vector3(x1, yTop, z0), Vector3.left);
            Quad(slot, new Vector3(x0, yb, z0), new Vector3(x1, yb, z0), new Vector3(x1, yTop, z0), new Vector3(x0, yTop, z0), Vector3.forward);
            Quad(slot, new Vector3(x0, yb, z1), new Vector3(x1, yb, z1), new Vector3(x1, yTop, z1), new Vector3(x0, yTop, z1), Vector3.back);
            Quad(slot, new Vector3(x0, yb, z0), new Vector3(x1, yb, z0), new Vector3(x1, yb, z1), new Vector3(x0, yb, z1), Vector3.up);
        }

        /// <summary>네 점(둘레 순서)의 사각형 하나. normal 쪽에서 보이게 한다</summary>
        public void Quad(int slot, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 normal)
        {
            var p = Slots[slot];
            int i = p.Vertices.Count;
            foreach (var v in new[] { a, b, c, d })
            {
                p.Vertices.Add(v);
                p.Normals.Add(normal);
                p.Uvs.Add(new Vector2(v.x + v.z, v.y));
            }

            Triangle(p, i, i + 1, i + 2, normal);
            Triangle(p, i, i + 2, i + 3, normal);
        }

        // ───────────── 둥근 면 ─────────────

        static Vector3 OnEllipse(float cx, float cz, float rx, float rz, int i, float y)
        {
            float a = i / (float)RoundSegments * Mathf.PI * 2f;
            return new Vector3(cx + Mathf.Cos(a) * rx, y, cz + Mathf.Sin(a) * rz);
        }

        /// <summary>타원 둘레의 바깥 법선 (x/rx², z/rz² 방향)</summary>
        static Vector3 EllipseNormal(float rx, float rz, int i)
        {
            float a = i / (float)RoundSegments * Mathf.PI * 2f;
            var n = new Vector3(Mathf.Cos(a) / rx, 0f, Mathf.Sin(a) / rz);
            return n / n.magnitude;
        }

        /// <summary>타원 옆면. outward 면 바깥을, 아니면 안쪽(가운데)을 향한다</summary>
        void Wall(int slot, float cx, float cz, float rx, float rz, float y0, float y1, bool outward)
        {
            var p = Slots[slot];
            int start = p.Vertices.Count;
            float sign = outward ? 1f : -1f;
            for (int i = 0; i <= RoundSegments; i++)
            {
                var n = EllipseNormal(rx, rz, i) * sign;
                float u = i / (float)RoundSegments;
                p.Vertices.Add(OnEllipse(cx, cz, rx, rz, i, y0));
                p.Vertices.Add(OnEllipse(cx, cz, rx, rz, i, y1));
                p.Normals.Add(n);
                p.Normals.Add(n);
                p.Uvs.Add(new Vector2(u, y0));
                p.Uvs.Add(new Vector2(u, y1));
            }

            for (int i = 0; i < RoundSegments; i++)
            {
                int b0 = start + i * 2, t0 = b0 + 1, b1 = b0 + 2, t1 = b0 + 3;
                var faceNormal = (p.Normals[b0] + p.Normals[b1]) * 0.5f;
                Triangle(p, b0, t0, t1, faceNormal);
                Triangle(p, b0, t1, b1, faceNormal);
            }
        }

        /// <summary>수평 타원판. up 이면 위를, 아니면 아래를 향한다</summary>
        void Disc(int slot, float cx, float cz, float rx, float rz, float y, bool up)
        {
            var p = Slots[slot];
            var n = up ? Vector3.up : Vector3.down;
            int hub = p.Vertices.Count;
            p.Vertices.Add(new Vector3(cx, y, cz));
            p.Normals.Add(n);
            p.Uvs.Add(new Vector2(cx, cz));
            for (int i = 0; i <= RoundSegments; i++)
            {
                var v = OnEllipse(cx, cz, rx, rz, i, y);
                p.Vertices.Add(v);
                p.Normals.Add(n);
                p.Uvs.Add(new Vector2(v.x, v.z));
            }

            for (int i = 0; i < RoundSegments; i++)
                Triangle(p, hub, hub + 1 + i, hub + 2 + i, n);
        }

        /// <summary>바깥 타원과 안쪽 타원 사이의 수평 테두리 (위를 향함)</summary>
        void Ring(int slot, float cx, float cz, float rx, float rz, float innerRx, float innerRz, float y)
        {
            var p = Slots[slot];
            int start = p.Vertices.Count;
            for (int i = 0; i <= RoundSegments; i++)
            {
                var outer = OnEllipse(cx, cz, rx, rz, i, y);
                var inner = OnEllipse(cx, cz, innerRx, innerRz, i, y);
                p.Vertices.Add(outer);
                p.Vertices.Add(inner);
                p.Normals.Add(Vector3.up);
                p.Normals.Add(Vector3.up);
                p.Uvs.Add(new Vector2(outer.x, outer.z));
                p.Uvs.Add(new Vector2(inner.x, inner.z));
            }

            for (int i = 0; i < RoundSegments; i++)
            {
                int o0 = start + i * 2, i0 = o0 + 1, o1 = o0 + 2, i1 = o0 + 3;
                Triangle(p, o0, i0, i1, Vector3.up);
                Triangle(p, o0, i1, o1, Vector3.up);
            }
        }

        /// <summary>
        /// 삼각형 하나. normal 쪽에서 볼 때 시계 방향(Unity 앞면)이 되도록 순서를 맞춘다
        /// (Unity 에서 앞면의 Cross(b - a, c - a) 는 보는 쪽을 향한다)
        /// </summary>
        static void Triangle(BoxMesh.Parts p, int a, int b, int c, Vector3 normal)
        {
            var va = p.Vertices[a];
            var cross = Cross(p.Vertices[b] - va, p.Vertices[c] - va);
            if (Dot(cross, normal) >= 0f) p.Triangles.AddRange(new[] { a, b, c });
            else p.Triangles.AddRange(new[] { a, c, b });
        }

        // Vector3.Cross·Dot 와 같지만 직접 계산 (따로 시험할 때 네이티브 호출이 없도록)
        static Vector3 Cross(Vector3 a, Vector3 b) => new(a.y * b.z - a.z * b.y, a.z * b.x - a.x * b.z, a.x * b.y - a.y * b.x);
        static float Dot(Vector3 a, Vector3 b) => a.x * b.x + a.y * b.y + a.z * b.z;

        /// <summary>모은 모양을 메쉬 하나로 (슬롯마다 서브메쉬). offset 만큼 옮긴다</summary>
        public Mesh ToMesh(string name, Vector3 offset)
        {
            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var uvs = new List<Vector2>();
            var submeshes = new List<List<int>>();
            foreach (var slot in Slots)
            {
                int start = vertices.Count;
                foreach (var v in slot.Vertices) vertices.Add(v + offset);
                normals.AddRange(slot.Normals);
                uvs.AddRange(slot.Uvs);
                var tris = new List<int>(slot.Triangles.Count);
                foreach (int t in slot.Triangles) tris.Add(t + start);
                submeshes.Add(tris);
            }

            var mesh = new Mesh { name = name };
            if (vertices.Count > ushort.MaxValue) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.subMeshCount = submeshes.Count;
            for (int i = 0; i < submeshes.Count; i++) mesh.SetTriangles(submeshes[i], i);
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
