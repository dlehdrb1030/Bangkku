using Bangkku.Core;
using Bangkku.FloorPlan;
using Bangkku.Placement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR;

namespace Bangkku.UI
{
    /// <summary>
    /// 가구 메뉴 - 데스크톱 화면 UI (팀장 가이드 3주차 ⑥). 화면 오른쪽 패널.
    /// 화면이 작으면 패널 안을 휠로 스크롤한다 (위: 새 박스 가구, 아래: 배치된 가구 목록).
    /// - 배치된 가구 목록: 누르면 선택, 선택한 가구 삭제
    /// - 새 박스 가구: 이름, W·D·H (mm), 치수 신뢰도(실측·도면·추정)를 넣고 추가 → 화면 가운데 바닥(바닥이 아니면 가장 넓은 방 가운데)에 만들고 선택
    ///   id 는 box_01, box_02 … 로 자동. source = box 라서 팀원의 catalog.json 없이 동작한다
    /// - Tab: 열기·닫기. 입력칸에 글자를 넣는 동안에는 DesktopController 의 Q/E·WASD 가 가구·시점을 움직이지 않는다
    /// VR 기기가 동작 중이면 숨긴다 (VR 손목 메뉴는 다음 작업).
    /// 씬에 빈 오브젝트로 두고 Prefab 칸에 FurnitureItem_Base 를 넣는다 (비워 두면 씬의 TestFurnitureSpawner 프리팹을 쓴다).
    /// </summary>
    public class FurnitureMenu : MonoBehaviour
    {
        [Tooltip("Assets/_Project/Prefabs/FurnitureItem_Base.prefab. 비워 두면 씬의 TestFurnitureSpawner 프리팹을 쓴다")]
        public FurnitureItem Prefab;

        [Tooltip("Play 시작할 때 메뉴를 펼쳐 둔다")]
        public bool StartOpen = true;

        [Tooltip("VR 기기가 동작 중이면 숨긴다")]
        public bool HideWhenXRActive = true;

        [Header("모양")]
        public float PanelWidth = 340f;

        [Tooltip("화면 위에서 패널까지 (px). 왼쪽 위 안내 글자와 겹치지 않게")]
        public float PanelTop = 96f;

        public int FontSize = 14;

        /// <summary>메뉴 입력칸에 글자를 넣는 중인지. DesktopController 가 단축키를 막는 데 쓴다</summary>
        public static bool IsTyping { get; private set; }

        static Rect _panelRect;
        static bool _panelVisible;

        /// <summary>화면 좌표(Input System 마우스 위치, 아래가 0)가 메뉴 위인지. DesktopController 가 클릭·휠을 막는 데 쓴다</summary>
        public static bool IsPointerOver(Vector2 screenPosition) =>
            _panelVisible && _panelRect.Contains(new Vector2(screenPosition.x, Screen.height - screenPosition.y));

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            IsTyping = false;
            _panelVisible = false;
        }

        static readonly string[] ConfidenceLabels = { "실측", "도면", "추정" };

        bool _open;
        string _name = "새 가구", _w = "1000", _d = "500", _h = "750";
        int _confidence = (int)Confidence.Drawing;
        string _message;
        bool _messageIsError;
        Vector2 _panelScroll;
        FloorPlanBuilder _floorPlan;
        GUIStyle _label, _header, _button, _field, _toggle, _box, _error;

        void Awake() => _open = StartOpen;

        void OnDisable()
        {
            IsTyping = false;
            _panelVisible = false;
        }

        bool Hidden => HideWhenXRActive && XRSettings.isDeviceActive;

