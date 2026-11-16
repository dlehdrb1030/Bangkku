using Bangkku.Core;
using Bangkku.FloorPlan;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.XR;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Interactors.Casters;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Movement;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Turning;
using UnityEngine.XR.Interaction.Toolkit.Samples.StarterAssets;

namespace Bangkku.Input
{
    /// <summary>
    /// XR Origin 과 방(FloorPlanBuilder)을 이어 준다. VR 기기가 없으면 XR Device Simulator 로 같은 동작을 한다.
    /// 1. 생성된 바닥(Floors 아래 전부, 문턱 포함)에 TeleportationArea 를 붙인다 (Interaction Layer = Teleport).
    /// 2. 레이 인터랙터가 바닥·벽·가구·고정설비 Layer 에 맞도록 Raycast Mask 에 더한다.
    ///    (Starter Assets 의 기본 마스크는 Default, UI 뿐이라 우리 Layer 를 통과해 버린다)
    ///    손의 Near-Far Interactor 는 원거리 레이에 같은 Layer 를, 근거리 구에 Furniture 를 더한다 (가구 잡기).
    /// 3. 처음 한 번 XR Origin 을 현관문 안쪽에 놓고 방 안쪽을 보게 한다 (평면도에서 자동으로 찾음).
    /// 4. VR 기기 없이(XR Device Simulator) 실행하면 눈높이를 서 있는 성인 높이(기본 1.6m)로 맞춘다.
    /// 5. 바닥 밖으로 떨어지면(바닥보다 2m 아래) 시작 위치로 되돌린다.
    /// 6. 오른쪽 스틱 회전을 부드러운 회전으로 바꾼다 (Starter Assets 기본은 45°씩 끊어 도는 스냅 회전).
    ///    가구를 잡고 있는 동안에는 VRFurnitureGrab 이 회전 Provider 를 꺼서 스틱이 가구 회전에만 쓰인다.
    /// 씬 오브젝트 하나에 붙이고 Floor Plan 과 Origin 을 지정한다. 비워 두면 씬에서 찾는다.
    /// </summary>
    public class XRRigSetup : MonoBehaviour
    {
        const string TeleportInteractionLayer = "Teleport";

        [Tooltip("방을 만드는 FloorPlanBuilder. 비워 두면 씬에서 찾는다")]
        public FloorPlanBuilder FloorPlan;

        [Tooltip("XR Origin (XR Rig) 루트. 비워 두면 씬에서 XR Origin 을 찾는다")]
        public Transform Origin;

        [Header("시작 위치")]
        [Tooltip("켜면 방을 만든 뒤 처음 한 번 XR Origin 을 아래 위치로 옮긴다")]
        public bool MoveToStart = true;

        [Tooltip("켜면 평면도에서 현관문(한쪽에만 바닥이 있는 문)을 찾아 그 안쪽에서 방을 바라보며 시작한다. 평면도를 바꿔도 따로 맞출 필요가 없다. " +
                 "현관문을 못 찾으면 아래 Start Plan Mm / Start Yaw Degrees 를 쓴다")]
        public bool StartAtEntrance = true;

        [Tooltip("현관문에서 안쪽으로 들어간 거리 (mm)")]
        public float EntranceInsideMm = 600f;

        [Tooltip("Start At Entrance 를 끄거나 현관문을 못 찾았을 때 쓰는 평면도 좌표 (mm)")]
        public Vector2 StartPlanMm = new(1400f, 600f);

        [Tooltip("Start At Entrance 를 끄거나 현관문을 못 찾았을 때 바라보는 방향 (도). 180 = 평면도 아래쪽(= Unity -Z)")]
        public float StartYawDegrees = 180f;

        [Header("시뮬레이터 눈높이")]
        [Tooltip("켜면 VR 기기 없이(XR Device Simulator) 실행할 때 카메라를 아래 눈높이에 둔다. 실제 헤드셋은 머리 높이를 그대로 쓰므로 적용하지 않는다")]
        public bool ApplySimulatorEyeHeight = true;

