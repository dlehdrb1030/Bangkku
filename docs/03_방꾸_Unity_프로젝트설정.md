# 방꾸: Unity 프로젝트 설정

> 이 문서는 **Unity 버전과 프로젝트 초기 설정의 기준**입니다.
> 사람과 Claude Code 에이전트 모두 이 문서를 따릅니다. 공통 규칙은 `00_방꾸_전체개발계획.md`를 봅니다.
>
> 표시
> - 👤 **사람이 Unity 에디터/Hub에서 직접** 해야 하는 작업
> - 🤖 **에이전트(Claude Code)가 해도 되는** 작업
> - ✅ 확인 방법

---

## 1. Unity 버전

| 항목 | 값 |
|---|---|
| **버전** | **Unity 6.3 LTS (6000.3.x)** |
| 패치 번호 | 첫날 Unity Hub의 최신 6.3 LTS 패치 하나로 정하고 **두 사람이 똑같이** 설치. 정한 값은 README의 "개발 환경" 항목에 기록 |
| 렌더 파이프라인 | **URP** |

### 이 버전을 고른 이유
- Unity 6.3은 현재 최신 LTS이고 2027년 12월까지 지원된다.
- Unity 6.0 LTS는 지원이 2026년 10월에 끝나서 새 프로젝트에는 쓰지 않는다.
- 3DGS 플러그인(aras-p/UnityGaussianSplatting)의 **URP 지원은 Unity 6 이상**이라 2022.3은 맞지 않는다.

### 에이전트 규칙
- 🤖 `ProjectSettings/ProjectVersion.txt`의 버전을 **절대 바꾸지 않는다.**
- 🤖 Unity API를 사용할 때 **Unity 6 기준 API**를 쓴다. 더 이상 쓰지 않는(obsolete) API가 컴파일 경고로 나오면 Unity 6 방식으로 고친다.
  - 예: `FindObjectOfType` → `FindFirstObjectByType` / `FindAnyObjectByType`
  - 예: `Rigidbody.velocity` → `Rigidbody.linearVelocity`
- 🤖 XR Interaction Toolkit은 **3.x API**를 쓴다. 2.x 예제 코드(네임스페이스 `UnityEngine.XR.Interaction.Toolkit` 하나로 모두 쓰던 방식)를 그대로 쓰지 않는다. 3.x에서는 `...Toolkit.Interactables`, `...Toolkit.Interactors` 등으로 나뉘어 있다.

---

## 2. Unity Hub 설치 모듈 👤

| 모듈 | 누가 | 용도 |
|---|---|---|
| Android Build Support (OpenJDK, Android SDK & NDK Tools 포함) | 둘 다 | Quest 단독 APK 테스트 |
| Windows Build Support | Windows VR PC 사용자 | PC VR 빌드 |
| Mac Build Support | 맥 사용자 (기본 포함) | |

---

## 3. 프로젝트 생성 👤 (팀장)

| 항목 | 값 |
|---|---|
| 템플릿 | **Universal 3D** (URP) |
| 프로젝트 이름 | `Bangkku` |
| 저장 위치 | **한글·공백 없는 경로** (예: `~/Projects/Bangkku`) |
| Unity Cloud / Version Control 연결 | 사용하지 않음 (Git 사용) |

> Hub의 VR 템플릿은 샘플 에셋이 많아서 쓰지 않는다. Universal 3D에 패키지를 직접 추가한다.

---

## 4. 패키지

### 4.1 설치할 패키지 👤 (Window > Package Manager)

| 패키지 | 이름 (`com.unity...`) | 비고 |
|---|---|---|
| XR Plugin Management | `com.unity.xr.management` | |
| OpenXR Plugin | `com.unity.xr.openxr` | |
| XR Interaction Toolkit | `com.unity.xr.interaction.toolkit` | 3.x. Samples 탭에서 **Starter Assets**, **XR Device Simulator** 임포트 |
| Input System | `com.unity.inputsystem` | XRI 설치 시 같이 설치됨 |
| Newtonsoft Json | `com.unity.nuget.newtonsoft-json` | "Add package by name"으로 입력 |
| 3DGS 플러그인 | aras-p/UnityGaussianSplatting | **팀원이 설치**, 사용 버전(릴리스 태그나 커밋)을 README에 기록 |

