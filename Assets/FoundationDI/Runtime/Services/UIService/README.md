# UIService

uGUI 기반 UI 표시/전환 시스템입니다. Presenter 타입으로 표시 모드(Page/Popup/Overlay)를 컴파일 타임에 고정하고, 모든 Show/Hide 전환을 단일 큐로 순차 직렬화합니다. 프리팹 로딩은 공용 [`IResourceService`](../ResourceService/README.md)에 위임하며, 백엔드(Resources/Addressables)는 어떤 `IResourceProvider`를 등록했는지로 결정됩니다.

- **3가지 표시 모드** — Page(단일 교체), Popup(LIFO 스택·모달), Overlay(상주, Popup 기준 Above/Below)
- **빌더 체인** — `Page<T>()` 즉시 인스턴스 반환 + Show 자동 enqueue → 같은 프레임 `.WithParams()/.OnAfterShow()/.WithTransition()/.WithOverlay()` 동기 체인
- **전환 직렬화** — `OperationQueue`로 모든 전환을 순차 처리(race 제거)
- **Presenter는 매 표시마다 새로 생성, View는 풀 재사용** — Presenter 인스턴스 캐시는 없음. `Page/Popup/Overlay<T>()`마다 새 Presenter 생성 + `OnInitialize` 재실행. View만 프리팹 키로 풀링되어 재사용됨.
- **상주 캔버스** — 단일 `[UIService]` Canvas(ScreenSpaceOverlay)는 `DontDestroyOnLoad`로 앱 전체에 1개만 상주. 씬 전환 시 자식 UI만 clear하고 캔버스는 유지.
- **WithOverlay** — Page/Popup과 오버레이를 동시에 노출(동시 애니메이션). `persistent` 옵션으로 페이지 전환 간 깜빡임 없이 유지.
- **트랜지션 추상화** — `IUITransition` + 기본 3종(Fade/Slide/Scale) MonoBehaviour 컴포넌트(공통 기반 `UITransitionBehaviour`), 폴백 Noop. Slide/Scale은 배경(Image)·컨텐츠 분리 연출 지원.

---

## 사용법

### 1) DI 등록 (VContainer)

`RegisterUIService` 호출 **전에 `IResourceService`가 등록**되어 있어야 합니다(프리팹 로드를 위임). 상주 캔버스가 앱 전체 단일 인스턴스가 되려면 UIService는 **프로젝트 루트 LifetimeScope**(`VContainerSettings.RootLifetimeScope`)에 등록해야 합니다.

```csharp
using VContainer;
using VContainer.Unity;
using DarkNaku.FoundationDI;

public class RootLifetimeScope : LifetimeScope
{
    // 인스펙터에서 Assets/Settings/UIServiceSettings.asset 을 연결한다.
    public UIServiceSettings settings;

    protected override void Configure(IContainerBuilder builder)
    {
        // 프리팹 로드 백엔드는 provider 등록 한 줄로 교체한다(Resources → Addressables 등).
        builder.Register<IResourceProvider, ResourcesProvider>(Lifetime.Singleton);
        builder.Register<IResourceService, ResourceService>(Lifetime.Singleton);
        builder.RegisterUIService(settings);
    }
}
```

> 백엔드는 `IResourceProvider` 구현체 선택으로 결정됩니다. 호스트 샘플은 `ResourcesProvider`(Resources)를 쓰며, Addressables는 선택입니다.

### 2) View와 Presenter 정의

`UIView`를 상속한 View(프리팹 루트에 부착)와, 표시 모드에 맞는 Presenter를 작성합니다. Presenter에 `[UIPrefab("키")]`로 프리팹 로드 키를 지정합니다.

```csharp
using DarkNaku.FoundationDI;

public class TitleView : UIView { }

[UIPrefab("UI/Title")]               // IResourceService.Load<GameObject>("UI/Title")로 로드
public class TitlePresenter : UIPagePresenter<TitleView>
{
    // 패키지를 다른 어셈블리에서 파생하므로 protected override로 선언한다.
    protected override void OnAfterShow() { /* 표시 직후 */ }
}
```

### 3) 표시 (Page / Popup / Overlay) + 빌더 체인

호출 즉시 인스턴스가 반환되고 Show가 자동으로 큐에 등록됩니다(`.Show()` 호출 불필요). 같은 프레임에 빌더 메서드를 체인할 수 있습니다.

