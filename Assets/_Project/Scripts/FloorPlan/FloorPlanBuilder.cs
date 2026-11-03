using UnityEngine;

namespace Bangkku.FloorPlan
{
    /// <summary>
    /// 평면도 JSON → 방 (전체개발계획 3장).
    /// 씬 원점에 둔 빈 오브젝트에 붙이고 Floor Plan Json 에 JSON 을 넣으면 Play 할 때 만든다.
    /// 다른 평면도는 JSON 만 바꾸면 된다.
    /// 만든 오브젝트는 이 오브젝트 아래 "Generated" 에 모인다 (씬에 저장되지 않음).
    /// 지금은 벽만 만든다. 개구부, 바닥·천장, 고정설비는 다음 단계에서 추가한다.
    /// </summary>
    public class FloorPlanBuilder : MonoBehaviour
    {
        const string GeneratedRootName = "Generated";

        [Tooltip("평면도 JSON (Assets/_Project/Data/FloorPlans/*.json)")]
        public TextAsset FloorPlanJson;

        [Tooltip("벽 재질. 비워 두면 큐브 기본 재질")]
        public Material WallMaterial;

        /// <summary>마지막으로 읽은 평면도. 생성 전이나 실패하면 null</summary>
        public FloorPlanData Data { get; private set; }

        /// <summary>생성한 오브젝트의 루트. 생성 전이나 실패하면 null</summary>
        public Transform GeneratedRoot { get; private set; }

        void Awake() => Build();

        [ContextMenu("평면도 다시 생성 (Play 중)")]
        void RebuildFromMenu() => Build();

        /// <summary>이전에 만든 것을 지우고 JSON 에서 다시 만든다. 실패하면 false (이유는 Console).</summary>
        public bool Build()
        {
            if (!Application.isPlaying)
            {
                Debug.LogWarning("[방꾸] 평면도 생성은 Play 중에만 합니다. Play 를 누르세요.", this);
                return false;
            }

            Clear();
            Data = FloorPlanLoader.Load(FloorPlanJson);
            if (Data == null) return false;

            WarnIfNotAtOrigin();

            GeneratedRoot = CreateChild(GeneratedRootName, transform);
            var walls = CreateChild("Walls", GeneratedRoot);
            int count = 0;
            foreach (var piece in WallMeshBuilder.ComputePieces(Data))
            {
                WallMeshBuilder.CreatePiece(piece, walls, WallMaterial);
                count++;
            }

            Debug.Log($"[방꾸] 평면도 {Data.Name} 생성: 벽 조각 {count}개", this);
            return true;
        }

        void Clear()
        {
            if (GeneratedRoot != null) Destroy(GeneratedRoot.gameObject);
            GeneratedRoot = null;
            Data = null;
        }

        static Transform CreateChild(string childName, Transform parent)
        {
            var child = new GameObject(childName).transform;
            child.SetParent(parent, false);
            return child;
        }

        /// <summary>
        /// layout.json 의 가구 좌표는 평면도 좌표 그대로라서, 방이 월드 원점에서 벗어나면 가구 위치가 어긋난다.
        /// </summary>
        void WarnIfNotAtOrigin()
        {
            var t = transform;
            if (t.position != Vector3.zero || t.rotation != Quaternion.identity || t.lossyScale != Vector3.one)
                Debug.LogWarning($"[방꾸] {name} 의 Transform 이 원점이 아닙니다 (Position 0, Rotation 0, Scale 1 이어야 함). " +
                                 "가구 저장 좌표와 방 위치가 어긋납니다.", this);
        }
    }
}
