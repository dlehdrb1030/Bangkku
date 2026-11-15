using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine;

namespace Bangkku.Furniture
{
    public static class CatalogLoader
    {
        public static Dictionary<string, CatalogItem> Load(TextAsset catalog)
        {
            var items = new Dictionary<string, CatalogItem>(StringComparer.Ordinal);
            try
            {
                if (catalog == null) throw new JsonSerializationException("catalog.json 연결이 필요합니다.");
                var data = JsonConvert.DeserializeObject<CatalogData>(catalog.text);
                if (data?.Items == null) throw new JsonSerializationException("items 목록이 없습니다.");
                foreach (var item in data.Items)
                {
                    if (item == null || string.IsNullOrWhiteSpace(item.Id) || item.Size == null ||
                        item.Size.W <= 0 || item.Size.D <= 0 || item.Size.H <= 0 ||
                        !Enum.IsDefined(typeof(FurnitureSource), item.Source) ||
                        (item.Source != FurnitureSource.Box && string.IsNullOrWhiteSpace(item.Prefab)))
                        throw new JsonSerializationException($"잘못된 카탈로그 항목: {item?.Id}");
                    if (!items.TryAdd(item.Id, item))
                        throw new JsonSerializationException($"중복 가구 ID: {item.Id}");
                }
                return items;
            }
            catch (JsonException ex)
            {
                Debug.LogError($"[방꾸] 카탈로그 로드 실패: {ex.Message}", catalog);
                items.Clear();
                return items;
            }
        }
    }
}
