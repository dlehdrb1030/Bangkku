using System.Collections.Generic;
using Bangkku.Placement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Interactors.Visuals;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Movement;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Turning;

namespace Bangkku.Input
{
    /// <summary>
    /// VR 가구 잡기 (팀장 가이드 2주차). XR Origin 의 손 인터랙터가 가구(XRGrabInteractable)를 잡으면
    /// 가구를 직접 움직이지 않고 SelectionManager 로 옮겨서, 데스크톱 모드와 같은 규칙(바닥 고정, 10mm 격자, 벽 30mm 스냅)을 쓴다.
    /// - 근거리 잡기: 잡는 순간 손이 가구 박스 안이나 바로 옆이면, 가구가 손의 수평 이동을 따라온다.
    /// - 원거리 잡기: 레이로 잡으면, 잡은 지점이 레이를 따라 같은 높이의 수평면 위로 움직인다 (데스크톱 드래그와 같은 방식).
    /// - 회전: 잡은 손의 스틱을 좌우로 넘기면 90°, 트리거를 누른 채 넘기면 15° (데스크톱 Q/E, Shift 와 같은 단위).
    /// - 잡는 동안은 스틱이 회전에 쓰이도록 시점 회전·이동·텔레포트를 잠근다.
    /// 가구의 XRGrabInteractable 은 잡기 판정만 하고 위치·회전 추적은 이 컴포넌트가 잡는 순간 끈다.
    /// XRRigSetup 과 같은 오브젝트에 붙인다.
    /// </summary>
    public class VRFurnitureGrab : MonoBehaviour
    {
        class Hold
        {
            public IXRSelectInteractor Interactor;
            public FurnitureItem Item;
            public InteractorHandedness Hand;

            /// <summary>true 면 원거리(레이), false 면 근거리(손)</summary>
            public bool Far;

            /// <summary>원거리는 레이 시작점, 근거리는 손</summary>
            public Transform Pointer;

            /// <summary>원거리: 잡은 지점의 높이 (m). 레이와 이 높이의 수평면이 만나는 점을 따라간다</summary>
            public float PlaneY;

            /// <summary>가구 원점 − 잡은 지점 (수평, m)</summary>
            public Vector3 Offset;

            /// <summary>스틱이 가운데로 돌아왔는지. 잡을 때 스틱이 이미 기울어져 있어도 바로 돌지 않도록 false 로 시작</summary>
            public bool StickArmed;
        }

        [Tooltip("XR Origin (XR Rig) 루트. 비워 두면 씬에서 XR Origin 을 찾는다")]
        public Transform Origin;

        [Header("이동")]
        [Tooltip("잡는 순간 손이 가구 박스에서 이 거리(m) 안이면 근거리 잡기, 아니면 원거리 잡기")]
        public float NearGrabDistance = 0.1f;

        [Tooltip("원거리 잡기에서 레이가 이보다 멀리(m) 가리키면 따라가지 않는다 (레이가 수평에 가까울 때 가구가 멀리 튀지 않도록)")]
        public float MaxRayDistance = 15f;

        [Header("회전 (잡은 손의 스틱 좌우)")]
        public float CoarseRotationDegrees = RotationSnap.CoarseDegrees;
        public float FineRotationDegrees = RotationSnap.FineDegrees;

        [Tooltip("스틱을 이만큼 넘기면 한 번 돈다")]
        [Range(0.1f, 1f)] public float StickPressThreshold = 0.75f;

        [Tooltip("스틱이 이 안으로 돌아와야 다음 회전")]
        [Range(0f, 0.9f)] public float StickReleaseThreshold = 0.3f;

        [Header("이동 잠금")]
        [Tooltip("켜면 가구를 잡는 동안 시점 회전·이동·텔레포트를 끈다 (스틱이 가구 회전과 겹치지 않도록)")]
        public bool LockLocomotionWhileHolding = true;

        readonly List<Hold> _holds = new();
        readonly List<XRBaseInteractor> _interactors = new();
        readonly List<Behaviour> _lockedProviders = new();

        InputAction _leftStick, _rightStick, _leftTrigger, _rightTrigger;

        void Awake()
        {
            // XR Device Simulator 의 가상 컨트롤러도 같은 이름으로 잡힌다
            _leftStick = new InputAction("BangkkuLeftStick", InputActionType.PassThrough, "<XRController>{LeftHand}/{Primary2DAxis}", expectedControlType: "Vector2");
            _rightStick = new InputAction("BangkkuRightStick", InputActionType.PassThrough, "<XRController>{RightHand}/{Primary2DAxis}", expectedControlType: "Vector2");
            _leftTrigger = new InputAction("BangkkuLeftTrigger", InputActionType.Button, "<XRController>{LeftHand}/{TriggerButton}");
            _rightTrigger = new InputAction("BangkkuRightTrigger", InputActionType.Button, "<XRController>{RightHand}/{TriggerButton}");
        }

