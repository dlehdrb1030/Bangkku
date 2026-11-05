using System.Collections.Generic;
using Bangkku.Core;
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

        [Tooltip("켜면 Play 시작할 때 oneroom_01 의 거실에 박스 가구 3개를 놓는다")]
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

        // 위치는 평면도 좌표 (mm), 가구 바닥면 중앙
        static readonly Spec[] Specs =
        {
            new() { Item = Box("sofa_01", "2인 소파", 1500, 800, 800, Confidence.Measured, "#7B9CC4"), PlanMm = new Vector2(1500, 5100), RotationY = 0f },
            new() { Item = Box("bed_01", "침대", 1100, 2000, 450, Confidence.Drawing, "#C49B7B"), PlanMm = new Vector2(2800, 4000), RotationY = 0f },
            new() { Item = Box("desk_01", "책상", 1200, 600, 740, Confidence.Estimated, "#9BC47B"), PlanMm = new Vector2(1400, 3300), RotationY = 0f },
        };

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

            foreach (var spec in Specs)
                _spawned.Add(Create(spec.Item, Units.PlanToWorld(spec.PlanMm.x, spec.PlanMm.y), spec.RotationY));

            Debug.Log($"[방꾸] 테스트 박스 가구 {_spawned.Count}개를 만들었습니다.", this);
        }

        /// <summary>박스 가구 하나를 만든다. Init 이 판정용 콜라이더 크기를 맞추고, Visual 에는 같은 크기의 색 상자를 넣는다.</summary>
        FurnitureItem Create(CatalogItem item, Vector3 position, float rotationY)
        {
            var furniture = Instantiate(Prefab, position, Quaternion.Euler(0f, rotationY, 0f), transform);
            furniture.Init(item);

            var visualParent = furniture.transform.Find("Visual");
            if (visualParent == null)
            {
                visualParent = new GameObject("Visual").transform;
                visualParent.SetParent(furniture.transform, false);
            }

            var size = furniture.SizeMeters;
            var visual = GameObject.CreatePrimitive(PrimitiveType.Cube);
            visual.name = "Box";
            visual.layer = Layers.Furniture;
            Destroy(visual.GetComponent<Collider>());

            var t = visual.transform;
            t.SetParent(visualParent, false);
            t.localPosition = new Vector3(0f, size.y * 0.5f, 0f);
            t.localScale = size;

            if (item.TryGetColor(out var color)) visual.GetComponent<MeshRenderer>().material.color = color;
            return furniture;
        }
    }
}
