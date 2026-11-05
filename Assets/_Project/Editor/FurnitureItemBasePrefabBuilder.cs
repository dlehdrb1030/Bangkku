using Bangkku.Core;
using Bangkku.Placement;
using UnityEditor;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace Bangkku.EditorTools
{
    /// <summary>
    /// FurnitureItem_Base.prefab (전체개발계획 8.1) 을 만든다.
    /// 팀원은 이 프리팹의 Prefab Variant 로 가구 프리팹을 만든다.
    /// 이미 있으면 만들지 않는다. 다시 만들면 자식 ID 가 바뀌어 Variant 의 override 가 깨지기 때문이다.
    /// </summary>
    public static class FurnitureItemBasePrefabBuilder
    {
        const string MenuPath = "Tools/방꾸/FurnitureItem_Base 프리팹 생성";
        const string PrefabPath = "Assets/_Project/Prefabs/FurnitureItem_Base.prefab";

        [MenuItem(MenuPath)]
        static void Create()
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) != null)
            {
                EditorUtility.DisplayDialog("방꾸",
                    $"{PrefabPath} 가 이미 있습니다.\n\n다시 만들면 팀원이 만든 Variant 가 깨질 수 있어서 덮어쓰지 않습니다. " +
                    "고칠 부분은 프리팹을 열어서 직접 수정하세요.", "확인");
                return;
            }

            var root = new GameObject("FurnitureItem_Base") { layer = Layers.Furniture };
            try
            {
                var body = root.AddComponent<Rigidbody>();
                body.isKinematic = true;
                body.useGravity = false;

                CreateChild(root.transform, "Visual");

                var colliderObject = CreateChild(root.transform, "Collider");
                var box = colliderObject.AddComponent<BoxCollider>();
                box.size = Vector3.one;
                box.center = new Vector3(0f, 0.5f, 0f);

                CreateChild(root.transform, "Outline");

                var labelAnchor = CreateChild(root.transform, "LabelAnchor").transform;
                labelAnchor.localPosition = new Vector3(0f, 1f, 0f);

                var item = root.AddComponent<FurnitureItem>();
                item.SizeMeters = Vector3.one;
                item.SizeConfidence = ConfidenceExtensions.Default;
                item.BoxCollider = box;
                item.LabelAnchor = labelAnchor;

                // 팀장 가이드 2주차: Kinematic, Track Rotation 끔 (회전은 RotationSnap 이 담당)
                var grab = root.AddComponent<XRGrabInteractable>();
                grab.movementType = XRBaseInteractable.MovementType.Kinematic;
                grab.trackRotation = false;
                grab.throwOnDetach = false;
                // 큰 가구를 잡은 지점 그대로 끌고 다니도록 (가구 중심이 손으로 튀어오지 않게)
                grab.useDynamicAttach = true;

                var prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath, out bool success);
                if (!success)
                {
                    Debug.LogError($"[방꾸] {PrefabPath} 저장에 실패했습니다.");
                    return;
                }

                Debug.Log($"[방꾸] {PrefabPath} 를 만들었습니다.", prefab);
                Selection.activeObject = prefab;
                EditorGUIUtility.PingObject(prefab);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        static GameObject CreateChild(Transform parent, string childName)
        {
            var child = new GameObject(childName) { layer = Layers.Furniture };
            child.transform.SetParent(parent, false);
            return child;
        }
    }
}