        void OnEnable()
        {
            _leftStick.Enable();
            _rightStick.Enable();
            _leftTrigger.Enable();
            _rightTrigger.Enable();

            if (!ResolveOrigin()) return;
            foreach (var interactor in Origin.GetComponentsInChildren<XRBaseInteractor>(true))
            {
                interactor.selectEntered.AddListener(OnSelectEntered);
                interactor.selectExited.AddListener(OnSelectExited);
                _interactors.Add(interactor);
            }
        }

        void OnDisable()
        {
            foreach (var interactor in _interactors)
            {
                if (interactor == null) continue;
                interactor.selectEntered.RemoveListener(OnSelectEntered);
                interactor.selectExited.RemoveListener(OnSelectExited);
            }

            _interactors.Clear();
            _holds.Clear();
            UpdateLocomotionLock();

            _leftStick.Disable();
            _rightStick.Disable();
            _leftTrigger.Disable();
            _rightTrigger.Disable();
        }

        void OnDestroy()
        {
            _leftStick?.Dispose();
            _rightStick?.Dispose();
            _leftTrigger?.Dispose();
            _rightTrigger?.Dispose();
        }

        bool ResolveOrigin()
        {
            if (Origin != null) return true;
            var xrOrigin = FindFirstObjectByType<Unity.XR.CoreUtils.XROrigin>();
            if (xrOrigin == null)
            {
                Debug.LogWarning("[방꾸] VRFurnitureGrab: XR Origin 이 씬에 없습니다. Starter Assets 의 'XR Origin (XR Rig)' 프리팹을 넣으세요.", this);
                return false;
            }

            Origin = xrOrigin.transform;
            return true;
        }

        // ───────────── 잡기·놓기 ─────────────

        void OnSelectEntered(SelectEnterEventArgs args)
        {
            var item = args.interactableObject.transform.GetComponentInParent<FurnitureItem>();
            if (item == null) return;

            // 위치·회전은 SelectionManager 가 바꾼다. XRI 가 같이 움직이면 서로 싸운다
            if (args.interactableObject is XRGrabInteractable grab)
            {
                grab.trackPosition = false;
                grab.trackRotation = false;
                grab.trackScale = false;
            }

            // 같은 손이 잡던 것, 다른 손이 잡던 같은 가구는 새로 잡은 쪽으로 넘긴다
            _holds.RemoveAll(h => h.Interactor == args.interactorObject || h.Item == item);
            var hold = CreateHold(args.interactorObject, item);
            _holds.Add(hold);

            SelectionManager.Instance.Select(item);
            UpdateLocomotionLock();
            Debug.Log($"[방꾸] VR 잡기: {item.ItemId} ({(hold.Far ? "원거리" : "근거리")}, {hold.Hand})", item);
        }

        void OnSelectExited(SelectExitEventArgs args)
        {
            // 놓아도 선택은 그대로 둔다 (거리 라벨·상태 표시가 이어지도록)
            if (_holds.RemoveAll(h => h.Interactor == args.interactorObject) > 0) UpdateLocomotionLock();
        }

        Hold CreateHold(IXRSelectInteractor interactor, FurnitureItem item)
        {
            var hand = interactor.transform;
            var itemPosition = item.transform.position;
            var hold = new Hold { Interactor = interactor, Item = item, Hand = interactor.handedness };

            var box = item.BoxCollider;
            bool near = box != null && (box.ClosestPoint(hand.position) - hand.position).sqrMagnitude <= NearGrabDistance * NearGrabDistance;
            if (near)
            {
                hold.Pointer = hand;
                hold.Offset = Flat(itemPosition - hand.position);
                return hold;
            }

            hold.Far = true;
            hold.Pointer = RayOrigin(interactor);
            var grabPoint = FarGrabPoint(interactor, hold.Pointer, item);
            hold.PlaneY = grabPoint.y;
            hold.Offset = Flat(itemPosition - grabPoint);
            return hold;
        }

        /// <summary>레이 시작점. Near-Far Interactor 는 원거리 레이의 기준점, Ray Interactor 는 Ray Origin, 그 밖에는 인터랙터 자신</summary>
        static Transform RayOrigin(IXRSelectInteractor interactor)
        {
            if (interactor is ICurveInteractionDataProvider curve && curve.curveOrigin != null) return curve.curveOrigin;
            if (interactor is XRRayInteractor ray && ray.rayOriginTransform != null) return ray.rayOriginTransform;
            return interactor.transform;
        }

