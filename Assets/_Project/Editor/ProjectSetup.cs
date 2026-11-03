using System.Collections.Generic;
using System.Linq;
using System.Text;
using Bangkku.Core;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;
using UnityEngine.Rendering;

namespace Bangkku.EditorTools
{
    /// <summary>
    /// docs/03_방꾸_Unity_프로젝트설정.md 의 5.1 Editor, 5.2 Player, 5.5 Layer 설정을 코드로 적용한다.
    /// 5.3 XR Plug-in Management, 5.4 Quality/URP 는 건드리지 않는다 (사람이 에디터에서 설정).
    /// 여러 번 실행해도 결과가 같다. 이미 맞는 값은 그대로 둔다.
    /// </summary>
    public static class ProjectSetup
    {
        const string MenuPath = "Tools/방꾸/프로젝트 설정 적용";

        // 5.1 Editor
        const string VersionControlMode = "Visible Meta Files";

        // 5.2 Player
        const string CompanyName = "Bangkku";
        const string ProductName = "방꾸";
        // 제품 이름이 한글이라 패키지 이름을 자동 생성에 맡기지 않고 직접 지정한다.
        const string ApplicationId = "com.Bangkku.Bangkku";
        // Meta 문서의 Quest 2/Pro/3 권장 minSdkVersion (Android 12L)
        const int AndroidMinSdk = 32;

        // ProjectSettings.asset 의 activeInputHandler 값
        const int InputHandlerOld = 0;
        const int InputHandlerNew = 1;
        const int InputHandlerBoth = 2;

        // 5.5 Tags and Layers. 번호와 이름은 Core/Layers.cs 상수를 따른다.
        static readonly (int index, string name)[] RequiredLayers =
        {
            (Layers.Wall, nameof(Layers.Wall)),
            (Layers.Floor, nameof(Layers.Floor)),
            (Layers.Furniture, nameof(Layers.Furniture)),
            (Layers.Fixture, nameof(Layers.Fixture)),
        };

        [MenuItem(MenuPath)]
        static void Apply()
        {
            var report = new Report();

            ApplyEditorSettings(report);
            ApplyPlayerSettings(report);
            ApplyLayers(report);

            AssetDatabase.SaveAssets();
            report.Print();
        }

        // ───────────── 5.1 Editor ─────────────

        static void ApplyEditorSettings(Report report)
        {
            if (VersionControlSettings.mode != VersionControlMode)
            {
                report.Changed("Version Control Mode", VersionControlSettings.mode, VersionControlMode);
                VersionControlSettings.mode = VersionControlMode;
            }
            else report.Same("Version Control Mode", VersionControlMode);

            if (EditorSettings.serializationMode != SerializationMode.ForceText)
            {
                report.Changed("Asset Serialization Mode", EditorSettings.serializationMode.ToString(), "ForceText");
                EditorSettings.serializationMode = SerializationMode.ForceText;
            }
            else report.Same("Asset Serialization Mode", "ForceText");
        }

        // ───────────── 5.2 Player ─────────────

