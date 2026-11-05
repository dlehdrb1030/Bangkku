using System.Runtime.Serialization;

namespace Bangkku.Core
{
    /// <summary>
    /// 치수 신뢰도 (전체개발계획 5장).
    /// JSON 값은 소문자 문자열("measured" / "drawing" / "estimated")이다.
    /// Newtonsoft StringEnumConverter 가 EnumMember 값을 읽고 쓴다.
    /// </summary>
    public enum Confidence
    {
        /// <summary>줄자 실측</summary>
        [EnumMember(Value = "measured")] Measured,

        /// <summary>도면·상품 상세 치수</summary>
        [EnumMember(Value = "drawing")] Drawing,

        /// <summary>표준값으로 추정</summary>
        [EnumMember(Value = "estimated")] Estimated,
    }

    public static class ConfidenceExtensions
    {
        // 판정 여유값 (mm)
        public const int MeasuredMarginMm = 10;
        public const int DrawingMarginMm = 30;
        public const int EstimatedMarginMm = 100;

        /// <summary>값이 없거나 알 수 없을 때 쓰는 기본값. 가장 보수적인 Estimated.</summary>
        public const Confidence Default = Confidence.Estimated;

        public static int MarginMm(this Confidence confidence) => confidence switch
        {
            Confidence.Measured => MeasuredMarginMm,
            Confidence.Drawing => DrawingMarginMm,
            _ => EstimatedMarginMm,
        };

        public static float MarginMeters(this Confidence confidence) => Units.MmToM(confidence.MarginMm());

        /// <summary>JSON 문자열 값 ("measured" 등)</summary>
        public static string ToJsonValue(this Confidence confidence) => confidence switch
        {
            Confidence.Measured => "measured",
            Confidence.Drawing => "drawing",
            _ => "estimated",
        };

        /// <summary>JSON 문자열 → Confidence. 알 수 없는 값이면 false 와 Default 를 돌려준다.</summary>
        public static bool TryParse(string value, out Confidence confidence)
        {
            switch (value?.Trim().ToLowerInvariant())
            {
                case "measured": confidence = Confidence.Measured; return true;
                case "drawing": confidence = Confidence.Drawing; return true;
                case "estimated": confidence = Confidence.Estimated; return true;
                default: confidence = Default; return false;
            }
        }
    }
}
