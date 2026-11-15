using System.Collections.Generic;
using Bangkku.Core;
using Bangkku.Furniture;
using Bangkku.Placement;
using UnityEngine;

namespace Bangkku.UI
{
    /// <summary>
    /// 가구 메뉴(데스크톱 FurnitureMenu, VR 손목 VRFurnitureMenu)에서 카탈로그 가구를 만드는 공통 부분.
    /// 가구 생성은 팀원의 FurnitureFactory.Create(itemId, 평면도 mm, 회전) 를 그대로 부른다.
    /// 씬에 FurnitureFactory 가 없으면 목록이 비고, 메뉴는 카탈로그 칸을 숨긴다 (박스 가구 추가·저장·불러오기는 그대로).
    /// </summary>
    public static class CatalogMenu
    {
        static FurnitureFactory _factory;
        static float _nextSearch;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            _factory = null;
            _nextSearch = 0f;
        }

        /// <summary>씬의 FurnitureFactory. 없으면 1초에 한 번만 다시 찾는다 (OnGUI 는 한 프레임에 여러 번 불린다)</summary>
        public static FurnitureFactory Factory
        {
            get
            {
                if (_factory != null) return _factory;
                if (Time.unscaledTime < _nextSearch) return null;
                _nextSearch = Time.unscaledTime + 1f;
                _factory = Object.FindAnyObjectByType<FurnitureFactory>();
                return _factory;
            }
        }

        /// <summary>메뉴에 띄울 카탈로그 가구 (catalog.json 순서). 팩토리가 없거나 카탈로그를 못 읽으면 빈 목록</summary>
        public static List<CatalogItem> Items()
        {
            var list = new List<CatalogItem>();
            var factory = Factory;
            if (factory == null) return list;
            foreach (var item in factory.Items.Values)
                if (item != null) list.Add(item);
            return list;
        }

        /// <summary>"1100×670×730"</summary>
        public static string SizeText(CatalogItem item) => $"{item.Size.W}×{item.Size.D}×{item.Size.H}";

        /// <summary>
        /// 카탈로그 가구를 position(월드, 가구 바닥면 중앙)에 만들고 선택한 뒤 다시 놓아 격자·벽 스냅을 적용한다 (새 박스 가구와 같은 순서).
        /// 만든 가구를 돌려주고, 못 만들면 null. message 는 메뉴에 보여 줄 한 줄
        /// </summary>
        public static FurnitureItem Spawn(CatalogItem item, Vector3 position, Transform parent, out string message)
        {
            var factory = Factory;
            if (factory == null)
            {
                message = "씬에 FurnitureFactory 가 없습니다.";
                return null;
            }

            if (item == null)
            {
                message = "카탈로그 가구를 찾을 수 없습니다.";
                return null;
            }

            Vector2 positionMm = Units.WorldToPlan(position);
            var furniture = factory.Create(item.Id, positionMm, 0f);
            if (furniture == null)
            {
                message = $"{item.Name} ({item.Id}) 를 만들지 못했습니다. Console 을 확인하세요.";
                return null;
            }

            // 박스 가구처럼 메뉴 아래에 둔다 (배치 불러오기는 FurnitureItem.All 로 지우므로 부모와 상관없다)
            if (parent != null) furniture.transform.SetParent(parent, true);

            var selection = SelectionManager.Instance;
            selection.Select(furniture);
            selection.MoveSelectedTo(position);

            message = $"{BoxFurniture.Describe(furniture.Data)} 추가";
            Debug.Log($"[방꾸] 카탈로그 가구: {BoxFurniture.Describe(furniture.Data)}", furniture);
            return furniture;
        }
    }
}
