using System;
using System.Collections.Generic;
using Bangkku.IO;
using Bangkku.Placement;
using UnityEngine;

namespace Bangkku.FloorPlan
{
    /// <summary>
    /// 실행 중에 평면도 바꾸기 (손목 메뉴·데스크톱 가구 메뉴의 "평면도" 버튼).
    /// 바꾸는 순서:
    /// 1. 선택을 풀고, 지금 배치를 지금 평면도 이름의 파일에 자동 저장 (LayoutSaver, 메뉴의 "배치 저장"과 같은 파일)
    /// 2. 가구를 지우고 FloorPlanBuilder 의 JSON 을 바꿔 방을 다시 만든다
    /// 3. 새 평면도로 저장한 배치가 있으면 자동으로 불러오고, 없으면 그 평면도의 기본 가구(TestFurnitureSpawner). 기본 가구가 없으면 빈 방
    /// 4. VR 시작 위치(XRRigSetup)·데스크톱 카메라(DesktopController)는 FloorPlanBuilder.Built 를 받아 새 평면도에 맞춘다
    /// Play 시작도 3과 같은 규칙이다 (TestFurnitureSpawner.Start 가 LoadSavedLayout 을 먼저 부른다).
    /// 그래서 보이는 배치가 늘 그 평면도의 저장 파일과 같아, 1의 자동 저장이 저장해 둔 배치를 기본 가구로 덮어쓰지 않는다.
    /// FloorPlanBuilder 와 같은 오브젝트에 붙이고 Plans 에 평면도 JSON 을 넣는다. 지금 씬 JSON 이 목록에 없어도 된다.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(FloorPlanBuilder))]
    public class FloorPlanSwitcher : MonoBehaviour
    {
        [Serializable]
        public class Entry
        {
            [Tooltip("메뉴 버튼 글자 (예: 원룸 16형). 비워 두면 JSON 파일 이름")]
            public string Label;

            [Tooltip("Assets/_Project/Data/FloorPlans/*.json (.dimensions.json 아님)")]
            public TextAsset Json;
        }

        [Tooltip("메뉴에 보일 평면도 목록 (2개 이상이어야 메뉴에 보인다)")]
        public List<Entry> Plans = new();

        [Tooltip("평면도를 바꾸기 전에 지금 배치를 자동 저장한다 (그 평면도의 저장 파일을 덮어씀)")]
        public bool AutoSave = true;

        [Tooltip("Play 시작·평면도를 바꿀 때 그 평면도로 저장한 배치가 있으면 기본 가구 대신 불러온다")]
        public bool AutoLoad = true;

        [Tooltip("저장한 배치가 없으면 그 평면도의 기본 가구를 놓는다 (씬의 TestFurnitureSpawner). 끄면 빈 방")]
        public bool SpawnDefaults = true;

        [Tooltip("배치를 불러올 때 박스 가구 프리팹 (FurnitureItem_Base). 비워 두면 씬의 TestFurnitureSpawner 프리팹")]
        public FurnitureItem BoxPrefab;

