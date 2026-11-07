using System.Collections.Generic;
using Bangkku.UI;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.XR;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Simulation;

namespace Bangkku.Input
{
    /// <summary>
    /// Main_VR 씬에서 VR 과 데스크톱 중 하나만 켠다 (팀장 가이드 4주차 통합, 03 문서 7장 "XR 초기화가 안 되면 데스크톱 모드").
    /// - 헤드셋이 동작 중(XRSettings.isDeviceActive) → VR: VR 묶음만 켠다 (XR Origin, 손목 메뉴 등). 시뮬레이터는 끈다
    /// - 헤드셋이 없음 → Without Headset 값대로
    ///   데스크톱(기본): 데스크톱 묶음만 켠다 (데스크톱 카메라 + DesktopController, 가구 메뉴). 맥·VR 장비 없는 PC·빌드에서 이 모드
    ///   시뮬레이터: VR 묶음 + XR Device Simulator 를 켠다 (맥 에디터에서 VR 조작 시험)
    /// 두 묶음에 모두 MainCamera 태그 카메라가 있어도 하나만 켜지므로 Camera.main 이 하나로 정해진다.
    /// 다른 스크립트의 Awake 보다 먼저 돌도록 실행 순서를 가장 앞에 둔다 (꺼질 묶음이 초기화되지 않게).
    /// 씬 오브젝트 하나에 붙이고, VR 묶음·데스크톱 묶음의 부모 오브젝트를 칸에 넣는다.
    /// 칸이 비어 있으면 XR Origin·XR Device Simulator·DesktopController·가구 메뉴를 씬에서 찾아 각각 켜고 끈다.
    /// </summary>
    [DefaultExecutionOrder(-10000)]
    public class ModeSelector : MonoBehaviour
    {
        public enum Mode
        {
            [InspectorName("VR (헤드셋)")] VR,
            [InspectorName("데스크톱")] Desktop,
            [InspectorName("VR 시뮬레이터")] Simulator,
        }

        public enum NoHeadsetMode
        {
            [InspectorName("데스크톱")] Desktop,
            [InspectorName("VR 시뮬레이터 (XR Device Simulator)")] Simulator,
        }

        [Tooltip("헤드셋이 없을 때 켤 모드. 빌드·시연은 데스크톱, 맥 에디터에서 VR 조작을 시험할 때만 시뮬레이터")]
        public NoHeadsetMode WithoutHeadset = NoHeadsetMode.Desktop;

        [Header("묶음 (비워 두면 씬에서 찾음)")]
        [Tooltip("VR 에서만 켤 오브젝트들의 부모. 예: XR Origin (XR Rig), XRRigSetup, VRFurnitureMenu")]
        public GameObject VrRoot;

        [Tooltip("데스크톱에서만 켤 오브젝트들의 부모. 예: Desktop Camera (DesktopController), FurnitureMenu")]
        public GameObject DesktopRoot;

        [Tooltip("XR Device Simulator. 시뮬레이터 모드에서만 켠다")]
        public GameObject Simulator;

        /// <summary>이번 실행에서 정해진 모드</summary>
        public static Mode Current { get; private set; } = Mode.Desktop;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Current = Mode.Desktop;

        /// <summary>헤드셋이 동작 중이면 VR, 아니면 고른 값</summary>
        public static Mode Decide(bool headsetActive, NoHeadsetMode withoutHeadset) =>
            headsetActive ? Mode.VR : withoutHeadset == NoHeadsetMode.Simulator ? Mode.Simulator : Mode.Desktop;

        public static bool UsesVr(Mode mode) => mode != Mode.Desktop;

        public static string ModeText(Mode mode) => mode switch
        {
            Mode.VR => "VR (헤드셋)",
            Mode.Simulator => "VR 시뮬레이터",
            _ => "데스크톱",
        };

        void Awake()
        {
            Current = Decide(XRSettings.isDeviceActive, WithoutHeadset);
            bool vr = UsesVr(Current);

            var vrObjects = VrRoot != null ? new List<GameObject> { VrRoot } : FindVrObjects();
            var desktopObjects = DesktopRoot != null ? new List<GameObject> { DesktopRoot } : FindDesktopObjects();
            var simulator = Simulator != null ? Simulator : FindSimulator();

            // 끌 묶음을 먼저 끈 뒤 켤 묶음을 켠다 (MainCamera 가 잠시라도 둘이 되지 않게)
            foreach (var go in vr ? desktopObjects : vrObjects) SetActive(go, false);
            if (simulator != null && Current != Mode.Simulator) SetActive(simulator, false);
            foreach (var go in vr ? vrObjects : desktopObjects) SetActive(go, true);
            if (simulator != null && Current == Mode.Simulator) SetActive(simulator, true);

            if (vrObjects.Count == 0 && vr)
                Debug.LogWarning("[방꾸] ModeSelector: VR 모드인데 XR Origin 을 찾지 못했습니다. VR Root 칸을 채우세요.", this);
            if (desktopObjects.Count == 0 && !vr)
                Debug.LogWarning("[방꾸] ModeSelector: 데스크톱 모드인데 DesktopController 를 찾지 못했습니다. Desktop Root 칸을 채우세요.", this);
            if (Current == Mode.Simulator && simulator == null)
                Debug.LogWarning("[방꾸] ModeSelector: 시뮬레이터 모드인데 XR Device Simulator 를 찾지 못했습니다.", this);

            Debug.Log($"[방꾸] 실행 모드: {ModeText(Current)}" +
                      (Current == Mode.Desktop && !XRSettings.isDeviceActive ? " (헤드셋 없음)" : string.Empty), this);
        }

        static void SetActive(GameObject go, bool active)
        {
            if (go != null && go.activeSelf != active) go.SetActive(active);
        }

        // 꺼져 있는 오브젝트도 찾는다 (사람이 씬에서 한쪽 묶음을 꺼 둔 채 저장했을 수도 있어서)

        static List<GameObject> FindVrObjects()
        {
            var list = new List<GameObject>();
            AddAll<XROrigin>(list);
            AddAll<XRRigSetup>(list);
            AddAll<VRFurnitureMenu>(list);
            return list;
        }

        static List<GameObject> FindDesktopObjects()
        {
            var list = new List<GameObject>();
            AddAll<DesktopController>(list);
            AddAll<FurnitureMenu>(list);
            return list;
        }

        static GameObject FindSimulator()
        {
            var simulator = FindAnyObjectByType<XRDeviceSimulator>(FindObjectsInactive.Include);
            return simulator != null ? simulator.gameObject : null;
        }

        static void AddAll<T>(List<GameObject> list) where T : Component
        {
            foreach (var c in FindObjectsByType<T>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (!list.Contains(c.gameObject)) list.Add(c.gameObject);
        }
    }
}
