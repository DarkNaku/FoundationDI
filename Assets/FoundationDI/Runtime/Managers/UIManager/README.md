# UIManager

uGUI 기반 UI 표시/전환 시스템입니다. Presenter 타입으로 표시 모드(Page/Popup/Overlay)를 컴파일 타임에 고정하고, 모든 Show/Hide 전환을 단일 큐로 순차 직렬화합니다. 프리팹 로딩은 공용 [`IResourceService`](../../Services/ResourceService/README.md)(Addressables)에 위임합니다.

- **3가지 표시 모드** — Page(단일 교체), Popup(LIFO 스택·모달), Overlay(상주, Popup 기준 Above/Below)
- **빌더 체인** — `Page<T>()` 즉시 인스턴스 반환 + Show 자동 enqueue → 같은 프레임 `.With()/.OnAfterShow()/.WithTransition()` 동기 체인
- **전환 직렬화** — `OperationQueue`로 모든 전환을 순차 처리(race 제거)
- **인스턴스 캐시** — Hide된 인스턴스를 타입별 재사용(다음 Show 시 `OnInitialize` 생략)
- **트랜지션 추상화** — `IUITransition` + 기본 3종(Fade/Slide/Scale) MonoBehaviour 컴포넌트(공통 기반 `UITransitionBehaviour`), 폴백 Noop. Slide/Scale은 배경(Image)·컨텐츠 분리 연출 지원

---

## 사용법

### 1) DI 등록 (VContainer)

`RegisterUIManager` 호출 **전에 `IResourceService`가 등록**되어 있어야 합니다(프리팹 로드를 위임).

```csharp
using UnityEngine;
using VContainer;
using VContainer.Unity;
using DarkNaku.FoundationDI;

public class RootLifetimeScope : LifetimeScope
{
    [SerializeField] private UIManagerSettings _uiSettings;

    protected override void Configure(IContainerBuilder builder)
    {
        builder.Register<IResourceService, AddressableResourceService>(Lifetime.Singleton);
        builder.RegisterUIManager(_uiSettings);
    }
}
```

### 2) View와 Presenter 정의

`UIView`를 상속한 View(프리팹 루트에 부착)와, 표시 모드에 맞는 Presenter를 작성합니다. Presenter에 `[UIPrefab("키")]`로 Addressables 키를 지정합니다.

```csharp
using DarkNaku.FoundationDI;

public class TitleView : UIView { }

[UIPrefab("UI/Title")]               // Addressables 주소
public class TitlePresenter : UIPagePresenter<TitleView>
{
    protected override void OnAfterShow() { /* 표시 직후 */ }   // 다른 어셈블리에서는 protected override
}
```

### 3) 표시 (Page / Popup / Overlay) + 빌더 체인

호출 즉시 인스턴스가 반환되고 Show가 자동으로 큐에 등록됩니다(`.Show()` 호출 불필요). 같은 프레임에 빌더 메서드를 체인할 수 있습니다.

```csharp
public class Example
{
    private readonly IUIManager _ui;
    public Example(IUIManager ui) => _ui = ui;

    public void Open()
    {
        _ui.Page<TitlePresenter>()
           .OnAfterShow(p => Debug.Log("표시 완료"));

        _ui.Popup<ConfirmPresenter>()
           .With(new ConfirmParams("정말 삭제할까요?"))   // IConfigurable<TParams> 필요
           .WithTransition(_fadeAsset);                    // per-show 트랜지션 오버라이드
    }
}
```

### 4) 닫기

Presenter의 `Hide()`로 숨깁니다. 숨겨진 인스턴스는 캐시에 보관되어 다음 표시 때 재사용되며, 실제 파괴는 `UIManager.Dispose()`(컨테이너 수명 종료) 시 일괄 처리됩니다.

```csharp
presenter.Hide();    // 숨김 + 인스턴스 캐시에 보관(재사용 가능)
```

### 5) 파라미터 전달

`IConfigurable<TParams>`를 Presenter에 구현하면 `.With(params)`로 값을 주입할 수 있습니다.

```csharp
public readonly struct ConfirmParams { public readonly string Message; public ConfirmParams(string m) => Message = m; }

[UIPrefab("UI/Confirm")]
public class ConfirmPresenter : UIPopupPresenter<ConfirmView>, IConfigurable<ConfirmParams>
{
    public void Configure(ConfirmParams p) => View.SetMessage(p.Message);
}
```

---

## API

### `IUIManager`

| 멤버 | 시그니처 | 설명 |
| --- | --- | --- |
| `Page<T>` | `T Page<T>() where T : UIPresenter` | Page 모드로 표시. 즉시 인스턴스 반환 + Show 자동 enqueue. |
| `Popup<T>` | `T Popup<T>() where T : UIPresenter` | Popup(스택) 모드로 표시. |
| `Overlay<T>` | `T Overlay<T>() where T : UIPresenter` | Overlay(상주) 모드로 표시. |

