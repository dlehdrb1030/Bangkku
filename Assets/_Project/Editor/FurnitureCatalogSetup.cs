using System.IO;
using Bangkku.Furniture;
using Bangkku.Placement;
using UnityEditor;
using UnityEngine;

namespace Bangkku.EditorTools
{
    public static class FurnitureCatalogSetup
    {
        const string Root = "Assets/_Project/";
        const string ListPath = Root + "Data/Furniture/FurniturePrefabList.asset";
        const string FactoryPath = Root + "Prefabs/FurnitureFactory.prefab";

        [MenuItem("Tools/방꾸/가구 카탈로그 연결")]
        public static void Build()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<TextAsset>(Root + "Data/Furniture/catalog.json");
            var items = CatalogLoader.Load(catalog);
            if (items.Count == 0) return;

            var basePrefab = AssetDatabase.LoadAssetAtPath<FurnitureItem>(Root + "Prefabs/FurnitureItem_Base.prefab");
            if (basePrefab == null)
            {
                Debug.LogError("[방꾸] FurnitureItem_Base를 찾을 수 없습니다.");
                return;
            }

            var entries = new System.Collections.Generic.List<FurniturePrefabList.Entry>();
            foreach (var item in items.Values)
            {
                if (item.Source == FurnitureSource.Box) continue;
                var prefab = AssetDatabase.LoadAssetAtPath<FurnitureItem>(Root + item.Prefab + ".prefab");
                if (prefab == null)
                {
                    Debug.LogError($"[방꾸] {item.Id}: 프리팹을 찾을 수 없습니다.");
                    return;
                }
                entries.Add(new FurniturePrefabList.Entry { Key = item.Prefab, Prefab = prefab });
            }

            var list = AssetDatabase.LoadAssetAtPath<FurniturePrefabList>(ListPath);
            if (list == null)
            {
                list = ScriptableObject.CreateInstance<FurniturePrefabList>();
                AssetDatabase.CreateAsset(list, ListPath);
            }
            var listSettings = new SerializedObject(list);
            var array = listSettings.FindProperty("entries");
            array.arraySize = entries.Count;
            for (int i = 0; i < entries.Count; i++)
            {
                var entry = array.GetArrayElementAtIndex(i);
                entry.FindPropertyRelative("Key").stringValue = entries[i].Key;
                entry.FindPropertyRelative("Prefab").objectReferenceValue = entries[i].Prefab;
            }
            listSettings.ApplyModifiedPropertiesWithoutUndo();

            // 기존 팩토리 프리팹의 GUID와 사용자 설정을 재실행 시에도 유지합니다.
            bool exists = File.Exists(FactoryPath);
            var root = exists ? PrefabUtility.LoadPrefabContents(FactoryPath) : new GameObject("FurnitureFactory");
            try
            {
                var factory = root.GetComponent<FurnitureFactory>();
                if (factory == null) factory = root.AddComponent<FurnitureFactory>();
                var settings = new SerializedObject(factory);
                settings.FindProperty("catalog").objectReferenceValue = catalog;
                settings.FindProperty("prefabs").objectReferenceValue = list;
                settings.FindProperty("boxPrefab").objectReferenceValue = basePrefab;
                settings.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(root, FactoryPath);
            }
            finally
            {
                if (exists) PrefabUtility.UnloadPrefabContents(root);
                else Object.DestroyImmediate(root);
            }
            AssetDatabase.SaveAssets();
        }
    }
}
