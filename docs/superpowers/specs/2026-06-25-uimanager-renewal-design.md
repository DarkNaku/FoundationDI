# UIManager 리뉴얼 설계

- **날짜**: 2026-06-25
- **상태**: 구현 완료 (2026-06-25)
- **대상**: `Assets/FoundationDI/Runtime/Managers/UIManager/` 전면 재설계

## 1. 배경 & 목표

현재 UIManager는 다음 문제를 가진다.

- 모든 진입점이 fire-and-forget(`.Forget()`)이라 연속 호출 시 `_currentPage`/`_popups` 상태가 깨짐 (race)
- 전환/팝업 완료를 호출자가 `await`하거나 결과를 받을 수 없음
- prefab 직접 참조라 모든 UI가 항상 메모리에 적재되고 언로드 수단이 없음
- 문자열 키 + 호출마다 전체 어셈블리를 도는 `PresenterType` 리플렉션
- Presenter `OnEnter/OnExit` ↔ View `OnEnterBefore/After`로 라이프사이클 책임이 분산

**목표**: 메서드 빌더 API + 순차 직렬화(`ShowQueue`) + 추상 리소스 로더를 갖춘 uGUI 기반 UI 시스템으로 전면 재설계한다. 표시 모드(Page/Popup/Overlay)는 Presenter 타입으로 고정하고 MVP(View+Presenter)는 유지한다.

