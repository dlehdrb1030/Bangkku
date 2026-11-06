using System.Collections.Generic;
using System.Text;
using Bangkku.Core;
using Bangkku.Furniture;
using Bangkku.Placement;
using UnityEngine;
using UnityEngine.XR;

namespace Bangkku.UI
{
    /// <summary>
    /// 상태 패널 (팀장 가이드 3주차 ⑦): 선택한 가구의 정보를 한곳에 보여 준다.
    /// - 이름 (id), 종류(스캔·메쉬·박스), 상태(여유·좁음·겹침), 판정 치수 W×D×H, 치수 신뢰도와 판정 여유값
    /// - 위치 (평면도 mm), 회전, 가장 가까운 벽·가구·고정설비와 그 대상의 신뢰도
    /// - 좁음이면 판정 근거 (PlacementValidator.DescribeTight), 겹치면 겹친 대상
    /// - 스캔 가구면 크기 보정 오차, 스캔 안 된 면
    /// 데스크톱: 화면 왼쪽 아래 패널 (IMGUI). VR 기기가 동작 중이면 숨긴다.
    /// VR: 손목 메뉴(VRFurnitureMenu)의 선택 줄이 CompactText 로 같은 정보를 줄여서 보여 준다.
    /// 씬에 두지 않아도 씬이 열릴 때 하나 만들어진다. 직접 두면 Inspector 에서 값을 바꿀 수 있다.
    /// </summary>
    public class StatusPanel : MonoBehaviour
    {
        [Tooltip("VR 기기가 동작 중이면 숨긴다")]
        public bool HideWhenXRActive = true;

        [Header("모양")]
        public float PanelWidth = 520f;

        public int FontSize = 15;

        static Rect _panelRect;
        static bool _panelVisible;

        /// <summary>화면 좌표(Input System 마우스 위치, 아래가 0)가 패널 위인지. DesktopController 가 클릭을 막는 데 쓴다</summary>
        public static bool IsPointerOver(Vector2 screenPosition) =>
            _panelVisible && _panelRect.Contains(new Vector2(screenPosition.x, Screen.height - screenPosition.y));

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => _panelVisible = false;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void CreateIfMissing()
        {
            if (FindAnyObjectByType<StatusPanel>() == null) new GameObject(nameof(StatusPanel)).AddComponent<StatusPanel>();
        }

        readonly List<Collider> _overlaps = new();
        readonly List<string> _lines = new();
        string _text = string.Empty;
        GUIStyle _style, _box;

        bool Hidden => HideWhenXRActive && XRSettings.isDeviceActive;

        void OnDisable() => _panelVisible = false;

        void LateUpdate()
        {
            // 판정은 Update 에서 끝나므로 LateUpdate 에서 이번 프레임 값으로 글을 만든다 (OnGUI 는 한 프레임에 여러 번 불린다)
            // 가구가 없는 씬(Test_FloorPlan 등)에서는 SelectionManager·PlacementValidator 를 만들지 않도록 먼저 본다
            if (Hidden || FurnitureItem.All.Count == 0)
            {
                _text = string.Empty;
                return;
            }

            var item = SelectionManager.Instance.Selected;
            if (item == null)
            {
                _text = "선택한 가구 없음. 가구를 클릭하거나 Tab 메뉴에서 고르세요.";
                return;
            }

            BuildLines(item, _lines);
            _text = string.Join("\n", _lines);
        }

        void OnGUI()
        {
            if (_text.Length == 0)
            {
                _panelVisible = false;
                return;
            }

            EnsureStyles();
            var content = new GUIContent(_text);
            float height = _style.CalcHeight(content, PanelWidth - _box.padding.horizontal) + _box.padding.vertical;
            _panelRect = new Rect(8f, Screen.height - height - 8f, PanelWidth, height);
            _panelVisible = true;
            GUI.Box(_panelRect, content, _box);
        }

        void EnsureStyles()
        {
            if (_style != null) return;
            _style = new GUIStyle(GUI.skin.label) { fontSize = FontSize, wordWrap = true, richText = true };
            _box = new GUIStyle(GUI.skin.box)
            {
                fontSize = FontSize,
                wordWrap = true,
                richText = true,
                alignment = TextAnchor.UpperLeft,
                padding = new RectOffset(12, 12, 10, 10),
                normal = { textColor = Color.white },
            };
        }

        // ───────────── 내용 ─────────────

