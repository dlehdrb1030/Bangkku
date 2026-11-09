# 방꾸: Windows VR PC 세팅과 데스크톱 테스트

> 맥에서 만든 것이 **Windows VR PC에서도 똑같이 동작하는지** 확인하는 순서입니다. 헤드셋은 아직 연결하지 않습니다.
> VR 연결은 다음 문서 `07_방꾸_VR_테스트.md`에서 합니다.
>
> | 단계 | 내용 | 커밋 |
> |---|---|---|
> | 0. 준비 | Unity·Git 설치, clone, 프로젝트 열기 | 없음 |
> | 1-A. 우리 작업 확인 | 3DGS 없이 데스크톱 모드 + Windows 빌드 | 없음 |
> | 1-B. 3DGS 가구 추가 | 플러그인 설치, 테스트 스캔 표시 | **하지 않음** (로컬에서만) |
>
> 1-A에서 막히면 1-B로 넘어가지 말고 먼저 원인을 찾습니다. 문제가 생기면 화면과 Console 로그를 남겨 둡니다 (마지막 "기록 양식").
> 기준: Unity **6000.3.27f2**, 플러그인 3DGS **v2.0.0** (2026-11-10 기준)

---

## 0. 준비

### 0.1 설치할 것

| 프로그램 | 받는 곳 / 방법 | 비고 |
|---|---|---|
| Git for Windows | git-scm.com | **Git LFS가 같이 설치됨**. 설치 옵션은 기본값 |
| Unity Hub | unity.com/download | |
| Unity **6000.3.27f2** | Unity Hub > Installs > Install Editor. 목록에 없으면 **Archive** 탭 → "download archive" 페이지에서 6000.3.27f2의 "Unity Hub" 버튼 | 다른 6.3 패치를 쓰지 않습니다 (두 사람 같은 버전) |
| Unity 모듈 | 설치할 때 기본값 그대로 | Windows 빌드(Mono)는 Windows용 에디터에 기본 포함. IL2CPP·Android 모듈은 이번 테스트에 필요 없음 |
| (2단계용) Meta Horizon Link | meta.com 에서 PC 앱 설치 | 07 문서에서 사용. 미리 깔아 둬도 됨 |

> GPU 드라이버를 최신으로 올려 둡니다 (NVIDIA/AMD 공식 사이트). DX12와 VR 모두 드라이버 영향이 큽니다.

### 0.2 저장소 받기

명령 프롬프트(cmd) 또는 PowerShell에서:

```bash
git lfs install
```

```bash
git clone -b dev https://github.com/dlehdrb1030/Bangkku.git C:\Projects\Bangkku
```

- **경로에 한글·공백이 없어야** 합니다 (03 문서 3장). `C:\Projects\Bangkku`를 추천합니다. 바탕화면·문서 폴더처럼 사용자 이름이 한글인 경로는 피합니다.
- **`dev` 브랜치**로 받습니다. `main`은 PR #26까지라 최근 작업(평면도 3종, 판정, 메뉴, `Main_VR` 등)이 없습니다.
- 확인:
  ```bash
  cd C:\Projects\Bangkku
  git branch --show-current
  git lfs ls-files
  ```
  - 첫 줄이 `dev`이면 됩니다.
  - `git lfs ls-files`에 `.png` 파일들(텍스처, 창밖 파노라마)이 나오면 LFS가 정상입니다. 이 파일들이 1KB 정도의 텍스트로 받아졌다면 `git lfs pull`을 실행합니다.

### 0.3 프로젝트 열기

1. Unity Hub > Projects > **Add > Add project from disk** → `C:\Projects\Bangkku` 선택
2. 목록에서 Editor Version이 **6000.3.27f2**인지 확인하고 엽니다
   - 다른 버전으로 열겠냐고 물으면 **취소**하고 6000.3.27f2를 설치합니다 (`ProjectSettings/ProjectVersion.txt`가 바뀌면 안 됨)
3. 처음 열 때는 패키지 설치와 임포트로 **10~30분** 걸릴 수 있습니다

### 0.4 열린 뒤 확인

