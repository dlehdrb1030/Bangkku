using System.Collections.Generic;
using Bangkku.Core;
using UnityEngine;

namespace Bangkku.Placement
{
    /// <summary>
    /// 가구 배치 판정 (팀장 가이드 3주차). 지금은 겹침만 본다.
    /// - 각 가구의 실측 BoxCollider 와 같은 박스로 Physics.OverlapBox 를 해서 벽·다른 가구·고정설비와 겹치면 Overlap, 아니면 Free.
    /// - 맞닿은 것은 겹침이 아니다. 박스를 ContactToleranceMm 만큼 줄여서 검사하므로 벽 스냅으로 벽에 붙인 가구는 Free 다.
    /// - 판정 시점: 선택한 가구가 움직이거나 돌 때(SelectionManager.SelectedPlacementChanged) 바로, 그리고 IntervalSeconds 마다 모든 가구.
    ///   한 가구가 움직이면 그 가구와 겹쳤던 다른 가구의 상태도 바뀌므로 매번 모든 가구를 본다 (가구 10개 정도라 가볍다).
    /// - 결과는 FurnitureItem.State 에 넣고, 바뀌면 FurnitureItem.PlacementChanged 가 호출된다.
    /// 간격(Tight, MinGapMeters)은 다음 작업에서 이어 붙인다.
    /// 씬에 따로 두지 않아도 가구가 처음 켜질 때 스스로 만들어진다.
    /// </summary>
    public class PlacementValidator : MonoBehaviour
    {
        static PlacementValidator _instance;

        /// <summary>씬의 PlacementValidator. 없으면 만든다</summary>
        public static PlacementValidator Instance
        {
            get
            {
                if (_instance != null) return _instance;
                _instance = FindFirstObjectByType<PlacementValidator>();
                if (_instance == null) _instance = new GameObject(nameof(PlacementValidator)).AddComponent<PlacementValidator>();
                return _instance;
            }
        }

        /// <summary>없으면 만든다. FurnitureItem 이 켜질 때 부른다</summary>
        public static void EnsureInstance() => _ = Instance;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => _instance = null;

        [Tooltip("모든 가구를 다시 판정하는 간격 (초)")]
        public float IntervalSeconds = 0.1f;

        [Tooltip("이만큼(mm)까지 파고든 것은 맞닿음으로 보고 겹침이 아니다. 벽에 붙인 가구가 계산 오차로 겹침이 되지 않도록")]
        public float ContactToleranceMm = 1f;

        [Tooltip("가구가 겹친 대상이 바뀔 때(겹침, 겹침 해제, 다른 대상과 겹침) 콘솔에 남긴다. 끝의 시간은 같은 로그가 Collapse 로 합쳐지지 않게 붙인다")]
        public bool LogChanges = true;

        const int BufferSize = 32;
        readonly Collider[] _buffer = new Collider[BufferSize];
        readonly List<Collider> _hits = new();
        readonly List<string> _names = new();
        readonly Dictionary<FurnitureItem, string> _lastTargets = new();
        readonly List<FurnitureItem> _forget = new();
        SelectionManager _selection;
        float _nextTime;

        void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Debug.LogWarning("[방꾸] PlacementValidator 가 씬에 두 개 있습니다. 하나를 지우세요.", this);
                return;
            }

