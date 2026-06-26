# UIManager 샘플 세트 설계

**날짜**: 2026-06-27
**대상**: `Assets/FoundationDI/Samples~/` (신규)
**상태**: 설계 승인됨 (구현 계획 대기)

## 배경 / 목표

FoundationDI의 UIManager는 uGUI 기반 UI 표시/전환 시스템이지만, 패키지에 실제 동작하는 예제가 전혀 없다(테스트는 코드로 가짜 프리팹만 생성). DarkNaku의 기존 [UIManager 패키지](https://github.com/DarkNaku/UIManager)가 제공하는 5개 샘플 스타일을 참고하되, **우리 API(uGUI, 자동-show 빌더, Addressables 위임)에 맞게 번안한** 공식 샘플 세트를 추가한다.

**목표**: Package Manager에서 import 후 추가 설정 없이 씬을 열고 Play하면 바로 동작하는, 핵심 사용 시나리오 4종 샘플.

## 핵심 결정 (브레인스토밍 결과)

| 결정 | 내용 | 근거 |
|---|---|---|
| 성격/위치 | **패키지 공식 샘플** — `Assets/FoundationDI/Samples~/` + `package.json`의 `samples` 등록 | repo와 동일한 배포 형태 |
| 동작 깊이 | **완전 동작** — 씬 + uGUI 프리팹 + 샘플 로더 | import 후 바로 실행 |
| 시나리오 범위 | **핵심 4개** (Basic / Page Navigation / Popup Modal / Overlay). Customization 제외 | YAGNI |
| 프리팹 로딩 | **인스펙터 매핑 로더** — `SampleResourceService : IResourceService` | import 직후 동작, Addressables 마찰 회피 |

## 우리 API와 repo의 불일치 (번안 규칙)

repo 개념을 그대로 가져오지 않고, 코드로 확인한 우리 실제 동작에 맞춘다.

| repo 개념 | 우리 실제 (확인됨) | 번안 |
|---|---|---|
| `Screen<T>().Show()` | `Page<T>()` — 자동-show, `.Show()` 없음 | `Page` 사용, 빌더 체인 즉시 |
| NonDismissable (바깥클릭 닫기 토글) | dismiss 메커니즘 **없음** — `presenter.Hide()`로만 닫힘 | 항목 제거. "바깥 클릭 닫기"는 프리팹/Presenter로 직접 구현하는 패턴으로 03에 선택 포함 |
| 8단계 라이프사이클(OnShow/OnHide 포함) | **5단계 이벤트**: `BeforeShow`/`AfterShow`/`BeforeHide`/`AfterHide`/`Destroyed`. OnShow/OnHide **없음** | OnShow/OnHide 시연 제외 |
| `OnShown`/`OnDestroyed` 빌더 | 빌더는 `OnBeforeShow`/`OnAfterShow`/`OnBeforeHide`/`OnAfterHide` **4개만**. Destroyed 구독 빌더 **없음** | 02 콜백 로그를 4종으로 한정 |
| Awaitable 자동 Hide / 결과 await | 그런 API 없음 | Presenter `public TResult Result` + `OnAfterHide(p => use p.Result)` 패턴 |
| 데이터 이벤트 구독(Overlay) | 이벤트 시스템 없음 | Overlay Presenter의 public 갱신 메서드 호출 |
| 같은 타입 Popup → 새 인스턴스 스택 | **정반대** — 같은 타입 이미 활성이면 "중복 무시·경고·기존 인스턴스 반환"(`Acquire`). 같은 타입 다중 불가 | Popup 스택은 **서로 다른 타입**으로 시연. 같은 타입 중복 무시도 함께 보여줌 |
| UXML / UI Toolkit | uGUI(MonoBehaviour View + 프리팹) | uGUI 프리팹(Canvas+CanvasGroup+GraphicRaycaster 루트) |

확인된 우리 API 사실:
- `IConfigurable<in TParams>` (`Presenters/IUIElementHost.cs`) — `.With(params)`로 `Configure(params)` 수신.
- `UIManagerSettings`: `DefaultPageTransition`/`DefaultPopupTransition`/`DefaultOverlayTransition` (`IUITransition`).
- 빌더 메서드(Page/Popup/Overlay 공통): `OnBeforeShow`/`OnAfterShow`/`OnBeforeHide`/`OnAfterHide`/`WithTransition`/`With<TParams>`.
- 모달 입력차단: Popup 표시 시 하위(Page/BelowOverlay/하위팝업)의 `CanvasGroup.interactable=false`, AboveOverlay 유지.
- 트랜지션 3종: `FadeTransitionAsset`/`ScaleTransitionAsset`/`SlideTransitionAsset`.

## 공통 인프라 (`Samples~/Common/`)

- **`SampleResourceService : IResourceService`** — 인스펙터에 `(string key, GameObject prefab)` 리스트를 들고, `Load<T>(key)`/`LoadAsync<T>(key)`가 매핑된 프리팹을 반환. `Release`/`Dispose`는 인스턴스 추적 없는 단순 구현(샘플은 Addressables 핸들이 없으므로 no-op 수준). repo의 "커스텀 로더" 예제 역할도 겸한다.
  - `IResourceService` API 시그니처: `UniTask<T> LoadAsync<T>(string key)`, `T Load<T>(string key)`, `void Release(string key)`, `Dispose()` — 모두 `where T : UnityEngine.Object`.
- **`SampleLifetimeScope : LifetimeScope`** — `builder.RegisterInstance<IResourceService>(sampleResourceService)` + `builder.RegisterUIManager(settings)`. 각 샘플 씬에 배치.
- **`SampleUIManagerSettings`** (ScriptableObject 에셋) — 모드별 기본 트랜지션(예: Page=Fade, Popup=Scale, Overlay=Fade) 지정.
- **uGUI 프리팹 규약**: View 프리팹 루트에 `Canvas`(nested) + `CanvasGroup` + `GraphicRaycaster` + `UIView` 파생. 최소 기능(버튼/라벨). 모달 차단이 `interactable` 기반이므로 CanvasGroup 필수(이미 `[RequireComponent]`).

## 4개 샘플

### 01 Basic Usage (`Samples~/01-BasicUsage/`)
- `MainMenuPage`(Page)에서 버튼으로 `Popup<T>()`/`Overlay<T>()` 호출.
- Popup 스택: `SettingsPopup` 위에 `ConfirmPopup`(서로 다른 타입)을 쌓아 LIFO 시연. 같은 타입 재호출 시 "중복 무시" 경고도 함께 보여줌.
- 핵심: `Page/Popup/Overlay<T>()` 자동-show, `.Show()` 불필요.

### 02 Page Navigation (`Samples~/02-PageNavigation/`)
- 다단계 Page 흐름(예: Title → CharacterList → CharacterDetail).
- `CharacterDetailPage : UIPagePresenter<CharacterDetailView>, IConfigurable<CharacterDetailParams>` — `.With(new CharacterDetailParams{ CharacterId = ... })`.
- 빌더 콜백 4종(`OnBeforeShow`/`OnAfterShow`/`OnBeforeHide`/`OnAfterHide`)으로 라이프사이클 로그 출력.

### 03 Popup Modal (`Samples~/03-PopupModal/`)
- `ConfirmPopup : UIPopupPresenter<ConfirmView>` — 확인/취소 버튼이 `Hide()` 호출.
- 모달 입력차단 시연: 팝업 표시 중 하위 Page 버튼이 비활성(클릭 무반응)됨을 보여줌.
- 결과 반환: `public bool Confirmed` 프로퍼티 + 호출측 `.OnAfterHide(p => { if (p.Confirmed) ... })`.
- (선택) "바깥 클릭으로 닫기"가 필요한 경우, dim 배경 이미지에 버튼을 두어 `Hide()`를 호출하는 패턴을 별도 팝업으로 시연 — "시스템엔 없지만 이렇게 만든다".

### 04 Overlay (`Samples~/04-Overlay/`)
- `HudOverlay`(Above, 기본) / `BackgroundOverlay`(Below, `Above => false`)로 레이어 차이 시연.
- 트랜지션 3종: `.WithTransition(fade/scale/slide)` 오버라이드 + Settings 기본값.
- HUD 데이터 갱신: `HudOverlay`의 public 메서드(예: `SetScore(int)`)를 호출해 갱신.
- 모달과의 관계: Popup을 띄우면 BelowOverlay는 입력 차단, AboveOverlay(HUD)는 유지됨을 함께 보여줌.

## 각 샘플 README

각 샘플 폴더에 짧은 README(무엇을 보여주는지 + 핵심 코드 스니펫 3~5줄).

## 제작 방법

UnityMCP로 프리팹·씬·ScriptableObject를 실제 생성한다. 각 샘플마다:
1. View/Presenter 스크립트 작성 → 컴파일 에러 0 확인.
2. uGUI 프리팹 생성(Canvas/CanvasGroup/GraphicRaycaster + UI 요소) + UIView 파생 부착.
3. 씬 생성(EventSystem, SampleLifetimeScope, SampleResourceService 매핑).
4. PlayMode 스모크(가능 범위) 또는 EditMode로 부트스트랩 검증.

## 파일 구조

```
Assets/FoundationDI/Samples~/
  Common/
    SampleResourceService.cs
    SampleLifetimeScope.cs
    SampleUIManagerSettings.cs        (+ .asset)
  01-BasicUsage/        (씬 + 프리팹 + Presenter/View + README)
  02-PageNavigation/
  03-PopupModal/
  04-Overlay/
```
`package.json`에 `samples` 4개 항목 등록(displayName / description / path).

## 범위 밖

- 05 Customization(커스텀 트랜지션/로더, VContainer 부모-자식 스코프) — 후속.
- UI Toolkit/UXML 버전.
- 정교한 비주얼 디자인(샘플은 동작 시연용 최소 UI).
- Addressables 실제 그룹 셋업(샘플은 인스펙터 매핑 로더로 우회).