선택:
- Meta XR Simulator: 맥에서 Quest 컨트롤러 시뮬레이션이 더 필요할 때

### 4.2 에이전트 규칙
- 🤖 패키지 설치·삭제·버전 변경은 **하지 않는다.** `Packages/manifest.json`을 수정해야 하면 사람에게 요청한다.
- 🤖 위 표에 없는 패키지가 필요하면 이유와 패키지 이름을 사람에게 제안만 한다.
- 🤖 3DGS 플러그인 내부 코드(`Assets/Plugins/` 또는 `Packages/`에 설치된 소스)는 수정하지 않는다.

---

## 5. Project Settings 👤

### 5.1 Editor
| 항목 | 값 |
|---|---|
| Version Control > Mode | **Visible Meta Files** |
| Asset Serialization > Mode | **Force Text** |

### 5.2 Player
| 탭 | 항목 | 값 |
|---|---|---|
| 공통 | Company Name / Product Name | `Bangkku` / `방꾸` |
| 공통 | Active Input Handling | **Input System Package (New)** (구 Input Manager 코드가 필요하면 Both) |
| 공통 | Color Space | **Linear** |
| **Windows** | Graphics APIs | Auto 해제 → **Direct3D12 맨 위, Direct3D11 삭제** (3DGS가 DX11 미지원) |
| macOS | Graphics APIs | Metal (기본값) |
| **Android** | Graphics APIs | Auto 해제 → **Vulkan만** (3DGS가 OpenGL ES 미지원) |
| Android | Minimum API Level | Meta 공식 문서의 Quest 권장값 |
| Android | Scripting Backend / Target Architectures | **IL2CPP / ARM64** |

### 5.3 XR Plug-in Management
| 탭 | 설정 |
|---|---|
| **Windows** | **OpenXR** 체크 → OpenXR > Enabled Interaction Profiles에 **Oculus Touch Controller Profile** 추가 (Meta Quest Touch Plus/Pro 프로필도 있으면 추가) |
| **Android** | **OpenXR** 체크 → OpenXR Feature Groups에서 **Meta Quest Support** 체크, Interaction Profile 동일하게 추가 |
| macOS | XR 사용 안 함 (에디터에서는 XR Device Simulator 사용) |

- 노란 경고·빨간 오류가 있으면 **XR Plug-in Management > Project Validation > Fix All**
- Render Mode(OpenXR): 기본 **Single Pass Instanced**. 3DGS가 한쪽 눈에만 보이거나 어긋나면 **Multi Pass**로 바꿔 비교 (1주차 팀원 확인 항목)

### 5.4 Quality / URP
| 항목 | 값 |
|---|---|
| 사용할 URP Asset | 템플릿 기본 PC용 URP Asset 사용 (Android에서는 Mobile용 Asset) |
| URP Renderer | **`GaussianSplatURPFeature` 추가** (팀원) |
| Render Graph Compatibility Mode | **꺼진 상태 유지** (3DGS 플러그인 요구사항) |
| 스플랫이 위아래 뒤집혀 보이면 | URP Asset의 **HDR 끄기**, Renderer의 **Intermediate Texture = Auto** |
| VSync / 목표 프레임 | VR 런타임이 관리, 코드에서 `Application.targetFrameRate`를 강제하지 않음 |

### 5.5 Tags and Layers
| Layer 번호 | 이름 |
|---|---|
| 6 | `Wall` |
| 7 | `Floor` |
| 8 | `Furniture` |
| 9 | `Fixture` |

- 🤖 코드에서 Layer는 `Core/Layers.cs` 상수로만 참조한다. (`LayerMask.NameToLayer("Wall")` 문자열을 여기저기 직접 쓰지 않는다)

---

## 6. 폴더·Git 초기화

### 6.1 🤖 에이전트가 해도 되는 것
- `00_방꾸_전체개발계획.md` 6장의 폴더 구조 생성 (`Assets/_Project/...`). 빈 폴더는 Git에 안 올라가므로 각 폴더에 `.gitkeep` 추가
- `.gitignore`: Unity 공식 템플릿 내용 + `.DS_Store`
- `.gitattributes`: 전체개발계획 9장의 LFS 설정 + 아래 Unity YAML 설정
  ```
  *.unity merge=unityyamlmerge eol=lf
  *.prefab merge=unityyamlmerge eol=lf
  *.asset merge=unityyamlmerge eol=lf
  *.meta merge=unityyamlmerge eol=lf
  *.cs text eol=lf
  *.json text eol=lf
  *.md text eol=lf
  ```
