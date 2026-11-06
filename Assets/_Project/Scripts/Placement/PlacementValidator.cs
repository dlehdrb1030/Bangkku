using System.Collections.Generic;
using Bangkku.Core;
using UnityEngine;

namespace Bangkku.Placement
{
    /// <summary>
    /// 가구 배치 판정 (팀장 가이드 3주차): 겹침과 간격.
    /// - 겹침: 각 가구의 실측 BoxCollider 와 같은 박스로 Physics.OverlapBox 를 해서 벽·다른 가구·고정설비와 겹치면 Overlap, 아니면 Free.
    ///   맞닿은 것은 겹침이 아니다. 박스를 ContactToleranceMm 만큼 줄여서 검사하므로 벽 스냅으로 벽에 붙인 가구는 Free 다.
    /// - 간격: 박스의 앞·뒤·왼쪽·오른쪽 4면을 각각 바깥으로 밀어 보며(BoxCast) 가장 먼저 닿는 벽·가구·고정설비까지 거리를 잰다.
    ///   가이드는 면 가운데에서 Raycast 하나지만, 면 전체를 밀어야 가구 모서리 쪽에 있는 설비도 놓치지 않는다.
    ///   4면 중 가장 짧은 값이 FurnitureItem.MinGapMeters. MaxGapMeters 안에 아무것도 없으면 PositiveInfinity.
    ///   대각선 방향(모서리 바깥)은 재지 않는다.
    /// - 판정 시점: 선택한 가구가 움직이거나 돌 때(SelectionManager.SelectedPlacementChanged) 바로, 그리고 IntervalSeconds 마다 모든 가구.
    ///   한 가구가 움직이면 그 가구와 겹쳤던 다른 가구의 상태도 바뀌므로 매번 모든 가구를 본다 (가구 10개 정도라 가볍다).
    /// - 결과는 FurnitureItem.State, MinGapMeters 에 넣고, 바뀌면 FurnitureItem.PlacementChanged 가 호출된다.
    /// 좁음(Tight) 판정과 신뢰도 여유값은 다음 작업에서 이어 붙인다.
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

        [Tooltip("간격을 재는 최대 거리 (m). 이보다 멀면 간격 없음(∞). 가이드 기준 1m")]
        public float MaxGapMeters = 1f;

        [Tooltip("가구가 겹친 대상이 바뀔 때(겹침, 겹침 해제, 다른 대상과 겹침) 콘솔에 남긴다")]
        public bool LogChanges = true;

        /// <summary>가구 한 면에서 가장 가까운 대상까지의 간격</summary>
        public struct GapResult
        {
            /// <summary>간격 (m). 범위 안에 아무것도 없으면 PositiveInfinity, 이미 겹쳐 있으면 0</summary>
            public float Meters;

            /// <summary>가장 가까운 대상. 없으면 null</summary>
            public Collider Target;

            /// <summary>가구 면 위의 점과 대상 위의 점 (월드, m). 거리 라벨·선 표시용</summary>
            public Vector3 From, To;

            public bool HasTarget => Target != null;

            public static GapResult None => new() { Meters = float.PositiveInfinity };
        }

        /// <summary>BoxCast 할 때 면 두께 (m). 면을 얇은 판으로 보고 민다</summary>
        const float CastHalfThicknessMeters = 0.0005f;

        const int BufferSize = 32;
        readonly Collider[] _buffer = new Collider[BufferSize];
        readonly RaycastHit[] _castHits = new RaycastHit[BufferSize];
        readonly Dictionary<FurnitureItem, GapResult> _gaps = new();
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
            var gap = MeasureGap(item);
            _gaps[item] = gap;
            item.SetPlacementResult(state, gap.Meters);

