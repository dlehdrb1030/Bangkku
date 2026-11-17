using System.Collections.Generic;
using Bangkku.Core;
using Bangkku.FloorPlan;
using Bangkku.Furniture;
using UnityEngine;

namespace Bangkku.Placement
{
    /// <summary>
    /// 평면도별 기본 가구 배치. Play 를 시작하거나 저장한 배치가 없는 평면도로 바꿀 때(FloorPlanSwitcher)
    /// 씬의 FurnitureFactory(팀원 catalog.json)로 3DGS 가구를 정해 둔 자리에 놓는다.
    /// - oneroom_02: 침대(오른쪽 아래 구석, 머리판은 발코니 쪽 벽), 검은 책상(맞은편 화장실 벽에 붙임)
    /// - tworoom_01: 침대(침실2 왼쪽 위 구석, 머리판은 창 쪽 위 벽), 소파(거실 오른쪽 벽에 붙임)
    /// - 정해 둔 자리가 없는 평면도는 빈 방
    /// 씬에 평면도 바꾸기(FloorPlanSwitcher)가 있으면 Play 시작도 평면도를 바꿀 때와 같은 규칙: 그 평면도로 저장한 배치가 있으면
    /// 기본 가구 대신 불러온다 (기본 가구 상태로 평면도를 바꿔 저장 파일이 덮어써지는 것을 막음). 테스트 씬(바꾸기 없음)은 늘 기본 가구.
    /// Prefab 칸(FurnitureItem_Base)은 가구 메뉴·배치 불러오기가 박스 가구를 만들 때 쓴다 (BoxFurniture.ResolvePrefab).
    /// </summary>
    public class TestFurnitureSpawner : MonoBehaviour
    {
        struct Spec
        {
            public string ItemId;
            public Vector2 PlanMm;
            public float RotationY;
        }

        [Tooltip("Assets/_Project/Prefabs/FurnitureItem_Base.prefab (박스 가구용. 가구 메뉴·배치 불러오기가 이 칸을 대신 쓴다)")]
        public FurnitureItem Prefab;

        [Tooltip("켜면 Play 시작할 때 씬 평면도의 기본 가구를 놓는다 (평면도 바꾸기가 있고 저장한 배치가 있으면 그 배치)")]
        public bool SpawnOnStart = true;

        /// <summary>만든 가구</summary>
        public IReadOnlyList<FurnitureItem> Spawned => _spawned;

        readonly List<FurnitureItem> _spawned = new();

        static Spec At(string itemId, float xMm, float yMm, float rotationY = 0f) =>
            new() { ItemId = itemId, PlanMm = new Vector2(xMm, yMm), RotationY = rotationY };

        // 평면도 이름 → 기본 가구 (catalog id, 평면도 좌표 mm = 가구 바닥면 중앙, Y축 회전).
        // 회전 0 = 정면이 평면도 위쪽, 90 = 오른쪽, 180 = 아래쪽. 90·270 이면 W 가 평면도 Y, D 가 평면도 X 방향.
        // 벽 면(중심선 ± 두께/2)에 딱 붙인 값이라 벽 스냅과 같은 상태다
        static readonly Dictionary<string, Spec[]> Layouts = new()
        {
            ["oneroom_02"] = new[]
            {
                // 침대 1700×2100: 오른쪽 벽(x 3825)·발코니 쪽 벽(y 7900)에 붙임 → x 2125~3825, y 5800~7900
                At("bed_01", 2975, 6850, 0f),
                // 책상 1100×670: 화장실 벽(y 3105)에 등을 대고 방 쪽을 봄 → x 2425~3525, y 3105~3775
                At("desk_01", 2975, 3440, 180f),
            },
            ["tworoom_01"] = new[]
            {
                // 11-16 팀장이 Play 에서 놓은 배치를 벽에 붙여 기본값으로
                // 침대: 머리판을 침실2 위쪽(창) 벽(y 150)에, 왼쪽 벽(x 75)에 붙임 → x 75~1775, y 150~2250
                At("bed_01", 925, 1200, 180f),
                // 소파: 거실 오른쪽 벽(x 4825)에 등을 대고 거실 쪽을 봄 → x 3895~4825, y 5770~8090
                At("sofa_01", 4360, 6930, 270f),
            },
        };

        void Start()
        {
            if (!SpawnOnStart) return;

            // FurnitureFactory 는 OnEnable 에서 CatalogSpawner 를 등록하고 FloorPlanBuilder 는 Awake 에서 방을 만들므로 Start 에서는 둘 다 준비됨
            var switcher = FloorPlanSwitcher.Instance;
            if (switcher != null && switcher.LoadSavedLayout(out _)) return;
            Spawn();
        }

        /// <summary>이전에 만든 가구를 지우고 씬 평면도의 기본 가구를 다시 만든다. 만든 개수</summary>
        public int Spawn()
        {
            foreach (var old in _spawned)
                if (old != null) Destroy(old.gameObject);
            _spawned.Clear();

            var builder = FindFirstObjectByType<FloorPlanBuilder>();
            string planName = builder != null && builder.Data != null ? builder.Data.Name : null;
            if (planName == null || !Layouts.TryGetValue(planName, out var layout))
            {
                Debug.Log($"[방꾸] {planName ?? "(평면도 없음)"} 평면도는 기본 가구가 없어 빈 방으로 시작합니다.", this);
                return 0;
            }

            var factory = FindAnyObjectByType<FurnitureFactory>();
            if (factory == null)
            {
                // 테스트 씬(Test_Desktop 등)에는 팩토리가 없다. 경고 대신 알림만
                Debug.Log("[방꾸] 기본 가구: 씬에 FurnitureFactory 가 없어 빈 방으로 시작합니다.", this);
                return 0;
            }

            foreach (var spec in layout)
            {
                var item = factory.Create(spec.ItemId, spec.PlanMm, spec.RotationY);
                if (item == null)
                {
                    Debug.LogWarning($"[방꾸] 기본 가구 {spec.ItemId} 를 만들지 못했습니다 (카탈로그에 없거나 프리팹 미연결).", this);
                    continue;
                }

                item.transform.SetParent(transform, true);
                _spawned.Add(item);
            }

            Debug.Log($"[방꾸] {planName} 기본 가구 {_spawned.Count}개를 놓았습니다.", this);
            return _spawned.Count;
        }
    }
}