| 확인 | 정상 | 아니면 |
|---|---|---|
| 에디터 제목 표시줄 끝 | **`<DX12>`** | Edit > Project Settings > Player > Windows 탭 > Other Settings > Graphics APIs 맨 위가 Direct3D12인지 확인 (저장소에 이미 D3D12만 들어 있음) |
| Console | 빨간 오류 없음 | 04 문서 1.3의 무시해도 되는 로그(`NoSubscription`, `Licensing`)는 제외. 그 밖의 오류는 기록 |
| Console의 `[방꾸] Layer ...` 경고 | 없음 | `Tools > 방꾸 > 프로젝트 설정 적용` 실행 |
| Project 창 `Assets/_Project/Scenes/` | `Main_VR`, `Test_FloorPlan`, `Test_Interaction`, `Test_Desktop` | `dev`가 아닌 브랜치로 받은 것 |
| `Assets/_Project/Materials/Textures/` | 마루·타일·벽지·천장 텍스처가 보임 | LFS 문제 → `git lfs pull` |
| Edit > Project Settings > Quality | Windows(PC) 기본값이 **PC** | PC 품질이 `PC_RPAsset` → `PC_Renderer`를 씀 (1-B에서 여기에 3DGS 기능을 넣음) |

> ⚠️ 이 PC에서 테스트하는 동안 **씬·설정을 저장하거나 커밋하지 않습니다.** 평면도를 바꾸는 등 씬을 고친 뒤 창을 닫을 때는 **Don't Save**를 누릅니다.
> 실수로 바뀌었으면 `git status`로 확인하고 `git restore <파일>`로 되돌립니다.

---

## 1-A. 우리 작업 확인 (3DGS 없이, 헤드셋 연결 안 함)

> 헤드셋을 **연결하지 않은** 상태에서 합니다. 연결되어 있으면 VR 모드로 켜집니다.

### 1-A.1 Main_VR 실행

1. `Assets/_Project/Scenes/Main_VR.unity`를 엽니다
2. Hierarchy에서 `ModeSelector`를 눌러 Inspector의 **Without Headset = 데스크톱**인지 확인합니다 (VR 시뮬레이터면 안 됨)
3. Play
4. Console에 아래 두 줄이 나오면 정상입니다
   - `[방꾸] 실행 모드: 데스크톱 ...`
   - `[방꾸] 테스트 박스 가구 3개를 만들었습니다 (oneroom_02).`
5. 화면: 원룸 26A형을 위에서 내려다보고, 박스 가구 3개(소파·침대·책상)가 있습니다

### 1-A.2 평면도·외관

| 확인 | 정상 |
|---|---|
| 벽·문·창 | 벽 끊김 없음. 방·욕실·발코니 문은 뚫려 있고 **현관문만 닫힌 문짝**(짙은 회색, 안쪽 은색 손잡이) |
| 바닥·천장 | 방은 마루, 현관·욕실·발코니는 타일. 무늬가 늘어나거나 깨지지 않음 |
| 마감재 | 걸레받이(벽 아래 흰 띠), 문틀·창틀, 창살 |
| 창밖 풍경 | 창으로 하늘·아파트·나무가 보임. 집이 떠 있어 보이지 않음 |
| 고정설비 | 주방(개수대·쿡탑), 신발장, 변기, 세면대 모양 |
| 그림자·조명 | 맥과 비슷한 밝기, 깨진 그림자 없음 |

**평면도 바꾸기 (투룸 36형)**
1. Play를 멈춤
2. Hierarchy `FloorPlan` → Inspector `FloorPlanBuilder`의 **Floor Plan Json**에 `Assets/_Project/Data/FloorPlans/tworoom_01`을 끌어 놓음
3. Play → Console `... (tworoom_01)`, 방 2개짜리 집, 침대는 침실2
4. 확인 후 Play를 멈추고 **Floor Plan Json을 `oneroom_02`로 되돌림** (또는 씬을 저장하지 않고 닫음)

### 1-A.3 조작 (데스크톱 모드)

