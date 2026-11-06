using System.Collections.Generic;
using Bangkku.Core;
using Bangkku.FloorPlan;
using Bangkku.Furniture;
using UnityEngine;

namespace Bangkku.Placement
{
    /// <summary>
    /// 테스트 씬용 박스 가구 생성기. catalog.json(팀원 소유) 없이 CatalogItem 을 코드로 만들어
    /// FurnitureItem_Base 프리팹에서 가구를 만든다 (source = box). 팀원의 3DGS 가구가 오기 전까지 배치 동작을 시험하는 용도다.
    /// 씬에 빈 오브젝트로 두고 Prefab 칸에 FurnitureItem_Base 프리팹을 넣는다.
    /// </summary>
    public class TestFurnitureSpawner : MonoBehaviour
    {
        struct Spec
        {
            public CatalogItem Item;
            public Vector2 PlanMm;
            public float RotationY;
        }

        [Tooltip("Assets/_Project/Prefabs/FurnitureItem_Base.prefab")]
        public FurnitureItem Prefab;

        [Tooltip("켜면 Play 시작할 때 씬 평면도의 거실에 박스 가구 3개를 놓는다 (평면도마다 정해 둔 위치, 없으면 가장 넓은 바닥 가운데)")]
        public bool SpawnOnStart = true;

        /// <summary>만든 가구</summary>
        public IReadOnlyList<FurnitureItem> Spawned => _spawned;

        readonly List<FurnitureItem> _spawned = new();

        static CatalogItem Box(string id, string name, int w, int d, int h, Confidence confidence, string color) => new()
        {
            Id = id,
            Name = name,
            Source = FurnitureSource.Box,
            Size = new SizeMm { W = w, D = d, H = h },
            SizeConfidence = confidence,
            Color = color,
        };

        static readonly CatalogItem Sofa = Box("sofa_01", "2인 소파", 1500, 800, 800, Confidence.Measured, "#7B9CC4");
        static readonly CatalogItem Bed = Box("bed_01", "침대", 1100, 2000, 450, Confidence.Drawing, "#C49B7B");
        static readonly CatalogItem Desk = Box("desk_01", "책상", 1200, 600, 740, Confidence.Estimated, "#9BC47B");

        static Spec At(CatalogItem item, float xMm, float yMm, float rotationY = 0f) =>
            new() { Item = item, PlanMm = new Vector2(xMm, yMm), RotationY = rotationY };

        // 평면도 이름 → 가구 위치 (평면도 좌표 mm, 가구 바닥면 중앙). 벽·고정설비와 겹치지 않게 골라 둔 값
        static readonly Dictionary<string, Spec[]> Layouts = new()
        {
            ["oneroom_01"] = new[] { At(Sofa, 1500, 5100), At(Bed, 2800, 4000), At(Desk, 1400, 3300) },
            ["oneroom_02"] = new[] { At(Sofa, 1000, 6000), At(Bed, 3270, 6900), At(Desk, 1500, 3800) },
            ["tworoom_01"] = new[] { At(Sofa, 2400, 7800), At(Bed, 625, 1150), At(Desk, 900, 5800) },
        };

        /// <summary>정해 둔 위치가 없는 평면도: 가장 넓은 바닥의 가운데에서 평면도 Y 방향으로 이 간격(mm)씩 늘어놓는다</summary>
        const float FallbackSpacingMm = 1500f;

        void Start()
        {
            if (SpawnOnStart) Spawn();
        }

        /// <summary>이전에 만든 가구를 지우고 다시 만든다</summary>
        public void Spawn()
        {
            if (Prefab == null)
            {
                Debug.LogError("[방꾸] TestFurnitureSpawner: Prefab 칸에 FurnitureItem_Base 프리팹을 넣으세요.", this);
                return;
            }

            foreach (var old in _spawned)
                if (old != null) Destroy(old.gameObject);
            _spawned.Clear();

            var builder = FindFirstObjectByType<FloorPlanBuilder>();
            var plan = builder != null ? builder.Data : null;
            string planName = plan?.Name ?? "(평면도 없음)";
            foreach (var spec in GetLayout(plan))
                _spawned.Add(Create(spec.Item, Units.PlanToWorld(spec.PlanMm.x, spec.PlanMm.y), spec.RotationY));

            Debug.Log($"[방꾸] 테스트 박스 가구 {_spawned.Count}개를 만들었습니다 ({planName}).", this);
        }

        /// <summary>평면도에 맞는 가구 위치. 정해 둔 것이 없으면 가장 넓은 바닥 가운데에 늘어놓는다 (벽과 겹칠 수 있음)</summary>
        Spec[] GetLayout(FloorPlanData plan)
        {
            if (plan?.Name != null && Layouts.TryGetValue(plan.Name, out var layout)) return layout;

            var center = BoxFurniture.LargestFloorCenterMm(plan) ?? Vector2.zero;

            Debug.LogWarning($"[방꾸] TestFurnitureSpawner: '{plan?.Name}' 평면도용 가구 위치가 없어 가장 넓은 바닥 가운데 {center} mm 에 늘어놓습니다. " +
                             "Layouts 에 위치를 추가하세요.", this);
            return new[]
            {
                At(Sofa, center.x, center.y - FallbackSpacingMm),
                At(Bed, center.x, center.y),
                At(Desk, center.x, center.y + FallbackSpacingMm),
            };
        }

        FurnitureItem Create(CatalogItem item, Vector3 position, float rotationY) =>
            BoxFurniture.Create(Prefab, item, position, rotationY, transform);
    }
}
