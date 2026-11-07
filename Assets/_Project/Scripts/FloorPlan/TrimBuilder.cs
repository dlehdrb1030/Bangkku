using System;
using System.Collections.Generic;
using Bangkku.Core;
using UnityEngine;

namespace Bangkku.FloorPlan
{
    public enum TrimKind
    {
        /// <summary>걸레받이 (벽 아래, 바닥과 만나는 곳)</summary>
        Baseboard,

        /// <summary>문선·창선 (개구부 둘레, 벽 앞뒤 면에 덧댄 판)</summary>
        Casing,

        /// <summary>문틀·창틀 (개구부 안쪽 면을 덮는 판)</summary>
        Lining,

        /// <summary>창 유리를 잡는 창살 (테두리 + 넓은 창의 가운데 살)</summary>
        Sash,
    }

    /// <summary>
    /// 마감재 상자 하나. Unity 좌표(m). Size 크기 상자를 Y축으로 YawDegrees 만큼 돌린 모양 (WallPiece 와 같은 규칙).
    /// </summary>
    public readonly struct TrimPiece
    {
        public readonly string Name;
        public readonly TrimKind Kind;

        /// <summary>상자 중심 (m)</summary>
        public readonly Vector3 Center;

        /// <summary>x = 놓인 방향 길이, y = 높이, z = 두께 (m)</summary>
        public readonly Vector3 Size;

        public readonly float YawDegrees;

        public TrimPiece(string name, TrimKind kind, Vector3 center, Vector3 size, float yawDegrees)
        {
            Name = name;
            Kind = kind;
            Center = center;
            Size = size;
            YawDegrees = yawDegrees;
        }
    }

    /// <summary>
    /// 걸레받이·문틀·창틀 (평면도에서 계산, JSON 형식은 그대로).
    /// - 걸레받이: 바닥 다각형의 변 중 벽면 위에 놓인 부분 (벽 없이 다른 바닥과 이어진 변에는 없음). 문 자리와 바닥까지 내려오는 창 자리는 비운다
    /// - 문선·창선: 개구부 둘레, 벽 앞뒤 두 면. 옆 벽에 막히면 그만큼 좁히거나 뺀다
    /// - 문틀·창틀: 개구부 안쪽 면 (벽 두께 전체)
    /// - 창살: 유리 둘레 + 폭 1.2m 이상 창은 가운데 살 (미닫이 창처럼)
    /// 보이기만 하는 장식이라 콜라이더가 없다. 판정(벽 스냅, 좁음·겹침, 거리 라벨)은 지금처럼 벽 조각으로만 한다.
    /// 계산은 평면도 mm 로 하고 결과만 m 로 바꾼다. 네이티브 Unity 호출이 없어 따로 시험할 수 있다.
    /// </summary>
    public static class TrimBuilder
    {
        public const int BaseboardHeightMm = 80;
        public const int BaseboardDepthMm = 12;
        public const int CasingWidthMm = 60;
        public const int CasingDepthMm = 15;
        public const int LiningMm = 10;
        public const int SashWidthMm = 45;
        public const int SashDepthMm = 50;

        /// <summary>이 폭 이상인 창은 가운데 살을 넣는다</summary>
        public const int MullionMinWidthMm = 1200;

        /// <summary>바닥 변이 벽면 위에 있다고 볼 거리 차이</summary>
        const double FaceToleranceMm = 20;

        /// <summary>이보다 짧은 조각은 만들지 않는다</summary>
        const double MinPieceMm = 5;

        const double Eps = 0.5;

        /// <summary>평면도 전체의 마감재. GameObject 는 만들지 않는다.</summary>
        public static List<TrimPiece> ComputeAll(FloorPlanData plan)
        {
            var walls = new List<WallInfo>();
            foreach (var wall in plan.Walls)
                walls.Add(new WallInfo(wall, plan.DefaultWallThickness));

            var openings = ComputeOpenings(plan, walls);
            var pieces = new List<TrimPiece>();
            foreach (var opening in openings)
                AddOpening(opening, plan.CeilingHeight.Value, pieces);
            AddBaseboards(plan, walls, openings, pieces);
            return pieces;
        }

