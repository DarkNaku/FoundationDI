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
- 테스트는 `Assets/FoundationDI/Tests/`의 `FoundationDI.Tests`(EditMode) asmdef에 있다. `FoundationDI` 런타임 asmdef와 NSubstitute/NUnit을 참조한다.

NuGet 의존성은 **NuGetForUnity**(`Assets/NuGet/`)가 `Assets/packages.config`로 관리하며, `Assets/Packages/`에 풀린다. UPM 의존성은 `Packages/manifest.json`에 있다 (VContainer, MessagePipe, R3, UniTask, Director 등은 git URL로 참조).

## 아키텍처

### DI 컴포지션 루트
`Assets/Scripts/LifetimeScopes/RootLifetimeScope.cs`가 VContainer의 `LifetimeScope`를 상속한 루트 스코프다. `RootLifetimeScope.prefab`으로 씬에 배치되며, `Configure(IContainerBuilder)`에서 서비스를 등록한다. 새 서비스는 인터페이스(`IXxxService`)로 등록하여 생성자 주입으로 소비한다.

### 핵심 서비스 (`Assets/FoundationDI/Runtime/`)
모든 런타임 코드는 단일 asmdef `FoundationDI`(`Runtime/FoundationDI.asmdef`)에 들어간다.

- **MessageService** (`Services/MessageService.cs`): MessagePipe 래퍼. `IObjectResolver`로 `IPublisher<T>`/`ISubscriber<T>`를 지연 해석해 `ConcurrentDictionary`에 캐싱. 동기/비동기(UniTask) pub-sub 제공.
- **UIService** (`Services/UIService/`): uGUI 기반 UI 시스템. 네임스페이스 `DarkNaku.FoundationDI`.
  - **빌더 API**: `_ui.Page<TPresenter>()` / `Popup<TPresenter>()` / `Overlay<TPresenter>()` → 인스턴스 즉시 반환 + Show 자동 enqueue (`.Show()` 별도 호출 불필요) → 같은 프레임 내 `.WithParams(params)` / `.OnAfterShow(...)` / `.WithTransition(...)` / `.WithOverlay(...)` 동기 체인.
  - **표시 모드**: Presenter 타입으로 컴파일 타임 고정 — `UIPagePresenter<TView>`(단일 교체) / `UIPopupPresenter<TView>`(LIFO 스택) / `UIOverlayPresenter<TView>`(Popup 기준 Above/Below 상주). View 공통 기반 `UIView : MonoBehaviour`.
  - **`OperationQueue`**: 모든 Show/Hide 전환을 단일 큐로 순차 직렬화 → race 조건 제거.
  - **prefab 매핑**: `[UIPrefab("키")]` 속성을 Presenter 타입에 부착. 로딩은 `IResourceService`에 위임(Resources/Addressables 중 어느 쪽이든 등록된 `IResourceProvider`가 결정).
  - **Presenter는 매 표시마다 새로 생성**(인스턴스 캐시 없음, `OnInitialize` 재실행) — **View는 프리팹 키로 풀링**되어 재사용됨.
  - **상주 캔버스**: 루트는 `UIServiceSettings.RootPrefab`으로 지정한 프리팹을 인스턴스화하며(렌더 모드/`CanvasScaler`/레이어 구성은 프리팹이 결정), 미지정 시 `UIRoot.CreateDefault()`(ScreenSpaceOverlay/1920x1080)로 폴백한다. `DontDestroyOnLoad`로 앱 전체에 1개만 상주하며 씬 전환 시 자식 UI만 clear.
  - **트랜지션**: `IUITransition` 추상화 + 기본 3종 MonoBehaviour 컴포넌트(`FadeTransition`/`ScaleTransition`/`SlideTransition`, 공통 기반 `UITransitionBehaviour`). 트윈 라이브러리 비의존 — `Awaitable` 자체 보간(`AnimationCurve` 인스펙터 커스터마이즈). 폴백 `NoopTransition`(즉시). 해석 우선순위: 빌더 오버라이드 > View의 트랜지션 컴포넌트 > Noop.
  - **DI 등록**: `builder.RegisterUIService(settings)` 확장 메서드(루트 `LifetimeScope`에서, `IResourceService` 등록 이후에 호출). Presenter/View는 VContainer가 주입.
  - **에디터 도구**(`Assets/FoundationDI/Editor/UIService/`): `Tools/FoundationDI/UI/Create UI Root Prefab`(루트 프리팹 생성) · `Setup/Clear Prefab Editing Environment`(프리팹을 실제 캔버스 안에서 편집) · `Create UI Element...`(View/Presenter 스크립트 + 프리팹 생성 마법사).
  - 상세: `Assets/FoundationDI/Runtime/Services/UIService/README.md`.