        [Tooltip("바닥에서 눈까지 (m). 기본값 1.6 = 한국 성인 남성 평균 키(약 172cm)의 눈높이. " +
                 "Starter Assets 기본값은 1.36 이라 앉은 눈높이처럼 가구가 크게 보인다")]
        public float SimulatorEyeHeightMeters = 1.6f;

        [Header("이동")]
        [Tooltip("왼쪽 스틱 이동 속도 (m/초). Starter Assets 기본값은 2.5. 11-16 Windows VR 시험 후 2/3 로 줄임 (빠를수록 멀미가 날 수 있음)")]
        public float MoveSpeedMetersPerSecond = 1.67f;

        [Header("회전")]
        [Tooltip("켜면 오른쪽 스틱 좌우로 부드럽게 돈다. 끄면 Starter Assets 기본 스냅 회전(45°씩)")]
        public bool SmoothTurn = true;

        [Tooltip("부드러운 회전 속도 (도/초). 11-16 Windows VR 시험 후 90 → 60 (Starter Assets 기본값과 같음). 빠를수록 멀미가 날 수 있음")]
        public float SmoothTurnDegreesPerSecond = 60f;

        [Tooltip("켜면 오른쪽 스틱을 아래로 당겨 180° 돈다 (Starter Assets 기본 켬). 11-16 시험 후 끔")]
        public bool TurnAround;

        [Header("떨어짐 복구")]
        [Tooltip("XR Origin 이 바닥보다 이만큼(m) 아래로 떨어지면 시작 위치로 되돌린다 (바닥 밖으로 나가 중력으로 떨어졌을 때). 0 이면 끔")]
        public float ResetBelowMeters = 2f;

        bool _moved;

        /// <summary>시작 위치로 옮긴 평면도 이름. 평면도가 바뀌면(FloorPlanSwitcher) 새 평면도의 시작 위치로 다시 옮긴다</summary>
        string _startedPlan;

        void OnEnable()
        {
            if (FloorPlan == null) FloorPlan = FindFirstObjectByType<FloorPlanBuilder>();
            if (FloorPlan != null) FloorPlan.Built += OnBuilt;
        }

        void OnDisable()
        {
            if (FloorPlan != null) FloorPlan.Built -= OnBuilt;
        }

        void Start()
        {
            if (ResolveOrigin())
            {
                if (ApplySimulatorEyeHeight) SetSimulatorEyeHeight();
                ApplyTurnMode();
            }

            if (FloorPlan == null)
            {
                Debug.LogWarning("[방꾸] XRRigSetup: 씬에 FloorPlanBuilder 가 없습니다.", this);
                return;
            }

            // FloorPlanBuilder 가 먼저 Awake 에서 만들었으면 이벤트를 놓쳤으므로 여기서 한 번 적용한다
            if (FloorPlan.GeneratedRoot != null) Apply();
        }

        void OnBuilt(FloorPlanBuilder builder) => Apply();

        void Update()
        {
            // 현관문은 막아 두었지만, 다른 틈으로 바닥 밖에 나가 떨어져도 계속 떨어지지 않게 한다
            if (ResetBelowMeters <= 0f || !_moved || Origin == null || FloorPlan == null || FloorPlan.Data == null) return;
            if (Origin.position.y >= -ResetBelowMeters) return;

            Debug.Log("[방꾸] 바닥 밖으로 떨어져서 시작 위치로 되돌립니다.", this);
            MoveOriginToStart();
        }

        void Apply()
        {
            if (!ResolveOrigin()) return;
            AddRayLayers();
            AddTeleportAreas();
            // 같은 평면도를 다시 만들 때(Inspector 메뉴)는 그대로 두고, 다른 평면도로 바뀌면 새 현관으로 옮긴다
            if (MoveToStart && (!_moved || _startedPlan != FloorPlan.Data?.Name)) MoveOriginToStart();
        }

        bool ResolveOrigin()
        {
            if (Origin != null) return true;
            var xrOrigin = FindFirstObjectByType<Unity.XR.CoreUtils.XROrigin>();
            if (xrOrigin == null)
            {
                Debug.LogWarning("[방꾸] XRRigSetup: XR Origin 이 씬에 없습니다. Starter Assets 의 'XR Origin (XR Rig)' 프리팹을 넣으세요.", this);
                return false;
            }

            Origin = xrOrigin.transform;
            return true;
        }