        /// <summary>
        /// 마감재 상자를 메쉬 하나로 합친다 (작은 상자가 많아서 오브젝트를 따로 만들지 않음).
        /// </summary>
        public static Mesh CreateMesh(string name, IReadOnlyList<TrimPiece> pieces)
        {
            var parts = new BoxMesh.Parts();
            foreach (var piece in pieces)
            {
                var rotation = Quaternion.Euler(0f, piece.YawDegrees, 0f);
                BoxMesh.AppendBox(parts, piece.Size, BoxMesh.FrameOrigin(piece.Center, rotation), piece.Center, rotation);
            }

            return parts.ToMesh(name);
        }

        // ───────────── 좌표계 ─────────────

        /// <summary>
        /// 평면도 위의 선분 하나를 기준으로 한 좌표 (mm). s = 시작점에서 선분 방향 거리, n = 왼쪽 법선 (-Uy, Ux) 방향 거리
        /// </summary>
        readonly struct Frame
        {
            public readonly double Ox, Oy, Ux, Uy, Length;

            public Frame(double x0, double y0, double x1, double y1)
            {
                double dx = x1 - x0, dy = y1 - y0;
                Length = Math.Sqrt(dx * dx + dy * dy);
                Ox = x0;
                Oy = y0;
                Ux = Length > 0 ? dx / Length : 1;
                Uy = Length > 0 ? dy / Length : 0;
            }

            public double S(double x, double y) => (x - Ox) * Ux + (y - Oy) * Uy;
            public double N(double x, double y) => -(x - Ox) * Uy + (y - Oy) * Ux;

            /// <summary>s0~s1, n0~n1, 높이 y0~y1 (모두 mm) 상자</summary>
            public TrimPiece Box(string name, TrimKind kind, double s0, double s1, double n0, double n1, double y0, double y1)
            {
                double s = (s0 + s1) * 0.5, n = (n0 + n1) * 0.5;
                double x = Ox + Ux * s - Uy * n;
                double y = Oy + Uy * s + Ux * n;
                var center = Units.PlanToWorld((float)x, (float)y, Units.MmToM((float)((y0 + y1) * 0.5)));
                var size = new Vector3(Units.MmToM((float)(s1 - s0)), Units.MmToM((float)(y1 - y0)), Units.MmToM((float)(n1 - n0)));
                // WallMeshBuilder 와 같은 식: 로컬 +X 가 선분 방향
                float yaw = (float)(Math.Atan2(Uy, Ux) * (180.0 / Math.PI));
                return new TrimPiece(name, kind, center, size, yaw);
            }
        }

        class WallInfo
        {
            public readonly WallData Data;
            public readonly Frame Frame;
            public readonly double Half;

            public WallInfo(WallData data, int defaultThicknessMm)
            {
                Data = data;
                Frame = new Frame(data.Start[0], data.Start[1], data.End[0], data.End[1]);
                Half = data.ThicknessOr(defaultThicknessMm) * 0.5;
            }

            /// <summary>이 벽이 차지하는 직사각형 (양 끝으로 두께/2 늘린 것, WallMeshBuilder 와 같음)을 다른 좌표계로 본 범위</summary>
            public void BoundsIn(Frame other, out double sLo, out double sHi, out double nLo, out double nHi)
            {
                sLo = nLo = double.MaxValue;
                sHi = nHi = double.MinValue;
                var f = Frame;
                foreach (double s in new[] { -Half, f.Length + Half })
                foreach (double n in new[] { -Half, Half })
                {
                    double x = f.Ox + f.Ux * s - f.Uy * n;
                    double y = f.Oy + f.Uy * s + f.Ux * n;
                    double cs = other.S(x, y), cn = other.N(x, y);
                    sLo = Math.Min(sLo, cs);
                    sHi = Math.Max(sHi, cs);
                    nLo = Math.Min(nLo, cn);
                    nHi = Math.Max(nHi, cn);
                }
            }
        }

        /// <summary>개구부 하나와, 벽 두 면(0 = 벽 왼쪽 +n, 1 = 오른쪽 -n)의 문선 폭 (앞쪽 Before, 뒤쪽 After)</summary>
        class OpeningInfo
        {
            public OpeningData Data;
            public WallInfo Wall;
            public double A, B;
            public readonly double[] Before = new double[2];
            public readonly double[] After = new double[2];
        }

