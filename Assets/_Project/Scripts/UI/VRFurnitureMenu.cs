using System.Collections.Generic;
using Bangkku.Core;
using Bangkku.FloorPlan;
using Bangkku.Furniture;
using Bangkku.IO;
using Bangkku.Placement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.UI;

namespace Bangkku.UI
{
    /// <summary>
    /// 가구 메뉴 - VR 손목 메뉴 (팀장 가이드 3주차 ⑥). 왼손 컨트롤러 위에 떠 있는 패널을 오른손 레이로 누른다.
    /// VR 에서는 글자 입력이 어려워서 치수는 ± 버튼으로 바꾼다.
    /// - 탭: "카탈로그 가구" / "새 박스 가구". 씬에 FurnitureFactory 가 있으면 카탈로그 탭으로 시작한다
    /// - 카탈로그 가구: 팀원 catalog.json 목록을 한 쪽에 CatalogPageSize 개씩 (◀ ▶ 로 넘김). 누르면 바라보는 방향 앞 바닥에 만들고 선택 (CatalogMenu)
    /// - 새 박스 가구 목록: 침대·책상·소파·옷장 버튼을 누르면 이름과 치수가 채워진다
    /// - W·D·H: -100 / -10 / +10 / +100 (mm), 신뢰도: 실측·도면·추정
    /// - 추가: 바라보는 방향 앞 바닥(바닥 밖이면 가장 넓은 방 가운데)에 박스 가구를 만들고 선택 (데스크톱 메뉴와 같은 BoxFurniture.AddNew)
    /// - 선택: 이전·다음으로 배치된 가구를 돌아가며 선택, 삭제. 선택한 가구 정보는 StatusPanel.CompactText (치수·신뢰도·가장 가까운 대상)
    /// - 배치 저장·불러오기: LayoutSaver (데스크톱 메뉴와 같은 파일)
    /// - 접기: 제목 줄만 남긴다
    /// - 뷰어 모드 버튼: ViewerMode 를 켠다. 뷰어 모드에서는 제목 줄("뷰어 모드" + "편집 모드" 버튼)만 남긴다
    /// 패널은 손목 위 Offset 자리에 두고 항상 머리 쪽을 보게 돌린다.
    /// EventSystem 이 없으면 XRUIInputModule 과 함께 만든다 (XR 레이로 UI 를 누르는 데 필요).
    /// 씬에 빈 오브젝트로 두고 Prefab 칸에 FurnitureItem_Base 를 넣는다 (비워 두면 씬의 TestFurnitureSpawner 프리팹).
    /// </summary>
    public class VRFurnitureMenu : MonoBehaviour
    {
        [Tooltip("Assets/_Project/Prefabs/FurnitureItem_Base.prefab. 비워 두면 씬의 TestFurnitureSpawner 프리팹을 쓴다")]
        public FurnitureItem Prefab;

        [Tooltip("메뉴를 붙일 손. 비워 두면 Anchor Name 으로 찾는다")]
        public Transform Anchor;

        [Tooltip("Anchor 가 비었을 때 찾을 오브젝트 이름 (Starter Assets XR Origin 의 왼손)")]
        public string AnchorName = "Left Controller";

        [Tooltip("손에서 패널 아래 가운데까지 (m, 월드 위쪽 기준)")]
        public Vector3 Offset = new(0f, 0.08f, 0f);

        [Tooltip("패널 가로 길이 (m)")]
        public float WidthMeters = 0.28f;

        [Tooltip("Play 시작할 때 펼쳐 둔다")]
        public bool StartOpen = true;

        [Tooltip("새 가구를 바라보는 방향으로 이만큼(m) 앞에 놓는다")]
        public float SpawnDistanceMeters = 1.5f;

        struct Preset
        {
            public string Name;
            public int W, D, H;
        }

        /// <summary>가구 목록 버튼 (표준 치수, 신뢰도는 따로 고른다)</summary>
        static readonly Preset[] Presets =
        {
            new() { Name = "침대", W = 1100, D = 2000, H = 450 },
            new() { Name = "책상", W = 1200, D = 600, H = 740 },
            new() { Name = "소파", W = 1500, D = 800, H = 800 },
            new() { Name = "옷장", W = 1000, D = 600, H = 2000 },
        };

