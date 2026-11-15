using System;
using System.Collections.Generic;
using Bangkku.Core;
using Bangkku.IO;
using Bangkku.Placement;
using UnityEngine;

namespace Bangkku.Furniture
{
    [DisallowMultipleComponent]
    public sealed class FurnitureFactory : MonoBehaviour
    {
        [SerializeField] TextAsset catalog;
        [SerializeField] FurniturePrefabList prefabs;
        [SerializeField] FurnitureItem boxPrefab;

        Dictionary<string, CatalogItem> items;
        Func<string, FurnitureItem> spawner;

        public IReadOnlyDictionary<string, CatalogItem> Items => items ??= CatalogLoader.Load(catalog);

        void OnEnable()
        {
            spawner = id => Create(id, Vector2.zero);
            LayoutSaver.CatalogSpawner = spawner;
        }

        void OnDisable()
        {
            if (LayoutSaver.CatalogSpawner == spawner)
                LayoutSaver.CatalogSpawner = null;
        }

        public FurnitureItem Create(string itemId, Vector2 positionMm, float rotationY = 0f)
        {
            if (string.IsNullOrEmpty(itemId) || !Items.TryGetValue(itemId, out var item)) return null;

            var position = Units.PlanToWorld(positionMm.x, positionMm.y);
            var prefab = item.Source == FurnitureSource.Box ? boxPrefab : prefabs?.Find(item.Prefab);
            if (prefab == null)
            {
                Debug.LogError($"[방꾸] {itemId}: 카탈로그 프리팹이 연결되지 않았습니다.", this);
                return null;
            }
            if (item.Source == FurnitureSource.Box)
                return BoxFurniture.Create(prefab, item, position, rotationY, null);

            var furniture = Instantiate(prefab, position, Quaternion.Euler(0f, rotationY, 0f));
            furniture.Init(item);
            return furniture;
        }
    }
}
