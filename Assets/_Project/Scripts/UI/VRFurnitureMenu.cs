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
    /// 한 번에 보이는 양을 줄이려고 탭 세 개로 나눈다. 위 두 줄은 항상 보인다.
    /// - 제목 줄: "가구 메뉴" + 뷰어 + 접기. 뷰어 모드에서는 제목 줄("뷰어 모드" + "편집 모드")만 남는다
    /// - 선택 요약 한 줄: 상태 색 점 + 이름 · 상태 · 가장 가까운 대상 거리 (선택이 없으면 "선택한 가구 없음")
    /// - 추가 탭: "카탈로그" / "박스 직접" 전환.
    ///   카탈로그 = 팀원 catalog.json 목록 한 쪽에 CatalogPageSize 개 (이름 + 치수, ◀ ▶ 로 넘김). 누르면 바라보는 앞 바닥에 만들고 선택 (CatalogMenu)
    ///   박스 직접 = 침대·책상·소파·옷장 프리셋, W·D·H ± 버튼, 신뢰도, 추가 (데스크톱 메뉴와 같은 BoxFurniture.AddNew). VR 에서는 글자 입력이 어려워 치수는 ± 버튼으로 바꾼다
    /// - 선택 탭: 왼쪽 가구 사진(FurnitureThumbnails, 없거나 박스 가구면 가구 색 상자) + 이름·id·종류·상태 칩, 치수, 신뢰도, 가장 가까운 대상 + 이전·다음·삭제
    /// - 배치 탭: 평면도 버튼(FloorPlanSwitcher 가 있고 2개 이상일 때, 누르면 지금 배치 저장 후 이동) + 배치 저장·불러오기 (LayoutSaver, 데스크톱 메뉴와 같은 파일)
    /// - 맨 아래 안내 한 줄: 마지막 동작 결과. InfoSeconds 뒤에 지운다
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

        [Tooltip("맨 아래 안내 글을 이만큼(초) 뒤에 지운다")]
        public float InfoSeconds = 5f;

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

        /// <summary>탭 번호</summary>
        const int AddTab = 0, SelectTab = 1, LayoutTab = 2;

        static readonly string[] TabNames = { "추가", "선택", "배치" };

        /// <summary>카탈로그 한 쪽에 보이는 가구 수 (한 줄에 2개)</summary>
        const int CatalogPageSize = 6;

        static readonly int[] Steps = { -100, -10, 10, 100 };
        static readonly string[] ConfidenceLabels = { "실측", "도면", "추정" };

        const float CanvasWidth = 640f;
        const int FontSize = 26;
        const int SmallFontSize = 21;

        /// <summary>선택 탭 왼쪽 사진 한 변 (캔버스 픽셀, 패널 폭 640 기준)</summary>
        const float ThumbnailPixels = 150f;

        /// <summary>추가 탭 카탈로그 버튼 높이와 그 안 왼쪽 사진 한 변 (버튼 안쪽 여백 CatalogPhotoMargin)</summary>
        const float CatalogButtonHeight = 92f, CatalogPhotoMargin = 6f;
        static readonly Color PanelColor = new(0.10f, 0.11f, 0.13f, 0.92f);
        static readonly Color StripColor = new(0.16f, 0.18f, 0.20f, 1f);
        static readonly Color ButtonColor = new(0.26f, 0.28f, 0.32f, 1f);
        static readonly Color ActiveColor = new(0.20f, 0.55f, 0.85f, 1f);
        static readonly Color AddColor = new(0.20f, 0.62f, 0.35f, 1f);
        static readonly Color DeleteColor = new(0.70f, 0.25f, 0.25f, 1f);
        static readonly Color MutedTextColor = new(0.73f, 0.74f, 0.76f, 1f);
        static readonly Color NoneDotColor = new(0.54f, 0.55f, 0.58f, 1f);

        string _name = "새 가구";
        int _w = 1000, _d = 500, _h = 750;
        Confidence _confidence = Confidence.Drawing;
        bool _open;
        int _tab = AddTab;
        float _infoUntil;

        Canvas _canvas;
        RectTransform _root;
        GameObject _body;
        Text _titleText, _viewerText, _toggleText, _summaryText, _infoText;
        GameObject _toggleButton;
        Image _viewerImage;
        Font _font;
        FloorPlanBuilder _floorPlan;
        float _nextAnchorSearch;

        readonly GameObject[] _tabBodies = new GameObject[TabNames.Length];
        readonly Image[] _tabImages = new Image[TabNames.Length];

        // 추가 탭
        List<CatalogItem> _catalogItems = new();
        int _catalogPage;
        readonly UnityEngine.UI.Button[] _catalogButtons = new UnityEngine.UI.Button[CatalogPageSize];
        readonly Text[] _catalogTexts = new Text[CatalogPageSize];
        readonly RawImage[] _catalogPhotos = new RawImage[CatalogPageSize];
        readonly Image[] _catalogPhotoBacks = new Image[CatalogPageSize];
        readonly Text[] _catalogPhotoLabels = new Text[CatalogPageSize];
        Text _pageText;
        GameObject _catalogBody, _boxBody;
        Image _catalogModeImage, _boxModeImage;
        Text _nameText, _wText, _dText, _hText;
        readonly Image[] _confidenceImages = new Image[3];

        // 선택 탭
        Text _selNameText, _selSubText, _selStateText, _selSizeText, _selConfidenceText, _selNearestText, _thumbLabel;
        Image _selStateImage, _thumbBackground;
        RawImage _thumbImage;
        FurnitureItem _thumbItem;
        bool _thumbShown;

        /// <summary>지난 프레임에 본 선택 가구 (바뀌면 선택 탭으로 넘어간다)</summary>
        FurnitureItem _lastSelected;

        // 배치 탭
        FloorPlanSwitcher _switcher;
        readonly List<Image> _planImages = new();

        void Start()
        {
            EnsureEventSystem();
            _catalogItems = CatalogMenu.Items();
            Build();
            _open = StartOpen;
            SetAddMode(_catalogItems.Count > 0);
            SetTab(AddTab);
            ApplyOpen();
            RefreshValues();
            RefreshCatalog();
            RefreshFloorPlans();
            // 데스크톱 V 키 등 다른 곳에서 모드를 바꿔도 따라간다
            ViewerMode.Changed += OnViewerModeChanged;
            if (_switcher != null) _switcher.Switched += OnFloorPlanSwitched;
        }

        void OnDestroy()
        {
            ViewerMode.Changed -= OnViewerModeChanged;
            if (_switcher != null) _switcher.Switched -= OnFloorPlanSwitched;
        }

        void OnViewerModeChanged(bool active) => ApplyOpen();

        void OnFloorPlanSwitched(FloorPlanSwitcher switcher) => RefreshFloorPlans();

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

            if (_infoUntil > 0f && Time.unscaledTime >= _infoUntil)
            {
                _infoUntil = 0f;
                _infoText.text = "";
            }

            if (!_open || ViewerMode.Active) return;
            var selected = FurnitureItem.All.Count > 0 ? SelectionManager.Instance.Selected : null;

            // 다른 가구를 고르면(잡기·추가) 선택 탭으로 넘어가 그 가구의 사진·치수·상태를 보여 준다.
            // 선택이 풀렸을 때(삭제 등)는 탭을 그대로 둔다. 지워진 가구도 다른 가구로 보도록 참조로 비교
            if (!ReferenceEquals(selected, _lastSelected))
            {
                _lastSelected = selected;
                if (selected != null && _tab != SelectTab) SetTab(SelectTab);
            }

            _summaryText.text = SummaryText(selected);
            if (_tab == SelectTab) RefreshSelected(selected);
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

        // ───────────── 탭·열기 ─────────────

        void SetTab(int tab)
        {
            _tab = tab;
            for (int i = 0; i < _tabBodies.Length; i++)
            {
                _tabBodies[i].SetActive(i == tab);
                _tabImages[i].color = i == tab ? ActiveColor : ButtonColor;
            }
        }

        /// <summary>추가 탭 안의 "카탈로그" / "박스 직접"</summary>
        void SetAddMode(bool catalog)
        {
            _catalogBody.SetActive(catalog);
            _boxBody.SetActive(!catalog);
            _catalogModeImage.color = catalog ? ActiveColor : ButtonColor;
            _boxModeImage.color = catalog ? ButtonColor : ActiveColor;
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
            _viewerText.text = viewer ? "편집 모드" : "뷰어";
            _viewerImage.color = viewer ? ActiveColor : ButtonColor;
            if (viewer) ShowInfo("");
        }

        void ShowInfo(string message)
        {
            _infoText.text = message;
            _infoUntil = string.IsNullOrEmpty(message) ? 0f : Time.unscaledTime + InfoSeconds;
        }

        // ───────────── 추가 탭 ─────────────

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

        void TurnPage(int direction)
        {
            _catalogPage = CycleIndex(_catalogPage, direction, PageCount(_catalogItems.Count, CatalogPageSize));
            RefreshCatalog();
        }

        /// <summary>쪽 수. 가구가 없으면 0</summary>
        public static int PageCount(int count, int pageSize) => count <= 0 || pageSize <= 0 ? 0 : (count + pageSize - 1) / pageSize;

        /// <summary>카탈로그 slot 번째 버튼: 지금 쪽의 가구를 바라보는 앞 바닥에 만들고 선택한다</summary>
        void AddCatalog(int slot)
        {
            int index = _catalogPage * CatalogPageSize + slot;
            if (index < 0 || index >= _catalogItems.Count) return;
            var furniture = CatalogMenu.Spawn(_catalogItems[index], SpawnPosition(), transform, out string message);
            ShowInfo(message);
            // 만든 가구는 선택된 상태라 선택 탭에서 사진·치수·상태를 바로 보여 준다
            if (furniture != null) SetTab(SelectTab);
        }

        void AddBox()
        {
            var prefab = BoxFurniture.ResolvePrefab(Prefab);
            if (prefab == null)
            {
                ShowInfo("Prefab 칸에 FurnitureItem_Base 를 넣으세요.");
                return;
            }

            var size = new SizeMm { W = _w, D = _d, H = _h };
            var furniture = BoxFurniture.AddNew(prefab, _name, size, _confidence, SpawnPosition(), transform);
            ShowInfo($"{BoxFurniture.Describe(furniture.Data)} 추가");
            if (furniture != null) SetTab(SelectTab);
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

        void RefreshValues()
        {
            _nameText.text = $"이름: {_name}";
            _wText.text = $"W {_w}";
            _dText.text = $"D {_d}";
            _hText.text = $"H {_h}";
            for (int i = 0; i < _confidenceImages.Length; i++)
                _confidenceImages[i].color = i == (int)_confidence ? ActiveColor : ButtonColor;
        }

        /// <summary>지금 쪽의 카탈로그 버튼 (왼쪽 가구 사진 + 오른쪽 이름). 빈 칸은 투명하게 두어 버튼 크기가 바뀌지 않게 한다</summary>
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
                var item = has ? _catalogItems[index] : null;
                _catalogTexts[slot].text = has ? $"<b>{item.Name}</b>" : "";

                // 사진이 없으면 어두운 칸에 종류 글자 (3DGS·메쉬·박스), 박스 가구는 가구 색
                var texture = has ? FurnitureThumbnails.Get(item.Id) : null;
                _catalogPhotos[slot].texture = texture;
                _catalogPhotos[slot].enabled = texture != null;
                _catalogPhotoBacks[slot].enabled = has;
                _catalogPhotoBacks[slot].color = has && item.TryGetColor(out var color) ? color : StripColor;
                _catalogPhotoLabels[slot].text = has && texture == null ? SourceShort(item.Source) : "";
            }

            _pageText.text = $"{_catalogPage + 1} / {pages}";
        }

        // ───────────── 선택 탭 ─────────────

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
            var item = FurnitureItem.All.Count > 0 ? SelectionManager.Instance.Selected : null;
            if (item == null)
            {
                ShowInfo("선택한 가구가 없습니다.");
                return;
            }

            ShowInfo($"{FurnitureMenu.DisplayName(item)} 삭제");
            BoxFurniture.Delete(item);
        }

        /// <summary>선택 탭 카드: 이름·id, 상태 칩, 치수, 신뢰도, 가장 가까운 대상</summary>
        void RefreshSelected(FurnitureItem item)
        {
            bool has = item != null;
            _selStateImage.gameObject.SetActive(has);
            RefreshThumbnail(item);
            if (!has)
            {
                _selNameText.text = "선택한 가구 없음";
                _selSubText.text = "가구를 잡거나 이전·다음으로 고르세요";
                _selSizeText.text = "-";
                _selConfidenceText.text = "-";
                _selNearestText.text = "-";
                return;
            }

            _selNameText.text = FurnitureMenu.DisplayName(item);
            _selSubText.text = item.Data != null ? $"{item.ItemId} · {SourceShort(item.Data.Source)}" : item.ItemId;
            _selStateText.text = FurnitureMenu.StateText(item.State);
            _selStateImage.color = StateColor(item.State) * new Color(0.75f, 0.75f, 0.75f, 1f);
            var size = item.SizeMeters;
            _selSizeText.text = $"{Units.MToMm(size.x)}×{Units.MToMm(size.z)}×{Units.MToMm(size.y)}";
            _selConfidenceText.text = ConfidenceLabel.ShortText(item.SizeConfidence);
            _selNearestText.text = StatusPanel.NearestText(item, PlacementValidator.Instance, false);
        }

        /// <summary>
        /// 왼쪽 사진: 가구 사진이 있으면 사진, 없으면 가구 색 상자(박스 가구는 "박스", 사진 없는 스캔 가구는 종류 글자).
        /// 선택이 바뀔 때만 다시 고른다
        /// </summary>
        void RefreshThumbnail(FurnitureItem item)
        {
            // 지워진 가구도 다른 가구로 보도록 참조로 비교한다 (Unity 의 == 는 지워진 가구를 null 과 같게 본다)
            if (_thumbShown && ReferenceEquals(item, _thumbItem)) return;
            _thumbShown = true;
            _thumbItem = item;

            var texture = item != null ? FurnitureThumbnails.Get(item.ItemId) : null;
            _thumbImage.texture = texture;
            _thumbImage.enabled = texture != null;
            _thumbBackground.color = ThumbnailColor(item);
            _thumbLabel.text = texture != null || item == null ? "" : item.Data != null ? SourceShort(item.Data.Source) : "";
        }

        /// <summary>사진 뒤 상자 색: 박스 가구면 그 가구 색, 아니면 어두운 회색</summary>
        static Color ThumbnailColor(FurnitureItem item)
        {
            if (item != null && item.Data != null && item.Data.TryGetColor(out var color)) return color;
            return StripColor;
        }

        /// <summary>"3DGS" / "메쉬" / "박스"</summary>
        static string SourceShort(FurnitureSource source) => source switch
        {
            FurnitureSource.Scan => "3DGS",
            FurnitureSource.Mesh => "메쉬",
            _ => "박스",
        };

        // ───────────── 배치 탭 ─────────────

        void SaveLayout()
        {
            LayoutSaver.Save(out string message);
            ShowInfo(message);
        }

        /// <summary>지금 가구를 지우고 저장한 배치로 바꾼다 (데스크톱 메뉴와 같은 파일)</summary>
        void LoadLayout()
        {
            LayoutSaver.Load(BoxFurniture.ResolvePrefab(Prefab), transform, out string message);
            ShowInfo(message);
        }

        void SwitchFloorPlan(int index)
        {
            if (_switcher == null) return;
            _switcher.SwitchTo(index, out string message);
            ShowInfo(message);
            RefreshFloorPlans();
        }

        /// <summary>평면도 버튼: 지금 평면도를 파란색으로</summary>
        void RefreshFloorPlans()
        {
            if (_switcher == null) return;
            int current = _switcher.CurrentIndex;
            for (int i = 0; i < _planImages.Count; i++)
                if (_planImages[i] != null) _planImages[i].color = i == current ? ActiveColor : ButtonColor;
        }

        // ───────────── 글 만들기 ─────────────

        static Color StateColor(PlacementState state) => state switch
        {
            PlacementState.Overlap => PlacementStateView.OverlapColor,
            PlacementState.Tight => PlacementStateView.TightColor,
            _ => PlacementStateView.FreeColor,
        };

        /// <summary>선택 요약 한 줄: "● 침대 · 여유 · 벽 32mm" (점은 상태 색). 선택이 없으면 회색 점 + "선택한 가구 없음"</summary>
        static string SummaryText(FurnitureItem item)
        {
            if (item == null) return $"<color=#{ColorUtility.ToHtmlStringRGB(NoneDotColor)}>●</color> 선택한 가구 없음";
            string dot = $"<color=#{ColorUtility.ToHtmlStringRGB(StateColor(item.State))}>●</color>";
            return $"{dot} {FurnitureMenu.DisplayName(item)} · {FurnitureMenu.StateText(item.State)} · {NearestShort(item)}";
        }

        /// <summary>"벽 32mm", "책상 120mm", "kitchen 50mm". 범위 안에 없으면 "주변 여유"</summary>
        static string NearestShort(FurnitureItem item)
        {
            if (!PlacementValidator.Instance.TryGetGap(item, out var gap) || !gap.HasTarget) return "주변 여유";
            string target = DistanceLabel.TargetKind(gap.Target) == DistanceLabel.Kind.Wall ? "벽" : DistanceLabel.TargetName(gap.Target);
            return $"{target} {Units.MToMm(gap.Meters)}mm";
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
            var layout = Vertical(canvasObject, 12);
            layout.padding = new RectOffset(18, 18, 14, 16);
            var fitter = canvasObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            // 제목 줄 (항상)
            var header = Row(_root, 52f);
            _titleText = Label(header, "가구 메뉴", FontSize + 4, FontStyle.Bold, 1f);
            var viewerButton = MakeButton(header, "뷰어", ViewerMode.Toggle, ButtonColor, 130f);
            _viewerText = viewerButton.GetComponentInChildren<Text>();
            _viewerImage = viewerButton.GetComponent<Image>();
            var toggleButton = MakeButton(header, "접기", ToggleOpen, ButtonColor, 110f);
            _toggleButton = toggleButton.gameObject;
            _toggleText = toggleButton.GetComponentInChildren<Text>();

            _body = Section(_root, "Body", 12);
            var body = (RectTransform)_body.transform;

            // 선택 요약 한 줄
            var strip = Row(body, 50f);
            strip.gameObject.AddComponent<Image>().color = StripColor;
            var stripLayout = strip.GetComponent<HorizontalLayoutGroup>();
            stripLayout.padding = new RectOffset(14, 14, 0, 0);
            _summaryText = Label(strip, "", FontSize - 2, FontStyle.Normal, 1f);

            // 탭
            var tabRow = Row(body, 60f);
            for (int i = 0; i < TabNames.Length; i++)
            {
                int tab = i;
                _tabImages[i] = MakeButton(tabRow, TabNames[i], () => SetTab(tab), ButtonColor, -1f, FontSize + 2).GetComponent<Image>();
            }

            _tabBodies[AddTab] = Section(body, "AddTab", 10);
            BuildAddTab((RectTransform)_tabBodies[AddTab].transform);
            _tabBodies[SelectTab] = Section(body, "SelectTab", 10);
            BuildSelectTab((RectTransform)_tabBodies[SelectTab].transform);
            _tabBodies[LayoutTab] = Section(body, "LayoutTab", 10);
            BuildLayoutTab((RectTransform)_tabBodies[LayoutTab].transform);

            // 맨 아래 안내 한 줄
            _infoText = Label(body, "", SmallFontSize, FontStyle.Normal, 0f);
            _infoText.color = MutedTextColor;
        }

        void BuildAddTab(RectTransform parent)
        {
            var modeRow = Row(parent, 46f);
            _catalogModeImage = MakeButton(modeRow, "카탈로그", () => SetAddMode(true), ButtonColor, -1f, FontSize - 4).GetComponent<Image>();
            _boxModeImage = MakeButton(modeRow, "박스 직접", () => SetAddMode(false), ButtonColor, -1f, FontSize - 4).GetComponent<Image>();

            _catalogBody = Section(parent, "Catalog", 10);
            BuildCatalog((RectTransform)_catalogBody.transform);

            _boxBody = Section(parent, "NewBox", 10);
            BuildBox((RectTransform)_boxBody.transform);
        }

        /// <summary>카탈로그: 가구 버튼 CatalogPageSize 개(한 줄에 2개, 왼쪽 사진 + 오른쪽 이름) + 쪽 넘김. 가구가 없으면 안내 한 줄.
        /// 누르면 가구를 만들고 선택 탭으로 넘어간다 (치수·신뢰도는 선택 탭에서)</summary>
        void BuildCatalog(RectTransform parent)
        {
            if (_catalogItems.Count == 0)
            {
                Label(parent, "카탈로그 가구 없음 (씬에 FurnitureFactory 가 없습니다)", SmallFontSize, FontStyle.Normal, 0f);
                return;
            }

            for (int first = 0; first < CatalogPageSize; first += 2)
            {
                var row = Row(parent, CatalogButtonHeight);
                for (int slot = first; slot < first + 2; slot++)
                {
                    int captured = slot;
                    var button = MakeButton(row, "", () => AddCatalog(captured), ButtonColor, -1f, FontSize - 2);
                    _catalogButtons[slot] = button;
                    BuildCatalogPhoto(button, slot);
                }
            }

            var pageRow = Row(parent, 50f);
            MakeButton(pageRow, "◀", () => TurnPage(-1), ButtonColor, 120f);
            _pageText = Label(pageRow, "", FontSize - 2, FontStyle.Normal, 1f);
            _pageText.alignment = TextAnchor.MiddleCenter;
            MakeButton(pageRow, "▶", () => TurnPage(1), ButtonColor, 120f);
            pageRow.gameObject.SetActive(PageCount(_catalogItems.Count, CatalogPageSize) > 1);
        }

        /// <summary>카탈로그 버튼 안: 왼쪽에 정사각 사진 칸(배경 + 사진 + 사진 없을 때 글자), 이름 글자는 사진 오른쪽에 왼쪽 정렬</summary>
        void BuildCatalogPhoto(UnityEngine.UI.Button button, int slot)
        {
            // MakeButton 이 만든 글자를 사진 칸(안에 글자가 또 있음)을 만들기 전에 잡아 둔다
            var text = button.GetComponentInChildren<Text>();
            float side = CatalogButtonHeight - CatalogPhotoMargin * 2f;
            var frame = new GameObject("Photo", typeof(RectTransform));
            frame.transform.SetParent(button.transform, false);
            var rect = (RectTransform)frame.transform;
            rect.anchorMin = new Vector2(0f, 0.5f);
            rect.anchorMax = new Vector2(0f, 0.5f);
            rect.pivot = new Vector2(0f, 0.5f);
            rect.anchoredPosition = new Vector2(CatalogPhotoMargin, 0f);
            rect.sizeDelta = new Vector2(side, side);
            _catalogPhotoBacks[slot] = frame.AddComponent<Image>();
            _catalogPhotoBacks[slot].raycastTarget = false;

            var photo = new GameObject("Image", typeof(RectTransform)).AddComponent<RawImage>();
            photo.transform.SetParent(frame.transform, false);
            Stretch(photo.rectTransform);
            photo.raycastTarget = false;
            _catalogPhotos[slot] = photo;

            var label = Label(rect, "", SmallFontSize, FontStyle.Bold, 0f);
            label.alignment = TextAnchor.MiddleCenter;
            label.color = MutedTextColor;
            Stretch(label.rectTransform);
            _catalogPhotoLabels[slot] = label;

            // MakeButton 의 글자는 버튼 전체에 늘어나 있다 → 사진 오른쪽으로 비켜 왼쪽 정렬
            text.rectTransform.SetAsLastSibling();
            text.rectTransform.offsetMin = new Vector2(side + CatalogPhotoMargin * 3f, 0f);
            text.rectTransform.offsetMax = new Vector2(-CatalogPhotoMargin, 0f);
            text.alignment = TextAnchor.MiddleLeft;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            _catalogTexts[slot] = text;
        }

        /// <summary>박스 직접: 프리셋 4개(2줄), 이름, W·D·H ± 버튼, 신뢰도, 추가</summary>
        void BuildBox(RectTransform parent)
        {
            for (int i = 0; i < Presets.Length; i += 2)
            {
                var row = Row(parent, 50f);
                for (int j = i; j < Mathf.Min(i + 2, Presets.Length); j++)
                {
                    var preset = Presets[j];
                    MakeButton(row, $"{preset.Name} {preset.W}×{preset.D}×{preset.H}", () => ApplyPreset(preset), ButtonColor, -1f, SmallFontSize);
                }
            }

            _nameText = Label(parent, "", FontSize, FontStyle.Bold, 0f);
            _wText = SizeRow(parent, 'W');
            _dText = SizeRow(parent, 'D');
            _hText = SizeRow(parent, 'H');

            var confidenceRow = Row(parent, 50f);
            for (int i = 0; i < ConfidenceLabels.Length; i++)
            {
                var confidence = (Confidence)i;
                var button = MakeButton(confidenceRow, $"{ConfidenceLabels[i]} {confidence.MarginMm()}mm", () => SetConfidence(confidence), ButtonColor, -1f, FontSize - 4);
                _confidenceImages[i] = button.GetComponent<Image>();
            }

            MakeButton(Row(parent, 60f), "추가", AddBox, AddColor, -1f, FontSize + 2);
        }

        /// <summary>선택 탭: 왼쪽 사진 + 오른쪽 이름·id·상태 칩, 치수·신뢰도·가까운 곳, 이전·다음, 삭제</summary>
        void BuildSelectTab(RectTransform parent)
        {
            var top = Row(parent, ThumbnailPixels);
            top.GetComponent<HorizontalLayoutGroup>().spacing = 14f;
            BuildThumbnail(top);

            var info = Section(top, "Info", 6);
            info.GetComponent<VerticalLayoutGroup>().childAlignment = TextAnchor.MiddleLeft;
            info.AddComponent<LayoutElement>().flexibleWidth = 1f;
            var infoRect = (RectTransform)info.transform;
            _selNameText = Label(infoRect, "", FontSize + 4, FontStyle.Bold, 0f);
            _selSubText = Muted(Label(infoRect, "", SmallFontSize, FontStyle.Normal, 0f));

            var chipRow = Row(infoRect, 40f);
            // 상태 칩: 눌리지 않는 색 상자 (버튼으로 만들면 눌림·흐림 색이 섞인다)
            var chip = new GameObject("StateChip", typeof(RectTransform));
            chip.transform.SetParent(chipRow, false);
            _selStateImage = chip.AddComponent<Image>();
            _selStateImage.raycastTarget = false;
            chip.AddComponent<LayoutElement>().preferredWidth = 120f;
            _selStateText = Label((RectTransform)chip.transform, "", FontSize - 4, FontStyle.Bold, 0f);
            _selStateText.alignment = TextAnchor.MiddleCenter;
            Stretch(_selStateText.rectTransform);
            Label(chipRow, "", FontSize, FontStyle.Normal, 1f); // 칩을 왼쪽에 붙이는 빈 칸

            _selSizeText = KeyValue(parent, "치수 (mm)");
            _selConfidenceText = KeyValue(parent, "신뢰도");
            _selNearestText = KeyValue(parent, "가까운 곳");

            var selectRow = Row(parent, 54f);
            MakeButton(selectRow, "◀ 이전", () => SelectNext(-1), ButtonColor);
            MakeButton(selectRow, "다음 ▶", () => SelectNext(1), ButtonColor);
            MakeButton(Row(parent, 54f), "삭제", DeleteSelected, DeleteColor);
        }

        /// <summary>사진 칸: 색 상자(배경) 위에 사진(RawImage), 사진이 없으면 가운데 글자</summary>
        void BuildThumbnail(RectTransform parent)
        {
            var frame = new GameObject("Thumbnail", typeof(RectTransform));
            frame.transform.SetParent(parent, false);
            _thumbBackground = frame.AddComponent<Image>();
            _thumbBackground.raycastTarget = false;
            var element = frame.AddComponent<LayoutElement>();
            element.preferredWidth = ThumbnailPixels;
            element.flexibleWidth = 0f;

            _thumbImage = new GameObject("Image", typeof(RectTransform)).AddComponent<RawImage>();
            _thumbImage.transform.SetParent(frame.transform, false);
            Stretch(_thumbImage.rectTransform);
            _thumbImage.raycastTarget = false;

            _thumbLabel = Label((RectTransform)frame.transform, "", FontSize - 2, FontStyle.Bold, 0f);
            _thumbLabel.alignment = TextAnchor.MiddleCenter;
            _thumbLabel.color = new Color(0.08f, 0.10f, 0.14f, 1f);
            Stretch(_thumbLabel.rectTransform);
        }

        static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        /// <summary>배치 탭: 평면도 버튼(있을 때) + 배치 저장·불러오기</summary>
        void BuildLayoutTab(RectTransform parent)
        {
            BuildFloorPlans(parent);

            Muted(Label(parent, "배치", SmallFontSize, FontStyle.Normal, 0f));
            var layoutRow = Row(parent, 54f);
            MakeButton(layoutRow, "저장", SaveLayout, ButtonColor);
            MakeButton(layoutRow, "불러오기", LoadLayout, ButtonColor);
        }

        /// <summary>평면도 줄: 한 줄에 2개씩. FloorPlanSwitcher 가 없거나 평면도가 2개 미만이면 만들지 않는다</summary>
        void BuildFloorPlans(RectTransform parent)
        {
            _switcher = FloorPlanSwitcher.Instance;
            if (_switcher == null || !_switcher.HasChoices) return;

            Muted(Label(parent, "평면도 (누르면 지금 배치 저장 후 이동)", SmallFontSize, FontStyle.Normal, 0f));
            RectTransform row = null;
            int inRow = 0;
            for (int i = 0; i < _switcher.Plans.Count; i++)
            {
                var entry = _switcher.Plans[i];
                if (entry == null || entry.Json == null) continue;
                if (row == null || inRow == 2)
                {
                    row = Row(parent, 56f);
                    inRow = 0;
                }

                int index = i;
                var button = MakeButton(row, _switcher.LabelOf(i), () => SwitchFloorPlan(index), ButtonColor, -1f, FontSize - 2);
                while (_planImages.Count < i) _planImages.Add(null);
                _planImages.Add(button.GetComponent<Image>());
                inRow++;
            }
        }

        static GameObject Section(RectTransform parent, string name, float spacing)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            Vertical(go, spacing);
            return go;
        }

        /// <summary>"치수 (mm)   1700×2100×1110" 한 줄. 값 글자를 돌려준다</summary>
        Text KeyValue(RectTransform parent, string key)
        {
            var row = Row(parent, 40f);
            var keyText = Muted(Label(row, key, SmallFontSize, FontStyle.Normal, 0f));
            keyText.GetComponent<LayoutElement>().preferredWidth = 170f;
            return Label(row, "", FontSize - 2, FontStyle.Normal, 1f);
        }

        Text SizeRow(RectTransform parent, char axis)
        {
            var row = Row(parent, 50f);
            var value = Label(row, "", FontSize, FontStyle.Bold, 1f);
            foreach (int step in Steps)
            {
                int delta = step;
                MakeButton(row, delta > 0 ? $"+{delta}" : delta.ToString(), () => Step(axis, delta), ButtonColor, 82f);
            }

            return value;
        }

        static Text Muted(Text text)
        {
            text.color = MutedTextColor;
            return text;
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
            label.supportRichText = true;
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
            label.supportRichText = true;
            label.raycastTarget = false;
            label.text = text;
            return button;
        }
    }
}
