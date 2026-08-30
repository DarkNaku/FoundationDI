# UINavigator

uGUI 기반 UI 표시/전환 시스템입니다. Presenter 타입으로 표시 모드(Page/Popup/Overlay)를 컴파일 타임에 고정하고, 모든 Show/Hide 전환을 단일 큐로 순차 직렬화합니다. 프리팹 로딩은 공용 [`IResourceService`](../../Services/ResourceService/README.md)에 위임하며, 백엔드(Resources/Addressables)는 어떤 `IResourceProvider`를 등록했는지로 결정됩니다.

- **3가지 표시 모드** — Page(단일 교체), Popup(LIFO 스택·모달), Overlay(상주, Popup 기준 Above/Below)
- **빌더 체인** — `Page<T>()` 즉시 인스턴스 반환 + Show 자동 enqueue → 같은 프레임 `.WithParams()/.OnAfterShow()/.WithTransition()/.WithOverlay()` 동기 체인
- **전환 직렬화** — `OperationQueue`로 모든 전환을 순차 처리(race 제거)
- **Presenter는 매 표시마다 새로 생성, View는 풀 재사용** — Presenter 인스턴스 캐시는 없음. `Page/Popup/Overlay<T>()`마다 새 Presenter 생성 + `OnInitialize` 재실행. View만 프리팹 키로 풀링되어 재사용됨.
- **씬 수명 캔버스** — 루트 Canvas는 자신을 만든 씬에 속한다. `UINavigatorSettings.RootPrefab`을 인스턴스화하며(렌더 모드/CanvasScaler/레이어는 프리팹이 결정), 미지정 시 코드 기본값(ScreenSpaceOverlay/1920x1080)으로 폴백. 씬이 언로드되면 캔버스·풀·프리젠터가 함께 파괴된다.
- **WithOverlay** — Page/Popup과 오버레이를 동시에 노출(동시 애니메이션). `persistent` 옵션으로 페이지 전환 간 깜빡임 없이 유지.
- **트랜지션 추상화** — `IUITransition` + 기본 3종(Fade/Slide/Scale) MonoBehaviour 컴포넌트(공통 기반 `UITransitionBehaviour`), 폴백 Noop. Slide/Scale은 배경(Image)·컨텐츠 분리 연출 지원.

---

## 사용법

### 1) DI 등록 (VContainer)

`RegisterUINavigator` 호출 **전에 `IResourceService`가 등록**되어 있어야 합니다(프리팹 로드를 위임). **`RegisterUINavigator`는 씬 `LifetimeScope`에서 호출합니다** — UINavigator는 씬 수명(루트 캔버스가 자신을 만든 씬에 속하고, 씬이 언로드되면 캔버스·풀·프리젠터가 함께 파괴됨)이기 때문입니다. `IResourceService`는 프로젝트 루트 `LifetimeScope`에 남겨도 됩니다(자식인 씬 스코프가 부모에서 해결합니다).

```csharp
using VContainer;
using VContainer.Unity;
using DarkNaku.FoundationDI;

public class RootLifetimeScope : LifetimeScope
{
    protected override void Configure(IContainerBuilder builder)
    {
        // 프리팹 로드 백엔드는 provider 등록 한 줄로 교체한다(Resources → Addressables 등).
        builder.Register<IResourceProvider, ResourcesProvider>(Lifetime.Singleton);
        builder.Register<IResourceService, ResourceService>(Lifetime.Singleton);
    }
}

// 씬에 배치되는 스코프. UINavigator를 여기서 등록하면 씬 수명을 갖는다.
public class SceneLifetimeScope : LifetimeScope
{
    // 인스펙터에서 Assets/Settings/UINavigatorSettings.asset 을 연결한다.
    public UINavigatorSettings settings;

    protected override void Configure(IContainerBuilder builder)
    {
        // IResourceService는 부모(RootLifetimeScope)에서 해결된다.
        builder.RegisterUINavigator(settings);
    }
}
```

