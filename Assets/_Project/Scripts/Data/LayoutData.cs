using System.Collections.Generic;
using Bangkku.Furniture;
using Newtonsoft.Json;
using UnityEngine.Scripting;

namespace Bangkku.IO
{
    /// <summary>
    /// 배치 저장 layout.json (전체개발계획 7.3). 팀장 관리.
    /// LayoutSaver 가 저장·불러오기에 쓴다.
    /// </summary>
    [Preserve]
    public class LayoutData
    {
        /// <summary>평면도 이름 (floorplan.json 의 name, 예: oneroom_01)</summary>
        [JsonProperty("floorplan")] public string Floorplan;

        [JsonProperty("placements")] public List<PlacementData> Placements = new();
    }

    [Preserve]
    public class PlacementData
    {
        /// <summary>catalog.json 의 가구 id</summary>
        [JsonProperty("itemId")] public string ItemId;

        /// <summary>가구 바닥면 중앙 [x, y] (평면도 좌표, mm)</summary>
        [JsonProperty("position")] public int[] Position;

        /// <summary>Y축 회전 (도)</summary>
        [JsonProperty("rotationY")] public float RotationY;

        /// <summary>
        /// 박스 가구(source = box)만: 다시 만들 때 쓸 이름·치수·신뢰도·색. 카탈로그 가구는 비워 둔다 (JSON 에 칸이 생기지 않음).
        /// 가구 메뉴로 추가한 새 박스 가구는 catalog.json 에 없어서 id 만으로는 다시 만들 수 없다.
        /// </summary>
        [JsonProperty("item", NullValueHandling = NullValueHandling.Ignore)] public CatalogItem Item;
    }
}
