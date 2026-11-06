using System.Collections.Generic;
using Bangkku.Core;
using Bangkku.FloorPlan;
using Bangkku.Furniture;
using UnityEngine;

namespace Bangkku.Placement
{
    /// <summary>
    /// 치수만 있는 박스 가구 (source = box) 를 FurnitureItem_Base 프리팹으로 만든다.
    /// 테스트 가구 생성기와 가구 메뉴(새 박스 가구)가 같이 쓴다. 3DGS·메쉬 가구는 팀원의 FurnitureFactory 가 만든다.
    /// </summary>
    public static class BoxFurniture
    {
        /// <summary>입력할 수 있는 치수 범위 (mm)</summary>
        public const int MinSizeMm = 10, MaxSizeMm = 6000;

        /// <summary>새 박스 가구 색 (돌아가며 쓴다)</summary>
        static readonly string[] Palette = { "#7B9CC4", "#C49B7B", "#9BC47B", "#B07BC4", "#C4B27B", "#7BC4BC", "#C47B8E" };

        /// <summary>
        /// 박스 가구 하나를 만든다. Init 이 판정용 콜라이더 크기를 맞추고, Visual 에는 같은 크기의 색 상자를 넣는다.
        /// position 은 가구 바닥면 중앙 (월드, m).
        /// </summary>
        public static FurnitureItem Create(FurnitureItem prefab, CatalogItem item, Vector3 position, float rotationY, Transform parent)
        {
            var furniture = Object.Instantiate(prefab, position, Quaternion.Euler(0f, rotationY, 0f), parent);
            furniture.Init(item);

            var visualParent = furniture.transform.Find("Visual");
            if (visualParent == null)
            {
                visualParent = new GameObject("Visual").transform;
                visualParent.SetParent(furniture.transform, false);
            }

            var size = furniture.SizeMeters;
            var visual = GameObject.CreatePrimitive(PrimitiveType.Cube);
            visual.name = "Box";
            visual.layer = Layers.Furniture;
            // 판정은 Collider 자식의 BoxCollider 하나로만 한다. 같은 프레임 판정에 걸리지 않도록 바로 지운다
            Object.DestroyImmediate(visual.GetComponent<Collider>());

            var t = visual.transform;
            t.SetParent(visualParent, false);
            t.localPosition = new Vector3(0f, size.y * 0.5f, 0f);
            t.localScale = size;

            if (item.TryGetColor(out var color)) visual.GetComponent<MeshRenderer>().material.color = color;
            return furniture;
        }

        /// <summary>
        /// 새 박스 가구를 만들어 선택하고 격자·벽 스냅을 적용한다. 데스크톱 메뉴와 VR 손목 메뉴가 같이 쓴다.
        /// id 는 box_01 부터 자동, 색은 id 번호대로. 이름이 비면 id. 만든 가구를 돌려준다.
        /// </summary>
        public static FurnitureItem AddNew(FurnitureItem prefab, string name, SizeMm size, Confidence confidence,
            Vector3 position, Transform parent)
        {
            string id = NextIdInScene();
            var item = new CatalogItem
            {
                Id = id,
                Name = string.IsNullOrWhiteSpace(name) ? id : name.Trim(),
                Source = FurnitureSource.Box,
                Size = size,
                SizeConfidence = confidence,
                Color = ColorFor(IdNumber(id) - 1),
            };

            var furniture = Create(prefab, item, position, 0f, parent);

            // 선택한 뒤 다시 놓아서 격자·벽 스냅을 적용한다
            var selection = SelectionManager.Instance;
            selection.Select(furniture);
            selection.MoveSelectedTo(position);

            Debug.Log($"[방꾸] 새 박스 가구: {Describe(item)}", furniture);
            return furniture;
        }

        /// <summary>"침대 (box_01) 1100×2000×450mm drawing"</summary>
        public static string Describe(CatalogItem item) =>
            $"{item.Name} ({item.Id}) {item.Size.W}×{item.Size.D}×{item.Size.H}mm {item.SizeConfidence.ToJsonValue()}";

        /// <summary>선택을 풀고 가구를 지운다</summary>
        public static void Delete(FurnitureItem item)
        {
            if (item == null) return;
            if (SelectionManager.Instance.Selected == item) SelectionManager.Instance.Deselect();
            Debug.Log($"[방꾸] 가구 삭제: {item.ItemId}", item);
            Object.Destroy(item.gameObject);
        }

