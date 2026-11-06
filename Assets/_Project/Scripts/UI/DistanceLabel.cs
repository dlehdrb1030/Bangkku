using Bangkku.Core;
using Bangkku.FloorPlan;
using Bangkku.Placement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace Bangkku.UI
{
    /// <summary>
    /// 거리 라벨 (팀장 가이드 3주차 ⑤): 선택한 가구에서 가장 가까운 벽·가구·고정설비까지 선을 긋고 "벽까지 32mm" 를 띄운다.
    /// - 값은 PlacementValidator.TryGetGap (가구 4면 중 가장 가까운 간격, 두 점). 1m 안에 아무것도 없으면 숨긴다
    /// - 선 색은 선택한 가구의 상태 색 (PlacementStateView 의 재질: 여유 초록 / 좁음 노랑 / 겹침 빨강)
    /// - 글자는 World Space Canvas. 카메라와 같은 방향으로 돌려 VR·데스크톱 모두 정면으로 읽힌다
    /// - 화면에서 비슷한 크기로 보이도록 카메라와의 거리에 비례해 키운다 (위에서 내려다보는 데스크톱 카메라에서도 읽히게)
    /// - 한글은 Unity 기본 글꼴(LegacyRuntime)이 OS 글꼴로 대신 그린다 (TextMeshPro 한글 글꼴 에셋이 없어도 됨)
    /// 씬에 두지 않아도 씬이 열릴 때 하나 만들어진다. 직접 두면 Inspector 에서 값을 바꿀 수 있다.
    /// </summary>
    public class DistanceLabel : MonoBehaviour
    {
        [Tooltip("라벨을 가구 윗면에서 이만큼(m) 띄운다")]
        public float LiftMeters = 0.15f;

        [Tooltip("카메라에서 1m 떨어졌을 때 라벨 글자 높이 (m). 거리에 비례해 커진다")]
        public float TextHeightPerMeter = 0.03f;

        [Tooltip("라벨 글자 높이 최소·최대 (m)")]
        public float MinTextHeight = 0.03f, MaxTextHeight = 0.6f;

        [Tooltip("선 굵기 (m)")]
        public float LineWidthMeters = 0.01f;

        [Tooltip("글자 색")]
        public Color TextColor = Color.white;

        [Tooltip("글자 뒤 배경 색 (반투명)")]
        public Color BackgroundColor = new(0f, 0f, 0f, 0.6f);

        // Canvas 픽셀 크기와 글자 크기. 실제 크기는 localScale 로 정한다
        const float CanvasWidth = 640f, CanvasHeight = 96f;
        const int FontSize = 64;

        LineRenderer _line;
        RectTransform _canvas;
        Text _text;
        GameObject _canvasObject;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void CreateIfMissing()
        {
            if (FindAnyObjectByType<DistanceLabel>() == null) new GameObject(nameof(DistanceLabel)).AddComponent<DistanceLabel>();
        }

        void Awake() => Build();

        void Build()
        {
            var lineObject = new GameObject("Line");
            lineObject.transform.SetParent(transform, false);
            _line = lineObject.AddComponent<LineRenderer>();
            _line.useWorldSpace = true;
            _line.positionCount = 2;
            _line.numCapVertices = 2;
            _line.shadowCastingMode = ShadowCastingMode.Off;
            _line.receiveShadows = false;
            _line.lightProbeUsage = LightProbeUsage.Off;
            _line.reflectionProbeUsage = ReflectionProbeUsage.Off;

            _canvasObject = new GameObject("Canvas", typeof(RectTransform));
            _canvasObject.transform.SetParent(transform, false);
            var canvas = _canvasObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            // 버튼이 없으므로 GraphicRaycaster 는 붙이지 않는다 (VR 레이·마우스 선택을 가로막지 않도록)
            _canvas = (RectTransform)_canvasObject.transform;
            _canvas.sizeDelta = new Vector2(CanvasWidth, CanvasHeight);

            var background = new GameObject("Background", typeof(RectTransform)).AddComponent<Image>();
            background.transform.SetParent(_canvas, false);
            Stretch(background.rectTransform);
            background.color = BackgroundColor;
            background.raycastTarget = false;

            _text = new GameObject("Text", typeof(RectTransform)).AddComponent<Text>();
            _text.transform.SetParent(_canvas, false);
            Stretch(_text.rectTransform);
            _text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            _text.fontSize = FontSize;
            _text.alignment = TextAnchor.MiddleCenter;
            _text.horizontalOverflow = HorizontalWrapMode.Overflow;
            _text.color = TextColor;
            _text.raycastTarget = false;

            SetVisible(false);
        }

        static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        void LateUpdate()
        {
            // 판정은 Update 에서 끝나므로 LateUpdate 에서 이번 프레임 값을 그린다
            // 가구가 없는 씬(Test_FloorPlan 등)에서는 SelectionManager·PlacementValidator 를 만들지 않도록 먼저 본다
            if (FurnitureItem.All.Count == 0)
            {
                SetVisible(false);
                return;
            }

            var item = SelectionManager.Instance.Selected;
            var cam = Camera.main;
            if (item == null || cam == null || !PlacementValidator.Instance.TryGetGap(item, out var gap) || !gap.HasTarget)
            {
                SetVisible(false);
                return;
            }

            SetVisible(true);

            _line.SetPosition(0, gap.From);
            _line.SetPosition(1, gap.To);
            _line.widthMultiplier = LineWidthMeters;
            var view = item.GetComponent<PlacementStateView>();
            var material = view != null ? view.LineMaterial(item.State) : null;
            _line.enabled = material != null;
            if (material != null) _line.sharedMaterial = material;

            float topY = item.transform.position.y + item.SizeMeters.y;
            var position = LabelPosition(gap.From, gap.To, topY, LiftMeters);
            _canvas.position = position;
            // 카메라와 같은 방향을 보게 하면 VR(옆에서 봄)과 데스크톱(위에서 내려다봄) 모두 글자가 바로 읽힌다
            _canvas.rotation = cam.transform.rotation;
            float height = TextHeight(Vector3.Distance(cam.transform.position, position), TextHeightPerMeter, MinTextHeight, MaxTextHeight);
            _canvas.localScale = Vector3.one * (height / CanvasHeight);

            _text.text = LabelText(TargetKind(gap.Target), TargetName(gap.Target), Units.MToMm(gap.Meters));
            _text.color = TextColor;
        }

        void SetVisible(bool visible)
        {
            if (_line.enabled != visible) _line.enabled = visible;
            if (_canvasObject.activeSelf != visible) _canvasObject.SetActive(visible);
        }

        public enum Kind { Wall, Furniture, Fixture, Other }

        static Kind TargetKind(Collider target)
        {
            int layer = target.gameObject.layer;
            if (layer == Layers.Wall) return Kind.Wall;
            if (layer == Layers.Fixture) return Kind.Fixture;
            if (layer == Layers.Furniture) return Kind.Furniture;
            return Kind.Other;
        }

        /// <summary>가구는 카탈로그 이름(없으면 id), 고정설비는 id, 그 외는 오브젝트 이름</summary>
        static string TargetName(Collider target)
        {
            var furniture = target.GetComponentInParent<FurnitureItem>();
            if (furniture != null) return furniture.Data != null && !string.IsNullOrEmpty(furniture.Data.Name) ? furniture.Data.Name : furniture.ItemId;
            var structure = target.GetComponentInParent<StructureConfidence>();
            return structure != null && !string.IsNullOrEmpty(structure.SourceId) ? structure.SourceId : target.name;
        }

        /// <summary>"벽까지 32mm", "책상까지 120mm", "고정설비 kitchen까지 50mm"</summary>
        public static string LabelText(Kind kind, string name, int mm) => kind switch
        {
            Kind.Wall => $"벽까지 {mm}mm",
            Kind.Fixture => $"고정설비 {name}까지 {mm}mm",
            _ => $"{name}까지 {mm}mm",
        };

        /// <summary>두 점의 가운데 위, 가구 윗면과 선 중 높은 쪽에서 lift 만큼 띄운 자리 (월드, m)</summary>
        public static Vector3 LabelPosition(Vector3 from, Vector3 to, float furnitureTopY, float lift)
        {
            var mid = (from + to) * 0.5f;
            mid.y = Mathf.Max(furnitureTopY, mid.y) + lift;
            return mid;
        }

        /// <summary>카메라와의 거리에 비례한 글자 높이 (m). 화면에서 비슷한 크기로 보이게 한다</summary>
        public static float TextHeight(float cameraDistance, float heightPerMeter, float min, float max) =>
            Mathf.Clamp(cameraDistance * heightPerMeter, min, max);
    }
}
