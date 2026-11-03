using System.Collections.Generic;
using System.Runtime.Serialization;
using Bangkku.Core;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using UnityEngine;
using UnityEngine.Scripting;

namespace Bangkku.Furniture
{
    /// <summary>
    /// 가구 카탈로그 catalog.json (전체개발계획 7.2). 팀원 관리, 클래스는 공용.
    /// FurnitureFactory 가 읽어서 FurnitureItem.Init(CatalogItem) 에 넘긴다.
    /// </summary>
    [Preserve]
    public class CatalogData
    {
        [JsonProperty("items")] public List<CatalogItem> Items = new();
    }

    public enum FurnitureSource
    {
        /// <summary>3DGS 스캔</summary>
        [EnumMember(Value = "scan")] Scan,

        /// <summary>대체 메쉬</summary>
        [EnumMember(Value = "mesh")] Mesh,

        /// <summary>치수만 있는 박스</summary>
        [EnumMember(Value = "box")] Box,
    }

    [Preserve]
    public class CatalogItem
    {
        /// <summary>에셋 ID (소문자_번호, 예: sofa_01)</summary>
        [JsonProperty("id")] public string Id;
        [JsonProperty("name")] public string Name;

        [JsonProperty("source"), JsonConverter(typeof(StringEnumConverter))]
        public FurnitureSource Source;

        /// <summary>프리팹 경로 (예: Prefabs/Furniture/sofa_01). box 는 없음</summary>
        [JsonProperty("prefab")] public string Prefab;

        /// <summary>판정 기준 치수 (mm)</summary>
        [JsonProperty("size")] public SizeMm Size = new();

        [JsonProperty("sizeConfidence"), JsonConverter(typeof(StringEnumConverter))]
        public Confidence SizeConfidence = ConfidenceExtensions.Default;

        /// <summary>스플랫에서 잰 치수 (mm). scan 만 해당, 없으면 null</summary>
        [JsonProperty("splatMeasuredSize")] public SizeMm SplatMeasuredSize;

        /// <summary>크기 보정 오차 비율 (0.021 = 2.1%). 없으면 null</summary>
        [JsonProperty("fitError")] public float? FitError;

        /// <summary>스캔하지 못한 면 (예: "back")</summary>
        [JsonProperty("unscannedFaces")] public List<string> UnscannedFaces = new();

        /// <summary>박스 색 "#RRGGBB". box 만 해당</summary>
        [JsonProperty("color")] public string Color;

        public bool TryGetColor(out Color color)
        {
            color = UnityEngine.Color.white;
            return !string.IsNullOrEmpty(Color) && ColorUtility.TryParseHtmlString(Color, out color);
        }
    }

    /// <summary>W = X, D = Z, H = Y (mm)</summary>
    [Preserve]
    public class SizeMm
    {
        [JsonProperty("w")] public int W;
        [JsonProperty("d")] public int D;
        [JsonProperty("h")] public int H;

        /// <summary>Unity 축 순서 (W, H, D) 의 m 단위 크기. FurnitureItem.SizeMeters 와 같은 순서</summary>
        public Vector3 ToMeters() => new(Units.MmToM(W), Units.MmToM(H), Units.MmToM(D));
    }
}
