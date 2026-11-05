using System;
using UnityEngine;

namespace Bangkku.Placement
{
    /// <summary>
    /// "선택한 가구" 하나를 VR 과 데스크톱이 함께 쓰게 하는 곳 (팀장 가이드 2주차 입력 공통화).
    /// 입력 방식과 상관없이 선택, 이동, 회전은 모두 여기를 거치므로 두 모드가 같은 규칙으로 동작한다.
    /// 지금의 규칙: 바닥 고정(SnapToFloor), 회전 단위(RotationSnap). 위치 스냅과 겹침 판정은 이 곳에 이어 붙인다.
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

        FurnitureItem _selected;

        /// <summary>선택한 적이 있는지. 가구가 지워지면 _selected 가 null 처럼 보여서, 지워진 것을 알아채려고 따로 둔다</summary>
        bool _hasSelection;

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
        /// 선택한 가구를 worldPosition 으로 옮긴다. 높이는 바닥에 고정된다.
        /// 선택한 가구가 없으면 아무것도 하지 않고 false.
        /// </summary>
        public bool MoveSelectedTo(Vector3 worldPosition)
        {
            if (_selected == null) return false;
            var t = _selected.transform;
            t.position = SnapToFloor.FloorPosition(worldPosition);
            SnapToFloor.Apply(t);
            SelectedPlacementChanged?.Invoke(_selected);
            return true;
        }

        /// <summary>선택한 가구를 Y축으로 delta 도 돌린다 (RotationSnap 규칙). 선택한 가구가 없으면 false</summary>
        public bool RotateSelected(float deltaDegrees)
        {
            if (_selected == null) return false;
            RotationSnap.Rotate(_selected.transform, deltaDegrees);
            SelectedPlacementChanged?.Invoke(_selected);
            return true;
        }
    }
}