        /// <summary>카탈로그 탭 한 쪽에 보이는 가구 수 (한 줄에 2개)</summary>
        const int CatalogPageSize = 6;

        static readonly int[] Steps = { -100, -10, 10, 100 };
        static readonly string[] ConfidenceLabels = { "실측", "도면", "추정" };

        const float CanvasWidth = 640f;
        const int FontSize = 26;
        static readonly Color PanelColor = new(0.10f, 0.11f, 0.13f, 0.88f);
        static readonly Color ButtonColor = new(0.26f, 0.28f, 0.32f, 1f);
        static readonly Color ActiveColor = new(0.20f, 0.55f, 0.85f, 1f);
        static readonly Color AddColor = new(0.20f, 0.62f, 0.35f, 1f);
        static readonly Color DeleteColor = new(0.70f, 0.25f, 0.25f, 1f);

        string _name = "새 가구";
        int _w = 1000, _d = 500, _h = 750;
        Confidence _confidence = Confidence.Drawing;
        bool _open;

        Canvas _canvas;
        RectTransform _root;
        GameObject _body;
        Text _titleText, _viewerText, _toggleText, _nameText, _wText, _dText, _hText, _selectedText, _infoText;
        readonly Image[] _confidenceImages = new Image[3];
        Font _font;
        FloorPlanBuilder _floorPlan;
        float _nextAnchorSearch;

        List<CatalogItem> _catalogItems = new();
        int _catalogPage;
        readonly UnityEngine.UI.Button[] _catalogButtons = new UnityEngine.UI.Button[CatalogPageSize];
        readonly Text[] _catalogTexts = new Text[CatalogPageSize];
        Text _pageText;
        GameObject _catalogBody, _boxBody;
        Image _catalogTabImage, _boxTabImage;
        GameObject _toggleButton;
        Image _viewerImage;

        void Start()
        {
            EnsureEventSystem();
            _catalogItems = CatalogMenu.Items();
            Build();
            _open = StartOpen;
            ApplyOpen();
            SetTab(_catalogItems.Count > 0);
            RefreshValues();
            RefreshCatalog();
            // 데스크톱 V 키 등 다른 곳에서 모드를 바꿔도 따라간다
            ViewerMode.Changed += OnViewerModeChanged;
        }

        void OnDestroy() => ViewerMode.Changed -= OnViewerModeChanged;

        void OnViewerModeChanged(bool active) => ApplyOpen();

        void LateUpdate()
        {
            var anchor = ResolveAnchor();
            var cam = Camera.main;
            bool visible = anchor != null && cam != null;
            if (_canvas.enabled != visible) _canvas.enabled = visible;
            if (!visible) return;
            if (_canvas.worldCamera != cam) _canvas.worldCamera = cam;

            // 손목 위에 띄우고 머리 쪽을 보게 돌린다 (캔버스 정면은 +Z 반대쪽이라 카메라 → 패널 방향을 앞으로)
            var position = anchor.position + Offset;
            _root.position = position;
            var toPanel = position - cam.transform.position;
            if (toPanel.sqrMagnitude > 1e-6f) _root.rotation = Quaternion.LookRotation(toPanel, Vector3.up);
            _root.localScale = Vector3.one * (WidthMeters / CanvasWidth);

            if (_open) _selectedText.text = SelectedText();
        }

        Transform ResolveAnchor()
        {
            if (Anchor != null) return Anchor;
            if (Time.unscaledTime < _nextAnchorSearch) return null;
            _nextAnchorSearch = Time.unscaledTime + 1f;
            var found = GameObject.Find(AnchorName);
            if (found != null) Anchor = found.transform;
            return Anchor;
        }

        /// <summary>XR 레이로 UI 를 누르려면 EventSystem + XRUIInputModule 이 있어야 한다. 이미 있으면 그대로 둔다</summary>
        static void EnsureEventSystem()
        {
            if (FindAnyObjectByType<EventSystem>() != null) return;
            new GameObject("EventSystem", typeof(EventSystem), typeof(XRUIInputModule));
        }

        // ───────────── 동작 ─────────────

