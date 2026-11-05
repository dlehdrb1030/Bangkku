using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Bangkku.FloorPlan
{
    /// <summary>FloorPlanLoader.Parse 결과. 오류가 하나라도 있으면 Data 는 null 이다.</summary>
    public sealed class FloorPlanLoadResult
    {
        public FloorPlanData Data;
        public readonly List<string> Errors = new();
        public readonly List<string> Warnings = new();

        public bool Success => Data != null && Errors.Count == 0;

        /// <summary>예: "oneroom_01: 벽 12, 개구부 4, 바닥 4, 고정설비 6, 범위 3600 × 6800 mm, 천장 2400 mm"</summary>
        public string Summary()
        {
            if (!Success) return $"오류 {Errors.Count}개";

            int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
            foreach (var wall in Data.Walls)
            {
                foreach (var p in new[] { wall.Start, wall.End })
                {
                    minX = Math.Min(minX, p[0]); maxX = Math.Max(maxX, p[0]);
                    minY = Math.Min(minY, p[1]); maxY = Math.Max(maxY, p[1]);
                }
            }

            return $"{Data.Name}: 벽 {Data.Walls.Count}, 개구부 {Data.Openings.Count}, 바닥 {Data.Floors.Count}, " +
                   $"고정설비 {Data.Fixtures.Count}, 범위 {maxX - minX} × {maxY - minY} mm (벽 중심선), 천장 {Data.CeilingHeight.Value} mm";
        }
    }

    /// <summary>
    /// floorplan.json (전체개발계획 7.1) 을 읽고 검사한다.
    /// 형식 오류(JSON 문법, 모르는 필드, 잘못된 enum 값)와 내용 오류(없는 벽 참조, 벽 길이를 넘는 개구부,
    /// 꼬인 바닥 다각형 등)를 모두 모아서 한 번에 알려 준다. 단위는 mm 그대로 두고, m 변환은 Builder 가 Units 로 한다.
    /// </summary>
    public static class FloorPlanLoader
    {
        static readonly JsonSerializerSettings Settings = new()
        {
            // 필드 이름 오타(thicknes 등)가 조용히 무시되지 않도록 모르는 필드는 오류로 처리
            MissingMemberHandling = MissingMemberHandling.Error,
        };

        static readonly Regex AssetIdPattern = new("^[a-z0-9]+(_[a-z0-9]+)*$");

        /// <summary>
        /// TextAsset(.json) 을 읽는다. 결과를 Console 에 남기고, 실패하면 null 을 돌려준다.
        /// FloorPlanBuilder 에서 Inspector 로 지정한 JSON 을 넘기는 용도. JSON 만 바꾸면 다른 평면도가 된다.
        /// </summary>
        public static FloorPlanData Load(TextAsset json)
        {
            if (json == null)
            {
                Debug.LogError("[방꾸] 평면도 JSON(TextAsset)이 지정되지 않았습니다.");
                return null;
            }

            var result = Parse(json.text, json.name);
            Report(result, json.name, json);
            return result.Data;
        }

        /// <summary>파일 경로로 읽는다 (StreamingAssets 등). 실패하면 Console 에 남기고 null.</summary>
        public static FloorPlanData LoadFile(string path)
        {
            string source = Path.GetFileName(path);
            FloorPlanLoadResult result;
            try
            {
                result = Parse(File.ReadAllText(path), source);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                result = new FloorPlanLoadResult();
                result.Errors.Add($"파일을 열 수 없습니다: {path} ({e.Message})");
            }

            Report(result, source, null);
            return result.Data;
        }

        /// <summary>
        /// JSON 문자열을 읽고 검사한다. Console 에 아무것도 남기지 않는다 (테스트·에디터 검사용).
        /// </summary>
        /// <param name="sourceName">메시지에 쓸 이름. 파일 이름이면 name 필드와 같은지도 확인한다</param>
        public static FloorPlanLoadResult Parse(string json, string sourceName = null)
        {
            var result = new FloorPlanLoadResult();
            if (string.IsNullOrWhiteSpace(json))
            {
                result.Errors.Add("내용이 비어 있습니다.");
                return result;
            }

            FloorPlanData data;
            try
            {
                data = JsonConvert.DeserializeObject<FloorPlanData>(json, Settings);
            }
            catch (JsonException e)
            {
                result.Errors.Add($"JSON 형식 오류: {ShortJsonMessage(e.Message)}");
                return result;
            }

            if (data == null)
            {
                result.Errors.Add("JSON 최상위가 객체({ ... })가 아닙니다.");
                return result;
            }

            Validate(data, sourceName, result.Errors, result.Warnings);
            if (result.Errors.Count == 0) result.Data = data;
            return result;
        }

        static void Report(FloorPlanLoadResult result, string source, Object context)
        {
            foreach (string warning in result.Warnings)
                Debug.LogWarning($"[방꾸] 평면도 {source}: {warning}", context);

            if (result.Success)
                Debug.Log($"[방꾸] 평면도 로드 {result.Summary()}", context);
            else
                Debug.LogError($"[방꾸] 평면도 {source} 를 읽지 못했습니다 (오류 {result.Errors.Count}개)\n- " +
                               string.Join("\n- ", result.Errors), context);
        }

        // ───────────── 내용 검사 ─────────────

        static void Validate(FloorPlanData d, string sourceName, List<string> errors, List<string> warnings)
        {
            // 생략 가능한 목록은 빈 목록으로 맞춘다 ("openings": null 도 허용)
            d.Openings ??= new List<OpeningData>();
            d.Floors ??= new List<FloorData>();
            d.Fixtures ??= new List<FixtureData>();

            if (string.IsNullOrWhiteSpace(d.Name))
                errors.Add("name 이 없습니다.");
            else
            {
                if (!AssetIdPattern.IsMatch(d.Name))
                    warnings.Add($"name \"{d.Name}\" 이 이름 규칙(소문자_번호, 예: oneroom_01)과 다릅니다.");
                string fileName = sourceName == null ? null : Path.GetFileNameWithoutExtension(sourceName);
                if (!string.IsNullOrEmpty(fileName) && fileName != d.Name)
                    warnings.Add($"name \"{d.Name}\" 과 파일 이름 \"{fileName}\" 이 다릅니다.");
            }

            if (d.Unit != "mm")
                errors.Add($"unit 은 \"mm\" 여야 합니다 (현재 \"{d.Unit}\").");

            int ceiling = 0;
            if (d.CeilingHeight == null)
                errors.Add("ceilingHeight 가 없습니다.");
            else if (d.CeilingHeight.Value <= 0)
                errors.Add($"ceilingHeight.value 는 0보다 커야 합니다 (현재 {d.CeilingHeight.Value}).");
            else
                ceiling = d.CeilingHeight.Value;

            var walls = ValidateWalls(d, errors);
            ValidateOpenings(d, walls, ceiling, errors, warnings);
            ValidateFloors(d, errors, warnings);
            ValidateFixtures(d, ceiling, errors, warnings);
        }

        /// <summary>올바른 벽만 id → 벽 으로 돌려준다 (개구부 검사에 사용)</summary>
        static Dictionary<string, WallData> ValidateWalls(FloorPlanData d, List<string> errors)
        {
            var valid = new Dictionary<string, WallData>();
            if (d.Walls == null || d.Walls.Count == 0)
            {
                errors.Add("walls 가 비어 있습니다. 벽이 하나 이상 있어야 합니다.");
                return valid;
            }

            var ids = new HashSet<string>();
            for (int i = 0; i < d.Walls.Count; i++)
            {
                var w = d.Walls[i];
                string at = $"walls[{i}]";
                if (w == null) { errors.Add($"{at} 가 null 입니다."); continue; }
                if (!CheckId(w.Id, at, ids, errors)) continue;
                at = $"{at} ({w.Id})";

                bool ok = CheckPoint(w.Start, $"{at} start", errors) & CheckPoint(w.End, $"{at} end", errors);
                if (ok && w.Start[0] == w.End[0] && w.Start[1] == w.End[1])
                {
                    errors.Add($"{at}: start 와 end 가 같습니다 (길이 0).");
                    ok = false;
                }

                if (w.Thickness is <= 0)
                {
                    errors.Add($"{at}: thickness 는 0보다 커야 합니다 (현재 {w.Thickness}).");
                    ok = false;
                }
                else if (w.Thickness == null && d.DefaultWallThickness <= 0)
                {
                    errors.Add($"{at}: thickness 가 없고 defaultWallThickness 도 0 이하입니다.");
                    ok = false;
                }

                if (ok) valid[w.Id] = w;
            }

            return valid;
        }

        static void ValidateOpenings(FloorPlanData d, Dictionary<string, WallData> walls, int ceiling,
            List<string> errors, List<string> warnings)
        {
            var ids = new HashSet<string>();
            var byWall = new Dictionary<string, List<OpeningData>>();
            for (int i = 0; i < d.Openings.Count; i++)
            {
                var o = d.Openings[i];
                string at = $"openings[{i}]";
                if (o == null) { errors.Add($"{at} 가 null 입니다."); continue; }
                if (!CheckId(o.Id, at, ids, errors)) continue;
                at = $"{at} ({o.Id})";

                if (string.IsNullOrEmpty(o.WallId) || !walls.TryGetValue(o.WallId, out var wall))
                {
                    // 벽 자체가 잘못된 경우는 벽 쪽 오류로 이미 알렸다
                    if (d.Walls == null || !d.Walls.Exists(w => w != null && w.Id == o.WallId))
                        errors.Add($"{at}: wallId \"{o.WallId}\" 인 벽이 없습니다.");
                    continue;
                }

                if (o.Width <= 0) errors.Add($"{at}: width 는 0보다 커야 합니다 (현재 {o.Width}).");
                if (o.Offset < 0) errors.Add($"{at}: offset 은 0 이상이어야 합니다 (현재 {o.Offset}).");

                double length = WallLength(wall);
                if (o.Width > 0 && o.Offset >= 0 && o.Offset + o.Width > length)
                    errors.Add($"{at}: offset + width ({o.Offset + o.Width}) 가 벽 {wall.Id} 길이 ({length:0}) 를 넘습니다.");

                if (o.Sill < 0) errors.Add($"{at}: sill 은 0 이상이어야 합니다 (현재 {o.Sill}).");
                if (o.Head <= o.Sill) errors.Add($"{at}: head ({o.Head}) 는 sill ({o.Sill}) 보다 커야 합니다.");
                if (ceiling > 0 && o.Head > ceiling) errors.Add($"{at}: head ({o.Head}) 가 천장 높이 ({ceiling}) 를 넘습니다.");
                if (o.Type == OpeningType.Door && o.Sill > 0) warnings.Add($"{at}: 문인데 sill 이 {o.Sill} 입니다. 보통 0 입니다.");

                if (!byWall.TryGetValue(wall.Id, out var list)) byWall[wall.Id] = list = new List<OpeningData>();
                list.Add(o);
            }

            // 한 벽 안에서 개구부끼리 겹치면 벽 조각을 만들 수 없다
            foreach (var pair in byWall)
            {
                var list = pair.Value;
                list.Sort((a, b) => a.Offset.CompareTo(b.Offset));
                for (int i = 1; i < list.Count; i++)
                {
                    var prev = list[i - 1];
                    if (list[i].Offset < prev.Offset + prev.Width)
                        errors.Add($"벽 {pair.Key}: 개구부 {prev.Id} 와 {list[i].Id} 가 겹칩니다.");
                }
            }
        }

        static void ValidateFloors(FloorPlanData d, List<string> errors, List<string> warnings)
        {
            if (d.Floors.Count == 0)
                warnings.Add("floors 가 비어 있습니다. 바닥이 없으면 텔레포트할 곳이 없습니다.");

            var ids = new HashSet<string>();
            for (int i = 0; i < d.Floors.Count; i++)
            {
                var f = d.Floors[i];
                string at = $"floors[{i}]";
                if (f == null) { errors.Add($"{at} 가 null 입니다."); continue; }
                if (!CheckId(f.Id, at, ids, errors)) continue;
                at = $"{at} ({f.Id})";

                if (f.Polygon == null || f.Polygon.Count < 3)
                {
                    errors.Add($"{at}: polygon 은 꼭짓점이 3개 이상이어야 합니다.");
                    continue;
                }

                bool pointsOk = true;
                for (int p = 0; p < f.Polygon.Count; p++)
                    pointsOk &= CheckPoint(f.Polygon[p], $"{at} polygon[{p}]", errors);
                if (!pointsOk) continue;

                for (int p = 0; p < f.Polygon.Count; p++)
                {
                    var a = f.Polygon[p];
                    var b = f.Polygon[(p + 1) % f.Polygon.Count];
                    if (a[0] == b[0] && a[1] == b[1])
                        errors.Add($"{at}: polygon[{p}] 와 다음 꼭짓점이 같은 점입니다.");
                }

                if (FindSelfIntersection(f.Polygon, out int e1, out int e2))
                    errors.Add($"{at}: polygon 변 {e1} 와 변 {e2} 가 교차합니다. 꼭짓점 순서를 확인하세요 (둘레를 따라 한 방향으로).");
                else if (SignedArea2(f.Polygon) == 0)
                    errors.Add($"{at}: polygon 넓이가 0 입니다 (꼭짓점이 한 줄에 있음).");
            }
        }

        static void ValidateFixtures(FloorPlanData d, int ceiling, List<string> errors, List<string> warnings)
        {
            var ids = new HashSet<string>();
            for (int i = 0; i < d.Fixtures.Count; i++)
            {
                var f = d.Fixtures[i];
                string at = $"fixtures[{i}]";
                if (f == null) { errors.Add($"{at} 가 null 입니다."); continue; }
                if (!CheckId(f.Id, at, ids, errors)) continue;
                at = $"{at} ({f.Id})";

                CheckPoint(f.Position, $"{at} position", errors);
                if (f.Size == null || f.Size.Length != 3)
                    errors.Add($"{at}: size 는 [W, D, H] 세 값이어야 합니다.");
                else if (f.Size[0] <= 0 || f.Size[1] <= 0 || f.Size[2] <= 0)
                    errors.Add($"{at}: size 의 값은 모두 0보다 커야 합니다 ([{string.Join(", ", f.Size)}]).");
                else if (ceiling > 0 && f.Size[2] > ceiling)
                    warnings.Add($"{at}: 높이 {f.Size[2]} 가 천장 높이 {ceiling} 보다 큽니다.");

                if (float.IsNaN(f.Rotation) || float.IsInfinity(f.Rotation))
                    errors.Add($"{at}: rotation 이 숫자가 아닙니다.");
            }
        }

        // ───────────── 도우미 ─────────────

        /// <summary>
        /// Newtonsoft 메시지 끝의 위치 정보(Path '...', line, position)는 남기고,
        /// 중간의 긴 "To fix this error ..." 설명은 뺀다.
        /// </summary>
        static string ShortJsonMessage(string message)
        {
            int fix = message.IndexOf("\nTo fix", StringComparison.Ordinal);
            int path = message.LastIndexOf("Path '", StringComparison.Ordinal);
            return fix >= 0 && path > fix ? message[..fix] + " " + message[path..] : message;
        }

        static bool CheckId(string id, string at, HashSet<string> ids, List<string> errors)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                errors.Add($"{at}: id 가 없습니다.");
                return false;
            }

            if (!ids.Add(id))
            {
                errors.Add($"{at}: id \"{id}\" 가 중복됩니다.");
                return false;
            }

            return true;
        }

        static bool CheckPoint(int[] point, string at, List<string> errors)
        {
            if (point != null && point.Length == 2) return true;
            errors.Add($"{at} 는 [x, y] 두 값이어야 합니다.");
            return false;
        }

        static double WallLength(WallData w)
        {
            double dx = w.End[0] - w.Start[0];
            double dy = w.End[1] - w.Start[1];
            return Math.Sqrt(dx * dx + dy * dy);
        }

        /// <summary>신발끈 공식 넓이 × 2 (부호 = 꼭짓점 방향). mm² 이라 long 으로 계산</summary>
        static long SignedArea2(List<int[]> polygon)
        {
            long sum = 0;
            for (int i = 0; i < polygon.Count; i++)
            {
                var a = polygon[i];
                var b = polygon[(i + 1) % polygon.Count];
                sum += (long)a[0] * b[1] - (long)b[0] * a[1];
            }

            return sum;
        }

        /// <summary>이웃하지 않는 두 변이 닿거나 교차하면 true. 귀 자르기 삼각분할은 꼬인 다각형을 처리하지 못한다</summary>
        static bool FindSelfIntersection(List<int[]> polygon, out int edgeA, out int edgeB)
        {
            int n = polygon.Count;
            for (int i = 0; i < n; i++)
            {
                for (int j = i + 1; j < n; j++)
                {
                    bool adjacent = j == i + 1 || (i == 0 && j == n - 1);
                    if (adjacent) continue;
                    if (SegmentsTouch(polygon[i], polygon[(i + 1) % n], polygon[j], polygon[(j + 1) % n]))
                    {
                        edgeA = i;
                        edgeB = j;
                        return true;
                    }
                }
            }

            edgeA = edgeB = -1;
            return false;
        }

        static bool SegmentsTouch(int[] p1, int[] p2, int[] q1, int[] q2)
        {
            long d1 = Cross(q1, q2, p1), d2 = Cross(q1, q2, p2);
            long d3 = Cross(p1, p2, q1), d4 = Cross(p1, p2, q2);
            if (((d1 > 0 && d2 < 0) || (d1 < 0 && d2 > 0)) && ((d3 > 0 && d4 < 0) || (d3 < 0 && d4 > 0)))
                return true;

            return (d1 == 0 && OnSegment(q1, q2, p1)) || (d2 == 0 && OnSegment(q1, q2, p2)) ||
                   (d3 == 0 && OnSegment(p1, p2, q1)) || (d4 == 0 && OnSegment(p1, p2, q2));
        }

        static long Cross(int[] o, int[] a, int[] b) =>
            (long)(a[0] - o[0]) * (b[1] - o[1]) - (long)(a[1] - o[1]) * (b[0] - o[0]);

        static bool OnSegment(int[] a, int[] b, int[] p) =>
            Math.Min(a[0], b[0]) <= p[0] && p[0] <= Math.Max(a[0], b[0]) &&
            Math.Min(a[1], b[1]) <= p[1] && p[1] <= Math.Max(a[1], b[1]);
    }
}