> `IResourceService`가 부모(`RootLifetimeScope`)에서 해결되려면 `SceneLifetimeScope`가 실제로 그 부모를 갖고 있어야 합니다. VContainer가 부모를 찾는 경로는 셋뿐입니다: (1) `VContainerSettings.RootLifetimeScope`에 `RootLifetimeScope`가 지정되어 있거나, (2) 씬의 `LifetimeScope` 인스펙터에서 `parentReference`에 직접 연결하거나, (3) `SceneLifetimeScope`의 GameObject를 `RootLifetimeScope` 계층 아래에 중첩합니다. `parentReference`를 비워둔 채 아무 설정도 하지 않으면 부모 없는 자식 스코프가 되어 `IResourceService` 해석이 실패합니다 — 조용히가 아니라 즉시 예외로 실패하므로, 셋 중 하나를 반드시 갖추는 것을 등록 절차의 일부로 여기세요(이 리포지토리의 호스트 프로젝트는 `Assets/Settings/VContainerSettings.asset`이 (1)을 담당합니다).

> 백엔드는 `IResourceProvider` 구현체 선택으로 결정됩니다. 호스트 샘플은 `ResourcesProvider`(Resources)를 쓰며, Addressables는 선택입니다.

> 캔버스 렌더 모드/기준 해상도를 커스터마이즈하려면 `settings.RootPrefab`에 루트 프리팹을 연결하세요(만드는 법은 아래 [에디터 워크플로](#에디터-워크플로-디자이너용) 1번 참고). 비워두면 코드 기본값으로 동작합니다.

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
    private readonly IUINavigator _ui;
    public Example(IUINavigator ui) => _ui = ui;

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

## 에디터 워크플로 (디자이너용)

### 1) 루트 프리팹 만들기 (프로젝트당 1회)

`Tools/FoundationDI/UI/Create UI Root Prefab` → 저장 위치 선택 → 생성된 프리팹을
`UINavigatorSettings`의 **Root Prefab**에 연결합니다. 캔버스 렌더 모드, `CanvasScaler`,
기준 해상도, 레이어 구성은 전부 이 프리팹이 결정합니다. 비워두면 코드 기본값
(ScreenSpaceOverlay / ScaleWithScreenSize / Expand / 1920x1080)으로 폴백합니다.

### 2) 새 UI 요소 만들기

`Tools/FoundationDI/UI/Create UI Element...` → 이름과 모드(Page/Popup/Overlay) 입력 → Create.

생성되는 것:

| 산출물 | 예시(이름 `Shop`, Popup) |
|---|---|
| View 스크립트 | `<Script Root>/ShopView.cs` — `public class ShopView : UIView` |
| Presenter 스크립트 | `<Script Root>/ShopPresenter.cs` — `[UIPrefab("UI/Shop")] public class ShopPresenter : UIPopupPresenter<ShopView>`(`protected override void OnInitialize()` 스텁 포함) |
| 프리팹 | `<Prefab Root>/Shop.prefab` — 루트(stretch + CanvasGroup + ShopView) + `Background` + `Content` |

컴파일이 끝나면(`[DidReloadScripts]`) 프리팹이 자동으로 조립되고 프리팹 편집 모드로 열립니다.
이 자동 열기는 **best-effort**입니다 — 포커스가 없거나 headless인 에디터에서는 지연 콜백이
실행되지 않을 수 있습니다(스크립트와 프리팹 자체는 항상 정상적으로 만들어지며, 콘솔 로그로
성공 여부를 확인할 수 있습니다). 이 경우 프리팹을 직접 더블클릭하면 됩니다.

경로/네임스페이스 기본값은 `Project Settings > FoundationDI > UI`에서 바꿉니다.
`Prefab Root`가 `Resources` 폴더 아래면 로드 키는 Resources 기준 상대 경로가 되고,
아니면 경로 전체가 Addressables 주소로 쓰입니다. 후자의 경우 **생성된 프리팹을 Addressables 그룹에
직접 추가해야** 로드됩니다 — 마법사는 주소를 계산해 `[UIPrefab]`에 적어줄 뿐, 등록까지 하지는 않습니다.

모드별 프리팹 템플릿:

| 모드 | 루트 | 자식 |
|---|---|---|
| Page | RectTransform(stretch) + CanvasGroup + View | 없음 |
| Popup | 위와 동일 | `Background`(Image, 검정 α=0.5, 모달 입력 차단), `Content`(중앙 정렬) |
| Overlay | 위와 동일 | 없음 |

Page와 Overlay는 전면 배경이 없으므로 빈 영역의 입력이 자연히 아래로 통과합니다.
`CanvasGroup.blocksRaycasts`는 끄지 않습니다 — 끄면 오버레이 안의 버튼까지 죽습니다.

---

## Canvas 수명

- 루트 Canvas는 **최초 표시 시 지연 생성**됩니다. `UINavigatorSettings`의 **Root Prefab**을 인스턴스화하며(렌더 모드·`CanvasScaler`·레이어 구성은 그 프리팹이 결정), 미지정 시 코드 기본값(`UIRoot.CreateDefault()` — ScreenSpaceOverlay / ScaleWithScreenSize / Expand / 1920x1080)으로 폴백합니다. 어느 경로든 상주화는 하지 않습니다 — 루트는 부모 없이 인스턴스화되어 **활성 씬에 붙고, 그 씬과 함께 파괴**됩니다. 레이어 렌더 순서(아래→위)는 `Page → BelowOverlay → Popup → AboveOverlay`.
- **정리 경로는 `UINavigator.Dispose()`(= 소유 스코프 dispose) 하나뿐입니다.** 진행 중인 큐를 취소하고, 활성 Presenter를 전부 teardown(`OnBeforeHide`/`OnAfterHide` 발화)하고, View 풀을 dispose한 뒤 캔버스 GameObject를 파괴합니다. 씬 전환 자체는 이 정리를 촉발하지 않습니다 — 보통 씬이 언로드되면 UINavigator를 소유한 씬 `LifetimeScope`도 함께 dispose되므로 결과적으로 같은 타이밍에 정리됩니다.
- 캔버스 GameObject가 (씬 언로드 등으로) **`Dispose()`보다 먼저 외부에서 파괴되면**(fake-null), 그 뒤의 `Page/Popup/Overlay<T>()` 호출은 **재구성되지 않습니다.** 이미 한 번이라도 표시가 있었다면 View 풀이 캔버스보다 오래 살아남아 `Root` getter의 fake-null 복구 분기가 실행되기 전에 실패합니다(예외가 로그로 남고 화면에는 아무것도 나타나지 않습니다). `Dispose()`가 이미 끝난 뒤라면 대신 `ObjectDisposedException`을 던집니다(자세한 내용은 아래 [알려진 한계](#알려진-한계) 참고).

### additive 씬

씬 둘이 각자 `LifetimeScope`를 가지면 `UINavigator`도 둘, 캔버스도 둘입니다. 각 씬이 자기 UI를 갖는다는 뜻이며 막지 않습니다. 겹침 정렬은 각 `RootPrefab`의 `Canvas.sortingOrder`로 정하세요 — 코어는 관여하지 않습니다.

### 알려진 한계

캔버스가 이미 파괴됐지만 아직 `Dispose()`가 오지 않은 창(씬 언로드 도중 게임 코드가 UI를 새로 여는 경우)에서 `Page<T>()`를 부르면 **실패합니다** — 예외가 로그로 남고 UI는 표시되지 않습니다. `Root` getter 자체에는 죽은 참조를 버리고 재구성하는 분기가 남아 있지만, 한 번이라도 표시가 있었던 뒤에는 View 풀(`Pool`)이 캔버스보다 먼저 만들어져 있어 그 분기에 도달하기 전에 실패합니다(자세한 메커니즘은 `UINavigator.cs`의 `Root`/`Pool` 주석 참고). 캔버스를 되살리는 것은 **현재 동작이 아니라 후속 과제**입니다. 씬 전환 중에는 UI를 새로 열지 마세요.

---

## Presenter는 새로 생성, View는 풀 재사용

- `Page/Popup/Overlay<T>()`를 호출할 때마다 `UIInstanceFactory.CreatePresenter`로 **새 Presenter 인스턴스**가 생성되고 `OnInitialize`가 다시 실행됩니다. Presenter 인스턴스 캐시는 **없습니다**.
- **View는 프리팹 키로 풀링**됩니다(`Pool.Get`/`Pool.Release`). Hide 시 View는 비활성화되어 풀로 돌아가고, 다음 Show 때 같은 키의 View가 재사용됩니다.
- 따라서 Presenter가 View 위젯(버튼 `onClick`, R3 `Subscribe` 등)에 건 구독은 **멱등하게** 등록해야 합니다. 재사용된 View에는 이전 핸들러가 남아있으므로, `OnInitialize`에서 remove-before-add 하거나 `OnAfterHide`에서 해제하세요.

```csharp
[UIPrefab("MenuPage")]
public class MenuPage : UIPagePresenter<MenuPageView>
{
    [Inject] private IUINavigator _ui;

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

### `IUINavigator`

| 멤버 | 시그니처 | 설명 |
| --- | --- | --- |
| `IsPopupVisible` | `bool IsPopupVisible { get; }` | 표시 중인 팝업이 하나 이상이면 true. |
| `Page<T>` | `T Page<T>() where T : UIPresenter` | Page 모드로 표시. 즉시 인스턴스 반환 + Show 자동 enqueue. |
| `Popup<T>` | `T Popup<T>() where T : UIPresenter` | Popup(스택) 모드로 표시. |
| `Overlay<T>` | `T Overlay<T>() where T : UIPresenter` | Overlay(상주) 모드로 표시. |

구현체 `UINavigator`는 `IUINavigator`, `IDisposable`을 구현하며 `RegisterUINavigator`로 등록합니다(생성자는 internal).

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

- `void RegisterUINavigator(this IContainerBuilder builder, UINavigatorSettings settings)` — UINavigator 등록 확장(`UINavigatorSettings`/`UIInstanceFactory`/`UINavigator as IUINavigator` 등록).
  **전제: 호출 전에 `IResourceService`가 등록되어 있어야 합니다.**
- **주입 대상**: Presenter는 생성 시(`UIInstanceFactory`), View는 프리팹 인스턴스 생성 시 계층 전체의 MonoBehaviour가 주입됩니다(`InjectGameObject`). 둘 다 `[Inject]` 필드를 쓸 수 있습니다.
  - View 주입은 **풀 인스턴스당 1회**입니다. 풀에서 재사용될 때는 다시 주입되지 않습니다(UINavigator가 dispose되면 풀도 함께 dispose되므로, 다음 씬에서는 새로 생성·주입됩니다).
  - Presenter/View는 UINavigator를 **등록한 스코프**의 리졸버로 주입됩니다. 씬 `LifetimeScope`에 등록했다면 그 씬 스코프(및 부모인 루트 스코프) 의존까지 해석되고, 형제 씬 스코프나 자식 스코프의 의존은 해석되지 않습니다.
- `UINavigatorSettings`(ScriptableObject) — `RootPrefab`(`UIRoot`) **하나만** 제공합니다. 캔버스 렌더 모드, `CanvasScaler`(스케일 모드/기준 해상도), 레이어 구성은 전부 이 프리팹이 결정합니다. `Tools/FoundationDI/UI/Create UI Root Prefab`으로 만듭니다(자세한 절차는 위 [에디터 워크플로](#에디터-워크플로-디자이너용) 참고). 비워두면 `UIRoot.CreateDefault()`가 조립한 코드 기본값(ScreenSpaceOverlay / Scale With Screen Size + Expand / 1920×1080)으로 폴백합니다.

---

## 매뉴얼

### 표시 모드

- **Page** — 단일 활성. 새 Page를 표시하면 이전 Page를 Hide하고 교체합니다.
- **Popup** — LIFO 스택. 여러 개가 쌓이며, **최상단 팝업만 입력 활성**이고 하위 팝업·Page·BelowOverlay는 입력 차단(모달)됩니다. AboveOverlay는 팝업 표시 중에도 입력을 유지합니다. 입력 차단은 `CanvasGroup.interactable`(`UIView.InputEnabled`) 토글이며, 클릭 흡수/통과(`blocksRaycasts`/`raycastTarget`)는 프리팹 책임입니다.
- **Overlay** — 상주형. `Above`(기본 true)면 Popup 위 레이어(AboveOverlay), false면 Popup 아래 레이어(BelowOverlay)에 배치됩니다. `WithOverlay`로 Page/Popup에 링크하거나 `Overlay<T>()`로 단독 표시할 수 있습니다.

### 표시 흐름과 OperationQueue

- `Page/Popup/Overlay<T>()`는 인스턴스를 **즉시 동기 반환**하고 실제 표시는 `OperationQueue`에 enqueue됩니다. 큐 작업은 `Awaitable.NextFrameAsync`로 한 프레임 양보한 뒤 실행되므로, 같은 프레임에 빌더 체인(`.With/.OnAfterShow/.WithTransition/.WithOverlay`)을 동기로 구성할 수 있습니다.
- 모든 Show/Hide 전환은 `OperationQueue`로 **순차 직렬화**되어 동시 전환의 race를 방지합니다. 큐 예외는 로그로 처리되며 루프를 중단하지 않습니다.
- 큐는 `Dispose` 시 `CancelAndClear`로 취소됩니다(씬 전환 자체는 별도로 큐를 건드리지 않습니다).

### 프리팹 로딩

- Presenter의 `[UIPrefab("키")]` 키로 `IResourceService.Load<GameObject>(key)`를 호출해 프리팹을 로드한 뒤 풀이 `Instantiate`합니다. 백엔드는 등록된 `IResourceProvider`에 따릅니다(Resources 또는 Addressables). **Addressables 전용이 아닙니다.**
- 프리팹 핸들 생명주기는 `IResourceService`가 참조 카운팅으로 관리합니다.

### 정리(Dispose)

- `UINavigator.Dispose()`는 진행 중 큐를 취소하고, 활성 Presenter를 전부 teardown하고, View 풀을 dispose한 뒤 캔버스를 파괴합니다. 정리 경로는 이것 하나뿐입니다(씬 전환 이벤트를 별도로 듣지 않습니다). 보통 DI 컨테이너(UINavigator를 등록한 스코프)가 수명을 관리하며, 씬 `LifetimeScope`에 등록했다면 씬 언로드 시 함께 dispose됩니다.

### 테스트

- **EditMode** (`Tests/Editor/UINavigator`): `DIRegistrationTests` · `ModeControllerTests` · `NoopTransitionTests` · `OperationQueueTests` · `PresenterLifecycleTests` · `UIInstanceFactoryTests` · `UIPrefabKeyResolverTests` · `UINavigatorSettingsTests` · `UIViewPoolLifecycleTests`, 그리고 에디터 도구용 `UIElementCreationRequestTests` · `UIElementCreationSettingsTests` · `UIElementNamingTests` · `UIElementPrefabBuilderTests` · `UIElementTemplatesTests` · `UIRootPrefabCreatorTests`.
- **PlayMode** (`Tests/Runtime/UINavigator`): `FadeTransitionTests` · `ScaleTransitionTests` · `SlideTransitionTests` · `UIRootTests` · `UINavigatorFlowTests` · `UINavigatorRootPrefabTests` · `UINavigatorSceneLifetimeTests` · `UINavigatorViewInjectionTests` · `UINavigatorWithOverlayTests` · `UIViewTests` · `UIViewTransitionResolveTests` (공용 헬퍼 `TransitionTestHelpers`).
- 프리팹 로드는 `IResourceService`를 NSubstitute로 대체해 가짜 프리팹을 주입합니다.

### 한계 / 후속 과제

- 캔버스·풀·프리젠터는 UINavigator를 등록한 스코프(보통 씬)의 수명을 따릅니다. 앱 전체에서 유지되는 UI가 필요하면 이 컴포넌트 밖에서 별도로 관리하세요(자세한 내용은 위 [Canvas 수명](#canvas-수명) 참고).
- 큐 작업 중 발생한 예외는 로그로 남지만 대기 중인 호출자에게 전파되지는 않습니다.
- 메인 스레드 전제(스레드 안전성 없음).

---

## 0.8.x → 0.9.0 마이그레이션

| 구 (0.8.x) | 신 (0.9.0) |
|---|---|
| `IUIService` | `IUINavigator` |
| `UIServiceSettings` | `UINavigatorSettings` |
| `builder.RegisterUIService(settings)` | `builder.RegisterUINavigator(settings)` |

**등록 위치가 바뀝니다**: 루트 `LifetimeScope` → 씬 `LifetimeScope`. `IResourceService`는 루트에 남겨도 됩니다(자식 스코프가 부모에서 해결).

**동작이 바뀝니다**: 씬이 언로드되면 캔버스·풀·프리젠터가 모두 파괴됩니다. 씬을 가로질러 살아남아야 하는 UI(로딩 화면·페이드)는 이 컴포넌트 밖에서 별도 캔버스로 만드세요.

**`InjectorService`로 주입되는 씬 배치 컴포넌트는 `IUINavigator`를 해결하지 못합니다.** `InjectorService`는 정적 리졸버 하나를 들고 있어, `RegisterInjector`가 루트에 있으면 씬 배치 MonoBehaviour가 루트 컨테이너로 주입됩니다. `IUINavigator`가 필요하면 `RegisterInjector`도 같은 씬 스코프에 두거나(권장하지 않음 — `InjectorService`는 정적 필드 하나(`_resolver`)를 공유하므로, 씬 스코프에 두면 그 씬이 언로드될 때 `InjectorService.Dispose()`가 이 정적 참조를 null로 만듭니다. 상주 씬(DontDestroyOnLoad)에서 더 먼저 주입받은 컴포넌트는 이미 죽은 컨테이너를 가리키는 참조를 들고 있다가, 이후 재주입·재해석 시도에서 조용히 실패합니다), UI를 `UIPresenter`/`View` 계층에서만 다루세요 — 이 경로는 `UIInstanceFactory`가 씬 스코프 리졸버를 쓰므로 정상 동작합니다.

`UIPresenter`/`UIView`/`UIRoot`/`[UIPrefab]`은 이름이 그대로이므로 **프리젠터·뷰 선언부는 손댈 필요가 없습니다.**

---

## 마이그레이션 (0.3.0 → 0.4.0)

**BREAKING:** `UINavigatorSettings.ReferenceResolution`이 제거되고 `RootPrefab`으로 대체되었습니다.

1. `Tools/FoundationDI/UI/Create UI Root Prefab`으로 루트 프리팹을 만듭니다.
2. 그 프리팹의 `CanvasScaler`에 기존에 쓰던 기준 해상도를 설정합니다.
3. `UINavigatorSettings`의 **Root Prefab**에 연결합니다.

연결하지 않아도 동작은 하지만, 기준 해상도가 코드 기본값(1920x1080)으로 폴백합니다.