        /// <summary>
        /// VR 기기가 없을 때 카메라 높이를 SimulatorEyeHeightMeters 로 맞춘다.
        /// 기기가 없으면 XR Origin 이 Tracking Origin 을 정하지 못해 Camera Offset 의 프리팹 높이(1.36m)를 그대로 쓰고,
        /// 시뮬레이터의 머리(HMD)는 Camera Offset 기준 높이 0 에서 시작하므로 Camera Offset 높이가 곧 눈높이다.
        /// 실제 헤드셋이 켜져 있으면 XR Origin 이 바닥 기준으로 맞추므로 건드리지 않는다.
        /// </summary>
        void SetSimulatorEyeHeight()
        {
            if (ModeSelector.HeadsetConnected()) return;

            var xrOrigin = Origin.GetComponent<XROrigin>();
            var offset = xrOrigin != null && xrOrigin.CameraFloorOffsetObject != null
                ? xrOrigin.CameraFloorOffsetObject.transform
                : null;
            if (offset == null)
            {
                Debug.LogWarning("[방꾸] XRRigSetup: XR Origin 의 Camera Floor Offset Object 가 없어 시뮬레이터 눈높이를 맞추지 못했습니다.", this);
                return;
            }

            var position = offset.localPosition;
            position.y = SimulatorEyeHeightMeters;
            offset.localPosition = position;
            Debug.Log($"[방꾸] VR 기기가 없어 시뮬레이터 눈높이를 {SimulatorEyeHeightMeters:0.00}m 로 맞췄습니다.", this);
        }

        /// <summary>
        /// 스틱 회전 방식을 정한다. Starter Assets 의 손마다 있는 ControllerInputActionManager 가 Turn(부드러운)·Snap Turn 입력 중 하나만 켠다.
        /// 왼손은 이동(Smooth Motion)을 쓰므로 매니저가 왼손 회전 입력을 끄고, 실제로는 오른손만 바뀐다.
        /// 회전 Provider(ContinuousTurnProvider·SnapTurnProvider)는 프리팹에서 둘 다 켜져 있고 입력만 바뀐다.
        /// 같은 자리에서 180° 돌기(TurnAround)와 왼쪽 스틱 이동 속도(ContinuousMoveProvider)도 맞춘다.
        /// </summary>
        void ApplyTurnMode()
        {
            int managers = 0;
            foreach (var manager in Origin.GetComponentsInChildren<ControllerInputActionManager>(true))
            {
                manager.smoothTurnEnabled = SmoothTurn;
                managers++;
            }

            foreach (var provider in Origin.GetComponentsInChildren<ContinuousTurnProvider>(true))
            {
                provider.turnSpeed = SmoothTurnDegreesPerSecond;
                provider.enableTurnAround = TurnAround;
            }

            foreach (var provider in Origin.GetComponentsInChildren<SnapTurnProvider>(true))
                provider.enableTurnAround = TurnAround;

            int movers = 0;
            foreach (var provider in Origin.GetComponentsInChildren<ContinuousMoveProvider>(true))
            {
                provider.moveSpeed = MoveSpeedMetersPerSecond;
                movers++;
            }

            if (managers == 0)
                Debug.LogWarning("[방꾸] XRRigSetup: ControllerInputActionManager 가 없어 회전 방식을 바꾸지 못했습니다.", this);
            else if (SmoothTurn)
                Debug.Log($"[방꾸] 부드러운 회전 {SmoothTurnDegreesPerSecond:0}°/초, 180° 돌기 {(TurnAround ? "켬" : "끔")}, 이동 {MoveSpeedMetersPerSecond:0.##}m/초", this);

            if (movers == 0)
                Debug.LogWarning("[방꾸] XRRigSetup: ContinuousMoveProvider 가 없어 이동 속도를 바꾸지 못했습니다.", this);
        }