        void ApplyPreset(Preset preset)
        {
            _name = preset.Name;
            _w = preset.W;
            _d = preset.D;
            _h = preset.H;
            RefreshValues();
        }

        void Step(char axis, int delta)
        {
            switch (axis)
            {
                case 'W': _w = BoxFurniture.StepSize(_w, delta); break;
                case 'D': _d = BoxFurniture.StepSize(_d, delta); break;
                default: _h = BoxFurniture.StepSize(_h, delta); break;
            }

            RefreshValues();
        }

        void SetConfidence(Confidence confidence)
        {
            _confidence = confidence;
            RefreshValues();
        }

        void SetTab(bool catalog)
        {
            _catalogBody.SetActive(catalog);
            _boxBody.SetActive(!catalog);
            _catalogTabImage.color = catalog ? ActiveColor : ButtonColor;
            _boxTabImage.color = catalog ? ButtonColor : ActiveColor;
        }

        void TurnPage(int direction)
        {
            _catalogPage = CycleIndex(_catalogPage, direction, PageCount(_catalogItems.Count, CatalogPageSize));
            RefreshCatalog();
        }

        /// <summary>쪽 수. 가구가 없으면 0</summary>
        public static int PageCount(int count, int pageSize) => count <= 0 || pageSize <= 0 ? 0 : (count + pageSize - 1) / pageSize;

        /// <summary>카탈로그 탭의 slot 번째 버튼: 지금 쪽의 가구를 바라보는 앞 바닥에 만들고 선택한다</summary>
        void AddCatalog(int slot)
        {
            int index = _catalogPage * CatalogPageSize + slot;
            if (index < 0 || index >= _catalogItems.Count) return;
            CatalogMenu.Spawn(_catalogItems[index], SpawnPosition(), transform, out string message);
            _infoText.text = message;
        }

        void Add()
        {
            var prefab = BoxFurniture.ResolvePrefab(Prefab);
            if (prefab == null)
            {
                _infoText.text = "Prefab 칸에 FurnitureItem_Base 를 넣으세요.";
                return;
            }

            var size = new SizeMm { W = _w, D = _d, H = _h };
            var furniture = BoxFurniture.AddNew(prefab, _name, size, _confidence, SpawnPosition(), transform);
            _infoText.text = $"{BoxFurniture.Describe(furniture.Data)} 추가";
        }

        /// <summary>바라보는 방향(수평) 앞 SpawnDistanceMeters 의 바닥. 바닥 밖이면 가장 넓은 방 가운데</summary>
        Vector3 SpawnPosition()
        {
            var preferred = Vector2.zero;
            var cam = Camera.main;
            if (cam != null)
                preferred = Units.WorldToPlan(InFront(cam.transform.position, cam.transform.forward, SpawnDistanceMeters));

            if (_floorPlan == null) _floorPlan = FindFirstObjectByType<FloorPlanBuilder>();
            var mm = BoxFurniture.SpawnPointMm(_floorPlan != null ? _floorPlan.Data : null, preferred);
            return Units.PlanToWorld(mm.x, mm.y);
        }

        /// <summary>머리 위치에서 바라보는 방향을 수평으로 눕혀 distance 만큼 앞의 바닥 점 (y = 0)</summary>
        public static Vector3 InFront(Vector3 head, Vector3 forward, float distance)
        {
            var flat = new Vector3(forward.x, 0f, forward.z);
            if (flat.sqrMagnitude < 1e-6f) flat = Vector3.forward;
            var point = head + flat.normalized * distance;
            point.y = 0f;
            return point;
        }

        void SelectNext(int direction)
        {
            var items = FurnitureItem.All;
            if (items.Count == 0) return;
            var selection = SelectionManager.Instance;
            int index = IndexOf(items, selection.Selected);
            selection.Select(items[CycleIndex(index, direction, items.Count)]);
        }

        static int IndexOf(IReadOnlyList<FurnitureItem> items, FurnitureItem item)
        {
            for (int i = 0; i < items.Count; i++)
                if (items[i] == item) return i;
            return -1;
        }

