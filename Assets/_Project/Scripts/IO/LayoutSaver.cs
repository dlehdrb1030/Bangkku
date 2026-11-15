using System;
using System.Collections.Generic;
using System.IO;
using Bangkku.Core;
using Bangkku.FloorPlan;
using Bangkku.Furniture;
using Bangkku.Placement;
using Newtonsoft.Json;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Bangkku.IO
{
    /// <summary>
    /// 배치 저장·불러오기 (팀장 가이드 4주차, 전체개발계획 7.3 layout.json).
    /// - 파일: Application.persistentDataPath/layouts/&lt;평면도 이름&gt;.json (평면도마다 하나)
    /// - 저장: 지금 켜져 있는 가구 전부의 id, 바닥면 중앙 위치(평면도 mm), Y축 회전(도).
    ///   박스 가구(source = box)는 다시 만들 수 있도록 이름·치수·신뢰도·색(item)도 함께 쓴다
    /// - 불러오기: 지금 가구를 모두 지우고 저장된 대로 다시 만든다 (스냅 없이 저장된 위치·회전 그대로).
    ///   박스 가구는 item 으로 BoxFurniture.Create, 카탈로그 가구(scan·mesh)는 CatalogSpawner 로 만든다
    /// - 저장한 평면도와 씬의 평면도가 다르면 불러오지 않는다
    /// 가구 메뉴(데스크톱 FurnitureMenu, VR 손목 VRFurnitureMenu)의 저장·불러오기 버튼이 부른다.
    /// </summary>
    public static class LayoutSaver
    {
        public const string FolderName = "layouts";

        /// <summary>
        /// 카탈로그 가구를 만드는 함수. 팀원의 FurnitureFactory 가 등록한다:
        /// catalog id → 프리팹을 만들고 Init 까지 한 가구 (위치·회전은 LayoutSaver 가 맞춘다). 모르는 id 면 null.
        /// 등록 전에는 카탈로그 가구를 건너뛰고 경고를 남긴다.
        /// </summary>
        public static Func<string, FurnitureItem> CatalogSpawner;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => CatalogSpawner = null;

        static readonly JsonSerializerSettings Settings = new()
        {
            Formatting = Formatting.Indented,
            NullValueHandling = NullValueHandling.Ignore,
        };

        /// <summary>저장 폴더 (persistentDataPath/layouts)</summary>
        public static string Folder => Path.Combine(Application.persistentDataPath, FolderName);

        /// <summary>평면도 이름에 맞는 저장 파일 경로</summary>
        public static string PathFor(string folder, string floorplan) => Path.Combine(folder, $"{floorplan}.json");

        /// <summary>씬 평면도의 저장 파일이 있는지</summary>
        public static bool HasSave(out string path)
        {
            path = null;
            var plan = CurrentPlanName();
            if (plan == null) return false;
            path = PathFor(Folder, plan);
            return File.Exists(path);
        }

        // ───────────── 저장 ─────────────

        /// <summary>지금 가구 배치를 씬 평면도 이름의 파일에 쓴다. 결과 문장을 message 로</summary>
        public static bool Save(out string message)
        {
            var plan = CurrentPlanName();
            if (plan == null)
            {
                message = "씬에 평면도(FloorPlanBuilder)가 없어 저장할 수 없습니다.";
                return false;
            }

            var layout = Capture(plan, FurnitureItem.All);
            string path = PathFor(Folder, plan);
            try
            {
                Directory.CreateDirectory(Folder);
                File.WriteAllText(path, ToJson(layout));
            }
            catch (Exception e)
            {
                message = $"저장 실패: {e.Message}";
                Debug.LogError($"[방꾸] 배치 {message} ({path})");
                return false;
            }

            message = $"가구 {layout.Placements.Count}개 배치를 저장했습니다.";
            Debug.Log($"[방꾸] {message} {path}");
            return true;
        }

        /// <summary>켜져 있는 가구들의 배치</summary>
        public static LayoutData Capture(string floorplan, IEnumerable<FurnitureItem> items)
        {
            var layout = new LayoutData { Floorplan = floorplan };
            foreach (var item in items)
            {
                if (item == null) continue;
                var t = item.transform;
                layout.Placements.Add(MakePlacement(item.ItemId, t.position, t.eulerAngles.y, item.Data));
            }

            return layout;
        }

        /// <summary>
        /// 가구 하나의 저장 항목. 위치는 평면도 mm (정수), 회전은 0~360 도. 박스 가구면 item 을 함께 담는다
        /// </summary>
        public static PlacementData MakePlacement(string itemId, Vector3 worldPosition, float rotationY, CatalogItem data)
        {
            var plan = Units.WorldToPlan(worldPosition);
            return new PlacementData
            {
                ItemId = itemId,
                Position = new[] { plan.x, plan.y },
                RotationY = NormalizeRotation(rotationY),
                Item = data != null && data.Source == FurnitureSource.Box ? BoxItem(data, itemId) : null,
            };
        }

        /// <summary>0 이상 360 미만, 소수 둘째 자리까지 (89.99998 같은 부동소수 찌꺼기를 없앤다)</summary>
        public static float NormalizeRotation(float degrees)
        {
            float r = Mathf.Round(degrees * 100f) / 100f % 360f;
            if (r < 0f) r += 360f;
            // 360 은 0 으로, -0 도 0 으로 (JSON 에 -0.0 이 찍히지 않게)
            return r >= 360f || r == 0f ? 0f : r;
        }

        /// <summary>박스 가구를 다시 만들 때 필요한 칸만 담은 복사본 (스캔 전용 칸은 뺀다)</summary>
        public static CatalogItem BoxItem(CatalogItem data, string itemId) => new()
        {
            Id = itemId,
            Name = data.Name,
            Source = FurnitureSource.Box,
            Size = new SizeMm { W = data.Size.W, D = data.Size.D, H = data.Size.H },
            SizeConfidence = data.SizeConfidence,
            Color = data.Color,
            UnscannedFaces = null,
        };

        public static string ToJson(LayoutData layout) => JsonConvert.SerializeObject(layout, Settings);

        // ───────────── 불러오기 ─────────────

        /// <summary>
        /// 씬 평면도 이름의 파일을 읽어 가구를 지우고 다시 만든다. prefab 은 박스 가구용 FurnitureItem_Base.
        /// 하나라도 만들었으면 true. 결과 문장을 message 로
        /// </summary>
        public static bool Load(FurnitureItem prefab, Transform parent, out string message)
        {
            var plan = CurrentPlanName();
            if (plan == null)
            {
                message = "씬에 평면도(FloorPlanBuilder)가 없어 불러올 수 없습니다.";
                return false;
            }

            string path = PathFor(Folder, plan);
            if (!File.Exists(path))
            {
                message = $"{plan} 평면도로 저장한 배치가 없습니다. 먼저 저장하세요.";
                return false;
            }

            string json;
            try
            {
                json = File.ReadAllText(path);
            }
            catch (Exception e)
            {
                message = $"불러오기 실패: {e.Message}";
                Debug.LogError($"[방꾸] 배치 {message} ({path})");
                return false;
            }

            if (!TryParse(json, out var layout, out string error) || !CheckFloorplan(layout.Floorplan, plan, out error))
            {
                message = $"불러오기 실패: {error}";
                Debug.LogError($"[방꾸] 배치 {message} ({path})");
                return false;
            }

            ClearFurniture();
            int created = 0;
            var skipped = new List<string>();
            foreach (var placement in layout.Placements)
            {
                var item = Spawn(placement, prefab, parent, out string reason);
                if (item != null) created++;
                else skipped.Add($"{placement.ItemId} ({reason})");
            }

            message = skipped.Count == 0
                ? $"가구 {created}개 배치를 불러왔습니다."
                : $"가구 {created}개를 불러오고 {skipped.Count}개는 건너뛰었습니다.";
            Debug.Log($"[방꾸] {message} {path}");
            if (skipped.Count > 0)
                Debug.LogWarning($"[방꾸] 불러오지 못한 가구: {string.Join(", ", skipped)}");
            return created > 0;
        }

        /// <summary>JSON 을 읽는다. 형식이 틀리면 false 와 이유. 항목별 검사(id, 위치 [x, y])는 Load 가 하나씩 하고 틀린 항목만 건너뛴다</summary>
        public static bool TryParse(string json, out LayoutData layout, out string error)
        {
            layout = null;
            error = null;
            try
            {
                layout = JsonConvert.DeserializeObject<LayoutData>(json);
            }
            catch (JsonException e)
            {
                error = $"JSON 형식 오류: {e.Message}";
                return false;
            }

            if (layout == null)
            {
                error = "빈 파일입니다.";
                return false;
            }

            layout.Placements ??= new List<PlacementData>();
            layout.Placements.RemoveAll(p => p == null);
            return true;
        }

        /// <summary>저장한 평면도와 지금 평면도가 같아야 한다 (위치가 평면도 좌표라서)</summary>
        public static bool CheckFloorplan(string saved, string current, out string error)
        {
            error = null;
            if (string.Equals(saved, current, StringComparison.Ordinal)) return true;
            error = $"저장한 평면도({saved ?? "없음"})와 지금 평면도({current})가 다릅니다.";
            return false;
        }

        /// <summary>저장 항목 하나로 가구를 만든다. 못 만들면 null 과 이유</summary>
        static FurnitureItem Spawn(PlacementData placement, FurnitureItem prefab, Transform parent, out string reason)
        {
            reason = null;
            if (string.IsNullOrEmpty(placement.ItemId))
            {
                reason = "id 없음";
                return null;
            }

            if (placement.Position == null || placement.Position.Length != 2)
            {
                reason = "위치는 [x, y] mm 여야 함";
                return null;
            }

            var position = Units.PlanToWorld(placement.Position);
            float rotation = placement.RotationY;

            if (placement.Item != null)
            {
                if (prefab == null)
                {
                    reason = "가구 메뉴 Prefab 칸이 비어 있음";
                    return null;
                }

                placement.Item.Id = placement.ItemId;
                placement.Item.Source = FurnitureSource.Box;
                return BoxFurniture.Create(prefab, placement.Item, position, rotation, parent);
            }

            if (CatalogSpawner == null)
            {
                reason = "카탈로그 가구: FurnitureFactory 연결 전";
                return null;
            }

            var furniture = CatalogSpawner(placement.ItemId);
            if (furniture == null)
            {
                reason = "카탈로그에 없는 id";
                return null;
            }

            if (parent != null && furniture.transform.parent == null) furniture.transform.SetParent(parent, true);
            furniture.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, rotation, 0f));
            return furniture;
        }

        /// <summary>
        /// 지금 가구를 모두 지운다. 바로 끄면 OnDisable 에서 FurnitureItem.All 에서 빠지므로
        /// 같은 프레임에 만드는 새 가구와 겹침 판정이 섞이지 않는다
        /// </summary>
        static void ClearFurniture()
        {
            if (FurnitureItem.All.Count == 0) return;
            SelectionManager.Instance.Deselect();
            var items = new List<FurnitureItem>(FurnitureItem.All);
            foreach (var item in items)
            {
                if (item == null) continue;
                item.gameObject.SetActive(false);
                Object.Destroy(item.gameObject);
            }
        }

        static FloorPlanBuilder _builder;

        /// <summary>씬 평면도의 name. 평면도가 없거나 아직 읽지 않았으면 null</summary>
        static string CurrentPlanName()
        {
            if (_builder == null) _builder = Object.FindFirstObjectByType<FloorPlanBuilder>();
            return _builder != null && _builder.Data != null ? _builder.Data.Name : null;
        }
    }
}
