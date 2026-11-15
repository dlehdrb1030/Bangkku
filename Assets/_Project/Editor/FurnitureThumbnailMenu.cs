using System.Collections.Generic;
using System.IO;
using Bangkku.Furniture;
using Bangkku.UI;
using GaussianSplatting.Runtime;
using UnityEditor;
using UnityEngine;

namespace Bangkku.EditorTools
{
    /// <summary>
    /// Tools > 방꾸 > 가구 사진 만들기: 카탈로그의 3DGS·메쉬 가구를 하나씩 찍어
    /// Assets/_Project/Resources/FurnitureThumbnails/&lt;id&gt;.png (256×256) 로 저장한다. 손목 메뉴 선택 탭이 쓴다.
    /// - 지금 열린 씬의 먼 자리(FarAway)에 프리팹을 잠깐 만들고, 정면(+Z) 오른쪽 위에서 판정 치수 전체가 들어오게 찍은 뒤 지운다
    ///   (HideAndDontSave 라 씬에 저장되지 않음). 실행 중에 만들어지는 방은 Play 밖에서는 없어서 함께 찍히지 않는다
    /// - 3DGS 플러그인(v2)은 .ply 를 에디터 업데이트 여러 번에 걸쳐 읽는다. 그래서 가구 하나씩 EditorApplication.update 로
    ///   읽기가 끝날 때까지(최대 LoadTimeoutSeconds) 기다렸다가 찍는다. 시간 안에 못 읽으면 그 가구는 건너뛴다
    /// - 그래도 사진이 비어 보이면 Game 뷰 캡처를 같은 이름으로 넣는다
    /// 박스 가구(source = box)는 사진 없이 메뉴가 가구 색 상자로 보여 준다.
    /// </summary>
    public static class FurnitureThumbnailMenu
    {
        const string Root = "Assets/_Project/";
        const string CatalogPath = Root + "Data/Furniture/catalog.json";
        const string OutputFolder = Root + "Resources/" + FurnitureThumbnails.ResourceFolder;

        static readonly Vector3 FarAway = new(1000f, 0f, 1000f);

        /// <summary>가구 중심에서 카메라 쪽 방향: 정면(+Z) 오른쪽 위</summary>
        static readonly Vector3 ViewDirection = new Vector3(0.55f, 0.6f, 1f).normalized;

        const float FieldOfView = 30f;
        const double LoadTimeoutSeconds = 20.0;

        /// <summary>읽기가 끝난 뒤 정렬·GPU 준비가 한 번 돌도록 기다리는 에디터 업데이트 수</summary>
        const int SettleUpdates = 3;

        /// <summary>손목 메뉴 배경과 비슷한 어두운 회색</summary>
        static readonly Color Background = new(0.18f, 0.19f, 0.22f, 1f);

        static readonly Queue<CatalogItem> Pending = new();
        static int _total, _made, _skipped;
        static CatalogItem _current;
        static GameObject _instance;
        static double _startTime;
        static int _settle;

        [MenuItem("Tools/방꾸/가구 사진 만들기")]
        public static void Build()
        {
            if (EditorApplication.isPlaying)
            {
                Debug.LogWarning("[방꾸] 가구 사진은 Play 를 끈 상태에서 만듭니다.");
                return;
            }

            if (_total > 0)
            {
                Debug.LogWarning("[방꾸] 가구 사진을 만드는 중입니다. 끝난 뒤 다시 누르세요.");
                return;
            }

            var catalog = AssetDatabase.LoadAssetAtPath<TextAsset>(CatalogPath);
            var items = CatalogLoader.Load(catalog);
            if (items.Count == 0)
            {
                Debug.LogError($"[방꾸] 카탈로그를 읽지 못했습니다: {CatalogPath}");
                return;
            }

            Directory.CreateDirectory(OutputFolder);
            Pending.Clear();
            foreach (var item in items.Values)
                if (item.Source != FurnitureSource.Box) Pending.Enqueue(item);

            _total = Pending.Count;
            _made = _skipped = 0;
            EditorApplication.update += Tick;
        }

        /// <summary>에디터 업데이트마다: 다음 가구를 만들고, 3DGS 읽기가 끝나면 찍고, 다음으로</summary>
        static void Tick()
        {
            try
            {
                if (_current == null && !StartNext())
                {
                    Finish();
                    return;
                }

                int done = _made + _skipped;
                if (EditorUtility.DisplayCancelableProgressBar("가구 사진 만들기", $"{_current.Name} ({done + 1}/{_total})", done / (float)_total))
                {
                    Cleanup();
                    Pending.Clear();
                    Finish();
                    return;
                }

                if (!IsLoaded(_instance))
                {
                    if (EditorApplication.timeSinceStartup - _startTime < LoadTimeoutSeconds) return;
                    Debug.LogWarning($"[방꾸] {_current.Id}: 3DGS 를 {LoadTimeoutSeconds:0}초 안에 읽지 못해 건너뜁니다.");
                    _skipped++;
                    Cleanup();
                    return;
                }

                if (_settle++ < SettleUpdates) return;

                Capture(_current, OutputPath(_current));
                _made++;
                Cleanup();
            }
            catch (System.Exception e)
            {
                Debug.LogException(e);
                Cleanup();
                Pending.Clear();
                Finish();
            }
        }