        static void ApplyPlayerSettings(Report report)
        {
            // 공통
            if (PlayerSettings.companyName != CompanyName)
            {
                report.Changed("Company Name", PlayerSettings.companyName, CompanyName);
                PlayerSettings.companyName = CompanyName;
            }
            else report.Same("Company Name", CompanyName);

            if (PlayerSettings.productName != ProductName)
            {
                report.Changed("Product Name", PlayerSettings.productName, ProductName);
                PlayerSettings.productName = ProductName;
            }
            else report.Same("Product Name", ProductName);

            ApplyApplicationId(report, NamedBuildTarget.Standalone, "Standalone");
            ApplyApplicationId(report, NamedBuildTarget.Android, "Android");

            ApplyActiveInputHandler(report);

            if (PlayerSettings.colorSpace != ColorSpace.Linear)
            {
                report.Changed("Color Space", PlayerSettings.colorSpace.ToString(), "Linear");
                PlayerSettings.colorSpace = ColorSpace.Linear;
            }
            else report.Same("Color Space", "Linear");

            // Windows: Direct3D12만 (3DGS가 DX11 미지원)
            ApplyGraphicsApis(report, BuildTarget.StandaloneWindows64, "Windows", GraphicsDeviceType.Direct3D12);

            // macOS: Metal 기본값 유지. 바꾸지 않고 확인만 한다.
            CheckMacGraphicsApis(report);

            // Android: Vulkan만 (3DGS가 OpenGL ES 미지원)
            ApplyGraphicsApis(report, BuildTarget.Android, "Android", GraphicsDeviceType.Vulkan);

            var minSdk = (AndroidSdkVersions)AndroidMinSdk;
            if (PlayerSettings.Android.minSdkVersion != minSdk)
            {
                report.Changed("Android Minimum API Level", ((int)PlayerSettings.Android.minSdkVersion).ToString(), AndroidMinSdk.ToString());
                PlayerSettings.Android.minSdkVersion = minSdk;
            }
            else report.Same("Android Minimum API Level", AndroidMinSdk.ToString());

            var backend = PlayerSettings.GetScriptingBackend(NamedBuildTarget.Android);
            if (backend != ScriptingImplementation.IL2CPP)
            {
                report.Changed("Android Scripting Backend", backend.ToString(), "IL2CPP");
                PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            }
            else report.Same("Android Scripting Backend", "IL2CPP");

            if (PlayerSettings.Android.targetArchitectures != AndroidArchitecture.ARM64)
            {
                report.Changed("Android Target Architectures", PlayerSettings.Android.targetArchitectures.ToString(), "ARM64");
                PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            }
            else report.Same("Android Target Architectures", "ARM64");
        }

        static void ApplyApplicationId(Report report, NamedBuildTarget target, string label)
        {
            string current = PlayerSettings.GetApplicationIdentifier(target);
            if (current != ApplicationId)
            {
                report.Changed($"{label} Package Name", current, ApplicationId);
                PlayerSettings.SetApplicationIdentifier(target, ApplicationId);
            }
            else report.Same($"{label} Package Name", ApplicationId);
        }

        /// <summary>
        /// Active Input Handling 은 공개 API가 없어서 ProjectSettings.asset 의 직렬화 값을 바꾼다.
        /// Both(2)는 문서에서 허용한 예외라 그대로 둔다.
        /// </summary>
        static void ApplyActiveInputHandler(Report report)
        {
            var playerSettings = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset").FirstOrDefault();
            if (playerSettings == null)
            {
                report.Warn("ProjectSettings.asset 을 읽지 못해 Active Input Handling 을 확인하지 못했습니다. 직접 확인하세요.");
                return;
            }

            var so = new SerializedObject(playerSettings);
            var prop = so.FindProperty("activeInputHandler");
            if (prop == null)
            {
                report.Warn("activeInputHandler 항목을 찾지 못했습니다. Player > Active Input Handling 을 직접 확인하세요.");
                return;
            }

            switch (prop.intValue)
            {
                case InputHandlerNew:
                    report.Same("Active Input Handling", "Input System Package (New)");
                    break;
                case InputHandlerBoth:
                    report.Same("Active Input Handling", "Both (문서 허용 예외, 유지)");
                    break;
                default:
                    report.Changed("Active Input Handling", prop.intValue == InputHandlerOld ? "Input Manager (Old)" : prop.intValue.ToString(), "Input System Package (New)");
                    prop.intValue = InputHandlerNew;
                    so.ApplyModifiedPropertiesWithoutUndo();
                    report.NeedsRestart = true;
                    break;
            }
        }

        static void ApplyGraphicsApis(Report report, BuildTarget target, string label, params GraphicsDeviceType[] apis)
        {
            string wanted = string.Join(", ", apis);
            bool useDefault = PlayerSettings.GetUseDefaultGraphicsAPIs(target);
            var current = PlayerSettings.GetGraphicsAPIs(target);

            if (!useDefault && current.SequenceEqual(apis))
            {
                report.Same($"{label} Graphics APIs", wanted);
                return;
            }

            report.Changed($"{label} Graphics APIs", useDefault ? "Auto" : string.Join(", ", current), wanted);
            PlayerSettings.SetUseDefaultGraphicsAPIs(target, false);
            PlayerSettings.SetGraphicsAPIs(target, apis);
        }

