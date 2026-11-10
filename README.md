# 방꾸 (Bangkku)

VR 기반 가구 배치 시뮬레이션. 가상현실 과목 Term Project.
평면도를 3D 공간으로 만들고, 3DGS로 만든 가구를 VR에서 배치하며 겹침·간격·공간감을 미리 확인한다.

- 팀장 이동규: 평면도·VR·판정·통합
- 팀원 김효진: 3DGS 가구·성능·평가

## 개발 환경

| 항목 | 값 |
|---|---|
| Unity | **6000.3.27f2** (Unity 6.3 LTS). 두 사람 모두 이 패치로 설치 |
| 렌더 파이프라인 | URP 17.3.0 |
| XR Plugin Management | 4.6.1 |
| OpenXR Plugin | 1.16.1 |
| XR Interaction Toolkit | 3.3.2 |
| Input System | 1.20.1 |
| Newtonsoft Json | 3.2.2 |
| 3DGS 플러그인 | aras-p/UnityGaussianSplatting `미정` (팀원이 설치 후 릴리스 태그 또는 커밋 기록) |
| Windows VR PC 담당 | `미정` |

Unity Hub 모듈: Android Build Support (OpenJDK, Android SDK & NDK Tools 포함). Windows VR PC에서는 Windows Build Support도 설치.

## 실행 방법

1. Git LFS 설치 후 clone
   ```bash
   git lfs install
   git clone <저장소 URL>
   ```
2. Unity Hub > Add > clone한 폴더 선택 → 버전이 **6000.3.27f2**인지 확인하고 연다.
3. 처음 열 때 패키지 설치와 임포트에 시간이 걸린다. 끝난 뒤 Console에 빨간 오류가 없어야 한다.
4. 맥(VR 없음): 테스트 씬에서 XR Device Simulator로 Play. 자세한 내용은 `docs/03_방꾸_Unity_프로젝트설정.md` 7장.
5. PC VR: Meta Horizon Link 연결 후 Play. `docs/03_방꾸_Unity_프로젝트설정.md` 9장.

Project Settings는 저장소에 커밋되어 있으므로 clone한 사람은 따로 설정하지 않는다.
설정이 문서와 어긋났을 때는 `Tools > 방꾸 > 프로젝트 설정 적용`으로 5.1 Editor, 5.2 Player, 5.5 Layer를 다시 맞출 수 있다 (XR Plug-in Management, URP는 제외).

## 문서

| 문서 | 내용 |
|---|---|
| [docs/00_방꾸_전체개발계획.md](docs/00_방꾸_전체개발계획.md) | 공통 규칙 (좌표·단위·이름·Layer), 폴더·소유자, 데이터 형식, 협업 방법 |
| [docs/01_방꾸_팀장_이동규.md](docs/01_방꾸_팀장_이동규.md) | 팀장 가이드 |
| [docs/02_방꾸_팀원_김효진.md](docs/02_방꾸_팀원_김효진.md) | 팀원 가이드 |
| [docs/03_방꾸_Unity_프로젝트설정.md](docs/03_방꾸_Unity_프로젝트설정.md) | Unity 버전, 패키지, Project Settings |
| [docs/04_방꾸_공용코드_안내.md](docs/04_방꾸_공용코드_안내.md) | 공용 코드(Core·Data) 사용법, 팀원 공지, 변경 기록 |
| [docs/05_방꾸_진행현황.md](docs/05_방꾸_진행현황.md) | 진행 현황, 리뷰·결정 대기, 다음 작업 순서 |
| [docs/06_방꾸_Windows_세팅_데스크톱테스트.md](docs/06_방꾸_Windows_세팅_데스크톱테스트.md) | Windows VR PC 세팅, 데스크톱 모드·빌드 테스트, 3DGS 로컬 테스트 |
| [docs/07_방꾸_VR_테스트.md](docs/07_방꾸_VR_테스트.md) | Quest + Link 연결, VR 기본·3DGS 테스트, FPS 측정, 되돌리기·기록 |
| [CLAUDE.md](CLAUDE.md) | Claude Code 작업 규칙 |

## 폴더

프로젝트 코드와 에셋은 모두 `Assets/_Project/` 아래에 둔다. 구조와 파일 소유자는 전체개발계획 6장을 따른다.

## Git

- 브랜치: `main`(안정된 버전만 병합) ← `dev`(통합) ← `feature/<담당>-<기능>`
- `.meta` 파일은 항상 함께 커밋한다.
- 큰 파일(`.ply`, `.splat`, `.glb`, `.fbx`, `.png`, `.jpg`, `.psd`)은 Git LFS로 관리한다. 원본 영상·원본 `.ply`는 구글 드라이브 `방꾸/raw/`에 둔다.

### (선택) UnityYAMLMerge 등록

`.gitattributes`가 씬·프리팹·에셋 병합에 `unityyamlmerge` 드라이버를 지정해 두었다. 등록하지 않으면 Git 기본 병합을 쓴다. 등록하려면 (맥 기준):

```bash
git config merge.unityyamlmerge.name "Unity SmartMerge"
git config merge.unityyamlmerge.driver "'/Applications/Unity/Hub/Editor/6000.3.27f2/Unity.app/Contents/Helpers/UnityYAMLMerge' merge -p %O %B %A %A"
git config merge.unityyamlmerge.recursive binary
```

Windows는 Unity 설치 폴더(`C:\Program Files\Unity\Hub\Editor\6000.3.27f2\Editor\Data\` 아래)에서 `UnityYAMLMerge.exe` 위치를 확인해 경로를 바꾼다.