| 입력 | 동작 |
|---|---|
| 좌클릭 | 가구 선택 / 빈 곳 클릭은 선택 해제 |
| 좌클릭 드래그 | 바닥 위로 이동. 10mm 격자, 벽 30mm 안이면 벽에 붙음 |
| **Alt** + 드래그 | 스냅 없이 이동 |
| Q / E | -90° / +90° 회전 |
| Shift + Q / E | 15° 회전 |
| 우클릭 누른 채 마우스 | 시점 회전 |
| 우클릭 누른 채 W A S D | 이동 (Shift로 빠르게) |
| 휠 | 확대·축소 |
| L | 신뢰도 라벨: 모든 가구 → 선택한 가구만 → 끄기 |
| Tab | 가구 메뉴 열기·닫기 |

### 1-A.4 판정·표시

| 해 볼 것 | 정상 |
|---|---|
| 가구를 방 가운데에 둠 | 윤곽선 **초록**(여유) |
| 벽에 붙임 (드래그해서 벽 쪽으로) | 벽에 딱 붙음, 거리 라벨 `벽까지 0mm`. 맞은편이 열려 있으면 초록, 양쪽이 좁으면 **노랑**(좁음) |
| 가구 두 개를 겹침 | 둘 다 윤곽선 **빨강** + 반투명 빨간 상자, Console에 겹침 로그 |
| 가구를 선택 | 가장 가까운 벽·가구·설비까지 선과 `○○까지 NNmm` 라벨 |
| 화면 왼쪽 아래 | 상태 패널: 이름·상태·치수·신뢰도·위치·가장 가까운 대상 |
| 가구 위 꼬리표 | `실측 ±10mm` / `도면 ±30mm` / `추정 ±100mm` |
| **한글** | 라벨·메뉴·상태 패널의 한글이 □나 ?로 깨지지 않음 (Windows 글꼴로 그려짐) |

### 1-A.5 가구 메뉴·저장

1. **Tab** → 화면 오른쪽 메뉴
2. 새 박스 가구: 이름 `테스트`, W `800` D `600` H `700`, 신뢰도 `실측` → 추가 → 화면 가운데 바닥에 생기고 선택됨
3. 배치된 가구 목록에서 하나를 눌러 선택 → 삭제
4. 가구 몇 개를 옮긴 뒤 **배치 저장**
   - 파일: `%USERPROFILE%\AppData\LocalLow\Bangkku\방꾸\layouts\oneroom_02.json` (탐색기 주소창에 `%USERPROFILE%\AppData\LocalLow\Bangkku`를 넣으면 열림)
5. 가구를 다시 옮긴 뒤 **배치 불러오기** → 저장한 위치·회전으로 돌아옴 (새로 만든 박스 가구도 다시 생김)

### 1-A.6 Windows 빌드

1. **File > Build Profiles**
2. 왼쪽 Platforms에서 **Windows** 선택. 활성 플랫폼이 아니면 **Switch Platform** (처음 한 번 시간이 걸림)
3. 확인
   - **Scene List**: `Assets/_Project/Scenes/Main_VR`만 체크 (`SampleScene`은 체크 해제 상태 그대로)
   - Architecture: **x86_64**
   - Development Build: 끔 (2단계 성능 측정 때 켬)
4. **Build** → 폴더는 `C:\Projects\Bangkku\Build\Windows` (`Build/`는 `.gitignore`에 있어 커밋되지 않음)
5. 만들어진 `방꾸.exe`(제품 이름)를 실행
   - 전체 화면 창으로 켜짐. 끄기는 **Alt + F4**
   - 1-A.1~1-A.5를 빠르게 다시 확인 (특히 한글, 저장·불러오기)
   - 실행 로그: `%USERPROFILE%\AppData\LocalLow\Bangkku\방꾸\Player.log` (오류가 있으면 이 파일을 남김)

> ⚠️ Switch Platform을 하면 `ProjectSettings/`가 바뀔 수 있습니다. 테스트가 끝나면 `git status`로 확인하고 커밋하지 않습니다. 필요한 변경이면 맥에서 따로 정리합니다.

### 1-A 통과 기준

- [ ] 에디터 `<DX12>`, Console 빨간 오류·경고 없음
- [ ] `oneroom_02`, `tworoom_01` 모두 맥과 같은 모습
- [ ] 조작·판정·라벨·메뉴·저장이 맥과 같음
- [ ] 한글이 깨지지 않음
- [ ] exe 빌드에서도 같음

---

## 1-B. 3DGS 가구 추가 (커밋하지 않고 로컬에서만)