        // ───────────── 문선 폭 (옆 벽·옆 개구부에 막히는지) ─────────────

        static List<OpeningInfo> ComputeOpenings(FloorPlanData plan, List<WallInfo> walls)
        {
            var result = new List<OpeningInfo>();
            foreach (var opening in plan.Openings)
            {
                var wall = walls.Find(w => w.Data.Id == opening.WallId);
                if (wall == null) continue; // FloorPlanLoader 가 이미 경고한다
                result.Add(new OpeningInfo { Data = opening, Wall = wall, A = opening.Offset, B = opening.Offset + opening.Width });
            }

            foreach (var o in result)
            for (int face = 0; face < 2; face++)
            {
                o.Before[face] = CasingWidth(o, face, true, walls, result);
                o.After[face] = CasingWidth(o, face, false, walls, result);
            }

            return result;
        }

        /// <summary>
        /// 개구부 한쪽 문선의 폭. 그 자리에 다른 벽이 있으면 벽면까지, 같은 벽의 다른 개구부와 가까우면 가운데까지 줄인다.
        /// 너무 좁으면 0 (문선 없음)
        /// </summary>
        static double CasingWidth(OpeningInfo o, int face, bool before, List<WallInfo> walls, List<OpeningInfo> openings)
        {
            var wall = o.Wall;
            double half = wall.Half;
            double bandLo = face == 0 ? half : -half - CasingDepthMm;
            double bandHi = face == 0 ? half + CasingDepthMm : -half;

            double edge = before ? o.A : o.B;
            // 문선이 갈 수 있는 끝 (앞쪽이면 작은 쪽 한계, 뒤쪽이면 큰 쪽 한계)
            double limit = before ? Math.Max(o.A - CasingWidthMm, -half) : Math.Min(o.B + CasingWidthMm, wall.Frame.Length + half);

            foreach (var other in walls)
            {
                if (other == wall) continue;
                other.BoundsIn(wall.Frame, out double sLo, out double sHi, out double nLo, out double nHi);
                if (nLo >= bandHi - Eps || nHi <= bandLo + Eps) continue; // 그 면 쪽에 없음
                if (before)
                {
                    if (sHi > limit + Eps && sLo < edge - Eps) limit = Math.Min(sHi, edge);
                }
                else
                {
                    if (sLo < limit - Eps && sHi > edge + Eps) limit = Math.Max(sLo, edge);
                }
            }

            foreach (var q in openings)
            {
                if (q == o || q.Wall != wall) continue;
                if (before && q.B <= o.A + Eps) limit = Math.Max(limit, (q.B + o.A) * 0.5);
                if (!before && q.A >= o.B - Eps) limit = Math.Min(limit, (q.A + o.B) * 0.5);
            }

            double width = before ? edge - limit : limit - edge;
            return width < MinPieceMm ? 0 : width;
        }

        // ───────────── 문·창 ─────────────

