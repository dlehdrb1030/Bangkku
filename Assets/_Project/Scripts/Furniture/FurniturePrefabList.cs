using System;
using Bangkku.Placement;
using UnityEngine;

namespace Bangkku.Furniture
{
    public sealed class FurniturePrefabList : ScriptableObject
    {
        [Serializable]
        public struct Entry
        {
            public string Key;
            public FurnitureItem Prefab;
        }

        [SerializeField] Entry[] entries = Array.Empty<Entry>();

        public FurnitureItem Find(string key)
        {
            foreach (var entry in entries)
                if (string.Equals(entry.Key, key, StringComparison.Ordinal))
                    return entry.Prefab;
            return null;
        }
    }
}