        /// <summary>칸에 넣은 프리팹, 없으면 씬의 TestFurnitureSpawner 프리팹</summary>
        public static FurnitureItem ResolvePrefab(FurnitureItem assigned)
        {
            if (assigned != null) return assigned;
            var spawner = Object.FindFirstObjectByType<TestFurnitureSpawner>();
            return spawner != null ? spawner.Prefab : null;
        }

        /// <summary>치수를 delta 만큼 바꾸고 MinSizeMm ~ MaxSizeMm 로 자른다 (VR 메뉴 ± 버튼)</summary>
        public static int StepSize(int currentMm, int deltaMm) => Mathf.Clamp(currentMm + deltaMm, MinSizeMm, MaxSizeMm);

        /// <summary>"box_07" → 7. 번호가 없으면 0</summary>
        public static int IdNumber(string id)
        {
            int underscore = id?.LastIndexOf('_') ?? -1;
            return underscore >= 0 && int.TryParse(id.Substring(underscore + 1), out int n) ? n : 0;
        }

        /// <summary>"box_01" 처럼 아직 쓰지 않은 가장 작은 번호의 id (에셋 ID 규칙: 소문자_번호)</summary>
        public static string NextId(IEnumerable<string> usedIds, string prefix = "box")
        {
            var used = new HashSet<string>(usedIds ?? new string[0]);
            for (int n = 1; ; n++)
            {
                string id = $"{prefix}_{n:00}";
                if (!used.Contains(id)) return id;
            }
        }

        /// <summary>지금 켜져 있는 가구의 id 를 피해서 다음 id</summary>
        public static string NextIdInScene(string prefix = "box")
        {
            var ids = new List<string>();
            foreach (var item in FurnitureItem.All)
                if (item != null) ids.Add(item.ItemId);
            return NextId(ids, prefix);
        }

        /// <summary>새 가구 색. 번호대로 돌아가며 고른다</summary>
        public static string ColorFor(int index) => Palette[((index % Palette.Length) + Palette.Length) % Palette.Length];

        /// <summary>
        /// 입력한 W·D·H 문자열(mm)을 읽는다. 정수가 아니거나 범위(MinSizeMm ~ MaxSizeMm) 밖이면 false 와 이유.
        /// </summary>
        public static bool TryParseSize(string w, string d, string h, out SizeMm size, out string error)
        {
            size = null;
            if (!TryParseOne(w, "가로(W)", out int wMm, out error)) return false;
            if (!TryParseOne(d, "깊이(D)", out int dMm, out error)) return false;
            if (!TryParseOne(h, "높이(H)", out int hMm, out error)) return false;
            size = new SizeMm { W = wMm, D = dMm, H = hMm };
            return true;
        }

        static bool TryParseOne(string text, string label, out int mm, out string error)
        {
            error = null;
            if (!int.TryParse(text?.Trim(), out mm))
            {
                error = $"{label} 를 mm 정수로 입력하세요.";
                return false;
            }

            if (mm < MinSizeMm || mm > MaxSizeMm)
            {
                error = $"{label} 는 {MinSizeMm}~{MaxSizeMm}mm 사이여야 합니다 (입력 {mm}).";
                return false;
            }

            return true;
        }

        /// <summary>
        /// 새 가구를 놓을 평면도 좌표 (mm). preferred 가 바닥 위면 그대로, 아니면 가장 넓은 바닥의 가운데.
        /// 평면도가 없으면 preferred 그대로.
        /// </summary>
        public static Vector2 SpawnPointMm(FloorPlanData plan, Vector2 preferredMm)
        {
            if (plan == null || FloorPlanSpots.IsOnFloor(plan, preferredMm)) return preferredMm;
            return LargestFloorCenterMm(plan) ?? preferredMm;
        }

        /// <summary>가장 넓은 바닥 다각형의 둘레 상자 가운데 (mm). 바닥이 없으면 null</summary>
        public static Vector2? LargestFloorCenterMm(FloorPlanData plan)
        {
            var floor = FloorPlanSpots.LargestFloor(plan);
            if (floor?.Polygon == null || floor.Polygon.Count == 0) return null;

            var min = new Vector2(float.MaxValue, float.MaxValue);
            var max = new Vector2(float.MinValue, float.MinValue);
            foreach (var p in floor.Polygon)
            {
                min = Vector2.Min(min, new Vector2(p[0], p[1]));
                max = Vector2.Max(max, new Vector2(p[0], p[1]));
            }

            return (min + max) * 0.5f;
        }
    }
}