```csharp
public class Example
{
    private readonly IUIService _ui;
    public Example(IUIService ui) => _ui = ui;

    public void Open()
    {
        _ui.Page<TitlePresenter>()
           .OnAfterShow(p => Debug.Log("표시 완료"));

        _ui.Popup<ConfirmPresenter>()
           .WithParams(new ConfirmParams("정말 삭제할까요?"))   // IConfigurable<TParams> 필요
           .WithTransition(_fadeTransition);               // per-show 트랜지션 오버라이드
    }
}
```

### 4) 닫기

Presenter의 `Hide()`로 숨깁니다. Hide되면 View는 풀로 반환되어 재사용됩니다. Presenter 인스턴스 자체는 캐시되지 않으므로 다음 표시 때 새로 생성됩니다.

```csharp
presenter.Hide();    // 숨김 요청(큐에 enqueue) + View 풀 반환
```

### 5) 파라미터 전달

`IConfigurable<TParams>`를 Presenter에 구현하면 `.WithParams(params)`로 값을 주입할 수 있습니다.

```csharp
public readonly struct ConfirmParams { public readonly string Message; public ConfirmParams(string m) => Message = m; }

[UIPrefab("UI/Confirm")]
public class ConfirmPresenter : UIPopupPresenter<ConfirmView>, IConfigurable<ConfirmParams>
{
    private ConfirmParams _params;

    // Configure는 View 바인딩 전에 동기 호출되므로 View에 접근하지 말고 params만 저장한다.
    public void Configure(ConfirmParams p) => _params = p;

    protected override void OnBeforeShow() => View.SetMessage(_params.Message);
}
```

---

## Canvas 수명(지속)

- `[UIService]` 루트 Canvas는 **최초 표시 시 지연 생성**되고, `renderMode = ScreenSpaceOverlay` + `DontDestroyOnLoad`로 **씬을 넘어 앱 전체에 1개만 상주**합니다(카메라 비의존). 레이어 렌더 순서(아래→위)는 `Page → BelowOverlay → Popup → AboveOverlay`.
- **씬 전환(activeSceneChanged) 시** UIService는 자식 UI 컨텐츠를 전부 clear합니다 — 활성 Presenter를 teardown(`OnBeforeHide`/`OnAfterHide` 발화)하고 진행 중인 큐를 취소하며 **View 풀을 dispose**합니다. **캔버스 자체는 유지**되며, 풀은 다음 표시 때 캔버스 아래에 재구성됩니다.
- 캔버스는 오직 `UIService.Dispose()`(= 소유 루트 스코프 dispose) 시에만 파괴됩니다. 그래서 앱 전체 단일 인스턴스가 되려면 지속되는 **프로젝트 루트 LifetimeScope**에 등록해야 합니다.
- 예외적으로 캔버스 GameObject가 외부에서 파괴되면(fake-null) 참조를 버리고 다음 표시에서 재구성합니다.

---

## Presenter는 새로 생성, View는 풀 재사용

- `Page/Popup/Overlay<T>()`를 호출할 때마다 `UIInstanceFactory.CreatePresenter`로 **새 Presenter 인스턴스**가 생성되고 `OnInitialize`가 다시 실행됩니다. Presenter 인스턴스 캐시는 **없습니다**.
- **View는 프리팹 키로 풀링**됩니다(`Pool.Get`/`Pool.Release`). Hide 시 View는 비활성화되어 풀로 돌아가고, 다음 Show 때 같은 키의 View가 재사용됩니다.
- 따라서 Presenter가 View 위젯(버튼 `onClick`, R3 `Subscribe` 등)에 건 구독은 **멱등하게** 등록해야 합니다. 재사용된 View에는 이전 핸들러가 남아있으므로, `OnInitialize`에서 remove-before-add 하거나 `OnAfterHide`에서 해제하세요.

```csharp
[UIPrefab("MenuPage")]
public class MenuPage : UIPagePresenter<MenuPageView>
{
    [Inject] private IUIService _ui;

    protected override void OnInitialize()
    {
        // 재사용 View에 핸들러가 누적되지 않도록 add 전에 remove 한다.
        View.startGameButton.onClick.RemoveAllListeners();
        View.startGameButton.onClick.AddListener(() => _ui.Page<GamePage>());
    }
}
```

---

## WithOverlay

Page/Popup에 `WithOverlay<TOverlay>(bool persistent = false, Action<TOverlay> configure = null)`를 체인하면 호스트와 **함께** 오버레이를 노출합니다.

