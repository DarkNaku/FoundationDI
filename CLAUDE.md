# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## 프로젝트 개요

FoundationDI는 DarkNaku의 DI(의존성 주입) 기반 Unity 게임 개발 파운데이션 패키지입니다. VContainer를 코어로 MessagePipe, R3, UniTask, Addressables를 조합한 공통 서비스 계층을 제공합니다.

- **Unity 버전**: 6000.3.17f1 (`ProjectSettings/ProjectVersion.txt`)
- **배포 형태**: UPM 패키지 (`Assets/FoundationDI/` = `com.darknaku.foundationdi`). 즉 이 리포지토리는 패키지 개발용 호스트 프로젝트이며, 재사용 코드는 모두 `Assets/FoundationDI/` 안에 있어야 한다. `Assets/Scripts/`는 패키지를 시험하는 호스트 프로젝트 전용 코드다.

## 개발 워크플로 (중요)

이 리포지토리는 **Kent Beck의 TDD + Tidy First 원칙**을 엄격히 따른다. `plan.md`가 작업의 단일 소스이며, `.claude/commands/`의 커스텀 슬래시 명령으로 사이클을 진행한다.

- `plan.md`: `[ ]` 미완료 / `[x]` 완료 테스트 목록. 다음 작업은 항상 첫 번째 미완료 항목이다.
- `/go` (RED): plan.md의 다음 미완료 테스트에 대한 **실패하는 테스트만** 작성. 프로덕션 코드는 건드리지 않는다.
- `/green` (GREEN): 테스트를 통과시키는 **최소 코드만** 작성. 하드코딩도 허용, 리팩토링 금지.
- `/refactor` (REFACTOR): 테스트가 통과하는 상태에서 한 번에 하나씩 구조 개선.
- `/commit`: 모든 테스트 통과 시에만 커밋.
- `/status`, `/help`: 진행 상황 / 명령어 도움말.

규칙:
- **테스트 함수 이름은 한국어로**, `should~` 형식 (예: `shouldReturnNullWhenGameObjectDestroyed`는 한국어 의도로 작성).
- **구조적 변경(STRUCTURAL)과 행동적 변경(BEHAVIORAL)을 절대 같은 커밋에 섞지 않는다.** 둘 다 필요하면 구조 변경을 먼저, 별도 커밋으로. 커밋 메시지 제목에 `[STRUCTURAL]` 또는 `[BEHAVIORAL]` 접두어를 단다 (`.claude/commands/commit.md` 참고).
- 한 번에 하나의 테스트만 작성하고, 매번 (장시간 테스트 제외) 전체 테스트를 돌린다.

## 빌드 / 컴파일 / 테스트

Unity 프로젝트이므로 CLI 빌드 명령은 없다. **모든 컴파일·테스트는 UnityMCP(MCP 서버)를 통해 수행한다.** Unity Editor가 떠 있고 `.mcp.json`의 `http://127.0.0.1:8086/mcp`에 연결되어 있어야 한다.

- 스크립트 생성/수정 후에는 `read_console`로 **컴파일 에러를 먼저 확인**한다. 컴파일이 끝나야(`editor_state.isCompiling == false`) 새 타입을 쓸 수 있다.
- 테스트는 Unity Test Framework로 실행한다: UnityMCP의 `run_tests` 사용 (EditMode/PlayMode).
- 모킹은 **NSubstitute 5.3.0** (`Assets/Packages/`, NuGetForUnity로 관리)을 사용한다.
- 테스트 코드는 아직 없다. 추가 시 Tests용 asmdef를 만들고 `FoundationDI` 런타임 asmdef와 NSubstitute/NUnit을 참조해야 한다.

NuGet 의존성은 **NuGetForUnity**(`Assets/NuGet/`)가 `Assets/packages.config`로 관리하며, `Assets/Packages/`에 풀린다. UPM 의존성은 `Packages/manifest.json`에 있다 (VContainer, MessagePipe, R3, UniTask, Director 등은 git URL로 참조).

## 아키텍처

### DI 컴포지션 루트
`Assets/Scripts/LifetimeScopes/RootLifetimeScope.cs`가 VContainer의 `LifetimeScope`를 상속한 루트 스코프다. `RootLifetimeScope.prefab`으로 씬에 배치되며, `Configure(IContainerBuilder)`에서 서비스를 등록한다. 새 서비스는 인터페이스(`IXxxService`)로 등록하여 생성자 주입으로 소비한다.