- **PoolService** (`Services/PoolService/`): 키 기반 GameObject 오브젝트 풀. `Resources.Load` 우선, 실패 시 Addressables fallback으로 프리팹을 로드(`Load()`). `ObjectPool<IPoolItem>` 기반이며 `PoolData`가 풀+Addressables 핸들을, `PoolItem`(MonoBehaviour)이 풀 항목 생명주기 콜백(`OnGetItem`/`OnReleaseItem` 등)과 지연 반환(`Release(delay)`)을 담당. **현재 `plan.md`의 활성 개선 대상**(crash/thread-safety/null-safety).
- **SoundService** (`Services/SoundService/`): 태그 기반 오디오 시스템.
  - 공개 API는 `ISoundService` 하나. `CreateSound/CreateMusic/CreatePlaylist/CreateDynamicMusic` 팩토리로 빌더를 만들고 체이닝 후 `Play()`. 빌더가 쓰는 내부 seam은 `ISoundEngine`(internal).
  - `SoundSource`(MonoBehaviour)를 `[SoundService] Sources Pool` 아래에 풀링. 페이드·루프·플레이리스트 진행·오클루전을 담당.
  - 데이터는 `SoundServiceSettings`(SO) 하나가 `SoundDataCollection`/`MusicDataCollection`/`OutputDataCollection`/`AudioMixer`를 들고 있고 DI로 주입된다. **런타임 Resources 의존 없음** — 에디터 도구만 `AssetDatabase`로 이 에셋을 찾는다.
  - `SFX`/`Track`/`Output`은 `[SerializeField] string`을 감싼 `partial struct`. 에디터가 `<DataRoot>/Generated/`에 상수를 생성하고 같은 폴더의 `.asmref`로 `FoundationDI` 어셈블리에 합류시킨다.
  - Output 볼륨 영속화는 `ISoundVolumeStorage` seam(기본 `PlayerPrefsVolumeStorage`).
  - 에디터 도구는 `Assets/FoundationDI/Editor/SoundService/`(IMGUI): Audio Creator / Audio Collection / Output Manager / Settings 창 + 유사 enum PropertyDrawer + MusicZone 인스펙터.
  - 상세: `Assets/FoundationDI/Runtime/Services/SoundService/README.md`.
- **AdService** (`Services/AdService/`): 광고 네트워크 중립 서비스. AdMob/LevelPlay/AppLovin 중 무엇을 붙이더라도 게임 코드는 `IAdService` 하나로 전면·보상·배너를 다룬다.
  - **3계층**: `Providers/`(SDK seam, `IAdProvider`/`IFullScreenAdapter`/`IBannerAdapter`) → `Ads/`(정책 계층, `FullScreenAdUnit`/`BannerAdUnit` — 재시도 백오프·보상 래치·자동 재로드·광고제거 게이트) → `AdService`(조립 + 이벤트 합류). 어댑터를 추가해도 정책 계층은 건드리지 않는 것이 설계 원칙.
  - **`ShowAsync`는 `Awaitable<AdShowResult>`**. `UnityAdDispatcher`가 `[AdService] Runner`(`HideAndDontSave`)를 통해 지연·프레임 대기를 펌프한다. `IAdDispatcher.Post`(메인스레드 마샬링)는 서비스 어디서도 쓰지 않는다 — **SDK 콜백을 메인 스레드로 마샬링하는 책임은 3사 어댑터 구현체에 있다.**
  - **`AdsRemoved`는 포맷별로 다르게 게이트한다**: 전면·배너는 차단, 보상형은 계속 동작. `IAdRemovalStorage`(기본 `PlayerPrefsAdRemovalStorage`)로 영속화된다.
  - **현재 Dummy provider만 구현됨** — 3사 실제 어댑터는 각각 별도 계획.
  - 상세: `Assets/FoundationDI/Runtime/Services/AdService/README.md`.

