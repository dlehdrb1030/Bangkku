using System;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace Bangkku.Placement
{
    /// <summary>
    /// 뷰어 모드: 배치한 가구를 인테리어 관점으로 둘러보는 모드. 다시 끄면 편집 모드로 돌아온다.
    /// - 켜면: 선택을 풀고, 판정 윤곽선(초록·노랑·빨강)·겹침 상자·거리 라벨·신뢰도 라벨·상태 패널을 숨긴다
    /// - 가구 선택·이동·회전·추가·삭제를 막는다. 시점 이동(데스크톱 WASD·VR 텔레포트·회전)은 그대로
    /// - VR: 가구의 XRGrabInteractable 을 꺼서 레이·손으로 잡히지 않게 한다 (켜는 동안 새로 생긴 가구도 FurnitureItem 이 맞춘다)
    /// - 판정(PlacementValidator)은 계속 돈다. 편집 모드로 돌아오면 바로 지금 상태 색이 보인다
    /// 바꾸는 곳: 데스크톱 V 키·가구 메뉴 버튼, VR 손목 메뉴 버튼. 화면 요소들은 Changed 를 받아 다시 그린다.
    /// </summary>
    public static class ViewerMode
    {
        /// <summary>뷰어 모드인지 (false = 편집 모드)</summary>
        public static bool Active { get; private set; }

        /// <summary>모드가 바뀐 뒤. 인자는 새 Active</summary>
        public static event Action<bool> Changed;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Active = false;
            Changed = null;
        }

        public static void Toggle() => Set(!Active);

        public static void Set(bool active)
        {
            if (active == Active) return;
            Active = active;

            // 선택이 남아 있으면 거리 라벨·상태 패널이 계속 그 가구를 가리킨다
            if (active && FurnitureItem.All.Count > 0) SelectionManager.Instance.Deselect();

            foreach (var item in FurnitureItem.All)
                if (item != null) ApplyTo(item);

            Debug.Log($"[방꾸] {(active ? "뷰어 모드: 판정 표시를 숨기고 가구 편집을 잠급니다" : "편집 모드: 판정 표시와 가구 편집을 다시 켭니다")}");
            Changed?.Invoke(active);
        }

        /// <summary>가구 하나의 잡기를 지금 모드에 맞춘다 (뷰어 모드면 끔). FurnitureItem 이 켜질 때도 부른다</summary>
        public static void ApplyTo(FurnitureItem item)
        {
            foreach (var interactable in item.GetComponents<XRBaseInteractable>())
                if (interactable.enabled == Active) interactable.enabled = !Active;
        }
    }
}