`UIManager`는 `IUIManager`, `IDisposable`을 구현하며 `RegisterUIManager`로 등록한다.

### Presenter 기반 타입

표시 모드별 추상 기반 클래스. `TView`는 `UIView` 파생.

| 타입 | 용도 |
| --- | --- |
| `UIPagePresenter<TView>` | 단일 교체(Page) |
| `UIPopupPresenter<TView>` | LIFO 스택(Popup) |
| `UIOverlayPresenter<TView>` | 상주(Overlay). `protected internal virtual bool Above => true` 오버라이드로 Popup 기준 Above/Below 선택 |

공통 빌더 메서드(모두 자기 자신 반환 → 체인 가능):

| 메서드 | 설명 |
| --- | --- |
| `With<TParams>(TParams p)` | Presenter가 `IConfigurable<TParams>`면 `Configure(p)` 호출 |
| `OnBeforeShow(Action<...> cb)` | BeforeShow 라이프사이클에 콜백 등록 |
| `OnAfterShow(Action<...> cb)` | AfterShow 라이프사이클에 콜백 등록 |
| `OnBeforeHide(Action<...> cb)` | BeforeHide 라이프사이클에 콜백 등록 |
| `OnAfterHide(Action<...> cb)` | AfterHide 라이프사이클에 콜백 등록 |
| `WithTransition(IUITransition t)` | 이번 표시에 한해 트랜지션 오버라이드 |

`UIPresenter<TView>`는 `protected TView View` 접근자를 제공한다.

### Presenter 명령 / 라이프사이클 훅 (`UIPresenter`)

명령: `void Hide()` (숨김 + 캐시 보관). 개별 파괴는 제공하지 않으며, 정리는 `UIManager.Dispose()`에서 일괄 수행된다.

오버라이드 가능한 훅(패키지 내부 선언은 `protected internal virtual`):
`OnInitialize` · `OnBeforeShow` · `OnAfterShow` · `OnBeforeHide` · `OnAfterHide` · `OnDestroyElement`.
> 패키지를 import해 **다른 어셈블리**에서 파생할 때는 `protected override`로 선언한다(`protected internal`의 `internal` 부분은 외부 어셈블리에 보이지 않음).

### `UIView : MonoBehaviour`

프리팹 루트에 부착하는 View 기반 클래스.

| 멤버 | 설명 |
| --- | --- |
| `RectTransform RectTransform` | 캐싱된 RectTransform |
| `bool InputEnabled` | CanvasGroup.interactable 활성/비활성(모달 입력 차단에 사용) |
| `IUITransition Transition` | per-show 트랜지션 오버라이드(코드 설정용) |
| `virtual void OnInitializeView()` | 인스턴스 최초 생성 시 1회 호출 |
| 부착된 트랜지션 컴포넌트 | View 루트 GameObject에 `UITransitionBehaviour` 파생 컴포넌트를 부착하면 `GetComponent<IUITransition>()`로 자동 해석 |

### 속성 / 인터페이스 / 트랜지션

- `[UIPrefab("키")]` — Presenter 클래스에 부착. 프리팹 로드 키(Addressables 주소).
- `IConfigurable<TParams>` — Presenter에 구현 시 `.With(params)`로 `Configure(params)` 수신.
- `IUITransition` — `UniTask ShowAsync(RectTransform, CancellationToken)` / `HideAsync(...)`.
  - 기본 구현(**MonoBehaviour 컴포넌트**, 공통 기반 `UITransitionBehaviour`): `FadeTransition`, `SlideTransition`, `ScaleTransition`. 폴백: `NoopTransition`(즉시).
  - View 루트 GameObject에 컴포넌트를 부착해 사용한다. 컴포넌트는 씬 인스턴스를 참조할 수 있으므로, 한 팝업 안에서 **배경과 컨텐츠를 분리 연출**할 수 있다.
    - `FadeTransition` — `_target`(CanvasGroup, 미지정 시 View 루트) 페이드.
    - `SlideTransition` — `_background`(**Image**, **선택적**·미지정 시 페이드 생략) 페이드 + `_content`(RectTransform, 미지정 시 View 루트) 슬라이드. `_direction`(Left/Right/Top/Bottom). 배경 페이드와 컨텐츠 슬라이드는 **병렬** 재생.
    - `ScaleTransition` — `_background`(**Image**, 선택적) 페이드 + `_content` 스케일(`_fromScale`, 기본 0.8). 병렬 재생.
    - 배경 페이드는 Image의 **디자인 알파(휴지 상태 `color.a`)까지** 진행한다(반투명 dim 배경의 원래 투명도 보존). 해당 알파는 최초 재생 시 1회 캡처된다.
    - 공통 인스펙터: `_duration`, `_ease`(AnimationCurve), `_unscaledTime`.

### DI / 설정

- `void RegisterUIManager(this IContainerBuilder builder, UIManagerSettings settings)` — UIManager 등록 확장.
  **전제: 호출 전에 `IResourceService`가 등록되어 있어야 한다.**