        /// <summary>목록을 돌아가며 다음 번호. 선택이 없으면(-1) 다음 = 0, 이전 = 마지막</summary>
        public static int CycleIndex(int current, int direction, int count)
        {
            if (count <= 0) return -1;
            if (current < 0) return direction >= 0 ? 0 : count - 1;
            return ((current + direction) % count + count) % count;
        }

        void DeleteSelected()
        {
            var item = SelectionManager.Instance.Selected;
            if (item == null)
            {
                _infoText.text = "선택한 가구가 없습니다.";
                return;
            }

            _infoText.text = $"{FurnitureMenu.DisplayName(item)} ({item.ItemId}) 삭제";
            BoxFurniture.Delete(item);
        }

        void SaveLayout()
        {
            LayoutSaver.Save(out string message);
            _infoText.text = message;
        }

        /// <summary>지금 가구를 지우고 저장한 배치로 바꾼다 (데스크톱 메뉴와 같은 파일)</summary>
        void LoadLayout()
        {
            LayoutSaver.Load(BoxFurniture.ResolvePrefab(Prefab), transform, out string message);
            _infoText.text = message;
        }

        void ToggleOpen()
        {
            _open = !_open;
            ApplyOpen();
        }

        /// <summary>펼침·접힘과 뷰어 모드에 맞춰 보이는 칸을 바꾼다. 뷰어 모드면 제목 줄만</summary>
        void ApplyOpen()
        {
            bool viewer = ViewerMode.Active;
            _body.SetActive(_open && !viewer);
            _toggleButton.SetActive(!viewer);
            _toggleText.text = _open ? "접기" : "펼치기";
            _titleText.text = viewer ? "뷰어 모드" : "가구 메뉴";
            _viewerText.text = viewer ? "편집 모드" : "뷰어 모드";
            _viewerImage.color = viewer ? ActiveColor : ButtonColor;
            if (viewer) _infoText.text = "";
        }

        /// <summary>선택한 가구 정보 (상태 패널의 짧은 판: 이름·상태 / 치수·신뢰도 / 가장 가까운 대상)</summary>
        static string SelectedText() => StatusPanel.CompactText(SelectionManager.Instance.Selected);

        void RefreshValues()
        {
            _nameText.text = $"이름: {_name}";
            _wText.text = $"W {_w}";
            _dText.text = $"D {_d}";
            _hText.text = $"H {_h}";
            for (int i = 0; i < _confidenceImages.Length; i++)
                _confidenceImages[i].color = i == (int)_confidence ? ActiveColor : ButtonColor;
        }

        /// <summary>지금 쪽의 카탈로그 버튼 글자. 빈 칸은 투명하게 두어 버튼 크기가 바뀌지 않게 한다</summary>
        void RefreshCatalog()
        {
            int pages = PageCount(_catalogItems.Count, CatalogPageSize);
            if (pages == 0) return;
            _catalogPage = Mathf.Clamp(_catalogPage, 0, pages - 1);

            for (int slot = 0; slot < CatalogPageSize; slot++)
            {
                int index = _catalogPage * CatalogPageSize + slot;
                bool has = index < _catalogItems.Count;
                var button = _catalogButtons[slot];
                button.interactable = has;
                button.targetGraphic.color = has ? ButtonColor : Color.clear;
                if (has)
                {
                    var item = _catalogItems[index];
                    _catalogTexts[slot].text = $"{item.Name}\n{CatalogMenu.SizeText(item)} {ConfidenceLabel.Name(item.SizeConfidence)}";
                }
                else
                {
                    _catalogTexts[slot].text = "";
                }
            }

            _pageText.text = $"{_catalogPage + 1} / {pages}";
        }

        // ───────────── 화면 만들기 ─────────────

