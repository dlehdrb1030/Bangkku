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
    /// 재질 칸을 비워 두면 단색 재질을 코드로 만든다 (벽만 큐브 기본 재질).
    /// </summary>
    public class FloorPlanBuilder : MonoBehaviour
    {
        const string GeneratedRootName = "Generated";
        static readonly Color WoodColor = new(0.80f, 0.68f, 0.52f);

        [Tooltip("평면도 JSON (Assets/_Project/Data/FloorPlans/*.json)")]
        public TextAsset FloorPlanJson;

        [Header("재질 (비워 두면 코드로 만든 단색)")]
        [Tooltip("벽 재질. 비워 두면 큐브 기본 재질")]
        public Material WallMaterial;

        [Tooltip("창 유리 재질 (URP Lit, Surface Type = Transparent). 비워 두면 반투명 하늘색")]
        public Material GlassMaterial;

        [Tooltip("바닥 material: \"wood\" 재질. 비워 두면 밝은 나무색")]
        public Material FloorMaterial;

        [Tooltip("바닥 material: \"tile\" 재질. 비워 두면 밝은 회색")]
        public Material TileMaterial;

        [Tooltip("천장 재질. 비워 두면 흰색")]
        public Material CeilingMaterial;

        [Tooltip("고정설비 박스 재질. 비워 두면 연한 베이지")]
        public Material FixtureMaterial;

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
            var (wallCount, glassCount) = BuildWalls(CreateChild("Walls", GeneratedRoot));
            int floorCount = BuildFloors(CreateChild("Floors", GeneratedRoot), CreateChild("Ceiling", GeneratedRoot));
            int fixtureCount = BuildFixtures(CreateChild("Fixtures", GeneratedRoot));

            Debug.Log($"[방꾸] 평면도 {Data.Name} 생성: 벽 조각 {wallCount}개, 유리 {glassCount}개 (개구부 {Data.Openings.Count}개), " +
                      $"바닥·천장 {floorCount}/{Data.Floors.Count}개, 고정설비 {fixtureCount}개", this);
            Built?.Invoke(this);
            return true;
        }

        (int walls, int glass) BuildWalls(Transform parent)
        {
            int wallCount = 0, glassCount = 0;
            foreach (var piece in WallMeshBuilder.ComputePieces(Data))
            {
                bool glass = piece.Kind == WallPieceKind.Glass;
                var material = glass ? Pick(GlassMaterial, "Glass", new Color(0.75f, 0.88f, 0.95f, 0.25f), true) : WallMaterial;
                WallMeshBuilder.CreatePiece(piece, parent, material);
                if (glass) glassCount++;
                else wallCount++;
            }

            return (wallCount, glassCount);
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

        /// <summary>고정설비를 박스(BoxCollider, Layer Fixture)로 만든다. position 은 바닥면 중앙, rotation 은 Y축 (도).</summary>
        int BuildFixtures(Transform parent)
        {
            var material = Pick(FixtureMaterial, "Fixture", new Color(0.88f, 0.86f, 0.80f));
            foreach (var fixture in Data.Fixtures)
            {
                var size = fixture.SizeMeters();
                var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                go.name = fixture.Id;
                go.layer = Layers.Fixture;

                var t = go.transform;
                t.SetParent(parent, false);
                t.localPosition = Units.PlanToWorld(fixture.Position, size.y * 0.5f);
                t.localRotation = Quaternion.Euler(0f, fixture.Rotation, 0f);
                t.localScale = size;

                // 고정설비는 JSON 에 신뢰도 칸이 없다 (Data/ 공용 형식). 가장 보수적인 estimated 로 판정한다
                go.AddComponent<StructureConfidence>().Set(fixture.Id, Confidence.Estimated);

                if (material != null) go.GetComponent<MeshRenderer>().sharedMaterial = material;
            }

            return Data.Fixtures.Count;
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