        static void AddOpening(OpeningInfo o, double ceiling, List<TrimPiece> pieces)
        {
            var f = o.Wall.Frame;
            double half = o.Wall.Half;
            double a = o.A, b = o.B, sill = o.Data.Sill, head = o.Data.Head;
            string id = o.Data.Id;
            bool window = o.Data.Type == OpeningType.Window;

            void Add(string suffix, TrimKind kind, double s0, double s1, double n0, double n1, double y0, double y1)
            {
                if (s1 - s0 < MinPieceMm || n1 - n0 < Eps || y1 - y0 < MinPieceMm) return;
                pieces.Add(f.Box($"{id}_{suffix}", kind, s0, s1, n0, n1, y0, y1));
            }

            // 문선·창선: 벽 앞뒤 면. 옆 기둥은 sill~head, 위는 기둥 바깥까지 (천장을 넘지 않게), 창은 아래도
            double top = Math.Min(head + CasingWidthMm, ceiling);
            double under = Math.Max(0, sill - CasingWidthMm);
            for (int face = 0; face < 2; face++)
            {
                double n0 = face == 0 ? half : -half - CasingDepthMm;
                double n1 = face == 0 ? half + CasingDepthMm : -half;
                double wb = o.Before[face], wa = o.After[face];
                if (wb > 0) Add($"casing{face}_l", TrimKind.Casing, a - wb, a, n0, n1, sill, head);
                if (wa > 0) Add($"casing{face}_r", TrimKind.Casing, b, b + wa, n0, n1, sill, head);
                Add($"casing{face}_t", TrimKind.Casing, a - wb, b + wa, n0, n1, head, top);
                if (window) Add($"casing{face}_b", TrimKind.Casing, a - wb, b + wa, n0, n1, under, sill);
            }

            // 문틀·창틀: 개구부 안쪽 면. 바닥에서 시작하는 개구부는 아래 틀이 없다
            bool bottom = sill >= MinPieceMm;
            double y0 = bottom ? sill + LiningMm : sill;
            double y1 = head - LiningMm;
            Add("lining_l", TrimKind.Lining, a, a + LiningMm, -half, half, y0, y1);
            Add("lining_r", TrimKind.Lining, b - LiningMm, b, -half, half, y0, y1);
            Add("lining_t", TrimKind.Lining, a, b, -half, half, y1, head);
            if (bottom) Add("lining_b", TrimKind.Lining, a, b, -half, half, sill, y0);

            if (!window) return;

            // 창살: 유리판(벽 가운데) 둘레
            double s0 = a + LiningMm, s1 = b - LiningMm;
            double d0 = -SashDepthMm * 0.5, d1 = SashDepthMm * 0.5;
            Add("sash_l", TrimKind.Sash, s0, s0 + SashWidthMm, d0, d1, y0, y1);
            Add("sash_r", TrimKind.Sash, s1 - SashWidthMm, s1, d0, d1, y0, y1);
            Add("sash_t", TrimKind.Sash, s0 + SashWidthMm, s1 - SashWidthMm, d0, d1, y1 - SashWidthMm, y1);
            Add("sash_b", TrimKind.Sash, s0 + SashWidthMm, s1 - SashWidthMm, d0, d1, y0, y0 + SashWidthMm);
            if (b - a >= MullionMinWidthMm)
            {
                double mid = (a + b) * 0.5;
                Add("sash_m", TrimKind.Sash, mid - SashWidthMm * 0.5, mid + SashWidthMm * 0.5, d0, d1, y0 + SashWidthMm, y1 - SashWidthMm);
            }
        }

        // ───────────── 걸레받이 ─────────────

        static void AddBaseboards(FloorPlanData plan, List<WallInfo> walls, List<OpeningInfo> openings, List<TrimPiece> pieces)
        {
            foreach (var floor in plan.Floors)
            {
                var poly = floor.Polygon;
                int count = poly.Count;
                if (count < 3) continue;

                // 다각형 방향: 넓이 > 0 이면 안쪽이 각 변의 왼쪽(+n)
                double area = 0;
                for (int i = 0; i < count; i++)
                {
                    var p = poly[i];
                    var q = poly[(i + 1) % count];
                    area += (double)p[0] * q[1] - (double)q[0] * p[1];
                }

                if (Math.Abs(area) < Eps) continue;
                double orient = Math.Sign(area);
                double nLo = orient > 0 ? 0 : -BaseboardDepthMm;
                double nHi = orient > 0 ? BaseboardDepthMm : 0;

                for (int i = 0; i < count; i++)
                {
                    var p = poly[i];
                    var q = poly[(i + 1) % count];
                    var r = poly[(i + 2) % count];
                    var edge = new Frame(p[0], p[1], q[0], q[1]);
                    if (edge.Length < MinPieceMm) continue;

                    var intervals = EdgeIntervals(p, q, edge, nLo, nHi, walls, openings);
                    if (intervals.Count == 0) continue;

                    // 방 쪽으로 튀어나온 벽 모서리(다각형이 안쪽으로 꺾인 꼭짓점)에서는 두 걸레받이 사이가 비므로,
                    // 이 변의 끝을 두께만큼 늘려 모서리를 덮는다 (다음 변은 늘리지 않아 서로 겹치지 않음)
                    double cross = (double)(q[0] - p[0]) * (r[1] - q[1]) - (double)(q[1] - p[1]) * (r[0] - q[0]);
                    bool reflex = cross * orient < 0;

                    for (int k = 0; k < intervals.Count; k++)
                    {
                        var (t0, t1) = intervals[k];
                        if (reflex && t1 >= edge.Length - Eps) t1 += BaseboardDepthMm;
                        pieces.Add(edge.Box($"{floor.Id}_base{i}_{k}", TrimKind.Baseboard, t0, t1, nLo, nHi, 0, BaseboardHeightMm));
                    }
                }
            }
        }