            _instance = this;
        }

        void OnEnable()
        {
            _selection = SelectionManager.Instance;
            _selection.SelectedPlacementChanged += OnSelectedMoved;
        }

        void OnDisable()
        {
            // 씬을 닫는 중에 Instance 를 부르면 SelectionManager 가 새로 만들어지므로 잡아 둔 참조만 쓴다
            if (_selection != null) _selection.SelectedPlacementChanged -= OnSelectedMoved;
        }

        void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }

        void Update()
        {
            if (Time.time < _nextTime) return;
            _nextTime = Time.time + IntervalSeconds;
            ValidateAll();
        }

        void OnSelectedMoved(FurnitureItem item) => ValidateAll();

        /// <summary>켜져 있는 모든 가구를 지금 판정한다</summary>
        public void ValidateAll()
        {
            // 이번 프레임에 옮긴 transform 을 물리 검사에 반영한다 (Auto Sync Transforms 가 꺼져 있다)
            Physics.SyncTransforms();
            var items = FurnitureItem.All;
            for (int i = 0; i < items.Count; i++) Validate(items[i]);
            ForgetMissingItems();
        }

        /// <summary>가구 하나를 판정해서 State 에 넣는다. 호출 전에 Physics.SyncTransforms 가 되어 있어야 한다</summary>
        public void Validate(FurnitureItem item)
        {
            if (item == null) return;

            FindOverlaps(item, _hits);
            var state = _hits.Count > 0 ? PlacementState.Overlap : PlacementState.Free;
            item.SetPlacementResult(state, item.MinGapMeters);

            if (LogChanges) LogIfChanged(item);
        }

        /// <summary>
        /// item 과 겹친 벽·가구·고정설비 콜라이더를 results 에 담는다 (다른 가구는 그 가구의 판정용 BoxCollider 하나로 센다).
        /// </summary>
        public void FindOverlaps(FurnitureItem item, List<Collider> results)
        {
            results.Clear();
            var box = item.BoxCollider;
            if (box == null || !box.enabled || !box.gameObject.activeInHierarchy) return;

            var t = box.transform;
            var center = t.TransformPoint(box.center);
            var halfExtents = ShrunkHalfExtents(box.size, t.lossyScale, Units.MmToM(ContactToleranceMm));
            int count = Physics.OverlapBoxNonAlloc(center, halfExtents, _buffer, t.rotation,
                Layers.PlacementObstacleMask, QueryTriggerInteraction.Ignore);
            if (count == BufferSize)
                Debug.LogWarning($"[방꾸] {item.ItemId}: 겹친 콜라이더가 {BufferSize}개를 넘어 일부만 셉니다.", item);

            for (int i = 0; i < count; i++)
            {
                var hit = _buffer[i];
                _buffer[i] = null;
                if (hit == box) continue;

                var owner = hit.GetComponentInParent<FurnitureItem>();
                if (owner == item) continue;

                // 가구는 판정용 BoxCollider 만 센다 (Visual 등에 남은 콜라이더는 무시)
                if (hit.gameObject.layer == Layers.Furniture && (owner == null || hit != owner.BoxCollider)) continue;

                results.Add(hit);
            }
        }

        /// <summary>
        /// 판정용 박스의 절반 크기 (m). 스케일을 반영하고 각 축에서 tolerance 만큼 줄인다.
        /// 너무 얇은 축은 0 이 되지 않도록 tolerance 만큼 남긴다.
        /// </summary>
        public static Vector3 ShrunkHalfExtents(Vector3 size, Vector3 lossyScale, float toleranceMeters)
        {
            var half = new Vector3(
                Mathf.Abs(size.x * lossyScale.x),
                Mathf.Abs(size.y * lossyScale.y),
                Mathf.Abs(size.z * lossyScale.z)) * 0.5f;
            float min = Mathf.Max(toleranceMeters, 0f);
            return new Vector3(
                Mathf.Max(half.x - toleranceMeters, min),
                Mathf.Max(half.y - toleranceMeters, min),
                Mathf.Max(half.z - toleranceMeters, min));
        }

        /// <summary>
        /// 겹친 대상 목록이 지난 판정과 달라지면 로그를 남긴다.
        /// 상태(Overlap/Free)만 보면 벽과 겹친 채 다른 가구에서 빠져나올 때 아무 로그가 없어서, 대상 목록으로 비교한다.
        /// </summary>
        void LogIfChanged(FurnitureItem item)
        {
            string targets = DescribeHits();
            _lastTargets.TryGetValue(item, out var previous);
            if (targets == (previous ?? string.Empty)) return;
            _lastTargets[item] = targets;

            float time = Time.time;
            if (targets.Length == 0)
                Debug.Log($"[방꾸] 겹침 해제: {item.ItemId}  ({time:0.00}초)", item);
            else
                Debug.Log($"[방꾸] 겹침: {item.ItemId} ↔ {targets}  ({time:0.00}초)", item);
        }

        /// <summary>_hits 의 이름 (가구는 ItemId, 벽·설비는 오브젝트 이름)을 정렬해서 잇는다. 같은 대상이면 순서와 상관없이 같은 문자열</summary>
        string DescribeHits()
        {
            if (_hits.Count == 0) return string.Empty;
            _names.Clear();
            foreach (var hit in _hits)
            {
                var owner = hit.GetComponentInParent<FurnitureItem>();
                _names.Add(owner != null ? owner.ItemId : hit.name);
            }

            _names.Sort(System.StringComparer.Ordinal);
            return string.Join(", ", _names);
        }

        /// <summary>꺼지거나 지워진 가구의 지난 로그 기록을 지운다 (다시 켜지면 처음부터)</summary>
        void ForgetMissingItems()
        {
            if (_lastTargets.Count <= FurnitureItem.All.Count) return;
            _forget.Clear();
            foreach (var item in _lastTargets.Keys)
                if (item == null || !item.isActiveAndEnabled) _forget.Add(item);
            foreach (var item in _forget) _lastTargets.Remove(item);
        }

#if UNITY_EDITOR
        void OnDrawGizmos()
        {
            // 겹친 가구의 판정 박스를 빨간 선으로 (Scene 뷰, Game 뷰는 Gizmos 버튼을 켜면). 윤곽선 색 작업 전 확인용
            Gizmos.color = Color.red;
            foreach (var item in FurnitureItem.All)
            {
                if (item == null || item.State != PlacementState.Overlap || item.BoxCollider == null) continue;
                var t = item.BoxCollider.transform;
                Gizmos.matrix = t.localToWorldMatrix;
                Gizmos.DrawWireCube(item.BoxCollider.center, item.BoxCollider.size);
            }

            Gizmos.matrix = Matrix4x4.identity;
        }
#endif
    }
}