- `README.md`: 개발 환경(확정된 Unity 패치 번호, 플러그인 버전), 실행 방법, 문서 위치
- `Assets/_Project/Scripts/` 아래 Assembly Definition은 **만들지 않는다** (4주 프로젝트라 설정 비용이 더 큼)

### 6.2 👤 사람이 하는 것
- `git init`, GitHub 저장소 연결, `git lfs install`
- 에디터에서 폴더가 보이는지, `.meta` 파일이 생겼는지 확인 후 커밋

---

## 7. 맥에서 VR 없이 테스트하는 설정 👤
1. XR Interaction Toolkit Samples에서 **XR Device Simulator** 임포트
2. 테스트 씬에 **XR Origin (XR Rig)** 프리팹(Starter Assets)과 **XR Device Simulator** 프리팹 배치
3. Play → 키보드·마우스로 머리/컨트롤러 이동 확인
4. 데스크톱 모드(`DesktopController`)는 XR 초기화가 안 되면 자동으로 켜지도록 팀장이 구현

## 8. Quest 단독 APK 테스트 👤 (선택, 박스·메쉬 가구만)
1. Quest 개발자 모드 활성화 (Meta Horizon 앱)
2. 맥에 **Meta Quest Developer Hub** 설치, 헤드셋 USB 연결
3. File > Build Profiles > **Android**로 전환 → Build → APK를 MQDH로 설치
4. 3DGS 가구는 Quest 단독에서 보장되지 않으므로 이 경로로 확인하지 않는다

## 9. Windows VR PC 설정 👤
1. 같은 Unity 6.3 LTS 패치 + Windows Build Support 설치
2. **Meta Horizon Link(Quest Link) 앱** 설치, 헤드셋 연결, OpenXR 런타임을 Meta로 설정
3. Graphics API가 **Direct3D12**인지 확인
4. 금요일 통합 테스트·성능 측정·최종 빌드를 이 PC에서

---

## 10. 첫날 체크리스트

**팀장 (맥)**
- [ ] 👤 Unity 6.3 LTS 설치, 패치 번호 결정
- [ ] 👤 Universal 3D로 `Bangkku` 생성 (한글 없는 경로)
- [ ] 👤 4.1 패키지 설치 (3DGS 플러그인 제외)
- [ ] 👤 5장 Project Settings, Layer 등록
- [ ] 🤖 6.1 폴더 구조, `.gitignore`, `.gitattributes`, README
- [ ] 👤 Git 초기화, LFS, GitHub 푸시
- [ ] 👤 XR Device Simulator로 빈 씬에서 이동 확인
- [ ] 🤖 `CLAUDE.md`, `docs/` 커밋

**팀원**
- [ ] 👤 같은 Unity 패치 설치, clone 후 프로젝트 열림 확인
- [ ] 👤 3DGS 플러그인 설치, URP Renderer에 `GaussianSplatURPFeature` 추가
- [ ] 👤 샘플 스플랫이 에디터에 보이는지 확인, 플러그인 버전 README 기록
- [ ] 👤 Windows VR PC 담당이면 9장 설정

**공통 확인 ✅**
- [ ] 두 사람의 `ProjectSettings/ProjectVersion.txt`가 같음
- [ ] clone 직후 열었을 때 콘솔 빨간 오류 없음
- [ ] 한 사람이 커밋한 씬을 다른 사람이 열었을 때 Missing 참조 없음 (`.meta` 누락 여부 확인)

---

## 11. 에이전트에게 첫 작업을 줄 때 (예시)
```
CLAUDE.md와 docs/03_방꾸_Unity_프로젝트설정.md를 읽어줘.
나는 팀장이고, 👤 표시된 작업(프로젝트 생성, 패키지 설치, Project Settings)은 끝냈어.
6.1에 있는 🤖 작업만 진행해줘:
- 전체개발계획 6장 폴더 구조 + .gitkeep
- .gitignore, .gitattributes, README.md
패키지나 ProjectSettings는 건드리지 말고, 끝나면 내가 에디터에서 확인할 항목을 알려줘.
```