- `UIManagerSettings` (ScriptableObject) — `ReferenceResolution`(기본 1920×1080) 제공. (공통 기본 트랜지션은 제거됨 — 트랜지션은 각 View에 부착한 컴포넌트로 지정한다.)

### Canvas / CanvasScaler

UIManager는 루트 Canvas(`[UIManager]`, ScreenSpaceOverlay)에 `CanvasScaler`를 **Scale With Screen Size + Screen Match Mode = Expand**로 구성하고, 기준 해상도로 `UIManagerSettings.ReferenceResolution`을 사용한다(설정이 없거나 0 이하면 1920×1080으로 폴백). 즉 해상도 대응은 UIManager가 자동 처리하므로 View 프리팹에 별도 `CanvasScaler`를 둘 필요가 없다.

---

## 매뉴얼

### 표시 모드

- **Page** — 단일 활성. 새 Page를 표시하면 이전 Page를 Hide하고 교체한다.
- **Popup** — LIFO 스택. 여러 개가 쌓이며, **최상단 팝업만 입력 활성**이고 하위 팝업·Page·BelowOverlay는 입력 차단(모달). AboveOverlay는 팝업 표시 중에도 입력을 유지한다. 입력 차단은 `CanvasGroup.interactable` 토글이며, 클릭 흡수/통과(`blocksRaycasts`/`raycastTarget`)는 프리팹 책임이다.
- **Overlay** — 상주형. `Above`(기본 true)면 Popup 위 레이어, false면 Popup 아래 레이어에 배치된다. 레이어 렌더 순서(아래→위)는 `Page → BelowOverlay → Popup → AboveOverlay`.

### 표시 흐름과 OperationQueue

- `Page/Popup/Overlay<T>()`는 인스턴스를 **즉시 동기 반환**하고 실제 표시는 `OperationQueue`에 enqueue된다. 따라서 같은 프레임에 빌더 체인(`.With/.OnAfterShow/.WithTransition`)을 동기로 구성할 수 있다.
- 모든 Show/Hide 전환은 `OperationQueue`로 **순차 직렬화**되어 동시 전환의 race를 방지한다.
- 같은 타입이 **이미 활성**이면 중복 요청은 경고 후 무시되고 기존 인스턴스를 반환한다.

### 인스턴스 캐시

- `Hide()`된 Presenter는 타입을 키로 `InstanceCache`에 보관되고, 다음 표시 시 재사용된다(이때 `OnInitialize`는 생략, `OnAfterShow` 등 표시 훅은 재호출).
- 개별 파괴 API는 없다. 캐시·활성 인스턴스의 정리(파괴)는 `UIManager.Dispose()`에서만 일괄 수행된다.

### 트랜지션 우선순위

`WithTransition(...)`(이번 표시 오버라이드 = `Transition`) > View 루트에 부착된 트랜지션 컴포넌트(`GetComponent<IUITransition>()`) > `NoopTransition`(즉시). 하나의 `IUITransition`이 `ShowAsync`/`HideAsync` 한 쌍을 정의하므로 show/hide 슬롯은 분리하지 않는다.

### 프리팹 로딩

- Presenter의 `[UIPrefab("키")]` 키로 `IResourceService.Load<GameObject>(key)`를 호출해 프리팹을 로드한 뒤 `Instantiate`한다(Addressables 전용). UI 프리팹은 **Addressables 항목**이어야 한다.
- 프리팹 핸들은 개별 Release하지 않으며, 컨테이너 수명 종료 시 `ResourceService.Dispose`로 일괄 해제된다.

### 정리(Dispose)

- `UIManager.Dispose()`는 진행 중 큐를 취소하고, 활성/캐시된 모든 Presenter에 `OnDestroyElement` + `Destroyed` 이벤트를 발화한 뒤 캐시/컨트롤러를 비우고 UI 루트 Canvas를 파괴한다. 보통 DI 컨테이너가 수명을 관리한다.

### 테스트

- EditMode (`Tests/Editor`): `UIInstanceFactoryTests`·`DIRegistrationTests`·`OperationQueueTests`·`InstanceCacheTests`·`PresenterLifecycleTests`·`ModeControllerTests`·`UIPrefabKeyResolverTests`·`NoopTransitionTests`.
- PlayMode (`Tests/Runtime`): `UIManagerFlowTests`(Page/Popup/Overlay 표시·재Show 활성화)·`UIViewTests`·`UIViewTransitionResolveTests`·`UIRootTests`·`FadeTransitionTests`·`SlideTransitionTests`·`ScaleTransitionTests`. 프리팹 로드는 `IResourceService`를 NSubstitute로 대체해 가짜 프리팹을 주입한다.

### 한계 / 후속 과제

- UI 프리팹은 Addressables 전용(Resources 경로 없음).
- 프리팹 Release를 UI 요소 생명주기에 연동(참조 카운팅 활용)하는 것은 후속 과제.
