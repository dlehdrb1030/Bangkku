using System;
using Bangkku.Core;
using Bangkku.Placement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR;

namespace Bangkku.Input
{
    /// <summary>
    /// 데스크톱 모드 (팀장 가이드 4장): VR 장비 없이 마우스와 키보드로 가구를 배치한다.
    /// - 좌클릭: 가구 선택 (Layer = Furniture 레이캐스트), 빈 곳 클릭은 선택 해제
    /// - 좌클릭 드래그: 바닥 평면(y = 0)과 마우스 레이의 교점으로 이동 (잡은 지점 그대로 따라옴)
    /// - Q / E: 선택한 가구를 Y축으로 -90° / +90° 회전, Shift 를 누르면 15°
    /// - 우클릭을 누른 채: 마우스로 시점 회전, W A S D 로 이동 (Shift 로 빠르게)
    /// - 마우스 휠: 보는 방향으로 확대·축소
    /// VR 이 켜져 있으면 스스로 꺼진다 (Disable When XR Active).
    /// 판정(겹침·간격)과 스냅은 다른 컴포넌트가 맡는다. 여기서는 위치와 회전만 바꾼다.
    /// </summary>
    public class DesktopController : MonoBehaviour
    {
        [Header("카메라")]
        [Tooltip("조작할 카메라. 비워 두면 Main Camera")]
        public Camera Camera;

        [Tooltip("이동 속도 (m/s)")]
        public float MoveSpeed = 3f;

        [Tooltip("Shift 를 누르면 곱해지는 이동 속도 배율")]
        public float FastMultiplier = 3f;

        [Tooltip("마우스 1픽셀당 시점 회전 (도)")]
        public float LookSensitivity = 0.15f;

        [Tooltip("휠 한 번에 보는 방향으로 움직이는 거리 (m)")]
        public float ZoomStepMeters = 0.5f;

        [Header("시작 시점")]
        [Tooltip("월드 좌표 (m). 기본값은 oneroom_01 위쪽")]
        public Vector3 StartPosition = new(1.8f, 9f, -3.4f);

        [Tooltip("x = 아래로 숙이는 각도 (90 = 바로 아래), y = 좌우 방향. 위에서 볼 때 화면 위쪽이 평면도 위쪽이 된다")]
        public Vector2 StartPitchYaw = new(90f, 0f);

        [Header("회전")]
        public float CoarseRotationDegrees = 90f;
        public float FineRotationDegrees = 15f;

        [Header("모드")]
        [Tooltip("켜면 VR 기기가 동작 중일 때 이 컴포넌트를 끈다")]
        public bool DisableWhenXRActive = true;

        /// <summary>지금 선택한 가구. 없으면 null</summary>
        public FurnitureItem Selected { get; private set; }

        /// <summary>선택이 바뀔 때 (해제는 null)</summary>
        public event Action<FurnitureItem> SelectionChanged;

        float _pitch;
        float _yaw;
        bool _dragging;
        Vector3 _grabOffset;
        GUIStyle _style;

        void Awake()
        {
            if (Camera == null) Camera = Camera.main;
            if (Camera == null) Debug.LogWarning("[방꾸] DesktopController: 카메라를 찾지 못했습니다. Camera 칸에 지정하세요.", this);
        }

        void Start()
        {
            if (DisableWhenXRActive && XRSettings.isDeviceActive)
            {
                Debug.Log("[방꾸] VR 기기가 동작 중이라 데스크톱 모드를 끕니다.", this);
                enabled = false;
                return;
            }

            if (Camera == null) return;
            _pitch = StartPitchYaw.x;
            _yaw = StartPitchYaw.y;
            Camera.transform.position = StartPosition;
            Camera.transform.rotation = Quaternion.Euler(_pitch, _yaw, 0f);
        }

        void OnDisable()
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            _dragging = false;
        }

        void Update()
        {
            var mouse = Mouse.current;
            var keyboard = Keyboard.current;
            if (mouse == null || keyboard == null || Camera == null) return;

            bool looking = mouse.rightButton.isPressed;
            SetCursorLocked(looking);
            if (looking)
            {
                _dragging = false;
                Look(mouse.delta.ReadValue());
                Move(keyboard);
            }
            else
            {
                HandleSelectionAndDrag(mouse);
                HandleRotation(keyboard);
            }

            Zoom(mouse.scroll.ReadValue().y);
        }

        // ───────────── 시점 ─────────────

        static void SetCursorLocked(bool locked)
        {
            var mode = locked ? CursorLockMode.Locked : CursorLockMode.None;
            if (Cursor.lockState != mode) Cursor.lockState = mode;
            if (Cursor.visible == locked) Cursor.visible = !locked;
        }

        void Look(Vector2 delta)
        {
            _yaw += delta.x * LookSensitivity;
            _pitch = Mathf.Clamp(_pitch - delta.y * LookSensitivity, 5f, 90f);
            Camera.transform.rotation = Quaternion.Euler(_pitch, _yaw, 0f);
        }

