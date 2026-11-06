using Bangkku.Core;
using Bangkku.FloorPlan;
using UnityEngine;
using UnityEngine.Rendering;

namespace Bangkku.Placement
{
    /// <summary>
    /// 판정 상태 표시 (팀장 가이드 3주차 ④): 판정 박스(실측 치수)의 윤곽선 색을 여유 초록 / 좁음 노랑 / 겹침 빨강으로,
    /// 겹치면 같은 크기의 반투명 빨간 상자도 보인다. 3DGS·메쉬 외관(Visual)의 색은 바꾸지 않는다.
    /// - 윤곽선: Outline 자식 아래 LineRenderer 6개 (아랫면 고리, 윗면 고리, 세로 모서리 4개). VR·데스크톱 모두 보인다
    /// - FurnitureItem.PlacementChanged 를 받아 바꾸고, 크기(SizeMeters)가 바뀌면 다시 맞춘다 (Init 이 Awake 뒤에 불려도 됨)
    /// - 프리팹에 붙이지 않아도 FurnitureItem 이 Awake 에서 붙인다. 재질 칸이 비어 있으면 같은 색의 재질을 코드로 만든다.
    ///   빌드에서 셰이더가 빠지지 않도록 FurnitureItem_Base 프리팹에 붙이고 State 재질 4개를 넣어 두는 것을 권장한다.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(FurnitureItem))]
    public class PlacementStateView : MonoBehaviour
    {
        // 재질 생성 메뉴(Tools > 방꾸 > 재질 생성·연결)와 코드 대체 재질이 같은 색을 쓴다
        public static readonly Color FreeColor = new(0.20f, 0.80f, 0.35f);
        public static readonly Color TightColor = new(0.98f, 0.75f, 0.12f);
        public static readonly Color OverlapColor = new(0.92f, 0.22f, 0.22f);
        public static readonly Color OverlapFillColor = new(0.92f, 0.22f, 0.22f, 0.3f);

        const string OutlineChildName = "Outline";

        [Header("재질 (비어 있으면 코드로 만듦)")]
        [Tooltip("Materials/StateFree")] public Material FreeMaterial;
        [Tooltip("Materials/StateTight")] public Material TightMaterial;
        [Tooltip("Materials/StateOverlap")] public Material OverlapMaterial;
        [Tooltip("Materials/StateOverlapFill (반투명)")] public Material OverlapFillMaterial;

        [Header("모양")]
        [Tooltip("윤곽선 굵기 (m)")]
        public float LineWidthMeters = 0.02f;

        [Tooltip("윤곽선·상자를 판정 박스보다 이만큼(m) 키워서 그린다. 외관 상자·바닥에 묻히지 않도록")]
        public float InflateMeters = 0.005f;

        [Tooltip("끄면 여유(초록)일 때 윤곽선을 숨기고 좁음·겹침일 때만 보인다")]
        public bool ShowFreeOutline = true;

        static Material _fallbackFree, _fallbackTight, _fallbackOverlap, _fallbackFill;

        FurnitureItem _item;
        Transform _outline;
        readonly LineRenderer[] _lines = new LineRenderer[6];
        MeshRenderer _fill;
        Vector3 _builtSize = new(float.NaN, float.NaN, float.NaN);
        float _builtInflate = float.NaN, _builtWidth = float.NaN;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => _fallbackFree = _fallbackTight = _fallbackOverlap = _fallbackFill = null;

        void Awake()
        {
            _item = GetComponent<FurnitureItem>();
            Build();
        }

        void OnEnable()
        {
            _item.PlacementChanged += OnPlacementChanged;
            Refresh();
        }

        void OnDisable() => _item.PlacementChanged -= OnPlacementChanged;

        void LateUpdate()
        {
            // Init 으로 크기가 바뀌었거나 Inspector 에서 굵기·여유를 바꿨으면 다시 맞춘다
            if (_item.SizeMeters != _builtSize || InflateMeters != _builtInflate || LineWidthMeters != _builtWidth) Resize();
        }

        void OnPlacementChanged(FurnitureItem item) => Refresh();

        // Play 중에 Inspector 에서 Show Free Outline·재질 칸을 바꾸면 바로 반영 (상태가 바뀔 때까지 기다리지 않도록)
        void OnValidate()
        {
            if (Application.isPlaying) Refresh();
        }