        /// <summary>씬의 평면도 바꾸기. 없으면 null (메뉴가 평면도 줄을 숨긴다)</summary>
        public static FloorPlanSwitcher Instance { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Instance = null;

        /// <summary>평면도를 바꾼 뒤 (메뉴가 버튼 색을 바꾼다)</summary>
        public event Action<FloorPlanSwitcher> Switched;

        FloorPlanBuilder _builder;

        /// <summary>메뉴에 보일 만한지 (평면도 2개 이상)</summary>
        public bool HasChoices => CountValid() >= 2;

        void Awake() => _builder = GetComponent<FloorPlanBuilder>();

        void OnEnable() => Instance = this;

        void OnDisable()
        {
            if (Instance == this) Instance = null;
        }

        int CountValid()
        {
            int count = 0;
            foreach (var entry in Plans)
                if (entry != null && entry.Json != null) count++;
            return count;
        }

        /// <summary>목록 i 번째의 버튼 글자</summary>
        public string LabelOf(int index)
        {
            var entry = Plans[index];
            if (entry == null || entry.Json == null) return "(비어 있음)";
            return string.IsNullOrEmpty(entry.Label) ? entry.Json.name : entry.Label;
        }

        /// <summary>지금 평면도의 목록 번호. 목록에 없으면 -1</summary>
        public int CurrentIndex
        {
            get
            {
                if (_builder == null) return -1;
                for (int i = 0; i < Plans.Count; i++)
                    if (Plans[i] != null && Plans[i].Json != null && Plans[i].Json == _builder.FloorPlanJson) return i;
                return -1;
            }
        }

        /// <summary>
        /// 목록 index 번째 평면도로 바꾼다. 결과 문장을 message 로.
        /// 새 JSON 을 먼저 읽어 보고, 읽지 못하면 지금 방·가구를 그대로 둔다
        /// </summary>
        public bool SwitchTo(int index, out string message)
        {
            if (index < 0 || index >= Plans.Count || Plans[index] == null || Plans[index].Json == null)
            {
                message = "목록에 없는 평면도입니다.";
                return false;
            }

            var entry = Plans[index];
            string label = LabelOf(index);
            if (_builder.FloorPlanJson == entry.Json && _builder.Data != null)
            {
                message = $"이미 {label} 입니다.";
                return false;
            }

            // 읽지 못하는 JSON 이면 저장·삭제 전에 멈춘다 (이유는 FloorPlanLoader 가 Console 에)
            if (FloorPlanLoader.Load(entry.Json) == null)
            {
                message = $"{label} 평면도를 읽지 못했습니다. Console 을 확인하세요.";
                return false;
            }

            if (FurnitureItem.All.Count > 0) SelectionManager.Instance.Deselect();

            // 1. 지금 배치 자동 저장. 실패하면 배치를 잃지 않도록 바꾸지 않는다
            string oldName = _builder.Data != null ? _builder.Data.Name : null;
            string saved = null;
            if (AutoSave && oldName != null)
            {
                if (!LayoutSaver.Save(out string saveMessage))
                {
                    message = $"지금 배치를 저장하지 못해 평면도를 바꾸지 않았습니다. {saveMessage}";
                    return false;
                }

                saved = $"{oldName} 배치 저장";
            }

            // 2. 가구를 지우고 방을 다시 만든다
            LayoutSaver.ClearFurniture();
            var previousJson = _builder.FloorPlanJson;
            _builder.FloorPlanJson = entry.Json;
            if (!_builder.Build())
            {
                // 미리 읽어 봤으므로 여기까지 오는 일은 드물다. 원래 평면도로 되돌린다
                _builder.FloorPlanJson = previousJson;
                _builder.Build();
                message = $"{label} 방을 만들지 못해 원래 평면도로 되돌렸습니다. Console 을 확인하세요.";
                return false;
            }

            // 3. 새 평면도의 저장 배치 불러오기. 없으면 기본 가구, 그것도 없으면 빈 방
            string loaded = "빈 방";
            if (LoadSavedLayout(out string loadMessage))
            {
                loaded = loadMessage;
            }
            else if (SpawnDefaults)
            {
                var spawner = FindFirstObjectByType<TestFurnitureSpawner>();
                int count = spawner != null ? spawner.Spawn() : 0;
                if (count > 0) loaded = $"기본 가구 {count}개";
            }

            message = saved != null ? $"{label}(으)로 이동. {saved}, {loaded}" : $"{label}(으)로 이동. {loaded}";
            Debug.Log($"[방꾸] 평면도 바꾸기: {message}", this);
            Switched?.Invoke(this);
            return true;
        }

        /// <summary>
        /// 지금 평면도로 저장한 배치가 있으면 불러온다 (AutoLoad 가 켜져 있을 때). 저장 파일이 있었으면 true:
        /// 불러온 가구 수와 상관없다 (빈 방을 저장했으면 빈 방). false 면 부른 쪽이 기본 가구를 놓는다
        /// </summary>
        public bool LoadSavedLayout(out string message)
        {
            message = null;
            if (!AutoLoad || !LayoutSaver.HasSave(out _)) return false;
            LayoutSaver.Load(BoxFurniture.ResolvePrefab(BoxPrefab), transform, out message);
            return true;
        }
    }
}
