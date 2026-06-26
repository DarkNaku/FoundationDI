# UIManager

uGUI 기반 UI 표시/전환 시스템입니다. Presenter 타입으로 표시 모드(Page/Popup/Overlay)를 컴파일 타임에 고정하고, 모든 Show/Hide 전환을 단일 큐로 순차 직렬화합니다. 프리팹 로딩은 공용 [`IResourceService`](../../Services/ResourceService/README.md)(Addressables)에 위임합니다.

- **3가지 표시 모드** — Page(단일 교체), Popup(LIFO 스택·모달), Overlay(상주, Popup 기준 Above/Below)
- **빌더 체인** — `Page<T>()` 즉시 인스턴스 반환 + Show 자동 enqueue → 같은 프레임 `.With()/.OnAfterShow()/.WithTransition()` 동기 체인
- **전환 직렬화** — `OperationQueue`로 모든 전환을 순차 처리(race 제거)
- **인스턴스 캐시** — Hide된 인스턴스를 타입별 재사용(다음 Show 시 `OnInitialize` 생략)
- **트랜지션 추상화** — `IUITransition` + 기본 3종(Fade/Scale/Slide) ScriptableObject, 폴백 Noop

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
        builder.Register<IResourceService>(_ => new ResourceService(), Lifetime.Singleton);
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
    protected internal override void OnAfterShow() { /* 표시 직후 */ }
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
| `Page<T>` | `T Page<T>() where T : UIPresenterBase` | Page 모드로 표시. 즉시 인스턴스 반환 + Show 자동 enqueue. |
| `Popup<T>` | `T Popup<T>() where T : UIPresenterBase` | Popup(스택) 모드로 표시. |
| `Overlay<T>` | `T Overlay<T>() where T : UIPresenterBase` | Overlay(상주) 모드로 표시. |

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
| `OnAfterShow(Action<...> cb)` | AfterShow 라이프사이클에 콜백 등록 |
| `OnAfterHide(Action<...> cb)` | AfterHide 라이프사이클에 콜백 등록 |
| `WithTransition(IUITransition t)` | 이번 표시에 한해 트랜지션 오버라이드 |

`UIPresenterBase<TView>`는 `protected TView View` 접근자를 제공한다.

### Presenter 명령 / 라이프사이클 훅 (`UIPresenterBase`)

명령: `void Hide()` (숨김 + 캐시 보관). 개별 파괴는 제공하지 않으며, 정리는 `UIManager.Dispose()`에서 일괄 수행된다.

오버라이드 가능한 훅(`protected internal virtual`):
`OnInitialize` · `OnBeforeShow` · `OnAfterShow` · `OnBeforeHide` · `OnAfterHide` · `OnDestroyElement`.

### `UIView : MonoBehaviour`

프리팹 루트에 부착하는 View 기반 클래스.

| 멤버 | 설명 |
| --- | --- |
| `RectTransform RectTransform` | 캐싱된 RectTransform |
| `bool InputEnabled` | CanvasGroup.interactable 활성/비활성(모달 입력 차단에 사용) |
| `IUITransition ShowTransition` / `HideTransition` | per-show 트랜지션(코드 설정용) |
| `virtual void OnInitializeView()` | 인스턴스 최초 생성 시 1회 호출 |
| 인스펙터 `_showTransition` / `_hideTransition` | 기본 트랜지션 에셋 슬롯 |

### 속성 / 인터페이스 / 트랜지션

- `[UIPrefab("키")]` — Presenter 클래스에 부착. 프리팹 로드 키(Addressables 주소).
- `IConfigurable<TParams>` — Presenter에 구현 시 `.With(params)`로 `Configure(params)` 수신.
- `IUITransition` — `UniTask PlayShow(RectTransform, CancellationToken)` / `PlayHide(...)`.
  - 기본 구현(ScriptableObject): `FadeTransitionAsset`, `ScaleTransitionAsset`, `SlideTransitionAsset`. 폴백: `NoopTransition`(즉시).

### DI / 설정

- `void RegisterUIManager(this IContainerBuilder builder, UIManagerSettings settings)` — UIManager 등록 확장.
  **전제: 호출 전에 `IResourceService`가 등록되어 있어야 한다.**
- `UIManagerSettings` (ScriptableObject) — `DefaultPageTransition` / `DefaultPopupTransition` / `DefaultOverlayTransition` 기본 트랜지션 제공.

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

`WithTransition(...)`(이번 표시 오버라이드) > `UIView` 인스펙터 에셋(`_showTransition`/`_hideTransition`) > `NoopTransition`(즉시).

### 프리팹 로딩

- Presenter의 `[UIPrefab("키")]` 키로 `IResourceService.Load<GameObject>(key)`를 호출해 프리팹을 로드한 뒤 `Instantiate`한다(Addressables 전용). UI 프리팹은 **Addressables 항목**이어야 한다.
- 프리팹 핸들은 개별 Release하지 않으며, 컨테이너 수명 종료 시 `ResourceService.Dispose`로 일괄 해제된다.

### 정리(Dispose)

- `UIManager.Dispose()`는 진행 중 큐를 취소하고, 활성/캐시된 모든 Presenter에 `OnDestroyElement` + `Destroyed` 이벤트를 발화한 뒤 캐시/컨트롤러를 비우고 UI 루트 Canvas를 파괴한다. 보통 DI 컨테이너가 수명을 관리한다.

### 테스트

- EditMode: `UIInstanceFactoryTests`(프리팹→Presenter 생성·바인딩), `DIRegistrationTests`(컨테이너 해석).
- PlayMode: `UIManagerFlowTests`(Page/Popup/Overlay 표시, 재Show 활성화). 프리팹 로드는 `IResourceService`를 NSubstitute로 대체해 가짜 프리팹을 주입한다.

### 한계 / 후속 과제

- UI 프리팹은 Addressables 전용(Resources 경로 없음).
- 프리팹 Release를 UI 요소 생명주기에 연동(참조 카운팅 활용)하는 것은 후속 과제.