            if (LogChanges) LogIfChanged(item);
        }

        /// <summary>마지막 판정에서 잰 가장 가까운 대상까지의 간격. 판정 전이면 false</summary>
        public bool TryGetGap(FurnitureItem item, out GapResult gap) => _gaps.TryGetValue(item, out gap);

        /// <summary>
        /// 가구 4면(로컬 ±X, ±Z)을 바깥으로 MaxGapMeters 까지 밀어 보고 가장 가까운 대상을 찾는다.
        /// 면은 박스 가운데에서 출발시킨다. BoxCast 는 출발할 때 이미 닿아 있는 것을 거리 0 으로 돌려주므로,
        /// 가운데에서 출발하면 면에 딱 붙은 벽도 (반쪽 두께) 만큼 떨어진 곳에서 잡혀 간격 0 으로 잴 수 있다.
        /// 호출 전에 Physics.SyncTransforms 가 되어 있어야 한다.
        /// </summary>
        public GapResult MeasureGap(FurnitureItem item)
        {
            var box = item.BoxCollider;
            if (box == null || !box.enabled || !box.gameObject.activeInHierarchy) return GapResult.None;

            var t = box.transform;
            var center = t.TransformPoint(box.center);
            var scale = t.lossyScale;
            var full = new Vector3(Mathf.Abs(box.size.x * scale.x), Mathf.Abs(box.size.y * scale.y), Mathf.Abs(box.size.z * scale.z)) * 0.5f;
            // 옆면에 맞닿은 벽(진행 방향과 나란한 벽)에 걸리지 않도록 면을 조금 줄인다
            var shrunk = ShrunkHalfExtents(box.size, scale, Units.MmToM(ContactToleranceMm));

            var best = GapResult.None;
            Cast(item, center, t.right, full.x, new Vector3(CastHalfThicknessMeters, shrunk.y, shrunk.z), t.rotation, ref best);
            Cast(item, center, -t.right, full.x, new Vector3(CastHalfThicknessMeters, shrunk.y, shrunk.z), t.rotation, ref best);
            Cast(item, center, t.forward, full.z, new Vector3(shrunk.x, shrunk.y, CastHalfThicknessMeters), t.rotation, ref best);
            Cast(item, center, -t.forward, full.z, new Vector3(shrunk.x, shrunk.y, CastHalfThicknessMeters), t.rotation, ref best);
            return best;
        }

        /// <summary>한 면을 direction 으로 밀어서 best 보다 가까운 대상이 있으면 바꾼다. halfDepth = 가운데에서 그 면까지 (m)</summary>
        void Cast(FurnitureItem item, Vector3 center, Vector3 direction, float halfDepth, Vector3 halfExtents, Quaternion rotation, ref GapResult best)
        {
            int count = Physics.BoxCastNonAlloc(center, halfExtents, direction, _castHits, rotation,
                halfDepth + MaxGapMeters, Layers.PlacementObstacleMask, QueryTriggerInteraction.Ignore);

            for (int i = 0; i < count; i++)
            {
                var hit = _castHits[i];
                if (!IsCounted(item, hit.collider)) continue;

                // 출발할 때 이미 닿아 있던 것 (가구 가운데까지 들어와 있음 = 겹침): 거리 0, 점은 (0, 0, 0) 으로 온다
                bool startedInside = hit.distance <= 0f && hit.point == Vector3.zero;
                float gap = startedInside ? 0f : Mathf.Max(hit.distance - halfDepth, 0f);
                if (gap >= best.Meters) continue;

                var face = center + direction * halfDepth;
                best = new GapResult
                {
                    Meters = gap,
                    Target = hit.collider,
                    To = startedInside ? face : hit.point,
                    From = startedInside ? face : hit.point - direction * gap,
                };
            }
        }

        /// <summary>판정에 세는 콜라이더인지: 자기 자신이 아니고, 가구라면 그 가구의 판정용 BoxCollider (Visual 등에 남은 콜라이더는 무시)</summary>
        static bool IsCounted(FurnitureItem item, Collider hit)
        {
            if (hit == null || hit == item.BoxCollider) return false;
            var owner = hit.GetComponentInParent<FurnitureItem>();
            if (owner == item) return false;
            return hit.gameObject.layer != Layers.Furniture || (owner != null && hit == owner.BoxCollider);
        }

        /// <summary>대상 이름: 가구는 ItemId, 벽·고정설비는 오브젝트 이름 (예: w3_0, kitchen)</summary>
        public static string DescribeTarget(Collider target)
        {
            if (target == null) return string.Empty;
            var owner = target.GetComponentInParent<FurnitureItem>();
            return owner != null ? owner.ItemId : target.name;
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
                if (IsCounted(item, hit)) results.Add(hit);
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

            if (targets.Length == 0)
                Debug.Log($"[방꾸] 겹침 해제: {item.ItemId}", item);
            else
                Debug.Log($"[방꾸] 겹침: {item.ItemId} ↔ {targets}", item);
        }

        /// <summary>_hits 의 이름 (가구는 ItemId, 벽·설비는 오브젝트 이름)을 정렬해서 잇는다. 같은 대상이면 순서와 상관없이 같은 문자열</summary>
        string DescribeHits()
        {
            if (_hits.Count == 0) return string.Empty;
            _names.Clear();
            foreach (var hit in _hits) _names.Add(DescribeTarget(hit));

            _names.Sort(System.StringComparer.Ordinal);
            return string.Join(", ", _names);
        }

        /// <summary>꺼지거나 지워진 가구의 지난 로그 기록을 지운다 (다시 켜지면 처음부터)</summary>
        void ForgetMissingItems()
        {
            if (_lastTargets.Count <= FurnitureItem.All.Count && _gaps.Count <= FurnitureItem.All.Count) return;
            _forget.Clear();
            foreach (var item in _lastTargets.Keys)
                if (item == null || !item.isActiveAndEnabled) _forget.Add(item);
            foreach (var item in _gaps.Keys)
                if ((item == null || !item.isActiveAndEnabled) && !_forget.Contains(item)) _forget.Add(item);
            foreach (var item in _forget)
            {
                _lastTargets.Remove(item);
                _gaps.Remove(item);
            }
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

            // 선택한 가구에서 가장 가까운 대상까지 노란 선 (거리 라벨 작업 전 확인용)
            var selected = _selection != null ? _selection.Selected : null;
            if (selected != null && _gaps.TryGetValue(selected, out var gap) && gap.HasTarget)
            {
                Gizmos.color = Color.yellow;
                Gizmos.DrawLine(gap.From, gap.To);
                Gizmos.DrawSphere(gap.To, 0.02f);
            }
        }
#endif
    }
}
