using System.Collections.Generic;
using Bangkku.Core;
using Bangkku.Placement;
using UnityEngine;
using UnityEngine.UI;

namespace Bangkku.UI
{
    /// <summary>
    /// 치수 신뢰도 라벨 (팀장 가이드 3주차 ⑦): 가구 윗면 위에 "실측 ±10mm" / "도면 ±30mm" / "추정 ±100mm" 작은 꼬리표를 띄운다.
    /// - 바탕색: 실측 청록 / 도면 파랑 / 추정 회색. 상태 색(여유 초록·좁음 노랑·겹침 빨강)과 겹치지 않게 골랐다
    /// - 자리: LabelAnchor(박스 윗면 중앙)에서 LiftMeters 위. LabelAnchor 가 없으면 원점 + 높이
    /// - 글자는 World Space Canvas. 카메라와 같은 방향으로 돌리고 거리에 비례해 키운다 (DistanceLabel 과 같은 방식)
    /// - Mode: 모든 가구 / 선택한 가구만 / 끄기. 데스크톱에서는 L 키로 바꾼다
    /// 꼬리표는 이 오브젝트 아래에 만든다 (가구 프리팹·팀원 Variant 를 건드리지 않도록).
    /// 씬에 두지 않아도 씬이 열릴 때 하나 만들어진다. 직접 두면 Inspector 에서 값을 바꿀 수 있다.
    /// </summary>
    public class ConfidenceLabel : MonoBehaviour
    {
        public enum ShowMode
        {
            [InspectorName("모든 가구")] All,
            [InspectorName("선택한 가구만")] Selected,
            [InspectorName("끄기")] Off,
        }

        public static readonly Color MeasuredColor = new(0.10f, 0.55f, 0.55f, 0.85f);
        public static readonly Color DrawingColor = new(0.22f, 0.38f, 0.78f, 0.85f);
        public static readonly Color EstimatedColor = new(0.40f, 0.40f, 0.44f, 0.85f);

        [Tooltip("어떤 가구에 꼬리표를 띄울지. 데스크톱에서는 L 키로 바꾼다")]
        public ShowMode Mode = ShowMode.All;

        [Tooltip("꼬리표를 가구 윗면에서 이만큼(m) 띄운다")]
        public float LiftMeters = 0.04f;

        [Tooltip("카메라에서 1m 떨어졌을 때 글자 높이 (m). 거리에 비례해 커진다")]
        public float TextHeightPerMeter = 0.02f;

        [Tooltip("글자 높이 최소·최대 (m)")]
        public float MinTextHeight = 0.02f, MaxTextHeight = 0.4f;

        /// <summary>L 키로 바꾼 표시 방식을 DesktopController 안내에 쓴다</summary>
        public static string ModeText(ShowMode mode) => mode switch
        {
            ShowMode.All => "모든 가구",
            ShowMode.Selected => "선택한 가구만",
            _ => "끄기",
        };

        // Canvas 픽셀 크기와 글자 크기. 실제 크기는 localScale 로 정한다
        const float CanvasWidth = 320f, CanvasHeight = 72f;
        const int FontSize = 48;

        class Tag
        {
            public GameObject Root;
            public RectTransform Rect;
            public Image Background;
            public Text Text;
            public Confidence Shown = (Confidence)(-1);
            public bool Seen;
        }

        readonly Dictionary<FurnitureItem, Tag> _tags = new();
        readonly List<FurnitureItem> _removed = new();
        Font _font;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void CreateIfMissing()
        {
            if (FindAnyObjectByType<ConfidenceLabel>() == null) new GameObject(nameof(ConfidenceLabel)).AddComponent<ConfidenceLabel>();
        }

        void Awake() => _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        /// <summary>모든 가구 → 선택한 가구만 → 끄기 → 모든 가구</summary>
        public void CycleMode()
        {
            Mode = NextMode(Mode);
            Debug.Log($"[방꾸] 신뢰도 라벨: {ModeText(Mode)}", this);
        }

        public static ShowMode NextMode(ShowMode mode) => (ShowMode)(((int)mode + 1) % 3);