        void Update()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null || Hidden || IsTyping) return;
            if (keyboard.tabKey.wasPressedThisFrame) _open = !_open;
        }

        void OnGUI()
        {
            if (Hidden)
            {
                _panelVisible = false;
                IsTyping = false;
                return;
            }

            EnsureStyles();
            var e = Event.current;
            float x = Screen.width - PanelWidth - 8f;

            if (!_open)
            {
                _panelRect = new Rect(x, PanelTop, PanelWidth, 34f);
                _panelVisible = true;
                IsTyping = false;
                if (GUI.Button(_panelRect, "가구 메뉴 열기 (Tab)", _button)) _open = true;
                return;
            }

            _panelRect = new Rect(x, PanelTop, PanelWidth, Mathf.Min(Screen.height - PanelTop - 8f, 600f));
            _panelVisible = true;

            // 패널 밖을 누르거나 Esc 를 누르면 입력칸 선택을 푼다 (Q/E·WASD 가 다시 가구·시점을 움직이도록)
            if ((e.type == EventType.MouseDown && !_panelRect.Contains(e.mousePosition)) ||
                (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape))
                GUIUtility.keyboardControl = 0;

            // 입력칸에서 Enter 를 누르면 추가
            if (e.type == EventType.KeyDown && (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter) &&
                GUIUtility.keyboardControl != 0)
            {
                TryAdd();
                e.Use();
            }

            GUILayout.BeginArea(_panelRect, _box);
            GUILayout.BeginHorizontal();
            GUILayout.Label("가구 메뉴", _header);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("닫기 (Tab)", _button, GUILayout.Width(96f))) _open = false;
            GUILayout.EndHorizontal();

            // 화면이 작아도 아래 내용에 닿도록 패널 전체를 스크롤한다. 자주 쓰는 입력칸을 위에 둔다
            _panelScroll = GUILayout.BeginScrollView(_panelScroll);
            DrawNewBox();
            GUILayout.Space(12f);
            DrawPlacedList();
            GUILayout.EndScrollView();
            GUILayout.EndArea();

            IsTyping = GUIUtility.keyboardControl != 0;
        }

        // ───────────── 배치된 가구 목록 ─────────────

        void DrawPlacedList()
        {
            var items = FurnitureItem.All;
            var selection = SelectionManager.Instance;
            var selected = selection.Selected;

            GUILayout.Label($"배치된 가구 ({items.Count})", _header);
            FurnitureItem clicked = null;
            for (int i = 0; i < items.Count; i++)
            {
                var item = items[i];
                if (item == null) continue;
                bool isSelected = item == selected;
                string text = $"{DisplayName(item)}  ({item.ItemId})  {StateText(item.State)}";
                if (GUILayout.Toggle(isSelected, text, _toggle) && !isSelected) clicked = item;
            }

            if (clicked != null) selection.Select(clicked);

            GUI.enabled = selected != null;
            if (GUILayout.Button(selected != null ? $"선택한 가구 삭제: {DisplayName(selected)}" : "선택한 가구 삭제", _button))
                Delete(selected);
            GUI.enabled = true;
        }

        /// <summary>카탈로그 이름, 없으면 id</summary>
        public static string DisplayName(FurnitureItem item) =>
            item.Data != null && !string.IsNullOrEmpty(item.Data.Name) ? item.Data.Name : item.ItemId;

        public static string StateText(PlacementState state) => state switch
        {
            PlacementState.Overlap => "겹침",
            PlacementState.Tight => "좁음",
            _ => "여유",
        };

        void Delete(FurnitureItem item)
        {
            if (item == null) return;
            ShowMessage($"{DisplayName(item)} ({item.ItemId}) 를 지웠습니다.", false);
            BoxFurniture.Delete(item);
        }

        // ───────────── 새 박스 가구 ─────────────

        void DrawNewBox()
        {
            GUILayout.Label("새 박스 가구 (치수 mm)", _header);

            GUILayout.BeginHorizontal();
            GUILayout.Label("이름", _label, GUILayout.Width(44f));
            _name = GUILayout.TextField(_name, 30, _field);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            _w = SizeField("W", _w);
            _d = SizeField("D", _d);
            _h = SizeField("H", _h);
            GUILayout.EndHorizontal();
            GUILayout.Label("W = 가로(X), D = 깊이(Z, 정면 방향), H = 높이", _label);

            GUILayout.BeginHorizontal();
            GUILayout.Label("신뢰도", _label, GUILayout.Width(44f));
            _confidence = GUILayout.Toolbar(_confidence, ConfidenceLabels, _button);
            GUILayout.EndHorizontal();
            GUILayout.Label($"판정 여유값 {((Confidence)_confidence).MarginMm()}mm", _label);

            if (GUILayout.Button("추가 (Enter)", _button, GUILayout.Height(30f))) TryAdd();

            if (!string.IsNullOrEmpty(_message)) GUILayout.Label(_message, _messageIsError ? _error : _label);
        }

        string SizeField(string label, string value)
        {
            GUILayout.Label(label, _label, GUILayout.Width(18f));
            return GUILayout.TextField(value, 5, _field, GUILayout.Width(70f));
        }

        /// <summary>입력값으로 박스 가구를 만들어 화면 가운데 바닥에 놓고 선택한다</summary>
        void TryAdd()
        {
            var prefab = ResolvePrefab();
            if (prefab == null)
            {
                ShowMessage("Prefab 칸에 FurnitureItem_Base 프리팹을 넣으세요.", true);
                return;
            }

            if (!BoxFurniture.TryParseSize(_w, _d, _h, out var size, out var error))
            {
                ShowMessage(error, true);
                return;
            }

            var furniture = BoxFurniture.AddNew(prefab, _name, size, (Confidence)_confidence, SpawnPosition(), transform);
            GUIUtility.keyboardControl = 0;
            ShowMessage($"{BoxFurniture.Describe(furniture.Data)} 추가. 드래그로 옮기세요.", false);
        }

        FurnitureItem ResolvePrefab() => BoxFurniture.ResolvePrefab(Prefab);

        /// <summary>화면 가운데가 가리키는 바닥. 바닥 밖이면 가장 넓은 방 가운데</summary>
        Vector3 SpawnPosition()
        {
            var preferred = Vector2.zero;
            var cam = Camera.main;
            if (cam != null)
            {
                var ray = cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
                var floorPlane = new Plane(Vector3.up, Vector3.zero);
                if (floorPlane.Raycast(ray, out float distance))
                    preferred = Units.WorldToPlan(ray.GetPoint(distance));
            }

            if (_floorPlan == null) _floorPlan = FindFirstObjectByType<FloorPlanBuilder>();
            var mm = BoxFurniture.SpawnPointMm(_floorPlan != null ? _floorPlan.Data : null, preferred);
            return Units.PlanToWorld(mm.x, mm.y);
        }

        void ShowMessage(string message, bool isError)
        {
            _message = message;
            _messageIsError = isError;
        }

        void EnsureStyles()
        {
            if (_label != null) return;
            _label = new GUIStyle(GUI.skin.label) { fontSize = FontSize, wordWrap = true };
            _header = new GUIStyle(_label) { fontStyle = FontStyle.Bold };
            _error = new GUIStyle(_label) { normal = { textColor = new Color(1f, 0.45f, 0.4f) } };
            _button = new GUIStyle(GUI.skin.button) { fontSize = FontSize };
            _field = new GUIStyle(GUI.skin.textField) { fontSize = FontSize };
            _toggle = new GUIStyle(GUI.skin.button) { fontSize = FontSize, alignment = TextAnchor.MiddleLeft };
            _box = new GUIStyle(GUI.skin.box) { padding = new RectOffset(10, 10, 8, 8) };
        }
    }
}