        static void CheckMacGraphicsApis(Report report)
        {
            if (PlayerSettings.GetUseDefaultGraphicsAPIs(BuildTarget.StandaloneOSX))
            {
                report.Same("macOS Graphics APIs", "Auto (Metal)");
                return;
            }

            var current = PlayerSettings.GetGraphicsAPIs(BuildTarget.StandaloneOSX);
            if (current.Length > 0 && current[0] == GraphicsDeviceType.Metal)
                report.Same("macOS Graphics APIs", string.Join(", ", current));
            else
                report.Warn($"macOS Graphics APIs 가 {string.Join(", ", current)} 입니다. 기본값(Metal)으로 되돌릴지 확인하세요.");
        }

        // ───────────── 5.5 Tags and Layers ─────────────

        static void ApplyLayers(Report report)
        {
            var tagManager = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset").FirstOrDefault();
            if (tagManager == null)
            {
                report.Warn("TagManager.asset 을 읽지 못해 Layer 를 등록하지 못했습니다.");
                return;
            }

            var so = new SerializedObject(tagManager);
            var layers = so.FindProperty("layers");

            foreach (var (index, name) in RequiredLayers)
            {
                var slot = layers.GetArrayElementAtIndex(index);
                if (slot.stringValue == name)
                {
                    report.Same($"Layer {index}", name);
                    continue;
                }

                // 다른 이름이 이미 있으면 덮어쓰지 않는다 (그 Layer를 쓰는 오브젝트가 있을 수 있음).
                if (!string.IsNullOrEmpty(slot.stringValue))
                {
                    report.Warn($"Layer {index} 에 '{slot.stringValue}' 가 이미 있어 '{name}' 을 등록하지 않았습니다. Tags and Layers 에서 직접 정리하세요.");
                    continue;
                }

                int existing = FindLayer(layers, name);
                if (existing >= 0)
                {
                    report.Warn($"'{name}' 이 Layer {existing} 에 이미 있습니다. 문서 기준은 Layer {index} 입니다. 직접 옮기세요.");
                    continue;
                }

                report.Changed($"Layer {index}", "(비어 있음)", name);
                slot.stringValue = name;
            }

            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static int FindLayer(SerializedProperty layers, string name)
        {
            for (int i = 0; i < layers.arraySize; i++)
            {
                if (layers.GetArrayElementAtIndex(i).stringValue == name)
                    return i;
            }
            return -1;
        }

        // ───────────── 결과 출력 ─────────────

        class Report
        {
            readonly List<string> changed = new();
            readonly List<string> same = new();
            readonly List<string> warnings = new();

            public bool NeedsRestart;

            public void Changed(string item, string from, string to) => changed.Add($"{item}: {from} → {to}");
            public void Same(string item, string value) => same.Add($"{item}: {value}");
            public void Warn(string message) => warnings.Add(message);

            public void Print()
            {
                var sb = new StringBuilder();
                sb.AppendLine($"[방꾸] 프로젝트 설정 적용 완료 (변경 {changed.Count}, 유지 {same.Count}, 경고 {warnings.Count})");
                AppendSection(sb, "변경", changed);
                AppendSection(sb, "이미 맞음", same);
                AppendSection(sb, "경고", warnings);
                sb.AppendLine();
                sb.AppendLine("XR Plug-in Management, URP 설정은 이 메뉴가 적용하지 않습니다. 03 문서 5.3, 5.4 를 보고 직접 설정하세요.");

                if (warnings.Count > 0) Debug.LogWarning(sb.ToString());
                else Debug.Log(sb.ToString());

                string dialog = $"변경 {changed.Count}개, 유지 {same.Count}개, 경고 {warnings.Count}개\n자세한 내용은 Console을 보세요.";
                if (NeedsRestart)
                    dialog += "\n\nActive Input Handling 이 바뀌었습니다. 에디터를 재시작해야 적용됩니다.";
                EditorUtility.DisplayDialog("방꾸 프로젝트 설정", dialog, "확인");
            }

            static void AppendSection(StringBuilder sb, string title, List<string> lines)
            {
                if (lines.Count == 0) return;
                sb.AppendLine($"\n■ {title}");
                foreach (var line in lines) sb.AppendLine($"  - {line}");
            }
        }
    }
}