        void Build()
        {
            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            var canvasObject = new GameObject("WristMenuCanvas", typeof(RectTransform));
            canvasObject.transform.SetParent(transform, false);
            _canvas = canvasObject.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.WorldSpace;
            _canvas.worldCamera = Camera.main;
            canvasObject.AddComponent<TrackedDeviceGraphicRaycaster>(); // XR 레이
            canvasObject.AddComponent<GraphicRaycaster>(); // 시뮬레이터에서 마우스

            _root = (RectTransform)canvasObject.transform;
            _root.pivot = new Vector2(0.5f, 0f); // 아래 가운데가 손목 위
            _root.sizeDelta = new Vector2(CanvasWidth, 100f);
            canvasObject.AddComponent<Image>().color = PanelColor;
            var layout = Vertical(canvasObject, 14);
            layout.padding = new RectOffset(18, 18, 14, 18);
            var fitter = canvasObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var header = Row(_root, 52f);
            _titleText = Label(header, "가구 메뉴", FontSize + 4, FontStyle.Bold, 1f);
            var viewerButton = MakeButton(header, "뷰어 모드", ViewerMode.Toggle, ButtonColor, 150f);
            _viewerText = viewerButton.GetComponentInChildren<Text>();
            _viewerImage = viewerButton.GetComponent<Image>();
            var toggleButton = MakeButton(header, "접기", ToggleOpen, ButtonColor, 120f);
            _toggleButton = toggleButton.gameObject;
            _toggleText = toggleButton.GetComponentInChildren<Text>();

            _body = new GameObject("Body", typeof(RectTransform));
            _body.transform.SetParent(_root, false);
            Vertical(_body, 10);
            var body = (RectTransform)_body.transform;

            // 탭: 카탈로그 가구 / 새 박스 가구 (둘 다 펼치면 패널이 너무 길어진다)
            var tabRow = Row(body, 56f);
            _catalogTabImage = MakeButton(tabRow, "카탈로그 가구", () => SetTab(true), ButtonColor).GetComponent<Image>();
            _boxTabImage = MakeButton(tabRow, "새 박스 가구", () => SetTab(false), ButtonColor).GetComponent<Image>();

            _catalogBody = Section(body, "Catalog");
            BuildCatalog((RectTransform)_catalogBody.transform);

            _boxBody = Section(body, "NewBox");
            var box = (RectTransform)_boxBody.transform;

            Label(box, "가구 목록 (누르면 치수 채움)", FontSize - 4, FontStyle.Normal, 0f);
            for (int i = 0; i < Presets.Length; i += 2)
            {
                var row = Row(box, 52f);
                for (int j = i; j < Mathf.Min(i + 2, Presets.Length); j++)
                {
                    var preset = Presets[j];
                    MakeButton(row, $"{preset.Name} {preset.W}×{preset.D}×{preset.H}", () => ApplyPreset(preset), ButtonColor, -1f, FontSize - 4);
                }
            }

            _nameText = Label(box, "", FontSize, FontStyle.Bold, 0f);
            _wText = SizeRow(box, 'W');
            _dText = SizeRow(box, 'D');
            _hText = SizeRow(box, 'H');

            var confidenceRow = Row(box, 52f);
            for (int i = 0; i < ConfidenceLabels.Length; i++)
            {
                var confidence = (Confidence)i;
                var button = MakeButton(confidenceRow, $"{ConfidenceLabels[i]} {confidence.MarginMm()}mm", () => SetConfidence(confidence), ButtonColor);
                _confidenceImages[i] = button.GetComponent<Image>();
            }

            var addRow = Row(box, 64f);
            MakeButton(addRow, "추가", Add, AddColor, -1f, FontSize + 2);

            _selectedText = Label(body, "선택: 없음", FontSize - 2, FontStyle.Normal, 0f);
            var selectRow = Row(body, 52f);
            MakeButton(selectRow, "◀ 이전", () => SelectNext(-1), ButtonColor);
            MakeButton(selectRow, "다음 ▶", () => SelectNext(1), ButtonColor);
            MakeButton(selectRow, "삭제", DeleteSelected, DeleteColor);

            var layoutRow = Row(body, 52f);
            MakeButton(layoutRow, "배치 저장", SaveLayout, ButtonColor);
            MakeButton(layoutRow, "배치 불러오기", LoadLayout, ButtonColor);

            _infoText = Label(body, "", FontSize - 4, FontStyle.Normal, 0f);
        }