- 오버레이는 호스트와 **동시에(concurrent)** 애니메이션되고, 호스트의 트랜지션 오버라이드(`WithTransition`)를 공유합니다.
- 호스트가 숨겨지면 링크된 오버레이도 **함께 숨겨집니다**.
- `configure`는 View 바인딩/`OnInitialize` 전에 호출되므로 **params만** 저장하고 View에 접근하지 마세요.

```csharp
_ui.Page<StagePage>()
   .WithOverlay<HudOverlay>(persistent: true)
   .WithOverlay<TouchGuardOverlay>();     // 기본(per-host) 오버레이
```

### persistent 시맨틱

- `persistent: false`(기본) — **호스트별 인스턴스**. 페이지 전환 시 오버레이도 hide되고, 다음 페이지가 요청하면 새로 생성됩니다.
- `persistent: true` — 페이지 전환에서 **다음 페이지도 같은 타입을 persistent로 요청**하면 오버레이는 teardown되지 않고 **소유권이 새 페이지로 이전**되어 연속 유지됩니다(깜빡임 없음). 이때 이미 초기화된 인스턴스를 재사용하므로 `configure`와 `OnInitialize`는 다시 호출되지 않습니다.
- 다음 페이지가 그 타입을 요청하지 않으면 오버레이는 정상적으로 hide됩니다.

---

## API

### `IUIService`

| 멤버 | 시그니처 | 설명 |
| --- | --- | --- |
| `IsPopupVisible` | `bool IsPopupVisible { get; }` | 표시 중인 팝업이 하나 이상이면 true. |
| `Page<T>` | `T Page<T>() where T : UIPresenter` | Page 모드로 표시. 즉시 인스턴스 반환 + Show 자동 enqueue. |
| `Popup<T>` | `T Popup<T>() where T : UIPresenter` | Popup(스택) 모드로 표시. |
| `Overlay<T>` | `T Overlay<T>() where T : UIPresenter` | Overlay(상주) 모드로 표시. |

구현체 `UIService`는 `IUIService`, `IDisposable`을 구현하며 `RegisterUIService`로 등록합니다(생성자는 internal).

### Presenter 기반 타입

표시 모드별 추상 기반 클래스. `TView`는 `UIView` 파생.

| 타입 | 용도 |
| --- | --- |
| `UIPagePresenter<TView>` | 단일 교체(Page) |
| `UIPopupPresenter<TView>` | LIFO 스택(Popup) |
| `UIOverlayPresenter<TView>` | 상주(Overlay). `protected internal virtual bool Above => true` 오버라이드로 Popup 기준 Above/Below 선택 |

세 타입 모두 `UIPresenterBuilder<TSelf, TView>`를 상속하며, 아래 빌더 메서드는 **자기 자신(`TSelf`)을 반환**해 체인이 가능합니다.