        void AddRayLayers()
        {
            // 원거리: 벽·바닥도 맞아야 벽 너머 가구를 잡지 않는다
            const int farMask = Layers.FloorMask | Layers.WallMask | Layers.FurnitureMask | Layers.FixtureMask;
            foreach (var ray in Origin.GetComponentsInChildren<XRRayInteractor>(true))
                ray.raycastMask = ray.raycastMask.value | farMask;

            // Starter Assets 의 Near-Far Interactor (XRI 3.x 기본 손 인터랙터): 원거리 레이와 근거리 구 모두 기본값이 Default Layer 뿐이다
            foreach (var nearFar in Origin.GetComponentsInChildren<NearFarInteractor>(true))
            {
                if (nearFar.farInteractionCaster is CurveInteractionCaster far)
                    far.raycastMask = far.raycastMask.value | farMask;
                if (nearFar.nearInteractionCaster is SphereInteractionCaster near)
                    near.physicsLayerMask = near.physicsLayerMask.value | Layers.FurnitureMask;
            }

            foreach (var direct in Origin.GetComponentsInChildren<XRDirectInteractor>(true))
                direct.physicsLayerMask = direct.physicsLayerMask.value | Layers.FurnitureMask;
        }

        void AddTeleportAreas()
        {
            var floors = FloorPlan.GeneratedRoot != null ? FloorPlan.GeneratedRoot.Find("Floors") : null;
            if (floors == null) return;

            int layers = InteractionLayerMask.GetMask(TeleportInteractionLayer);
            if (layers == 0)
                Debug.LogWarning($"[방꾸] XRRigSetup: Interaction Layer \"{TeleportInteractionLayer}\" 가 없습니다. " +
                                 "Edit > Project Settings > XR Interaction Toolkit > Interaction Layers 를 확인하세요.", this);

            var provider = Origin.GetComponentInChildren<TeleportationProvider>(true);
            if (provider == null) provider = FindFirstObjectByType<TeleportationProvider>();

            int count = 0;
            foreach (Transform floor in floors)
            {
                if (floor.GetComponent<TeleportationArea>() != null) continue;

                // 꺼 둔 채로 붙여야 Awake 가 설정한 값(Interaction Layer, Provider)을 보고 등록된다
                bool wasActive = floor.gameObject.activeSelf;
                floor.gameObject.SetActive(false);
                var area = floor.gameObject.AddComponent<TeleportationArea>();
                area.interactionLayers = layers;
                area.teleportationProvider = provider;
                floor.gameObject.SetActive(wasActive);
                count++;
            }

            if (count > 0) Debug.Log($"[방꾸] 텔레포트 영역 {count}개를 바닥에 붙였습니다.", this);
        }

        void MoveOriginToStart()
        {
            var planMm = StartPlanMm;
            float yaw = StartYawDegrees;
            if (StartAtEntrance)
            {
                if (FloorPlanSpots.TryGetEntrance(FloorPlan.Data, EntranceInsideMm, out var entrance, out float entranceYaw))
                {
                    planMm = entrance;
                    yaw = entranceYaw;
                }
                else
                    Debug.LogWarning($"[방꾸] XRRigSetup: 평면도에서 현관문(한쪽에만 바닥이 있는 문)을 찾지 못해 Start Plan Mm {StartPlanMm} 에서 시작합니다.", this);
            }

            var position = Units.PlanToWorld(planMm.x, planMm.y);

            // 새로 만든 바닥 콜라이더를 바로 검사할 수 있게 하고, 시작 위치 아래에 바닥이 있는지 확인한다
            Physics.SyncTransforms();
            if (Physics.Raycast(position + Vector3.up * 2f, Vector3.down, out var hit, 5f, Layers.FloorMask))
                position.y = hit.point.y;
            else
                Debug.LogWarning($"[방꾸] XRRigSetup: 시작 위치 {planMm} mm 아래에 바닥이 없습니다. 평면도 안쪽 좌표로 바꾸세요.", this);

            // CharacterController 가 켜져 있으면 transform 을 바로 옮겨도 이전 위치로 되돌아간다
            var controller = Origin.GetComponent<CharacterController>();
            if (controller != null) controller.enabled = false;
            Origin.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
            if (controller != null) controller.enabled = true;

            _moved = true;
            _startedPlan = FloorPlan.Data?.Name;
        }
    }
}
