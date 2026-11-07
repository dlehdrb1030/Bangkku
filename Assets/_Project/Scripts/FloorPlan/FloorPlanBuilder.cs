using System;
using System.Collections.Generic;
using Bangkku.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace Bangkku.FloorPlan
{
    /// <summary>
    /// 평면도 JSON → 방 (전체개발계획 3장): 벽·개구부, 바닥, 천장, 고정설비.
    /// 씬 원점에 둔 빈 오브젝트에 붙이고 Floor Plan Json 에 JSON 을 넣으면 Play 할 때 만든다.
    /// 다른 평면도는 JSON 만 바꾸면 된다.
    /// 만든 오브젝트는 이 오브젝트 아래 "Generated" 에 모인다 (씬에 저장되지 않음).
    /// 재질 칸을 비워 두면 단색 재질을 코드로 만든다.
    /// </summary>
    public class FloorPlanBuilder : MonoBehaviour
    {
        const string GeneratedRootName = "Generated";
        static readonly Color WoodColor = new(0.80f, 0.68f, 0.52f);

        [Tooltip("평면도 JSON (Assets/_Project/Data/FloorPlans/*.json)")]
        public TextAsset FloorPlanJson;

        [Header("재질 (비워 두면 코드로 만든 단색)")]
        [Tooltip("벽 재질. 비워 두면 미색")]
        public Material WallMaterial;

        [Tooltip("창 유리 재질 (URP Lit, Surface Type = Transparent). 비워 두면 반투명 하늘색")]
        public Material GlassMaterial;

        [Tooltip("바닥 material: \"wood\" 재질. 비워 두면 밝은 나무색")]
        public Material FloorMaterial;

        [Tooltip("바닥 material: \"tile\" 재질. 비워 두면 밝은 회색")]
        public Material TileMaterial;

        [Tooltip("천장 재질. 비워 두면 흰색")]
        public Material CeilingMaterial;

        [Tooltip("고정설비 몸통·문짝 재질. 비워 두면 연한 베이지")]
        public Material FixtureMaterial;

        [Tooltip("고정설비 도기·상판 재질 (변기·세면대·싱크대 상판·냉장고). 비워 두면 흰색 광택")]
        public Material PorcelainMaterial;

        [Tooltip("고정설비 손잡이·수전·개수대 재질. 비워 두면 은색 금속")]
        public Material MetalMaterial;

        [Tooltip("고정설비 어두운 부분 재질 (걸레받이 자리·쿡탑). 비워 두면 짙은 회색")]
        public Material DarkMaterial;

        [Tooltip("걸레받이·문틀·창틀 재질. 비워 두면 흰색")]
        public Material TrimMaterial;

        [Tooltip("창밖 풍경 재질 (Unlit + 파노라마, Tools > 방꾸 > 방 텍스처 생성·적용). 비워 두면 창밖 풍경을 만들지 않음 (하늘만 보임)")]
        public Material ExteriorMaterial;

        /// <summary>평면도를 (다시) 만들 때마다 끝에서 불린다. 바닥에 텔레포트 영역을 붙이는 등 생성물에 기능을 더하는 용도 (XRRigSetup)</summary>
        public event Action<FloorPlanBuilder> Built;

        /// <summary>마지막으로 읽은 평면도. 생성 전이나 실패하면 null</summary>
        public FloorPlanData Data { get; private set; }

        /// <summary>생성한 오브젝트의 루트. 생성 전이나 실패하면 null</summary>
        public Transform GeneratedRoot { get; private set; }

        /// <summary>재질 칸이 비었을 때 코드로 만든 재질. 이 컴포넌트가 없어질 때 지운다</summary>
        readonly Dictionary<string, Material> _fallbackMaterials = new();

        /// <summary>바닥·천장 메쉬. GameObject 를 지워도 메쉬는 남으므로 다시 만들 때 직접 지운다</summary>
        readonly List<Mesh> _meshes = new();

        void Awake() => Build();

        void OnDestroy()
        {
            Clear();
            foreach (var material in _fallbackMaterials.Values)
                if (material != null) Destroy(material);
            _fallbackMaterials.Clear();
        }

        [ContextMenu("평면도 다시 생성 (Play 중)")]
        void RebuildFromMenu() => Build();

        /// <summary>이전에 만든 것을 지우고 JSON 에서 다시 만든다. 실패하면 false (이유는 Console).</summary>
        public bool Build()
        {
            if (!Application.isPlaying)
            {
                Debug.LogWarning("[방꾸] 평면도 생성은 Play 중에만 합니다. Play 를 누르세요.", this);
                return false;
            }

            Clear();
            Data = FloorPlanLoader.Load(FloorPlanJson);
            if (Data == null) return false;

            WarnIfNotAtOrigin();

            GeneratedRoot = CreateChild(GeneratedRootName, transform);
            var wallRoot = CreateChild("Walls", GeneratedRoot);
            var (wallCount, glassCount) = BuildWalls(wallRoot);
            BuildEntranceDoors(wallRoot);
            int floorCount = BuildFloors(CreateChild("Floors", GeneratedRoot), CreateChild("Ceiling", GeneratedRoot));
            int fixtureCount = BuildFixtures(CreateChild("Fixtures", GeneratedRoot));
            int trimCount = BuildTrim(GeneratedRoot);
            BuildExterior(GeneratedRoot);

            Debug.Log($"[방꾸] 평면도 {Data.Name} 생성: 벽 조각 {wallCount}개, 유리 {glassCount}개 (개구부 {Data.Openings.Count}개), " +
                      $"바닥·천장 {floorCount}/{Data.Floors.Count}개, 고정설비 {fixtureCount}개, 걸레받이·문틀·창틀 {trimCount}조각", this);
            Built?.Invoke(this);
            return true;
        }

        (int walls, int glass) BuildWalls(Transform parent)
        {
            int wallCount = 0, glassCount = 0;
            foreach (var piece in WallMeshBuilder.ComputePieces(Data))
            {
                bool glass = piece.Kind == WallPieceKind.Glass;
                var material = glass
                    ? Pick(GlassMaterial, "Glass", new Color(0.75f, 0.88f, 0.95f, 0.25f), true)
                    : Pick(WallMaterial, "Wall", new Color(0.93f, 0.92f, 0.89f));
                WallMeshBuilder.CreatePiece(piece, parent, material, _meshes);
                if (glass) glassCount++;
                else wallCount++;
            }

            return (wallCount, glassCount);
        }

        /// <summary>
        /// 바깥과 이어진 문(현관문)을 닫힌 문짝으로 막는다 (EntranceDoor). 문 밖은 바닥이 없어 VR 에서 걸어 나가면 떨어지기 때문.
        /// 문짝은 벽처럼 Layer Wall + BoxCollider + 벽 신뢰도, 재질은 Dark. 안쪽 손잡이는 Metal, 콜라이더 없음
        /// </summary>
        void BuildEntranceDoors(Transform parent)
        {
            var leafMaterial = PickLit(DarkMaterial, "Dark", new Color(0.16f, 0.16f, 0.17f), 0.6f, 0f);
            var handleMaterial = PickLit(MetalMaterial, "Metal", new Color(0.78f, 0.79f, 0.80f), 0.7f, 0.6f);
            foreach (var leaf in EntranceDoor.ComputeLeaves(Data))
            {
                var rotation = Quaternion.Euler(0f, leaf.YawDegrees, 0f);
                var go = BoxMesh.CreateObject(leaf.Name, parent, leaf.Center, rotation, leaf.Size, leafMaterial, _meshes);
                go.layer = Layers.Wall;
                go.AddComponent<StructureConfidence>().Set(leaf.Wall.Id, leaf.Wall.Confidence);

                var handleMesh = BoxMesh.Create(leaf.Name + "_handle", leaf.HandleSize, Vector3.zero);
                _meshes.Add(handleMesh);
                var handle = CreateMeshObject(leaf.Name + "_handle", parent, handleMesh, handleMaterial);
                handle.transform.SetLocalPositionAndRotation(leaf.HandleCenter, rotation);
            }
        }

        /// <summary>바닥(Layer Floor, MeshCollider)과 같은 모양의 천장을 만든다. 만든 바닥 수를 돌려준다.</summary>
        int BuildFloors(Transform floorParent, Transform ceilingParent)
        {
            float ceilingHeight = Units.MmToM(Data.CeilingHeight.Value);
            var ceilingMaterial = Pick(CeilingMaterial, "Ceiling", new Color(0.95f, 0.95f, 0.94f));
            int count = 0;
            foreach (var floor in Data.Floors)
            {
                var floorMesh = PolygonMesh.BuildMesh(floor.Id, floor.Polygon, 0f, true);
                if (floorMesh == null)
                {
                    Debug.LogError($"[방꾸] 바닥 {floor.Id} 를 삼각분할하지 못했습니다. JSON 에서 직사각형 여러 개로 나눠 주세요.", this);
                    continue;
                }

                _meshes.Add(floorMesh);
                var floorObject = CreateMeshObject(floor.Id, floorParent, floorMesh, FloorMaterialFor(floor));
                floorObject.layer = Layers.Floor;
                floorObject.AddComponent<MeshCollider>().sharedMesh = floorMesh;

                // 천장은 그림자를 만들지 않는다 (방 안이 전부 그늘이 되지 않도록)
                var ceilingMesh = PolygonMesh.BuildMesh(floor.Id + "_ceiling", floor.Polygon, ceilingHeight, false);
                _meshes.Add(ceilingMesh);
                var ceiling = CreateMeshObject(floor.Id + "_ceiling", ceilingParent, ceilingMesh, ceilingMaterial);
                ceiling.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
                count++;
            }

            BuildThresholds(floorParent);
            return count;
        }

        /// <summary>문 아래 벽 두께 부분의 바닥 (Layer Floor). 문을 지나 텔레포트·이동할 수 있게 한다.</summary>
        void BuildThresholds(Transform parent)
        {
            foreach (var opening in Data.Openings)
            {
                if (opening.Type != OpeningType.Door) continue;
                var wall = Data.Walls.Find(w => w.Id == opening.WallId);
                var polygon = WallMeshBuilder.ComputeThresholdPolygon(wall, opening, Data.DefaultWallThickness);

                string objectName = opening.Id + "_threshold";
                var mesh = PolygonMesh.BuildMesh(objectName, polygon, 0f, true);
                _meshes.Add(mesh);
                var go = CreateMeshObject(objectName, parent, mesh, Pick(FloorMaterial, "Wood", WoodColor));
                go.layer = Layers.Floor;
                go.AddComponent<MeshCollider>().sharedMesh = mesh;
            }
        }

        /// <summary>
        /// 고정설비 (Layer Fixture, size 크기 BoxCollider). position 은 바닥면 중앙, rotation 은 Y축 (도).
        /// 아는 type(counter, cabinet, wardrobe, fridge, toilet, washbasin)은 모양(FixtureModels)으로 그리고, 모르는 type 은 상자로 그린다.
        /// 판정은 어느 쪽이든 같은 size 상자(BoxCollider)로 한다.
        /// </summary>
        int BuildFixtures(Transform parent)
        {
            var material = Pick(FixtureMaterial, "Fixture", new Color(0.88f, 0.86f, 0.80f));
            var modelMaterials = new Material[FixtureModels.PartCount];
            modelMaterials[(int)FixturePart.Body] = material;
            modelMaterials[(int)FixturePart.Porcelain] = PickLit(PorcelainMaterial, "Porcelain", new Color(0.95f, 0.95f, 0.94f), 0.85f, 0f);
            modelMaterials[(int)FixturePart.Metal] = PickLit(MetalMaterial, "Metal", new Color(0.78f, 0.79f, 0.80f), 0.7f, 0.6f);
            modelMaterials[(int)FixturePart.Dark] = PickLit(DarkMaterial, "Dark", new Color(0.16f, 0.16f, 0.17f), 0.6f, 0f);

            foreach (var fixture in Data.Fixtures)
            {
                var size = fixture.SizeMeters();
                var position = Units.PlanToWorld(fixture.Position, size.y * 0.5f);
                var rotation = Quaternion.Euler(0f, fixture.Rotation, 0f);
                var model = FixtureModels.Build(fixture.Type, size);
                GameObject go;
                if (model != null)
                {
                    // 상자 가운데에 놓인 오브젝트라 바닥 기준 모양을 높이 절반만큼 내린다
                    go = new GameObject(fixture.Id);
                    go.transform.SetParent(parent, false);
                    go.transform.localPosition = position;
                    go.transform.localRotation = rotation;
                    var mesh = model.ToMesh(fixture.Id, new Vector3(0f, -size.y * 0.5f, 0f));
                    _meshes.Add(mesh);
                    go.AddComponent<MeshFilter>().sharedMesh = mesh;
                    go.AddComponent<MeshRenderer>().sharedMaterials = modelMaterials;
                    go.AddComponent<BoxCollider>().size = size;
                }
                else
                {
                    // 벽과 같은 m 단위 UV 상자 (스케일 1, 크기는 메쉬·BoxCollider)
                    go = BoxMesh.CreateObject(fixture.Id, parent, position, rotation, size, material, _meshes);
                }

                go.layer = Layers.Fixture;

                // 고정설비는 JSON 에 신뢰도 칸이 없다 (Data/ 공용 형식). 가장 보수적인 estimated 로 판정한다
                go.AddComponent<StructureConfidence>().Set(fixture.Id, Confidence.Estimated);
            }

            return Data.Fixtures.Count;
        }

        /// <summary>
        /// 걸레받이·문틀·창틀 (TrimBuilder). 조각이 많아 메쉬 하나로 합친 오브젝트 "Trim" 하나로 만든다.
        /// 보이기만 하는 장식이라 콜라이더가 없고(판정은 벽 조각으로), 그림자도 만들지 않는다 (작은 판이라 차이가 거의 없음)
        /// </summary>
        int BuildTrim(Transform parent)
        {
            var pieces = TrimBuilder.ComputeAll(Data);
            if (pieces.Count == 0) return 0;

            var mesh = TrimBuilder.CreateMesh("Trim", pieces);
            _meshes.Add(mesh);
            var go = CreateMeshObject("Trim", parent, mesh, Pick(TrimMaterial, "Trim", new Color(0.97f, 0.97f, 0.96f)));
            go.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
            return pieces.Count;
        }

        /// <summary>
        /// 창밖 풍경 (ExteriorBuilder). 재질 칸이 비면 만들지 않는다 (파노라마 텍스처가 있어야 의미가 있음).
        /// 조명·그림자와 무관 (Unlit, 그림자 안 만들고 안 받음), 콜라이더 없음
        /// </summary>
        void BuildExterior(Transform parent)
        {
            if (ExteriorMaterial == null) return;

            ExteriorBuilder.Placement(Data, out var center, out float radius);
            var mesh = ExteriorBuilder.CreateMesh(center, radius);
            _meshes.Add(mesh);
            var go = CreateMeshObject("Exterior", parent, mesh, ExteriorMaterial);
            var renderer = go.GetComponent<MeshRenderer>();
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        }

        Material FloorMaterialFor(FloorData floor)
        {
            switch (floor.Material)
            {
                case "tile":
                    return Pick(TileMaterial, "Tile", new Color(0.80f, 0.82f, 0.83f));
                case "wood":
                case null:
                    return Pick(FloorMaterial, "Wood", WoodColor);
                default:
                    Debug.LogWarning($"[방꾸] 바닥 {floor.Id} 의 material \"{floor.Material}\" 은 모르는 값이라 wood 로 만듭니다 (wood, tile).", this);
                    return Pick(FloorMaterial, "Wood", WoodColor);
            }
        }

        void Clear()
        {
            if (GeneratedRoot != null) Destroy(GeneratedRoot.gameObject);
            GeneratedRoot = null;
            Data = null;

            foreach (var mesh in _meshes)
                if (mesh != null) Destroy(mesh);
            _meshes.Clear();
        }

        static GameObject CreateMeshObject(string objectName, Transform parent, Mesh mesh, Material material)
        {
            var go = new GameObject(objectName);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = material;
            return go;
        }

        static Transform CreateChild(string childName, Transform parent)
        {
            var child = new GameObject(childName).transform;
            child.SetParent(parent, false);
            return child;
        }

        /// <summary>지정한 재질이 있으면 그것을, 없으면 같은 이름으로 한 번만 만든 단색 재질을 쓴다.</summary>
        Material Pick(Material assigned, string fallbackName, Color color, bool transparent = false)
        {
            if (assigned != null) return assigned;
            if (!_fallbackMaterials.TryGetValue(fallbackName, out var material))
                _fallbackMaterials[fallbackName] = material =
                    UrpMaterials.CreateLit(fallbackName + " (자동 생성)", color, transparent ? 0.9f : 0.5f, transparent);
            return material;
        }

        /// <summary>Pick 과 같고, 코드로 만들 때 매끈함·금속성을 정한다 (고정설비 모델 재질)</summary>
        Material PickLit(Material assigned, string fallbackName, Color color, float smoothness, float metallic)
        {
            if (assigned != null) return assigned;
            if (!_fallbackMaterials.TryGetValue(fallbackName, out var material))
                _fallbackMaterials[fallbackName] = material =
                    UrpMaterials.CreateLit(fallbackName + " (자동 생성)", color, smoothness, false, metallic);
            return material;
        }

        /// <summary>
        /// layout.json 의 가구 좌표는 평면도 좌표 그대로라서, 방이 월드 원점에서 벗어나면 가구 위치가 어긋난다.
        /// </summary>
        void WarnIfNotAtOrigin()
        {
            var t = transform;
            if (t.position != Vector3.zero || t.rotation != Quaternion.identity || t.lossyScale != Vector3.one)
                Debug.LogWarning($"[방꾸] {name} 의 Transform 이 원점이 아닙니다 (Position 0, Rotation 0, Scale 1 이어야 함). " +
                                 "가구 저장 좌표와 방 위치가 어긋납니다.", this);
        }
    }
}
