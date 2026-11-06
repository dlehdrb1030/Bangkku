using System;
using System.Collections.Generic;
using Bangkku.Core;
using Bangkku.Furniture;
using UnityEngine;

namespace Bangkku.Placement
{
    public enum PlacementState { Free, Tight, Overlap }

    /// <summary>
    /// 배치 가능한 가구 하나 (전체개발계획 8.1, 8.2). 팀장 작성.
    /// 판정은 Visual(3DGS·메쉬)이 아니라 Collider 자식의 실측 BoxCollider 로만 한다.
    ///
    /// 프리팹 구조 (FurnitureItem_Base.prefab):
    /// FurnitureItem (루트, Layer = Furniture, Rigidbody kinematic, XRGrabInteractable)
    /// ├── Visual       팀원: 3DGS 렌더러 / 메쉬 / 박스
    /// ├── Collider     BoxCollider. Init 이 size = 실측 치수, center.y = 높이/2 로 맞춘다
    /// ├── Outline      팀장: 박스 윤곽선
    /// └── LabelAnchor  Init 이 박스 윗면 중앙으로 옮긴다
    ///
    /// 가구 원점은 바닥면 중앙, 정면은 +Z. Collider 자식은 루트와 같은 위치·회전·스케일 1 이어야 한다.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody))]
    public class FurnitureItem : MonoBehaviour
    {
        static readonly List<FurnitureItem> AllItems = new();

        /// <summary>켜져 있는 가구 전부. PlacementValidator 가 판정할 때 쓴다</summary>
        public static IReadOnlyList<FurnitureItem> All => AllItems;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => AllItems.Clear();

        /// <summary>catalog id</summary>
        public string ItemId;

        /// <summary>실측 W, H, D (m). Unity 축 순서 X, Y, Z</summary>
        public Vector3 SizeMeters = Vector3.one;

        public Confidence SizeConfidence = ConfidenceExtensions.Default;

        /// <summary>판정용</summary>
        public BoxCollider BoxCollider;

        public Transform LabelAnchor;

        public PlacementState State { get; private set; } = PlacementState.Free;

        /// <summary>가장 가까운 벽·가구까지 (m). 판정 전이거나 범위 안에 아무것도 없으면 PositiveInfinity</summary>
        public float MinGapMeters { get; private set; } = float.PositiveInfinity;

        /// <summary>Init 으로 받은 카탈로그 항목. 이름 표시 등 UI 용. Init 전이면 null</summary>
        public CatalogItem Data { get; private set; }

        /// <summary>State 나 MinGapMeters 가 바뀌면 호출된다. 윤곽선·라벨이 구독한다.</summary>
        public event Action<FurnitureItem> PlacementChanged;

        /// <summary>FurnitureFactory 가 프리팹 생성 직후 호출한다.</summary>
        public void Init(CatalogItem data)
        {
            if (data == null)
            {
                Debug.LogError($"[방꾸] {name}: Init 에 null 이 들어왔습니다.", this);
                return;
            }

            Data = data;
            ItemId = data.Id;
            SizeMeters = data.Size.ToMeters();
            SizeConfidence = data.SizeConfidence;
            name = data.Id;

            ApplySize();
        }

        /// <summary>
        /// 판정 결과 반영. PlacementValidator 만 호출한다.
        /// </summary>
        internal void SetPlacementResult(PlacementState state, float minGapMeters)
        {
            // 둘 다 PositiveInfinity 면 Approximately 가 false 라서 == 를 먼저 본다 (그러지 않으면 매번 PlacementChanged 가 불린다)
            bool sameGap = minGapMeters == MinGapMeters || Mathf.Approximately(minGapMeters, MinGapMeters);
            if (state == State && sameGap) return;

            State = state;
            MinGapMeters = minGapMeters;
            PlacementChanged?.Invoke(this);
        }

        void Reset()
        {
            // 컴포넌트를 처음 붙였을 때 8.1 기본값
            var body = GetComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;
            gameObject.layer = Layers.Furniture;
        }

        void Awake()
        {
            // Variant 에서 실수로 바뀌어도 판정이 깨지지 않도록 실행 시 다시 맞춘다.
            var body = GetComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;
            gameObject.layer = Layers.Furniture;

            if (BoxCollider == null)
                Debug.LogError($"[방꾸] {name}: BoxCollider 가 연결되지 않았습니다. 판정이 동작하지 않습니다.", this);
            else
                BoxCollider.gameObject.layer = Layers.Furniture;
        }

        void OnEnable()
        {
            if (!AllItems.Contains(this)) AllItems.Add(this);
            PlacementValidator.EnsureInstance();
        }

        void OnDisable() => AllItems.Remove(this);

        /// <summary>SizeMeters 를 BoxCollider 와 LabelAnchor 에 반영한다.</summary>
        void ApplySize()
        {
            if (BoxCollider != null)
            {
                BoxCollider.size = SizeMeters;
                BoxCollider.center = new Vector3(0f, SizeMeters.y * 0.5f, 0f);
                WarnIfNotAligned(BoxCollider.transform, "Collider");
            }

            if (LabelAnchor != null)
            {
                LabelAnchor.localPosition = new Vector3(0f, SizeMeters.y, 0f);
            }
        }

        void WarnIfNotAligned(Transform child, string label)
        {
            if (child == transform) return;
            bool aligned = child.parent == transform
                           && child.localPosition == Vector3.zero
                           && child.localRotation == Quaternion.identity
                           && child.localScale == Vector3.one;
            if (!aligned)
                Debug.LogWarning($"[방꾸] {name}: {label} 자식은 루트 바로 아래에서 위치 0, 회전 0, 스케일 1 이어야 판정 박스가 맞습니다.", this);
        }

#if UNITY_EDITOR
        void OnDrawGizmosSelected()
        {
            // 판정 박스 (실측 치수). Outline 이 생기기 전 확인용
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireCube(new Vector3(0f, SizeMeters.y * 0.5f, 0f), SizeMeters);
        }
#endif
    }
}