        /// <summary>Outline 자식 아래에 윤곽선 6개와 겹침 상자를 만든다</summary>
        void Build()
        {
            _outline = transform.Find(OutlineChildName);
            if (_outline == null)
            {
                _outline = new GameObject(OutlineChildName) { layer = Layers.Furniture }.transform;
                _outline.SetParent(transform, false);
            }

            // 박스 좌표를 가구 원점(바닥면 중앙) 기준으로 쓰므로 Outline 은 루트와 같은 자리여야 한다
            _outline.localPosition = Vector3.zero;
            _outline.localRotation = Quaternion.identity;
            _outline.localScale = Vector3.one;

            string[] names = { "Bottom", "Top", "Edge0", "Edge1", "Edge2", "Edge3" };
            for (int i = 0; i < _lines.Length; i++)
            {
                var go = new GameObject(names[i]) { layer = Layers.Furniture };
                go.transform.SetParent(_outline, false);
                var line = go.AddComponent<LineRenderer>();
                line.useWorldSpace = false;
                line.loop = i < 2;
                line.numCornerVertices = 0;
                line.numCapVertices = 0;
                line.shadowCastingMode = ShadowCastingMode.Off;
                line.receiveShadows = false;
                line.lightProbeUsage = LightProbeUsage.Off;
                line.reflectionProbeUsage = ReflectionProbeUsage.Off;
                _lines[i] = line;
            }

            var fill = GameObject.CreatePrimitive(PrimitiveType.Cube);
            fill.name = "OverlapFill";
            fill.layer = Layers.Furniture;
            // 판정은 Collider 자식의 BoxCollider 하나로만 한다. 이 상자에 콜라이더가 있으면 안 된다
            DestroyImmediate(fill.GetComponent<Collider>());
            fill.transform.SetParent(_outline, false);
            _fill = fill.GetComponent<MeshRenderer>();
            _fill.shadowCastingMode = ShadowCastingMode.Off;
            _fill.receiveShadows = false;

            Resize();
        }

        /// <summary>SizeMeters 에 맞춰 윤곽선 점과 상자 크기를 다시 계산한다</summary>
        void Resize()
        {
            _builtSize = _item.SizeMeters;
            _builtInflate = InflateMeters;
            _builtWidth = LineWidthMeters;

            var c = BoxCorners(_item.SizeMeters, InflateMeters);
            SetPoints(_lines[0], c[0], c[1], c[2], c[3]);
            SetPoints(_lines[1], c[4], c[5], c[6], c[7]);
            for (int i = 0; i < 4; i++) SetPoints(_lines[2 + i], c[i], c[4 + i]);
            foreach (var line in _lines) line.widthMultiplier = LineWidthMeters;

            var size = _item.SizeMeters;
            var t = _fill.transform;
            t.localPosition = new Vector3(0f, size.y * 0.5f, 0f);
            t.localScale = size + Vector3.one * (InflateMeters * 2f);
        }

        static void SetPoints(LineRenderer line, params Vector3[] points)
        {
            line.positionCount = points.Length;
            line.SetPositions(points);
        }

        /// <summary>
        /// 가구 원점(바닥면 중앙) 기준 박스 꼭짓점 8개 (m). 0~3 = 아랫면, 4~7 = 윗면, i 와 i+4 가 세로 모서리.
        /// inflate 만큼 사방으로 키운다. 아랫면도 inflate 만큼 띄워 바닥에 묻히지 않게 한다.
        /// </summary>
        public static Vector3[] BoxCorners(Vector3 size, float inflate)
        {
            float x = size.x * 0.5f + inflate, z = size.z * 0.5f + inflate;
            float bottom = inflate, top = size.y + inflate;
            return new[]
            {
                new Vector3(-x, bottom, -z), new Vector3(x, bottom, -z), new Vector3(x, bottom, z), new Vector3(-x, bottom, z),
                new Vector3(-x, top, -z), new Vector3(x, top, -z), new Vector3(x, top, z), new Vector3(-x, top, z),
            };
        }

        /// <summary>현재 상태에 맞춰 윤곽선 재질과 겹침 상자를 바꾼다</summary>
        public void Refresh()
        {
            if (_item == null || _fill == null) return;
            var state = _item.State;

            bool showLines = ShowFreeOutline || state != PlacementState.Free;
            var lineMaterial = LineMaterial(state);
            foreach (var line in _lines)
            {
                line.enabled = showLines && lineMaterial != null;
                line.sharedMaterial = lineMaterial;
            }

            var fillMaterial = Pick(OverlapFillMaterial, ref _fallbackFill, "StateOverlapFill", OverlapFillColor, true);
            _fill.enabled = state == PlacementState.Overlap && fillMaterial != null;
            _fill.sharedMaterial = fillMaterial;
        }

        Material LineMaterial(PlacementState state) => state switch
        {
            PlacementState.Overlap => Pick(OverlapMaterial, ref _fallbackOverlap, "StateOverlap", OverlapColor, false),
            PlacementState.Tight => Pick(TightMaterial, ref _fallbackTight, "StateTight", TightColor, false),
            _ => Pick(FreeMaterial, ref _fallbackFree, "StateFree", FreeColor, false),
        };

        /// <summary>칸에 넣은 재질, 없으면 모든 가구가 함께 쓰는 코드 생성 재질</summary>
        static Material Pick(Material assigned, ref Material fallback, string materialName, Color color, bool transparent)
        {
            if (assigned != null) return assigned;
            if (fallback == null) fallback = UrpMaterials.CreateUnlit(materialName + " (자동 생성)", color, transparent);
            return fallback;
        }
    }
}