        /// <summary>
        /// 바닥 변 p→q 에서 걸레받이가 들어갈 구간 (변 시작에서 거리 mm).
        /// 벽면 위에 놓인 부분만 남기고, 그 벽의 문·바닥 창 자리(문선 폭 포함)와
        /// 걸레받이 자리(변에서 방 쪽 nLo~nHi)를 차지한 다른 벽(바닥 다각형보다 길게 그린 벽 끝 등)은 뺀다
        /// </summary>
        static List<(double, double)> EdgeIntervals(int[] p, int[] q, Frame edge, double nLo, double nHi,
            List<WallInfo> walls, List<OpeningInfo> openings)
        {
            var keep = new List<(double, double)>();
            var cut = new List<(double, double)>();
            foreach (var wall in walls)
            {
                var f = wall.Frame;
                double sp = f.S(p[0], p[1]), np = f.N(p[0], p[1]);
                double sq = f.S(q[0], q[1]), nq = f.N(q[0], q[1]);
                bool onFace = Math.Abs(np - nq) <= FaceToleranceMm && Math.Abs(Math.Abs(np) - wall.Half) <= FaceToleranceMm;
                if (!onFace)
                {
                    // 이 변이 놓인 벽이 아닌데 걸레받이 자리를 차지하면 그 구간을 뺀다
                    wall.BoundsIn(edge, out double bsLo, out double bsHi, out double bnLo, out double bnHi);
                    if (bnLo < nHi - Eps && bnHi > nLo + Eps) cut.Add((bsLo, bsHi));
                    continue;
                }

                double lo = Math.Max(Math.Min(sp, sq), -wall.Half);
                double hi = Math.Min(Math.Max(sp, sq), f.Length + wall.Half);
                if (hi - lo < MinPieceMm) continue;

                double dir = sq >= sp ? 1 : -1;
                double T(double s) => (s - sp) * dir;
                keep.Add(Ordered(T(lo), T(hi)));

                int face = np > 0 ? 0 : 1;
                foreach (var o in openings)
                {
                    if (o.Wall != wall || o.Data.Sill >= BaseboardHeightMm) continue;
                    cut.Add(Ordered(T(o.A - o.Before[face]), T(o.B + o.After[face])));
                }
            }

            return Subtract(Union(keep), cut, 0, edge.Length);
        }

        static (double, double) Ordered(double a, double b) => a <= b ? (a, b) : (b, a);

        /// <summary>겹치거나 맞닿은 구간을 합친다</summary>
        public static List<(double, double)> Union(List<(double, double)> intervals)
        {
            var sorted = new List<(double, double)>(intervals);
            sorted.Sort((x, y) => x.Item1.CompareTo(y.Item1));
            var result = new List<(double, double)>();
            foreach (var (a, b) in sorted)
            {
                if (result.Count > 0 && a <= result[^1].Item2 + Eps)
                    result[^1] = (result[^1].Item1, Math.Max(result[^1].Item2, b));
                else
                    result.Add((a, b));
            }

            return result;
        }

        /// <summary>구간들에서 cut 을 빼고 min~max 로 자른다. MinPieceMm 보다 짧은 조각은 버린다</summary>
        public static List<(double, double)> Subtract(List<(double, double)> intervals, List<(double, double)> cut, double min, double max)
        {
            var current = new List<(double, double)>();
            foreach (var (a, b) in intervals)
                current.Add((Math.Max(a, min), Math.Min(b, max)));

            foreach (var (c0, c1) in cut)
            {
                var next = new List<(double, double)>();
                foreach (var (a, b) in current)
                {
                    if (c1 <= a || c0 >= b)
                    {
                        next.Add((a, b));
                        continue;
                    }

                    if (c0 > a) next.Add((a, c0));
                    if (c1 < b) next.Add((c1, b));
                }

                current = next;
            }

            current.RemoveAll(x => x.Item2 - x.Item1 < MinPieceMm);
            return current;
        }
    }
}