        /// <summary>카탈로그 탭: 가구 버튼 CatalogPageSize 개(한 줄에 2개) + 쪽 넘김. 가구가 없으면 안내 한 줄</summary>
        void BuildCatalog(RectTransform parent)
        {
            if (_catalogItems.Count == 0)
            {
                Label(parent, "카탈로그 가구 없음 (씬에 FurnitureFactory 가 없습니다)", FontSize - 4, FontStyle.Normal, 0f);
                return;
            }

            Label(parent, "누르면 바라보는 앞 바닥에 놓음", FontSize - 4, FontStyle.Normal, 0f);
            for (int first = 0; first < CatalogPageSize; first += 2)
            {
                var row = Row(parent, 72f);
                for (int slot = first; slot < first + 2; slot++)
                {
                    int captured = slot;
                    var button = MakeButton(row, "", () => AddCatalog(captured), ButtonColor, -1f, FontSize - 6);
                    _catalogButtons[slot] = button;
                    _catalogTexts[slot] = button.GetComponentInChildren<Text>();
                }
            }

            var pageRow = Row(parent, 52f);
            MakeButton(pageRow, "◀", () => TurnPage(-1), ButtonColor, 120f);
            _pageText = Label(pageRow, "", FontSize - 2, FontStyle.Normal, 1f);
            _pageText.alignment = TextAnchor.MiddleCenter;
            MakeButton(pageRow, "▶", () => TurnPage(1), ButtonColor, 120f);
            pageRow.gameObject.SetActive(PageCount(_catalogItems.Count, CatalogPageSize) > 1);
        }

        static GameObject Section(RectTransform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            Vertical(go, 10);
            return go;
        }

        Text SizeRow(RectTransform parent, char axis)
        {
            var row = Row(parent, 52f);
            var value = Label(row, "", FontSize, FontStyle.Bold, 1f);
            foreach (int step in Steps)
            {
                int delta = step;
                MakeButton(row, delta > 0 ? $"+{delta}" : delta.ToString(), () => Step(axis, delta), ButtonColor, 82f);
            }

            return value;
        }

        static VerticalLayoutGroup Vertical(GameObject go, float spacing)
        {
            var layout = go.AddComponent<VerticalLayoutGroup>();
            layout.spacing = spacing;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            return layout;
        }

        static RectTransform Row(RectTransform parent, float height)
        {
            var go = new GameObject("Row", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var layout = go.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 8f;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false; // 너비는 각 칸의 LayoutElement 로 (고정 너비 버튼 + 남는 칸 나눠 갖기)
            layout.childForceExpandHeight = true;
            go.AddComponent<LayoutElement>().preferredHeight = height;
            return (RectTransform)go.transform;
        }

        Text Label(RectTransform parent, string text, int fontSize, FontStyle style, float flexibleWidth)
        {
            var go = new GameObject("Label", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var label = go.AddComponent<Text>();
            label.font = _font;
            label.fontSize = fontSize;
            label.fontStyle = style;
            label.color = Color.white;
            label.alignment = TextAnchor.MiddleLeft;
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            label.verticalOverflow = VerticalWrapMode.Overflow;
            label.raycastTarget = false;
            label.text = text;
            var element = go.AddComponent<LayoutElement>();
            element.flexibleWidth = flexibleWidth;
            element.minHeight = fontSize + 10f;
            return label;
        }

        UnityEngine.UI.Button MakeButton(RectTransform parent, string text, UnityEngine.Events.UnityAction onClick, Color color,
            float width = -1f, int fontSize = FontSize)
        {
            var go = new GameObject("Button", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var image = go.AddComponent<Image>();
            image.color = color;
            var button = go.AddComponent<UnityEngine.UI.Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(onClick);
            var element = go.AddComponent<LayoutElement>();
            if (width > 0f)
            {
                element.preferredWidth = width;
                element.flexibleWidth = 0f;
            }
            else
            {
                element.flexibleWidth = 1f; // 같은 줄의 다른 버튼과 남는 너비를 나눈다
            }

            var label = new GameObject("Text", typeof(RectTransform)).AddComponent<Text>();
            label.transform.SetParent(go.transform, false);
            var rect = label.rectTransform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            label.font = _font;
            label.fontSize = fontSize;
            label.color = Color.white;
            label.alignment = TextAnchor.MiddleCenter;
            label.raycastTarget = false;
            label.text = text;
            return button;
        }
    }
}