### 핵심 서비스 (`Assets/FoundationDI/Runtime/`)
모든 런타임 코드는 단일 asmdef `FoundationDI`(`Runtime/FoundationDI.asmdef`)에 들어간다.

- **MessageService** (`Services/MessageService.cs`): MessagePipe 래퍼. `IObjectResolver`로 `IPublisher<T>`/`ISubscriber<T>`를 지연 해석해 `ConcurrentDictionary`에 캐싱. 동기/비동기(UniTask) pub-sub 제공.
- **UIManager** (`Managers/UIManager/`): uGUI 기반 UI 시스템. 네임스페이스 `DarkNaku.FoundationDI`.
  - **빌더 API**: `uiManager.Page<TPresenter>()` / `Popup<TPresenter>()` / `Overlay<TPresenter>()` → 인스턴스 즉시 반환 + Show 자동 enqueue (`.Show()` 별도 호출 불필요) → 같은 프레임 내 `.With(params)` / `.OnShown(...)` / `.WithTransition(...)` 동기 체인.
  - **표시 모드**: Presenter 타입으로 컴파일 타임 고정 — `UIPagePresenter<TView>`(단일 교체) / `UIPopupPresenter<TView>`(LIFO 스택) / `UIOverlayPresenter<TView>`(Above/Below 상주). View 공통 기반 `UIView : MonoBehaviour`.
  - **`ShowQueue`**: 모든 Show/Hide 전환을 단일 큐로 순차 직렬화 → race 조건 제거.
  - **prefab 매핑**: `[UIPrefab("키")]` 속성을 Presenter 타입에 부착. `IUIAssetLoader`(기본 `ResourcesUILoader`, 옵션 `AddressablesUILoader`)로 로드.
  - **`InstanceCache`**: Hide 후 인스턴스를 타입 키로 보관·재사용. 다음 Show 시 `OnInitialize` 생략.
  - **트랜지션**: `IUITransition` 추상화 + 기본 3종(`FadeTransitionAsset`/`ScaleTransitionAsset`/`SlideTransitionAsset`) ScriptableObject. 트윈 라이브러리 비의존 — UniTask 자체 보간(`AnimationCurve` 인스펙터 커스터마이즈). 폴백 `NoopTransition`(즉시). 해석 우선순위: 빌더 오버라이드 > `UIView` 인스펙터 > Noop.
  - **DI 등록**: `builder.RegisterUIManager(settings)` 확장 메서드(루트 `LifetimeScope` 에서 호출). Presenter/View는 `_container.Inject()`로 의존성 주입.
- **PoolService** (`Services/PoolService/`): 키 기반 GameObject 오브젝트 풀. `Resources.Load` 우선, 실패 시 Addressables fallback으로 프리팹을 로드(`Load()`). `ObjectPool<IPoolItem>` 기반이며 `PoolData`가 풀+Addressables 핸들을, `PoolItem`(MonoBehaviour)이 풀 항목 생명주기 콜백(`OnGetItem`/`OnReleaseItem` 등)과 지연 반환(`Release(delay)`)을 담당. **현재 `plan.md`의 활성 개선 대상**(crash/thread-safety/null-safety).
- **SoundService** (`Services/SoundService/`): SFX/BGM 재생, `PlayerPrefs` 볼륨 영속화, R3 `Observable.EveryUpdate(PostLateUpdate)`로 프레임당 중복 SFX 방지. 클립 로드도 Resources→Addressables fallback.

공통 패턴: 각 서비스는 `Resources.Load<T>()`를 먼저 시도하고 실패 시 `Addressables.LoadAssetAsync<T>().WaitForCompletion()`으로 폴백한 뒤 핸들을 보관해 두었다가 dispose 시 해제한다.

### 네임스페이스
런타임 코드는 `DarkNaku.FoundationDI` 단일 네임스페이스로 통일한다(UIManager 리뉴얼로 구 `FoundationDI` 네임스페이스는 제거됨). 새 코드를 추가할 때 같은 디렉터리의 기존 파일이 쓰는 네임스페이스를 따른다.

### 기타 의존성
PrimeTween(트위닝, tgz로 로컬 설치), Director(DarkNaku의 씬/플로우 라이브러리), Input System, URP 2D가 구성되어 있다.