API 스타일은 [DarkNaku/UIManager](https://github.com/DarkNaku/UIManager)(UI Toolkit 기반)의 메서드 빌더 패턴을 참고하되, 본 프로젝트는 **uGUI + MVP**로 포팅한다.

**호환성**: 전면 재설계(호환 무시). 현재 이 시스템의 실제 사용처가 사실상 없어 부담이 작다.

## 2. 핵심 설계 결정 (요약)

| 항목 | 결정 |
|---|---|
| 렌더링 스택 | uGUI 유지 (`MonoBehaviour`/`GraphicRaycaster`/prefab) |
| element 모델 | MVP 유지 — 공통 `UIView` + 모드별 Presenter |
| 모드 분리 | **Presenter 타입으로 고정**: `UIPagePresenter`/`UIPopupPresenter`/`UIOverlayPresenter` |
| 네비게이션 | Page=단일 교체, Popup=LIFO 스택, Overlay=Above/Below 상주 |
| API | 메서드 빌더: `Page<T>()`/`Popup<T>()`/`Overlay<T>()` → 핸들 반환 → 체이닝 |
| 동시성 | `ShowQueue`로 모든 Show/Hide/Destroy 순차 직렬화 |
| prefab 매핑 | `[UIPrefab("Path/X")]` 속성 (Presenter에 부착, 1회 캐싱) |
| 리소스 로딩 | `IUIAssetLoader` 추상화 (Resources / Addressables 구현) |
| 파라미터 | `IConfigurable<TParams>` + `.With(params)` (타입 안전) |
| 인스턴스 캐시 | **항상 캐시** — Hide 시 비활성 보관, 다음 Show에 재사용 |
| DI 스코프 | 단일 루트 `LifetimeScope` (element별 child scope 없음) |
| Popup 모달 | dim 배경 + 하위 레이어 입력 차단 포함 |
| 트랜지션 | `IUITransition` 추상화 + 기본 3종(Fade/Scale/Slide)을 ScriptableObject 에셋으로 제공. **트윈 라이브러리 미사용** — UniTask 자체 보간 |
| 비동기 | UniTask 기반 (프로젝트 표준) |

### 제외 (YAGNI)
- dismiss 입력 처리(뒤로가기/바깥 클릭) 및 `NonDismissable` 플래그
- 전역 `InputBlocker` — `ShowQueue` 직렬화 + 요소별 `InputEnabled`로 충분하므로 불필요

## 3. 공개 API

```csharp
// 페이지: 교체형
uiManager.Page<MainMenuPresenter>().Show();

// 타입 안전 파라미터 + 빌더 콜백
uiManager.Page<CharacterDetailPresenter>()
    .With(new CharacterDetailParams { Id = 123 })
    .OnShown(p => analytics.Track("CharDetail"))
    .Show();

// 팝업: 스택 + 모달 dim, 결과 받기
var confirm = uiManager.Popup<ConfirmPresenter>()
    .With(new ConfirmParams { Message = "삭제할까요?" });
confirm.OnAfterHide(p => { if (p.Result) Delete(); });
confirm.Show();

// 오버레이: 상시 HUD
uiManager.Overlay<CurrencyHudPresenter>().Show();

// 트랜지션 per-show 오버라이드
uiManager.Popup<SettingsPresenter>()
    .WithTransition(slideFromBottom)   // IUITransition 에셋
    .Show();
```

- `Page<T>()`는 `where T : UIPagePresenter`로 제약되어 **모드와 진입점이 컴파일 타임에 일치**한다.
- 팩토리는 인스턴스를 **즉시 반환**(Root 부착 + `OnInitialize` 완료)하고, 실제 Show 전환은 `ShowQueue`에 enqueue된다.
- 반환된 인스턴스에 `.With()`/`.OnShown()` 등을 동기 체인으로 호출해 Show 직전에 파라미터·구독자를 등록한다.

## 4. 컴포넌트 구조

### 4.1 Presenter 계층 (모드별 분리)
참고 레포의 `UIElementBase` 역할. 라이프사이클·빌더·커맨드의 주체.

- **`UIPresenterBase<TView>`** (공통, `TView : UIView`)
  - 8단계 라이프사이클 훅: `OnInitialize`, `OnBeforeShow`, `OnShow`, `OnAfterShow`, `OnBeforeHide`, `OnHide`, `OnAfterHide`, `OnDestroy`
  - 구독자(빌더 콜백): `OnShown`, `OnAfterHide` 등 — per-show로 등록, Hide 완료 시 초기화
  - 커맨드: `Hide()`, `Close()` (매니저에 요청 위임)
  - `View` 프로퍼티 보유
- **`UIPagePresenter<TView>`** : 교체형. 페이지 전용 동작
- **`UIPopupPresenter<TView>`** : 모달(dim + 하위 입력 차단), 스택 push
- **`UIOverlayPresenter<TView>`** : Above/Below HUD, 입력 통과
- **`IConfigurable<TParams>`** : `Configure(TParams)` — `.With(params)`가 호출

### 4.2 View 계층 (공통)
- **`UIView : MonoBehaviour`**
  - 위젯 배선 (`SerializeField`)
  - `[SerializeField] UITransitionAsset _showTransition / _hideTransition` — 인스펙터에서 등장/퇴장 연출 지정 (미지정 시 즉시 표시/숨김)
  - `RectTransform` / `CanvasGroup` 노출 — 트랜지션 적용 대상 (CanvasGroup은 필요 시 자동 확보)
  - `InputEnabled` (GraphicRaycaster 토글)
  - `OnEnterBefore/After`, `OnExitBefore/After` 선택적 훅
  - prefab 루트에 부착

### 4.3 Manager / Controllers
- **`UIManager`** (facade, `IUIManager`)
  - `Page/Popup/Overlay<T>()` 팩토리
  - 캐시 조회 → 없으면 `UIInstanceFactory`로 생성 → `OnInitialize` → 인스턴스 반환
  - Show/Hide/Destroy를 `ShowQueue`에 enqueue
- **`ShowQueue`**
  - 모든 표시 전환을 단일 큐로 **순차 직렬화** (race 제거)
  - UniTask 기반 처리 루프, dispose 시 취소
- **`PageController`** : 활성 page 하나 추적, 새 page Show 시 이전 page Hide 후 교체 (`Active`/`SetActive`/`Clear`)
- **`PopupController`** : 팝업 LIFO 스택. 최상단만 입력 받고 나머지는 모달 차단 (`Push`/`Remove`/`Top`)
- **`OverlayController`** : Above/Below 두 레이어의 상주 집합. 순서 무관 등록/해제
- **`InstanceCache`** : Hide 후 인스턴스를 타입 키로 보관, 다음 Show에 재사용
- **`UIInstanceFactory`** : 키 → prefab 로드 → `Instantiate` → `UIView` 추출 → Presenter 생성 + `_container.Inject()`

### 4.4 Loading / Settings
- **`IUIAssetLoader`** : `GameObject Load(string key)` / `void Release(string key)`
  - `ResourcesUILoader` (기본) — `Resources.Load<GameObject>`
  - `AddressablesUILoader` — `Addressables.LoadAssetAsync<GameObject>().WaitForCompletion()`
- **`UIPrefabAttribute`** : `[UIPrefab("Path/X")]` — Presenter 타입에 부착, 키 결정 (타입→키 매핑 1회 캐싱)
- **`UIManagerSettings`** (ScriptableObject) : 루트 Canvas/레이어 설정, 기본 로더 선택, 모드별 기본 트랜지션

### 4.5 트랜지션 (Transitions)
등장/퇴장 연출을 추상화한다. **트윈 라이브러리(PrimeTween 등)에 의존하지 않고 UniTask 기반 자체 보간으로 구현한다.**

- **`IUITransition`**
  ```csharp
  UniTask PlayShow(RectTransform target, CancellationToken ct);
  UniTask PlayHide(RectTransform target, CancellationToken ct);
  ```
  Fade 대상 `CanvasGroup`은 `target`에서 확보(없으면 추가).
- **`UITransitionAsset : ScriptableObject, IUITransition`** (추상 베이스) — 공통 파라미터: `duration`, `AnimationCurve ease`(트윈 의존 없이 인스펙터에서 곡선 커스터마이즈), unscaled time 여부
- 기본 구현 3종:
  - **`FadeTransitionAsset`** — `CanvasGroup.alpha` 0↔1
  - **`ScaleTransitionAsset`** — `RectTransform.localScale` (from→1)
  - **`SlideTransitionAsset`** — `RectTransform.anchoredPosition` (방향 enum: Left/Right/Top/Bottom)
- **`NoopTransition`** — 즉시 표시/숨김 (기본 폴백)
- 자체 보간: `duration` 동안 매 프레임 `await UniTask.Yield()` → `t = elapsed/duration` → `ease.Evaluate(t)` → `Lerp`. `CancellationToken`으로 중도 취소
- **트랜지션 해석 우선순위** (`UIManager`가 Show/Hide 시 결정):
  1. 빌더 `.WithTransition(IUITransition)` per-show 오버라이드
  2. `UIView`의 `_showTransition` / `_hideTransition` 인스펙터 할당
  3. `UIManagerSettings`의 모드별 기본 트랜지션
  4. `NoopTransition` (즉시)

## 5. 데이터 흐름 (한 번의 Show)

1. `Page<T>()` 호출 → `InstanceCache.TryGet`; 없으면 `UIInstanceFactory.Create` → `OnInitialize` → 인스턴스 반환
2. 호출자가 동기 체인으로 `.With()`/`.OnShown()` 등록
3. 같은 프레임 끝에 enqueue된 Show 작업이 `ShowQueue`에서 실행:
   - 한 프레임 yield(체인 등록 보장) → 이전 page Hide(Page 모드) → 해당 레이어에 Root 부착
   - `OnBeforeShow` → 해석된 트랜지션 `PlayShow` → `OnShow` → `OnAfterShow`
   - 각 단계에서 구독자 발화
4. `Close()`/`Hide()` → 큐에 Hide → `OnBeforeHide` → 해석된 트랜지션 `PlayHide` → 레이어에서 제거 → `OnHide`/`OnAfterHide`
5. Hide 완료 → `InstanceCache.Register`로 **항상 보관**(GameObject 비활성, scope 유지). transient 상태(구독자·파라미터) 초기화. 다음 Show 시 `OnInitialize` 생략하고 재사용

## 6. DI / 생명주기

- **단일 루트 `LifetimeScope`** 에서 `UIManager`, `IUIAssetLoader`, `UIManagerSettings`, 각 컨트롤러를 등록
- Presenter/View는 `_container.Inject()`로 의존성 주입 (element별 child scope 없음)
- `UIManager.Dispose()` 시 활성/캐시 인스턴스 전부 `OnDestroy` + GameObject 파괴

## 7. 테스트 전략 (TDD)

본 프로젝트는 TDD + NSubstitute 중심이다.

- **컨트롤러를 순수 클래스로 분리** → EditMode 단위 테스트: `ShowQueue`(직렬화 순서), `InstanceCache`(보관/재사용), `PageController`(교체), `PopupController`(LIFO·모달 picking), `OverlayController`(등록/해제)
- **Presenter**: `IUIAssetLoader`·매니저 협력자를 NSubstitute로 모킹해 라이프사이클·파라미터 흐름 검증
- **View**(MonoBehaviour)·전환 연출: PlayMode 테스트 최소화
- 테스트 함수명은 한국어 `should~` 형식 (프로젝트 규칙)

## 8. 폴더 구조 (제안)

```
Assets/FoundationDI/Runtime/Managers/UIManager/
  UIManager.cs              IUIManager.cs
  Presenters/
    UIPresenterBase.cs
    UIPagePresenter.cs   UIPopupPresenter.cs   UIOverlayPresenter.cs
    IConfigurable.cs
  Views/
    UIView.cs
  Controllers/
    ShowQueue.cs
    PageController.cs   PopupController.cs   OverlayController.cs
    InstanceCache.cs    UIInstanceFactory.cs
  Loading/
    IUIAssetLoader.cs   ResourcesUILoader.cs   AddressablesUILoader.cs
  Transitions/
    IUITransition.cs   UITransitionAsset.cs   NoopTransition.cs
    FadeTransitionAsset.cs   ScaleTransitionAsset.cs   SlideTransitionAsset.cs
  Settings/
    UIManagerSettings.cs   UIPrefabAttribute.cs
```

기존 `UIManager.cs`/`UIPresenter.cs`/`UIView.cs`/`UISetting.cs`는 제거·대체한다.

**네임스페이스**: 기존 UIManager 계열은 `FoundationDI`, 서비스 계층은 `DarkNaku.FoundationDI`로 혼재한다. 본 리뉴얼에서 UIManager 계열도 **`DarkNaku.FoundationDI`로 통일**한다.

## 9. 미해결/후속

- `IUIAssetLoader`의 Resources→Addressables 폴백 로직을 `PoolService`·`SoundService`와 공통화할지는 별도 작업으로 분리 (이번 스코프 외)
- Addressables 핸들 해제 시점(`Release`) 정책은 구현 단계에서 확정

### 이번 구현 범위에서 빠진 계획 갭 (후속 작업 대상)

1. **Popup dim 시각 배경 미구현**: 설계의 `CreatePopupModalWrapper`(어두운 반투명 배경 오버레이) 생성 로직이 구현되지 않았다. 현재 `UIPopupPresenter`는 최상단 팝업 외 입력 차단(`InputEnabled` 토글)만 동작한다. dim 배경의 시각적 연출이 필요하면 별도 task로 추가 구현한다.

2. **트랜지션 우선순위 3단계 미적용**: 설계 §4.5의 4단계 우선순위 중 3번 단계("`UIManagerSettings`의 모드별 기본 트랜지션")가 미적용 상태다. 현재 해석 순서는 빌더 `.WithTransition()` 오버라이드 > `UIView` 인스펙터 `_showTransition`/`_hideTransition` > `NoopTransition` 3단계로만 동작한다. `UIManagerSettings`에 모드별 기본 트랜지션을 추가하고 이를 3번 우선순위로 삽입하는 작업이 필요하다.
