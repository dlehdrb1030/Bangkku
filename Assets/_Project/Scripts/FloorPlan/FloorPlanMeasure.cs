using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using UnityEngine.Scripting;

namespace Bangkku.FloorPlan
{
    /// <summary>벽의 어느 면을 기준으로 잴지. Near/Far 는 상대 벽 쪽 / 반대쪽 면이다.</summary>
    public enum WallFace
    {
        /// <summary>벽 중심선</summary>
        [EnumMember(Value = "center")] Center,

        /// <summary>상대 벽을 향한 면 (두 벽 사이 빈 공간의 폭을 잴 때)</summary>
        [EnumMember(Value = "near")] Near,

        /// <summary>상대 벽 반대쪽 면 (바깥면까지 잴 때)</summary>
        [EnumMember(Value = "far")] Far,
    }

    [Preserve]
    public class DimensionEnd
    {
        [JsonProperty("wall")] public string Wall;

        [JsonProperty("at"), JsonConverter(typeof(StringEnumConverter))]
        public WallFace At = WallFace.Center;
    }

    /// <summary>두 벽 사이 거리 하나. expectedMm 이 없으면 비교 없이 측정값만 알려 준다 (실측할 곳을 미리 재 볼 때).</summary>
    [Preserve]
    public class DimensionEntry
    {
        [JsonProperty("id")] public string Id;
        [JsonProperty("label")] public string Label;
        [JsonProperty("from")] public DimensionEnd From;
        [JsonProperty("to")] public DimensionEnd To;

        /// <summary>도면에 적힌 값 또는 실측값 (mm)</summary>
        [JsonProperty("expectedMm")] public int? ExpectedMm;

        /// <summary>이 항목만 다른 허용 오차를 쓸 때 (mm)</summary>
        [JsonProperty("toleranceMm")] public int? ToleranceMm;
    }

    /// <summary>평면도 하나에 대한 치수 기준 목록 (예: oneroom_01.dimensions.json)</summary>
    [Preserve]
    public class DimensionFile
    {
        /// <summary>대상 평면도 이름 (floorplan.json 의 name)</summary>
        [JsonProperty("floorplan")] public string Floorplan;

        /// <summary>허용 오차 (mm). 팀장 가이드 완료 기준은 ±2cm</summary>
        [JsonProperty("toleranceMm")] public int ToleranceMm = 20;

        [JsonProperty("dimensions")] public List<DimensionEntry> Dimensions = new();
    }

    public enum DimensionStatus
    {
        /// <summary>허용 오차 안</summary>
        Pass,

        /// <summary>허용 오차 밖</summary>
        Fail,

        /// <summary>비교할 값이 없어 측정값만 구함</summary>
        Info,

        /// <summary>잴 수 없음 (없는 벽, 평행하지 않음 등)</summary>
        Error,
    }

    public class DimensionResult
    {
        public DimensionEntry Entry;
        public DimensionStatus Status;

        /// <summary>측정값 (mm). Error 면 0</summary>
        public double MeasuredMm;

        /// <summary>측정값 − 기준값 (mm). 기준값이 없으면 0</summary>
        public double DiffMm;

        public int ToleranceMm;

        /// <summary>Error 일 때 이유</summary>
        public string Message;
    }

    /// <summary>
    /// 평면도의 두 벽 사이 거리를 재서 도면·실측 치수와 비교한다 (팀장 가이드 2주차 "치수 비교 기능").
    /// 두 벽은 서로 평행해야 하고, 서로 평행한 두 직선 사이의 수직 거리를 잰다 (벽이 이어져 있지 않아도 된다).
    /// 계산은 mm 그대로 한다.
    /// </summary>
    public static class FloorPlanMeasure
    {
        static readonly JsonSerializerSettings Settings = new() { MissingMemberHandling = MissingMemberHandling.Error };

        /// <summary>기준 파일 JSON 을 읽고 검사한다. 실패하면 null 과 이유</summary>
        public static DimensionFile ParseFile(string json, out string error)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(json))
            {
                error = "내용이 비어 있습니다.";
                return null;
            }

            DimensionFile file;
            try
            {
                file = JsonConvert.DeserializeObject<DimensionFile>(json, Settings);
            }
            catch (JsonException e)
            {
                error = "JSON 형식 오류: " + e.Message;
                return null;
            }

            if (file == null)
            {
                error = "JSON 최상위가 객체({ ... })가 아닙니다.";
                return null;
            }