| 메서드 | 설명 |
| --- | --- |
| `WithParams<TParams>(TParams p)` | Presenter가 `IConfigurable<TParams>`면 `Configure(p)` 호출(아니면 경고 후 무시) |
| `OnBeforeShow(Action<TSelf> cb)` | BeforeShow 라이프사이클에 콜백 등록 |
| `OnAfterShow(Action<TSelf> cb)` | AfterShow 라이프사이클에 콜백 등록 |
| `OnBeforeHide(Action<TSelf> cb)` | BeforeHide 라이프사이클에 콜백 등록 |
| `OnAfterHide(Action<TSelf> cb)` | AfterHide 라이프사이클에 콜백 등록 |
| `WithTransition(IUITransition t)` | 이번 표시에 한해 트랜지션 오버라이드 |
| `WithOverlay<TOverlay>(bool persistent = false, Action<TOverlay> configure = null)` | 호스트와 함께 오버레이 노출(위 [WithOverlay](#withoverlay) 참고) |

`UIPresenter<TView>`는 `protected TView View` 접근자를 제공합니다.

### Presenter 명령 / 라이프사이클 훅 (`UIPresenter`)

명령: `void Hide()` (숨김 요청 enqueue → View 풀 반환). 개별 파괴 API는 없습니다.

오버라이드 가능한 훅(패키지 내부 선언은 `protected internal virtual`):
`OnInitialize` · `OnBeforeShow` · `OnAfterShow` · `OnBeforeHide` · `OnAfterHide`.
> 패키지를 import해 **다른 어셈블리**에서 파생할 때는 `protected override`로 선언합니다(`protected internal`의 `internal` 부분은 외부 어셈블리에 보이지 않음).

### `UIView : MonoBehaviour`

프리팹 루트에 부착하는 View 기반 클래스. `[RequireComponent(typeof(CanvasGroup))]`이며 `IPoolItem`을 구현해 풀링됩니다.

| 멤버 | 설명 |
| --- | --- |
| `RectTransform RectTransform` | 캐싱된 RectTransform |
| `bool InputEnabled` | `CanvasGroup.interactable` 활성/비활성(모달 입력 차단에 사용) |
| `IUITransition Transition` | per-show 트랜지션 오버라이드(코드 설정용, `WithTransition`이 이 값을 세팅) |
| `public virtual void OnInitializeView()` | 물리 인스턴스 최초 생성 시 1회 호출(풀 `OnCreateItem`) |
| `protected virtual void OnDestroyView()` | 물리 인스턴스 파괴 시 호출(풀 `OnDestroyItem`) |
| 부착된 트랜지션 컴포넌트 | View 루트 GameObject에 `IUITransition`(예: `UITransitionBehaviour` 파생) 컴포넌트를 부착하면 `GetComponent<IUITransition>()`로 자동 해석 |

### 속성 / 인터페이스

- `[UIPrefab("키")]` — Presenter 클래스에 부착. 프리팹 로드 키. `IResourceService.Load<GameObject>(key)`로 로드.
- `IConfigurable<TParams>` — Presenter에 구현 시 `.WithParams(params)`로 `Configure(params)` 수신. Configure는 View 바인딩 전에 호출되므로 View 접근 금지.

### 트랜지션

- `IUITransition` — `Awaitable ShowAsync(RectTransform target, CancellationToken ct)` / `Awaitable HideAsync(...)`. 하나의 `IUITransition`이 show/hide 한 쌍을 정의합니다.
- 공통 기반 `UITransitionBehaviour`(MonoBehaviour) — 트윈 라이브러리 없이 `Awaitable.NextFrameAsync`로 매 프레임 보간. 인스펙터 필드: `_duration`(기본 0.2s), `_ease`(AnimationCurve), `_unscaledTime`(기본 true). `_duration <= 0`이면 즉시 끝 상태 적용.
- 기본 구현(**MonoBehaviour 컴포넌트**, View 루트에 부착):
  - `FadeTransition` — `_target`(CanvasGroup, 미지정 시 View 루트) 알파 페이드.
  - `SlideTransition` — `_content`(RectTransform, 미지정 시 View 루트) 슬라이드 + `_background`(**Image, 선택적**·미지정 시 페이드 생략) 페이드. `_direction`(Left/Right/Top/Bottom, 기본 Bottom). 슬라이드와 배경 페이드를 한 `Animate` 루프에서 함께 적용.
  - `ScaleTransition` — `_content` 스케일(`_fromScale`, 기본 0.8 → 1) + `_background`(Image, 선택적) 페이드. 함께 적용.
  - 배경 페이드는 Image의 **디자인 알파(휴지 상태 `color.a`)까지** 진행합니다(반투명 dim 배경의 원래 투명도 보존). 해당 알파는 최초 재생 시 1회 캡처됩니다.
- 폴백 `NoopTransition`(즉시 완료).
- **해석 우선순위**: `WithTransition(...)` 오버라이드(`UIView.Transition`) > View 루트의 트랜지션 컴포넌트(`GetComponent<IUITransition>()`) > `NoopTransition`.

### DI / 설정

- `void RegisterUIService(this IContainerBuilder builder, UIServiceSettings settings)` — UIService 등록 확장(`UIServiceSettings`/`UIInstanceFactory`/`UIService as IUIService` 등록).
  **전제: 호출 전에 `IResourceService`가 등록되어 있어야 합니다.**
- **주입 대상**: Presenter는 생성 시(`UIInstanceFactory`), View는 프리팹 인스턴스 생성 시 계층 전체의 MonoBehaviour가 주입됩니다(`InjectGameObject`). 둘 다 `[Inject]` 필드를 쓸 수 있습니다.
  - View 주입은 **풀 인스턴스당 1회**입니다. 풀에서 재사용될 때는 다시 주입되지 않습니다(씬 전환 시 풀이 dispose되므로 다음 표시에서 새로 생성·주입됩니다).
  - UIService는 루트 스코프에 등록되므로 Presenter/View 모두 **루트 스코프 의존만** 해석됩니다.
- `UIServiceSettings`(ScriptableObject) — `ReferenceResolution`(기본 1920×1080) **하나만** 제공합니다. CanvasScaler를 **Scale With Screen Size + Screen Match Mode = Expand**로 구성하고 기준 해상도로 사용합니다(설정이 없거나 0 이하면 1920×1080 폴백). sorting-layer/카메라/plane 필드는 ScreenSpaceOverlay 전환과 함께 제거되었습니다.

---

## 매뉴얼

### 표시 모드

- **Page** — 단일 활성. 새 Page를 표시하면 이전 Page를 Hide하고 교체합니다.
- **Popup** — LIFO 스택. 여러 개가 쌓이며, **최상단 팝업만 입력 활성**이고 하위 팝업·Page·BelowOverlay는 입력 차단(모달)됩니다. AboveOverlay는 팝업 표시 중에도 입력을 유지합니다. 입력 차단은 `CanvasGroup.interactable`(`UIView.InputEnabled`) 토글이며, 클릭 흡수/통과(`blocksRaycasts`/`raycastTarget`)는 프리팹 책임입니다.
- **Overlay** — 상주형. `Above`(기본 true)면 Popup 위 레이어(AboveOverlay), false면 Popup 아래 레이어(BelowOverlay)에 배치됩니다. `WithOverlay`로 Page/Popup에 링크하거나 `Overlay<T>()`로 단독 표시할 수 있습니다.

### 표시 흐름과 OperationQueue

- `Page/Popup/Overlay<T>()`는 인스턴스를 **즉시 동기 반환**하고 실제 표시는 `OperationQueue`에 enqueue됩니다. 큐 작업은 `Awaitable.NextFrameAsync`로 한 프레임 양보한 뒤 실행되므로, 같은 프레임에 빌더 체인(`.With/.OnAfterShow/.WithTransition/.WithOverlay`)을 동기로 구성할 수 있습니다.
- 모든 Show/Hide 전환은 `OperationQueue`로 **순차 직렬화**되어 동시 전환의 race를 방지합니다. 큐 예외는 로그로 처리되며 루프를 중단하지 않습니다.
- 큐는 씬 전환/Dispose 시 `CancelAndClear`로 취소됩니다.

### 프리팹 로딩

- Presenter의 `[UIPrefab("키")]` 키로 `IResourceService.Load<GameObject>(key)`를 호출해 프리팹을 로드한 뒤 풀이 `Instantiate`합니다. 백엔드는 등록된 `IResourceProvider`에 따릅니다(Resources 또는 Addressables). **Addressables 전용이 아닙니다.**
- 프리팹 핸들 생명주기는 `IResourceService`가 참조 카운팅으로 관리합니다.

### 정리(Dispose)

- `UIService.Dispose()`는 `activeSceneChanged` 구독을 해제하고, 진행 중 큐를 취소하며, 활성 Presenter를 전부 teardown하고 View 풀을 dispose한 뒤 상주 캔버스를 파괴합니다. 보통 DI 컨테이너(루트 스코프)가 수명을 관리합니다.

### 테스트

- **EditMode** (`Tests/Editor/UIService`): `DIRegistrationTests` · `ModeControllerTests` · `NoopTransitionTests` · `OperationQueueTests` · `PresenterLifecycleTests` · `UIInstanceFactoryTests` · `UIPrefabKeyResolverTests` · `UIServiceSettingsTests` · `UIViewPoolLifecycleTests`.
- **PlayMode** (`Tests/Runtime/UIService`): `FadeTransitionTests` · `ScaleTransitionTests` · `SlideTransitionTests` · `UIRootTests` · `UIServiceFlowTests` · `UIServiceSceneResetTests` · `UIServiceWithOverlayTests` · `UIViewTests` · `UIViewTransitionResolveTests` (공용 헬퍼 `TransitionTestHelpers`).
- 프리팹 로드는 `IResourceService`를 NSubstitute로 대체해 가짜 프리팹을 주입합니다.

### 한계 / 후속 과제

- 상주 캔버스 단일 인스턴스는 UIService를 지속 루트 스코프에 등록했을 때만 보장됩니다(자식 스코프에 등록하면 스코프 dispose 시 캔버스가 파괴됨).
- 큐 작업 중 발생한 예외는 로그로 남지만 대기 중인 호출자에게 전파되지는 않습니다.
- 메인 스레드 전제(스레드 안전성 없음).
