using System;
using System.Collections.Generic;
using Bangkku.FloorPlan;
using UnityEngine;

namespace Bangkku.Placement
{
    /// <summary>
    /// "선택한 가구" 하나를 VR 과 데스크톱이 함께 쓰게 하는 곳 (팀장 가이드 2주차 입력 공통화).
    /// 입력 방식과 상관없이 선택, 이동, 회전은 모두 여기를 거치므로 두 모드가 같은 규칙으로 동작한다.
    /// 규칙: 바닥 고정(SnapToFloor), 회전 단위(RotationSnap), 위치 스냅(PositionSnap: 10mm 격자, 벽 30mm).
    /// 겹침 판정은 SelectedPlacementChanged 에 이어 붙인다.
    /// 씬에 따로 두지 않아도 Instance 를 처음 쓸 때 스스로 만들어진다.
    /// </summary>
    public class SelectionManager : MonoBehaviour
    {
        static SelectionManager _instance;

        /// <summary>씬의 SelectionManager. 없으면 만든다</summary>
        public static SelectionManager Instance
        {
            get
            {
                if (_instance != null) return _instance;
                _instance = FindFirstObjectByType<SelectionManager>();
                if (_instance == null) _instance = new GameObject(nameof(SelectionManager)).AddComponent<SelectionManager>();
                return _instance;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => _instance = null;

        [Header("위치 스냅")]
        [Tooltip("가구 위치를 격자에 맞춘다")]
        public bool GridSnap = true;

        [Tooltip("격자 크기 (mm)")]
        public int GridMm = PositionSnap.GridMm;

        [Tooltip("가구 가장자리가 벽에서 가까우면 벽에 붙인다")]
        public bool WallSnap = true;

        [Tooltip("이 거리(mm) 이내면 벽에 붙인다")]
        public int WallSnapMm = PositionSnap.WallThresholdMm;

        FurnitureItem _selected;

        /// <summary>선택한 적이 있는지. 가구가 지워지면 _selected 가 null 처럼 보여서, 지워진 것을 알아채려고 따로 둔다</summary>
        bool _hasSelection;

        FloorPlanBuilder _floorPlan;
        List<WallRect> _walls;
        bool _wallsDirty = true;

        /// <summary>지금 선택한 가구. 없거나 지워졌으면 null</summary>
        public FurnitureItem Selected => _selected;

        /// <summary>선택이 바뀔 때 (해제는 null)</summary>
        public event Action<FurnitureItem> SelectionChanged;

        /// <summary>선택한 가구의 위치나 회전을 이 클래스가 바꾼 뒤. 겹침 판정, 거리 라벨 갱신용</summary>
        public event Action<FurnitureItem> SelectedPlacementChanged;

        void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Debug.LogWarning("[방꾸] SelectionManager 가 씬에 두 개 있습니다. 하나를 지우세요.", this);
                return;
            }

            _instance = this;
        }

        void OnDestroy()
        {
            if (_instance == this) _instance = null;
            if (_floorPlan != null) _floorPlan.Built -= OnFloorPlanBuilt;
        }

        void Update()
        {
            // 선택한 가구가 지워졌으면 선택을 풀고 SelectionChanged 로 알린다
            if (_hasSelection && _selected == null) Select(null);
        }

        /// <summary>가구를 선택한다. null 이면 선택을 푼다.</summary>
        public void Select(FurnitureItem item)
        {
            bool same = item == _selected && _hasSelection == (item != null);
            if (same) return;
            _selected = item;
            _hasSelection = item != null;
            SelectionChanged?.Invoke(item);
        }

        public void Deselect() => Select(null);

        /// <summary>
        /// 선택한 가구를 worldPosition 으로 옮긴다. 높이는 바닥에 고정되고, snap 이 true 면 격자·벽 스냅을 적용한다.
        /// 선택한 가구가 없으면 아무것도 하지 않고 false.
        /// </summary>
        /// <param name="snap">false 면 위치 스냅 없이 자유롭게 (바닥 고정은 항상 적용)</param>
        public bool MoveSelectedTo(Vector3 worldPosition, bool snap = true)
        {
            if (_selected == null) return false;
            Place(worldPosition, snap);
            return true;
        }

        /// <summary>
        /// 선택한 가구를 Y축으로 delta 도 돌린다 (RotationSnap 규칙). 중심은 그대로 두고, 돌린 뒤 벽이 30mm 이내면 벽에 붙인다.
        /// 격자 스냅은 하지 않는다 (벽에 붙은 가구를 돌릴 때 위치가 5mm 씩 밀리지 않도록). 선택한 가구가 없으면 false
        /// </summary>
        public bool RotateSelected(float deltaDegrees, bool snap = true)
        {
            if (_selected == null) return false;
            RotationSnap.Rotate(_selected.transform, deltaDegrees);
            Place(_selected.transform.position, snap, applyGrid: false);
            return true;
        }

        void Place(Vector3 worldPosition, bool snap, bool applyGrid = true)
        {
            var t = _selected.transform;
            var position = SnapToFloor.FloorPosition(worldPosition);
            if (snap)
            {
                var xz = PositionSnap.Apply(
                    new Vector2(position.x, position.z),
                    new Vector2(_selected.SizeMeters.x * 0.5f, _selected.SizeMeters.z * 0.5f),
                    t.eulerAngles.y,
                    GetWalls(),
                    GridSnap && applyGrid ? GridMm : 0,
                    WallSnap ? WallSnapMm : 0);
                position = new Vector3(xz.x, position.y, xz.y);
            }

            t.position = position;
            SnapToFloor.Apply(t);
            SelectedPlacementChanged?.Invoke(_selected);
        }

        /// <summary>씬의 평면도에서 가구가 붙을 수 있는 벽. 평면도를 다시 만들면 다시 계산한다. 평면도가 없으면 null (격자만 적용)</summary>
        IReadOnlyList<WallRect> GetWalls()
        {
            if (_floorPlan == null)
            {
                _floorPlan = FindFirstObjectByType<FloorPlanBuilder>();
                if (_floorPlan != null) _floorPlan.Built += OnFloorPlanBuilt;
            }

            if (_floorPlan == null || _floorPlan.Data == null) return null;
            if (_wallsDirty || _walls == null)
            {
                _walls = PositionSnap.WallsFromPieces(WallMeshBuilder.ComputePieces(_floorPlan.Data));
                _wallsDirty = false;
            }

            return _walls;
        }

        void OnFloorPlanBuilt(FloorPlanBuilder builder) => _wallsDirty = true;
    }
}