        /// <summary>레이가 가구 판정 박스에 닿은 점. 못 찾으면 인터랙터가 아는 레이 끝점, 그것도 없으면 박스 윗면 중앙</summary>
        Vector3 FarGrabPoint(IXRSelectInteractor interactor, Transform pointer, FurnitureItem item)
        {
            var ray = new Ray(pointer.position, pointer.forward);
            if (item.BoxCollider != null && item.BoxCollider.Raycast(ray, out var hit, MaxRayDistance)) return hit.point;
            if (interactor is ICurveInteractionDataProvider curve && curve.TryGetCurveEndPoint(out var end) != EndPointType.None) return end;
            return item.transform.position + Vector3.up * item.SizeMeters.y;
        }

        // ───────────── 매 프레임 ─────────────

        void LateUpdate()
        {
            bool removed = false;
            for (int i = _holds.Count - 1; i >= 0; i--)
            {
                var hold = _holds[i];
                if (hold.Item == null || hold.Pointer == null)
                {
                    _holds.RemoveAt(i);
                    removed = true;
                    continue;
                }

                // 두 손으로 서로 다른 가구를 잡았으면 마지막에 잡은(선택된) 가구만 움직인다
                if (hold.Item != SelectionManager.Instance.Selected) continue;

                HandleRotation(hold);
                if (TryGetTarget(hold, out var target)) SelectionManager.Instance.MoveSelectedTo(target);
            }

            if (removed) UpdateLocomotionLock();
        }

        bool TryGetTarget(Hold hold, out Vector3 target)
        {
            if (!hold.Far)
            {
                target = DesktopController.DragPosition(hold.Pointer.position, hold.Offset);
                return true;
            }

            return TryGetFarTarget(new Ray(hold.Pointer.position, hold.Pointer.forward), hold.PlaneY, hold.Offset, MaxRayDistance, out target);
        }

        void HandleRotation(Hold hold)
        {
            var stick = hold.Hand switch
            {
                InteractorHandedness.Left => _leftStick,
                InteractorHandedness.Right => _rightStick,
                _ => null,
            };
            if (stick == null) return;

            int direction = StickStep(stick.ReadValue<Vector2>().x, ref hold.StickArmed, StickPressThreshold, StickReleaseThreshold);
            if (direction == 0) return;

            var trigger = hold.Hand == InteractorHandedness.Left ? _leftTrigger : _rightTrigger;
            float step = trigger.IsPressed() ? FineRotationDegrees : CoarseRotationDegrees;
            SelectionManager.Instance.RotateSelected(direction * step);
        }

        // ───────────── 이동 잠금 ─────────────

        void UpdateLocomotionLock()
        {
            bool shouldLock = LockLocomotionWhileHolding && _holds.Count > 0 && Origin != null;
            if (shouldLock)
            {
                if (_lockedProviders.Count > 0) return;
                foreach (var behaviour in Origin.GetComponentsInChildren<Behaviour>(true))
                {
                    if (!IsLocomotion(behaviour) || !behaviour.enabled) continue;
                    behaviour.enabled = false;
                    _lockedProviders.Add(behaviour);
                }

                return;
            }

            foreach (var behaviour in _lockedProviders)
                if (behaviour != null) behaviour.enabled = true;
            _lockedProviders.Clear();
        }

        static bool IsLocomotion(Behaviour behaviour) =>
            behaviour is SnapTurnProvider or ContinuousTurnProvider or ContinuousMoveProvider
                or TeleportationProvider or GrabMoveProvider or TwoHandedGrabMoveProvider;

        // ───────────── 계산 (테스트하기 쉽게 정적 함수로 분리) ─────────────

        static Vector3 Flat(Vector3 v) => new(v.x, 0f, v.z);

        /// <summary>
        /// 원거리 잡기의 목표 위치: 레이와 높이 planeY 수평면의 교점 + 오프셋 (y = 0).
        /// 레이가 평면을 향하지 않거나 교점이 maxDistance 보다 멀면 false (가구를 그대로 둔다).
        /// </summary>
        public static bool TryGetFarTarget(Ray ray, float planeY, Vector3 offset, float maxDistance, out Vector3 target)
        {
            target = default;
            if (!DesktopController.TryGetPlanePoint(ray, planeY, out var point)) return false;
            if ((point - ray.origin).sqrMagnitude > maxDistance * maxDistance) return false;
            target = DesktopController.DragPosition(point, offset);
            return true;
        }

        /// <summary>
        /// 스틱 좌우 값 x 를 한 번씩 끊어진 회전 입력으로 바꾼다. press 이상 넘기면 +1(오른쪽) / -1(왼쪽) 한 번,
        /// release 안으로 돌아와야 다시 입력을 받는다. 그 밖에는 0.
        /// </summary>
        public static int StickStep(float x, ref bool armed, float press, float release)
        {
            float magnitude = Mathf.Abs(x);
            if (magnitude <= release)
            {
                armed = true;
                return 0;
            }

            if (!armed || magnitude < press) return 0;
            armed = false;
            return x > 0f ? 1 : -1;
        }
    }
}
