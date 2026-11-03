using System.Collections.Generic;
using System.Runtime.Serialization;
using Bangkku.Core;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using UnityEngine;
using UnityEngine.Scripting;

namespace Bangkku.FloorPlan
{
    /// <summary>
    /// 평면도 floorplan.json (전체개발계획 7.1). 팀장 관리.
    /// 길이는 모두 mm 정수 그대로 보관한다. m 변환은 쓰는 쪽에서 Units 로 한다.
    /// 좌표 [x, y] 는 평면도 좌표이며 Units.PlanToWorld 로 Unity 좌표가 된다.
    /// </summary>
    [Preserve]
    public class FloorPlanData
    {
        [JsonProperty("name")] public string Name;
        [JsonProperty("unit")] public string Unit = "mm";
        [JsonProperty("ceilingHeight")] public ConfidentValue CeilingHeight = new();
        [JsonProperty("defaultWallThickness")] public int DefaultWallThickness;
        [JsonProperty("walls")] public List<WallData> Walls = new();
        [JsonProperty("openings")] public List<OpeningData> Openings = new();
        [JsonProperty("floors")] public List<FloorData> Floors = new();
        [JsonProperty("fixtures")] public List<FixtureData> Fixtures = new();
    }

    /// <summary>값(mm)과 신뢰도 쌍. 예: { "value": 2400, "confidence": "estimated" }</summary>
    [Preserve]
    public class ConfidentValue
    {
        [JsonProperty("value")] public int Value;

        [JsonProperty("confidence"), JsonConverter(typeof(StringEnumConverter))]
        public Confidence Confidence = ConfidenceExtensions.Default;
    }

    [Preserve]
    public class WallData
    {
        [JsonProperty("id")] public string Id;

        /// <summary>벽 중심선 시작점 [x, y] (mm)</summary>
        [JsonProperty("start")] public int[] Start;

        /// <summary>벽 중심선 끝점 [x, y] (mm)</summary>
        [JsonProperty("end")] public int[] End;

        /// <summary>생략하면 FloorPlanData.DefaultWallThickness 를 쓴다. ThicknessOr 로 읽는다.</summary>
        [JsonProperty("thickness")] public int? Thickness;

        [JsonProperty("confidence"), JsonConverter(typeof(StringEnumConverter))]
        public Confidence Confidence = ConfidenceExtensions.Default;

        public int ThicknessOr(int defaultThickness) => Thickness ?? defaultThickness;
    }

    public enum OpeningType
    {
        [EnumMember(Value = "door")] Door,
        [EnumMember(Value = "window")] Window,
    }

    [Preserve]
    public class OpeningData
    {
        [JsonProperty("id")] public string Id;
        [JsonProperty("wallId")] public string WallId;

        [JsonProperty("type"), JsonConverter(typeof(StringEnumConverter))]
        public OpeningType Type;

        /// <summary>벽 start 에서 개구부 시작까지 거리 (mm)</summary>
        [JsonProperty("offset")] public int Offset;
        [JsonProperty("width")] public int Width;

        /// <summary>바닥에서 개구부 아래끝까지 (mm). 문은 0</summary>
        [JsonProperty("sill")] public int Sill;

        /// <summary>바닥에서 개구부 위끝까지 (mm)</summary>
        [JsonProperty("head")] public int Head;
    }

    [Preserve]
    public class FloorData
    {
        [JsonProperty("id")] public string Id;
        [JsonProperty("material")] public string Material;

        /// <summary>바닥 다각형 꼭짓점 [[x, y], ...] (mm)</summary>
        [JsonProperty("polygon")] public List<int[]> Polygon = new();
    }

    [Preserve]
    public class FixtureData
    {
        [JsonProperty("id")] public string Id;
        [JsonProperty("type")] public string Type;

        /// <summary>바닥면 중앙 [x, y] (mm)</summary>
        [JsonProperty("position")] public int[] Position;

        /// <summary>[W, D, H] (mm). W = X, D = Z, H = Y</summary>
        [JsonProperty("size")] public int[] Size;

        /// <summary>Y축 회전 (도)</summary>
        [JsonProperty("rotation")] public float Rotation;

        /// <summary>Unity 축 순서 (W, H, D) 의 m 단위 크기</summary>
        public Vector3 SizeMeters() => new(Units.MmToM(Size[0]), Units.MmToM(Size[2]), Units.MmToM(Size[1]));
    }
}