        /// <summary>데스크톱 패널에 쓰는 여러 줄 정보</summary>
        void BuildLines(FurnitureItem item, List<string> lines)
        {
            lines.Clear();
            var validator = PlacementValidator.Instance;
            var data = item.Data;

            lines.Add($"<b>{FurnitureMenu.DisplayName(item)}</b> ({item.ItemId}){(data != null ? $" · {SourceText(data.Source)}" : "")}");
            lines.Add($"상태: <b>{FurnitureMenu.StateText(item.State)}</b>");
            lines.Add($"치수: {SizeText(item.SizeMeters)}");
            lines.Add($"신뢰도: {ConfidenceText(item.SizeConfidence)}");

            var plan = Units.WorldToPlan(item.transform.position);
            lines.Add($"위치: ({plan.x}, {plan.y}) mm · 회전 {Mathf.RoundToInt(item.transform.eulerAngles.y) % 360}°");

            lines.Add($"가장 가까운 대상: {NearestText(item, validator)}");

            if (item.State == PlacementState.Overlap)
            {
                validator.FindOverlaps(item, _overlaps);
                if (_overlaps.Count > 0) lines.Add($"겹친 대상: {OverlapText(_overlaps)}");
            }

            if (validator.TryGetTight(item, out var tight) && tight.HasValue)
                lines.Add($"좁음 판정 {PlacementValidator.RuleLetter(validator.TightRule)}: " +
                          PlacementValidator.DescribeTight(tight, Units.MmToM(validator.MinTightGapMm)));

            string scan = ScanText(data);
            if (scan.Length > 0) lines.Add(scan);
        }

        /// <summary>
        /// VR 손목 메뉴에 쓰는 짧은 정보 (3줄).
        /// "선택: 2인 소파 (sofa_01) 좁음" / "W 1500 × D 800 × H 800 mm · 실측 ±10mm" / "가장 가까운 대상: 벽 w3_0 32mm"
        /// </summary>
        public static string CompactText(FurnitureItem item)
        {
            if (item == null) return "선택: 없음";
            var sb = new StringBuilder();
            sb.Append($"선택: {FurnitureMenu.DisplayName(item)} ({item.ItemId}) {FurnitureMenu.StateText(item.State)}\n");
            sb.Append($"{SizeText(item.SizeMeters)} · {ConfidenceLabel.ShortText(item.SizeConfidence)}\n");
            sb.Append($"가장 가까운 대상: {NearestText(item, PlacementValidator.Instance, false)}");
            return sb.ToString();
        }

        /// <summary>"벽 w3_0 32mm (도면 ±30mm)". MaxGap 안에 아무것도 없으면 "1m 안에 없음"</summary>
        static string NearestText(FurnitureItem item, PlacementValidator validator, bool withConfidence = true)
        {
            if (!validator.TryGetGap(item, out var gap) || !gap.HasTarget) return $"{validator.MaxGapMeters:0.#}m 안에 없음";
            string target = TargetText(DistanceLabel.TargetKind(gap.Target), DistanceLabel.TargetName(gap.Target));
            string text = $"{target} {Units.MToMm(gap.Meters)}mm";
            return withConfidence ? $"{text} ({ConfidenceLabel.ShortText(gap.TargetConfidence)})" : text;
        }

        static string OverlapText(List<Collider> overlaps)
        {
            var names = new List<string>();
            foreach (var collider in overlaps)
            {
                string name = TargetText(DistanceLabel.TargetKind(collider), DistanceLabel.TargetName(collider));
                if (!names.Contains(name)) names.Add(name);
            }

            return string.Join(", ", names);
        }

        // ───────────── 글 만들기 (테스트 가능한 순수 함수) ─────────────

        /// <summary>판정 치수 "W 1500 × D 800 × H 800 mm". size 는 Unity 축 순서 (W=X, H=Y, D=Z) 의 m</summary>
        public static string SizeText(Vector3 sizeMeters) =>
            $"W {Units.MToMm(sizeMeters.x)} × D {Units.MToMm(sizeMeters.z)} × H {Units.MToMm(sizeMeters.y)} mm";

        /// <summary>"실측 (판정 여유 ±10mm)"</summary>
        public static string ConfidenceText(Confidence confidence) =>
            $"{ConfidenceLabel.Name(confidence)} (판정 여유 ±{confidence.MarginMm()}mm)";

        public static string SourceText(FurnitureSource source) => source switch
        {
            FurnitureSource.Scan => "3DGS 스캔",
            FurnitureSource.Mesh => "대체 메쉬",
            _ => "박스",
        };

        /// <summary>"벽 w3_0", "가구 책상", "고정설비 kitchen"</summary>
        public static string TargetText(DistanceLabel.Kind kind, string name) => kind switch
        {
            DistanceLabel.Kind.Wall => $"벽 {name}",
            DistanceLabel.Kind.Furniture => $"가구 {name}",
            DistanceLabel.Kind.Fixture => $"고정설비 {name}",
            _ => name,
        };

        /// <summary>
        /// 스캔 가구의 보정 정보 "크기 보정 오차 2.1% · 스캔 안 된 면: back". 해당 정보가 없으면 빈 문자열
        /// </summary>
        public static string ScanText(CatalogItem data)
        {
            if (data == null) return string.Empty;
            var parts = new List<string>();
            if (data.FitError.HasValue) parts.Add($"크기 보정 오차 {data.FitError.Value * 100f:0.#}%");
            if (data.UnscannedFaces != null && data.UnscannedFaces.Count > 0)
                parts.Add($"스캔 안 된 면: {string.Join(", ", data.UnscannedFaces)}");
            return string.Join(" · ", parts);
        }
    }
}