> 플러그인 정식 설치는 **팀원 작업**입니다 (04 문서 4.7). 여기서는 "Windows DX12에서 3DGS가 보이는가"만 확인하고 끝나면 되돌립니다.
> 1-A가 통과한 뒤에 합니다.

### 1-B.1 테스트 스캔 받기

팀원 `hj` 브랜치의 `Models/` 폴더에 있는 파일 2개입니다.

| 파일 | 크기 | 스플랫 수 |
|---|---|---|
| `office_chair_clean_unscaled.ply` | 14.9MB | 62,922 |
| `rect_desk_planar_unscaled.ply` | 30.0MB | 127,280 |

받는 방법 (둘 중 하나):
- **GitHub 웹**: 저장소 > 브랜치 `hj` > `Models` > 파일 > 오른쪽 위 **Download raw file**
- **맥에서 복사**: 맥의 `~/Downloads/splat_test/`에 같은 파일이 있음 (USB·클라우드로 옮김)

> ⚠️ 프로젝트를 `hj` 브랜치로 **체크아웃하지 않습니다.** `hj`는 `main`(PR #26)에서 딴 브랜치라 최근 작업이 모두 빠집니다.

### 1-B.2 플러그인 설치 (로컬)

1. Unity에서 **Window > Package Manager**
2. 왼쪽 위 **+** > **Install package from git URL...**
3. 입력:
   ```
   https://github.com/aras-p/UnityGaussianSplatting.git?path=/package#v2.0.0
   ```
4. 목록에 **Gaussian Splatting 2.0.0**이 보이면 완료
   - 실패하면: GitHub `aras-p/UnityGaussianSplatting` > Releases > **v2.0.0**의 Source code(zip)를 받아 압축을 풀고, **+ > Install package from disk...** → 풀린 폴더의 `package/package.json` 선택
   - Git URL 설치가 Git 오류를 내면 Git for Windows가 PATH에 있는지 확인 (Unity를 다시 시작)

### 1-B.3 URP 설정 (로컬)

1. Project 창에서 `Assets/Settings/PC_Renderer` 선택
2. Inspector 맨 아래 **Add Renderer Feature** > **Gaussian Splat URP Feature**
3. **Edit > Project Settings > Graphics**에 Render Graph의 "Compatibility Mode" 항목이 있으면 **꺼진 상태**인지 확인 (플러그인 요구사항. 항목이 없으면 넘어감)

### 1-B.4 씬에 놓기

1. Project 창 `Assets`에서 우클릭 > Create > Folder → 이름 **`StreamingAssets`**
2. 1-B.1의 `.ply` 2개를 탐색기에서 이 폴더로 끌어 놓음
3. `Main_VR` 씬을 열고 (저장하지 않을 것)
4. Hierarchy 빈 곳 우클릭 > **Create Empty** → 이름 `Splat_Chair`
5. Inspector > **Add Component** > `Gaussian Splat Renderer`
6. **PLY / SPZ** 칸에 `office_chair_clean_unscaled`를 끌어 놓음 → Inspector 아래 Splats `62,922`가 보이면 읽힌 것
7. Transform 입력

   | 오브젝트 | Position | Rotation | Scale |
   |---|---|---|---|
   | `Splat_Chair` | X **2.0**, Y **0**, Z **-5.0** | X **90**, Y 0, Z 0 | 1, 1, 1 |
   | `Splat_Desk` (책상 파일로 같은 방법) | X **2.9**, Y **0**, Z **-4.6** | X **90**, Y 0, Z 0 | 1, 1, 1 |

   - Rotation X = 90: 이 스캔은 위쪽이 -Z라서 세움 (11-09 맥 테스트로 확인). 책상이 뒤집혀 보이면 X = **-90**
   - Position (0, 0, 0)은 평면도 원점(바깥 벽 모서리)이라 방 밖입니다. 위 값은 `oneroom_02` 거실의 빈 자리
   - 스캔 원점이 바닥면 중앙이 아니라서 몇 cm~수십 cm 어긋날 수 있음. 눈으로 맞춤
8. Play (데스크톱 모드)

> 이 스플랫은 `FurnitureItem`이 아니라서 **선택·이동·판정이 되지 않습니다.** 겉모습과 성능만 봅니다. 크기도 아직 실측 보정 전입니다 (의자 높이 약 0.9m, 책상 약 0.55m).

### 1-B.5 확인

| 확인 | 정상 | 아니면 |
|---|---|---|
| 보이는지 | Scene 뷰·Game 뷰 모두에서 의자·책상이 보임 | URP Feature가 `PC_Renderer`에 들어갔는지, Quality가 PC인지, 제목 표시줄 `<DX12>`인지 |
| 위아래 | 뒤집히지 않음 | 03 문서 5.4: `Assets/Settings/PC_RPAsset`의 **HDR 끄기**, `PC_Renderer`의 Intermediate Texture = **Auto** |
| 방향 | 바퀴(다리)가 바닥 쪽 | Rotation X를 반대 부호로 |
| 깜빡임 | 박스 가구나 다른 스플랫 가까이 둬도 깜빡이지 않음 | 기록 (전체개발계획 11장 "스플랫 간 정렬 깜빡임") |
| Console | 빨간 오류 없음 | 오류 문구 기록 |
| FPS | Game 뷰 오른쪽 위 **Stats** → FPS 값 기록 (스플랫 0개 / 의자만 / 둘 다) | 참고값. 기준(72fps)은 VR에서 잼 |
| exe 빌드 | 1-A.6처럼 빌드해 exe에서도 보임 (`StreamingAssets` 폴더가 빌드에 자동으로 들어감) | `방꾸_Data\StreamingAssets\`에 `.ply`가 있는지 |

### 1-B 통과 기준

- [ ] DX12 에디터에서 의자·책상 표시, 뒤집힘·깜빡임 없음
- [ ] Console 오류 없음
- [ ] exe 빌드에서도 표시
- [ ] FPS 기록

### 1-B.6 2단계로 갈 때

- **VR 테스트(07 문서 2-B)도 이 상태에서 하므로 아직 되돌리지 않습니다.**
- VR까지 끝난 뒤 07 문서 "3. 끝나고 할 일"의 되돌리기 순서를 따릅니다.
- 2단계를 바로 하지 않는다면 지금 되돌립니다:
  1. Unity를 닫음 (씬은 **Don't Save**)
  2. 명령 프롬프트에서
     ```bash
     cd C:\Projects\Bangkku
     git restore Packages/manifest.json Packages/packages-lock.json Assets/Settings/PC_Renderer.asset Assets/Settings/PC_RPAsset.asset
     rmdir /s /q Assets\StreamingAssets
     del Assets\StreamingAssets.meta
     git status
     ```
  3. `git status`에 남은 변경이 없으면 끝 (`ProjectSettings/`가 남아 있으면 07 문서 3장 참고)

---

## 기록 양식

테스트하면서 아래 표를 채워 두면 맥에서 고칠 때와 docs/05 기록에 씁니다.

**환경**

| 항목 | 값 |
|---|---|
| 날짜 | |
| PC (CPU / GPU / RAM) | |
| GPU 드라이버 버전 | |
| Unity / Graphics API | 6000.3.27f2 / DX12 |
| 저장소 커밋 (`git log --oneline -1`) | |
| 3DGS 플러그인 | v2.0.0 |

**결과**

| 단계 | 항목 | 결과 (✅ / ❌) | 메모 (오류 문구, 스크린샷 파일명) |
|---|---|---|---|
| 0 | 프로젝트 열림, Console | | |
| 1-A | Main_VR 데스크톱 모드 | | |
| 1-A | 평면도 2종 외관 | | |
| 1-A | 조작 | | |
| 1-A | 판정·라벨·상태 패널 | | |
| 1-A | 한글 | | |
| 1-A | 메뉴·저장·불러오기 | | |
| 1-A | exe 빌드 | | |
| 1-B | 플러그인 설치 | | |
| 1-B | 3DGS 표시 (에디터) | | |
| 1-B | 3DGS 표시 (exe) | | |

**FPS (데스크톱, Stats)**

| 상태 | 에디터 FPS | exe FPS |
|---|---|---|
| 스플랫 없음 | | |
| 의자 (6.3만) | | |
| 의자 + 책상 (19만) | | |
