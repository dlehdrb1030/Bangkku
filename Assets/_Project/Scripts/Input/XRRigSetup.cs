using Bangkku.Core;
using Bangkku.FloorPlan;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation;

namespace Bangkku.Input
{
    /// <summary>
    /// XR Origin 과 방(FloorPlanBuilder)을 이어 준다. VR 기기가 없으면 XR Device Simulator 로 같은 동작을 한다.
    /// 1. 생성된 바닥(Floors 아래 전부, 문턱 포함)에 TeleportationArea 를 붙인다 (Interaction Layer = Teleport).
    /// 2. 레이 인터랙터가 바닥·벽·가구·고정설비 Layer 에 맞도록 Raycast Mask 에 더한다.
    ///    (Starter Assets 의 기본 마스크는 Default, UI 뿐이라 우리 Layer 를 통과해 버린다)
    /// 3. 처음 한 번 XR Origin 을 시작 위치(평면도 좌표 mm)에 놓고 방 안쪽을 보게 한다.
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

        [Tooltip("평면도 좌표 (mm). 기본값은 현관 가운데 (oneroom_01)")]
        public Vector2 StartPlanMm = new(1400f, 600f);

        [Tooltip("바라보는 방향 (도). 180 = 평면도 아래쪽(= Unity -Z), 현관에서 방 안쪽")]
        public float StartYawDegrees = 180f;

        bool _moved;

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
            if (FloorPlan == null)
            {
                Debug.LogWarning("[방꾸] XRRigSetup: 씬에 FloorPlanBuilder 가 없습니다.", this);
                return;
            }

            // FloorPlanBuilder 가 먼저 Awake 에서 만들었으면 이벤트를 놓쳤으므로 여기서 한 번 적용한다
            if (FloorPlan.GeneratedRoot != null) Apply();
        }

        void OnBuilt(FloorPlanBuilder builder) => Apply();

        void Apply()
        {
            if (!ResolveOrigin()) return;
            AddRayLayers();
            AddTeleportAreas();
            if (MoveToStart && !_moved) MoveOriginToStart();
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

        void AddRayLayers()
        {
            const int mask = Layers.FloorMask | Layers.WallMask | Layers.FurnitureMask | Layers.FixtureMask;
            foreach (var ray in Origin.GetComponentsInChildren<XRRayInteractor>(true))
                ray.raycastMask = ray.raycastMask.value | mask;
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
            var position = Units.PlanToWorld(StartPlanMm.x, StartPlanMm.y);

            // 새로 만든 바닥 콜라이더를 바로 검사할 수 있게 하고, 시작 위치 아래에 바닥이 있는지 확인한다
            Physics.SyncTransforms();
            if (Physics.Raycast(position + Vector3.up * 2f, Vector3.down, out var hit, 5f, Layers.FloorMask))
                position.y = hit.point.y;
            else
                Debug.LogWarning($"[방꾸] XRRigSetup: 시작 위치 {StartPlanMm} mm 아래에 바닥이 없습니다. 평면도 안쪽 좌표로 바꾸세요.", this);

            // CharacterController 가 켜져 있으면 transform 을 바로 옮겨도 이전 위치로 되돌아간다
            var controller = Origin.GetComponent<CharacterController>();
            if (controller != null) controller.enabled = false;
            Origin.SetPositionAndRotation(position, Quaternion.Euler(0f, StartYawDegrees, 0f));
            if (controller != null) controller.enabled = true;

            _moved = true;
        }
    }
}