        /// <summary>다음 가구 프리팹을 먼 자리에 만든다. 남은 가구가 없으면 false</summary>
        static bool StartNext()
        {
            while (Pending.Count > 0)
            {
                var item = Pending.Dequeue();
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Root + item.Prefab + ".prefab");
                if (prefab == null)
                {
                    Debug.LogWarning($"[방꾸] {item.Id}: 프리팹 {item.Prefab} 을 찾지 못했습니다.");
                    _skipped++;
                    continue;
                }

                _current = item;
                _instance = Object.Instantiate(prefab, FarAway, Quaternion.identity);
                foreach (var t in _instance.GetComponentsInChildren<Transform>(true)) t.gameObject.hideFlags = HideFlags.HideAndDontSave;
                _startTime = EditorApplication.timeSinceStartup;
                _settle = 0;
                return true;
            }

            return false;
        }

        /// <summary>3DGS 가 없으면(메쉬 가구) 바로 true. 있으면 모두 읽기가 끝나고 GPU 데이터가 생겼을 때 true</summary>
        static bool IsLoaded(GameObject instance)
        {
            foreach (var splat in instance.GetComponentsInChildren<GaussianSplatRenderer>())
                if (splat.isLoading || !splat.HasValidRenderSetup) return false;
            return true;
        }

        static string OutputPath(CatalogItem item) => $"{OutputFolder}/{item.Id}.png";

        static void Cleanup()
        {
            if (_instance != null) Object.DestroyImmediate(_instance);
            _instance = null;
            _current = null;
        }

        static void Finish()
        {
            EditorApplication.update -= Tick;
            EditorUtility.ClearProgressBar();
            AssetDatabase.Refresh();
            Debug.Log($"[방꾸] 가구 사진 {_made}개를 {OutputFolder} 에 만들었습니다." +
                      (_skipped > 0 ? $" {_skipped}개는 건너뛰었습니다 (Console 경고 참고)." : ""));
            _total = 0;
        }

        /// <summary>판정 치수(m)로 둘러싼 구가 화면에 꽉 차게 찍어 PNG 로 저장한다</summary>
        static void Capture(CatalogItem item, string path)
        {
            GameObject cameraObject = null;
            RenderTexture target = null;
            Texture2D image = null;
            try
            {
                var size = item.Size.ToMeters();
                var center = FarAway + Vector3.up * (size.y * 0.5f);
                float radius = size.magnitude * 0.5f;
                float distance = radius / Mathf.Sin(FieldOfView * 0.5f * Mathf.Deg2Rad) * 1.02f;

                cameraObject = new GameObject("ThumbnailCamera") { hideFlags = HideFlags.HideAndDontSave };
                var camera = cameraObject.AddComponent<Camera>();
                camera.transform.position = center + ViewDirection * distance;
                camera.transform.LookAt(center);
                camera.fieldOfView = FieldOfView;
                camera.nearClipPlane = 0.05f;
                camera.farClipPlane = distance + radius * 2f;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = Background;
                camera.allowMSAA = false;

                target = new RenderTexture(FurnitureThumbnails.Size, FurnitureThumbnails.Size, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                camera.targetTexture = target;
                // 3DGS 는 카메라마다 정렬 결과를 준비하므로 두 번 그려 두 번째 결과를 쓴다
                camera.Render();
                camera.Render();

                var previous = RenderTexture.active;
                RenderTexture.active = target;
                image = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
                image.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
                image.Apply();
                RenderTexture.active = previous;
                camera.targetTexture = null;

                File.WriteAllBytes(path, image.EncodeToPNG());
                AssetDatabase.ImportAsset(path);
                ApplyImportSettings(path);
            }
            finally
            {
                if (image != null) Object.DestroyImmediate(image);
                if (target != null) Object.DestroyImmediate(target);
                if (cameraObject != null) Object.DestroyImmediate(cameraObject);
            }
        }

        /// <summary>UI 용: 밉맵 끔, 256px, 가장자리 반복 없음</summary>
        static void ApplyImportSettings(string path)
        {
            if (AssetImporter.GetAtPath(path) is not TextureImporter importer) return;
            importer.textureType = TextureImporterType.Default;
            importer.mipmapEnabled = false;
            importer.maxTextureSize = FurnitureThumbnails.Size;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.sRGBTexture = true;
            importer.SaveAndReimport();
        }
    }
}