            if (string.IsNullOrWhiteSpace(file.Floorplan)) error = "floorplan(대상 평면도 이름)이 없습니다.";
            else if (file.ToleranceMm <= 0) error = $"toleranceMm 은 0보다 커야 합니다 (현재 {file.ToleranceMm}).";
            else if (file.Dimensions == null || file.Dimensions.Count == 0) error = "dimensions 가 비어 있습니다.";
            else
            {
                var ids = new HashSet<string>();
                for (int i = 0; i < file.Dimensions.Count && error == null; i++)
                {
                    var d = file.Dimensions[i];
                    string at = $"dimensions[{i}]";
                    if (d == null) error = $"{at} 가 null 입니다.";
                    else if (string.IsNullOrWhiteSpace(d.Id)) error = $"{at}: id 가 없습니다.";
                    else if (!ids.Add(d.Id)) error = $"{at}: id \"{d.Id}\" 가 중복됩니다.";
                    else if (d.From == null || string.IsNullOrEmpty(d.From.Wall)) error = $"{at} ({d.Id}): from.wall 이 없습니다.";
                    else if (d.To == null || string.IsNullOrEmpty(d.To.Wall)) error = $"{at} ({d.Id}): to.wall 이 없습니다.";
                    else if (d.ToleranceMm is <= 0) error = $"{at} ({d.Id}): toleranceMm 은 0보다 커야 합니다.";
                }
            }

            return error == null ? file : null;
        }

        /// <summary>기준 목록의 모든 항목을 재서 비교한다. 항목 순서가 결과 순서다.</summary>
        public static List<DimensionResult> Check(FloorPlanData plan, DimensionFile file)
        {
            var results = new List<DimensionResult>(file.Dimensions.Count);
            foreach (var entry in file.Dimensions)
            {
                int tolerance = entry.ToleranceMm ?? file.ToleranceMm;
                var result = new DimensionResult { Entry = entry, ToleranceMm = tolerance };

                if (!TryMeasure(plan, entry.From, entry.To, out double mm, out string error))
                {
                    result.Status = DimensionStatus.Error;
                    result.Message = error;
                }
                else
                {
                    result.MeasuredMm = mm;
                    if (entry.ExpectedMm is { } expected)
                    {
                        result.DiffMm = mm - expected;
                        result.Status = Math.Abs(result.DiffMm) <= tolerance ? DimensionStatus.Pass : DimensionStatus.Fail;
                    }
                    else
                    {
                        result.Status = DimensionStatus.Info;
                    }
                }

                results.Add(result);
            }

            return results;
        }

        /// <summary>
        /// 두 벽 사이의 거리 (mm). 두 벽이 평행하지 않거나 같은 직선이거나 없는 벽이면 false 와 이유.
        /// </summary>
        public static bool TryMeasure(FloorPlanData plan, DimensionEnd from, DimensionEnd to, out double mm, out string error)
        {
            mm = 0;
            var a = FindWall(plan, from.Wall);
            var b = FindWall(plan, to.Wall);
            if (a == null) { error = $"벽 \"{from.Wall}\" 이 없습니다."; return false; }
            if (b == null) { error = $"벽 \"{to.Wall}\" 이 없습니다."; return false; }
            if (a == b) { error = "from 과 to 가 같은 벽입니다."; return false; }

            Direction(a, out double ax, out double ay);
            Direction(b, out double bx, out double by);
            if (Math.Abs(ax * by - ay * bx) > 1e-6)
            {
                error = $"벽 {a.Id} 와 {b.Id} 가 평행하지 않습니다.";
                return false;
            }

            // a 의 법선 방향으로 b 중심선이 얼마나 떨어져 있는지 (부호 있음)
            double nx = -ay, ny = ax;
            double offset = (Center(b, 0) - Center(a, 0)) * nx + (Center(b, 1) - Center(a, 1)) * ny;
            if (Math.Abs(offset) < 1e-6)
            {
                error = $"벽 {a.Id} 와 {b.Id} 의 중심선이 같은 직선입니다.";
                return false;
            }

            double sign = Math.Sign(offset);
            double halfA = a.ThicknessOr(plan.DefaultWallThickness) * 0.5;
            double halfB = b.ThicknessOr(plan.DefaultWallThickness) * 0.5;
            double posA = Position(from.At, true, offset, halfA, sign);
            double posB = Position(to.At, false, offset, halfB, sign);

            // 두 면이 서로 엇갈리면(벽 두께가 겹침) 잴 수 없다
            if ((posB - posA) * sign <= 0)
            {
                error = $"벽 {a.Id} 의 {from.At} 면이 벽 {b.Id} 의 {to.At} 면과 같거나 겹칩니다.";
                return false;
            }

            mm = Math.Abs(posB - posA);
            error = null;
            return true;
        }

        /// <summary>
        /// 법선 방향 위치. 상대 벽 쪽이 Near: 첫 번째 벽에서는 sign 방향, 두 번째 벽에서는 그 반대 방향이다.
        /// </summary>
        static double Position(WallFace face, bool isFirst, double offset, double half, double sign)
        {
            double center = isFirst ? 0 : offset;
            double toward = isFirst ? sign : -sign;
            return face switch
            {
                WallFace.Near => center + toward * half,
                WallFace.Far => center - toward * half,
                _ => center,
            };
        }

        static WallData FindWall(FloorPlanData plan, string id) => plan.Walls.Find(w => w != null && w.Id == id);

        static double Center(WallData w, int axis) => (w.Start[axis] + w.End[axis]) * 0.5;

        static void Direction(WallData w, out double x, out double y)
        {
            double dx = w.End[0] - w.Start[0];
            double dy = w.End[1] - w.Start[1];
            double length = Math.Sqrt(dx * dx + dy * dy);
            x = dx / length;
            y = dy / length;
        }
    }
}