        void Move(Keyboard keyboard)
        {
            float forward = (keyboard.wKey.isPressed ? 1f : 0f) - (keyboard.sKey.isPressed ? 1f : 0f);
            float right = (keyboard.dKey.isPressed ? 1f : 0f) - (keyboard.aKey.isPressed ? 1f : 0f);
            if (forward == 0f && right == 0f) return;

            bool fast = keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed;
            var t = Camera.transform;
            var direction = (t.forward * forward + t.right * right).normalized;
            t.position += direction * (MoveSpeed * (fast ? FastMultiplier : 1f) * Time.deltaTime);
        }

        void Zoom(float scrollY)
        {
            if (Mathf.Abs(scrollY) < 0.01f) return;
            var t = Camera.transform;
            t.position += t.forward * (Mathf.Clamp(scrollY, -1f, 1f) * ZoomStepMeters);
        }

        // ───────────── 선택·이동 ─────────────

        void HandleSelectionAndDrag(Mouse mouse)
        {
            var ray = Camera.ScreenPointToRay(mouse.position.ReadValue());

            if (mouse.leftButton.wasPressedThisFrame)
            {
                if (Physics.Raycast(ray, out var hit, 100f, Layers.FurnitureMask))
                {
                    var item = hit.collider.GetComponentInParent<FurnitureItem>();
                    Select(item);
                    var grab = Vector3.zero;
                    _dragging = item != null && TryGetFloorPoint(ray, out grab);
                    if (_dragging)
                    {
                        _grabOffset = item.transform.position - grab;
                        _grabOffset.y = 0f;
                    }
                }
                else
                {
                    Select(null);
                    _dragging = false;
                }
            }

            if (_dragging && mouse.leftButton.isPressed && Selected != null)
            {
                if (TryGetFloorPoint(ray, out var point))
                    Selected.transform.position = DragPosition(point, _grabOffset);
            }

            if (mouse.leftButton.wasReleasedThisFrame) _dragging = false;
        }

        void Select(FurnitureItem item)
        {
            if (Selected == item) return;
            Selected = item;
            SelectionChanged?.Invoke(item);
        }

        void HandleRotation(Keyboard keyboard)
        {
            if (Selected == null) return;

            float step = keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed
                ? FineRotationDegrees
                : CoarseRotationDegrees;
            float delta = 0f;
            if (keyboard.qKey.wasPressedThisFrame) delta -= step;
            if (keyboard.eKey.wasPressedThisFrame) delta += step;
            if (delta == 0f) return;

            var t = Selected.transform;
            t.rotation = Quaternion.Euler(0f, RotateYaw(t.eulerAngles.y, delta), 0f);
        }

        // ───────────── 계산 (테스트하기 쉽게 정적 함수로 분리) ─────────────

        /// <summary>
        /// 현재 Y 회전에 delta 를 더하고 0 이상 360 미만의 정수 도로 맞춘다.
        /// 90° 단위와 15° 단위를 오가도 소수점 오차가 쌓이지 않는다.
        /// </summary>
        public static float RotateYaw(float currentYawDegrees, float deltaDegrees)
        {
            float yaw = Mathf.Repeat(Mathf.Round(currentYawDegrees + deltaDegrees), 360f);
            return yaw >= 360f ? 0f : yaw;
        }

        /// <summary>레이와 높이 planeY 의 수평면이 만나는 점. 레이가 평면과 평행하거나 뒤쪽이면 false</summary>
        public static bool TryGetPlanePoint(Ray ray, float planeY, out Vector3 point)
        {
            var plane = new Plane(Vector3.up, new Vector3(0f, planeY, 0f));
            if (plane.Raycast(ray, out float distance))
            {
                point = ray.GetPoint(distance);
                return true;
            }

            point = default;
            return false;
        }

        static bool TryGetFloorPoint(Ray ray, out Vector3 point) => TryGetPlanePoint(ray, 0f, out point);

        /// <summary>바닥 교점 + 잡은 지점의 오프셋. 가구는 바닥에 붙어 있으므로 y = 0</summary>
        public static Vector3 DragPosition(Vector3 floorPoint, Vector3 grabOffset)
        {
            var position = floorPoint + grabOffset;
            position.y = 0f;
            return position;
        }

        // ───────────── 화면 안내 ─────────────

        void OnGUI()
        {
            _style ??= new GUIStyle(GUI.skin.label) { fontSize = 16, normal = { textColor = Color.white } };

            string selected = "선택: 없음";
            if (Selected != null)
            {
                var plan = Units.WorldToPlan(Selected.transform.position);
                selected = $"선택: {Selected.ItemId}   위치 ({plan.x}, {plan.y}) mm   회전 {Selected.transform.eulerAngles.y:0}°";
            }

            GUI.Label(new Rect(12, 8, 900, 26), selected, _style);
            GUI.Label(new Rect(12, 32, 900, 26),
                "좌클릭: 선택·드래그   Q/E: 90° 회전 (Shift: 15°)   우클릭+마우스: 시점   우클릭+WASD: 이동   휠: 확대", _style);
        }
    }
}