        void LateUpdate()
        {
            foreach (var tag in _tags.Values) tag.Seen = false;

            // 가구가 없는 씬(Test_FloorPlan 등)에서는 SelectionManager 를 만들지 않도록 먼저 본다
            var cam = Camera.main;
            if (FurnitureItem.All.Count > 0 && cam != null && Mode != ShowMode.Off)
            {
                var selected = SelectionManager.Instance.Selected;
                foreach (var item in FurnitureItem.All)
                {
                    if (item == null) continue;
                    if (Mode == ShowMode.Selected && item != selected) continue;
                    var tag = GetOrCreate(item);
                    tag.Seen = true;
                    UpdateTag(tag, item, cam);
                }
            }

            // 이번 프레임에 그리지 않은 꼬리표는 숨기고, 가구가 지워졌으면 꼬리표도 지운다
            _removed.Clear();
            foreach (var pair in _tags)
            {
                if (pair.Value.Seen) continue;
                if (pair.Key == null) _removed.Add(pair.Key);
                else if (pair.Value.Root.activeSelf) pair.Value.Root.SetActive(false);
            }

            foreach (var key in _removed)
            {
                Destroy(_tags[key].Root);
                _tags.Remove(key);
            }
        }

        void UpdateTag(Tag tag, FurnitureItem item, Camera cam)
        {
            if (!tag.Root.activeSelf) tag.Root.SetActive(true);

            if (tag.Shown != item.SizeConfidence)
            {
                tag.Shown = item.SizeConfidence;
                tag.Text.text = ShortText(item.SizeConfidence);
                tag.Background.color = ColorOf(item.SizeConfidence);
            }

            var position = TopCenter(item) + Vector3.up * LiftMeters;
            tag.Rect.position = position;
            // 카메라와 같은 방향을 보게 하면 VR(옆에서 봄)과 데스크톱(위에서 내려다봄) 모두 글자가 바로 읽힌다
            tag.Rect.rotation = cam.transform.rotation;
            float height = DistanceLabel.TextHeight(Vector3.Distance(cam.transform.position, position),
                TextHeightPerMeter, MinTextHeight, MaxTextHeight);
            tag.Rect.localScale = Vector3.one * (height / CanvasHeight);
        }

        /// <summary>LabelAnchor(박스 윗면 중앙). 없으면 가구 원점 + 높이</summary>
        static Vector3 TopCenter(FurnitureItem item) =>
            item.LabelAnchor != null
                ? item.LabelAnchor.position
                : item.transform.position + item.transform.up * item.SizeMeters.y;

        Tag GetOrCreate(FurnitureItem item)
        {
            if (_tags.TryGetValue(item, out var tag)) return tag;

            var root = new GameObject($"Confidence_{item.ItemId}", typeof(RectTransform));
            root.transform.SetParent(transform, false);
            var canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            // 버튼이 없으므로 GraphicRaycaster 는 붙이지 않는다 (VR 레이·마우스 선택을 가로막지 않도록)
            var rect = (RectTransform)root.transform;
            rect.sizeDelta = new Vector2(CanvasWidth, CanvasHeight);

            var background = new GameObject("Background", typeof(RectTransform)).AddComponent<Image>();
            background.transform.SetParent(rect, false);
            Stretch(background.rectTransform);
            background.raycastTarget = false;

            var text = new GameObject("Text", typeof(RectTransform)).AddComponent<Text>();
            text.transform.SetParent(rect, false);
            Stretch(text.rectTransform);
            text.font = _font;
            text.fontSize = FontSize;
            text.alignment = TextAnchor.MiddleCenter;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.color = Color.white;
            text.raycastTarget = false;

            tag = new Tag { Root = root, Rect = rect, Background = background, Text = text };
            _tags.Add(item, tag);
            return tag;
        }

        static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        /// <summary>"실측" / "도면" / "추정"</summary>
        public static string Name(Confidence confidence) => confidence switch
        {
            Confidence.Measured => "실측",
            Confidence.Drawing => "도면",
            _ => "추정",
        };

        /// <summary>"실측 ±10mm" (판정 여유값)</summary>
        public static string ShortText(Confidence confidence) => $"{Name(confidence)} ±{confidence.MarginMm()}mm";

        public static Color ColorOf(Confidence confidence) => confidence switch
        {
            Confidence.Measured => MeasuredColor,
            Confidence.Drawing => DrawingColor,
            _ => EstimatedColor,
        };
    }
}