공통 패턴: 런타임에 리소스를 로드하는 서비스(PoolService, ResourceService 등)는 `Resources.Load<T>()`를 먼저 시도하고 실패 시 `Addressables.LoadAssetAsync<T>().WaitForCompletion()`으로 폴백한 뒤 핸들을 보관해 두었다가 dispose 시 해제한다. SoundService는 `SoundData`가 `AudioClip`을 컴파일 타임 직접 참조로 보유하므로 이 패턴에 해당하지 않는다.

### 네임스페이스
런타임 코드는 `DarkNaku.FoundationDI` 단일 네임스페이스로 통일한다(UIManager 리뉴얼로 구 `FoundationDI` 네임스페이스는 제거됨). 새 코드를 추가할 때 같은 디렉터리의 기존 파일이 쓰는 네임스페이스를 따른다.

### 기타 의존성
PrimeTween(트위닝, tgz로 로컬 설치), Director(DarkNaku의 씬/플로우 라이브러리), Input System, URP 2D가 구성되어 있다.

# SERVICE ARCHITECTURE (프로젝트 규약)

새 서비스나 시스템을 만들 때는 기존 서비스 패턴을 따른다. 위치: `Assets/FoundationDI/Runtime/Services/<ServiceName>/`.

## 서비스 작성 규약

- 네임스페이스는 `DarkNaku.FoundationDI`.
- `IXxxService : IDisposable` 인터페이스 + `XxxService` 구현 클래스 쌍으로 작성한다.
- VContainer로 등록한다 (`RootLifetimeScope.Configure`에서 `builder.Register<IXxxService, XxxService>(Lifetime.Singleton)`).
- **테스트 가능성을 위한 seam 분리**: 외부 의존(Addressables, 파일 IO 등)은 `IXxxProvider` 같은 인터페이스로 추상화하고, 기본 생성자는 실제 구현을 주입하고 별도 생성자는 인터페이스를 주입받게 한다. EditMode 단위 테스트는 NSubstitute로 이 seam을 대체해 외부 의존 없이 검증한다.
- 테스트 어셈블리는 `FoundationDI.Tests`(EditMode, `overrideReferences: true`, `nunit.framework.dll`/`NSubstitute.dll`/`Castle.Core.dll` precompiled 참조)를 사용한다.

## 리소스 로딩은 ResourceService에 위임한다

- **에셋 로딩이 필요한 모든 서비스/시스템은 직접 `Addressables`/`Resources`를 호출하지 말고 `IResourceService`에 위임한다.** (Addressables 호출과 핸들 생명주기가 한 곳에서 참조 카운팅으로 관리되도록.)
- `IResourceService` API: `UniTask<T> LoadAsync<T>(string key)`, `T Load<T>(string key)`(동기, `WaitForCompletion`), `void Release(string key)`, `Dispose()`. 모두 `where T : UnityEngine.Object`.
- 키 단위 캐싱 + 참조 카운팅: 로드 1회 ↔ `Release` 1회 짝을 맞춘다. 참조가 0이 되면 실제 핸들이 해제된다.
- 같은 키 동시 `LoadAsync`는 내부에서 중복 제거되어 Addressables 로드가 1회만 발생한다.
- ResourceService가 캐시·반환하는 것은 **에셋 원본**이다(인스턴스 아님). 프리팹은 받아서 호출자가 `Instantiate`한다.
- 상세 사용법·API·매뉴얼: `Assets/FoundationDI/Runtime/Services/ResourceService/README.md`.
- 알려진 범위 외 항목(설계 시 참고): 에러 처리 미구현(로드 중 예외 시 대기 호출자 미완료 가능), 스레드 안전성 없음(메인 스레드 전제).

> 향후 `PoolService`의 중복 로딩 로직도 `IResourceService` 위임으로 전환 예정(별도 계획). `SoundService`는 `SoundData`가 `AudioClip`을 컴파일 타임 직접 참조로 보유하므로 위임 대상이 아니다.
