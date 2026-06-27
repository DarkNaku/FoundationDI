# UIManager 샘플 세트 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.
>
> **실행 주의:** 이 계획은 UnityMCP로 프리팹·씬·ScriptableObject 에셋을 생성한다. 서브에이전트는 UnityMCP 세션에 접근하지 못하므로, **에셋 생성·컴파일·테스트 검증은 컨트롤러(메인 세션)가 직접 수행**한다. 스크립트 작성은 서브에이전트가 할 수 있다.

**Goal:** FoundationDI UIManager의 핵심 사용 시나리오 4종(Basic / Page Navigation / Popup Modal / Overlay)을, import 후 바로 동작하는 패키지 공식 샘플로 추가한다.

**Architecture:** `Assets/FoundationDI/Samples~/`에 공통 인프라(`SampleResourceService` 인스펙터 매핑 로더, `SampleLifetimeScope`, settings 에셋)와 4개 샘플(각 씬 + uGUI 프리팹 + Presenter/View/Demo 스크립트)을 둔다. `package.json`의 `samples`로 등록한다.

**Tech Stack:** Unity 6000.3.17f1, uGUI, VContainer, UniTask, FoundationDI UIManager API.

## Global Constraints

- 네임스페이스: 샘플 코드는 `DarkNaku.FoundationDI.Samples`.
- 위치: `Assets/FoundationDI/Samples~/` (UPM `Samples~` 규약 — import 전엔 Unity가 무시).
- 우리 API 사실(verbatim):
  - 표시: `IUIManager.Page<T>()` / `Popup<T>()` / `Overlay<T>()` — 자동-show, `.Show()` 없음. `T : UIPresenter`.
  - 빌더(Page/Popup/Overlay 공통): `OnBeforeShow`/`OnAfterShow`/`OnBeforeHide`/`OnAfterHide`(각 `Action<해당Presenter<TView>>`), `WithTransition(IUITransition)`, `With<TParams>(TParams)`.
  - 파라미터: `IConfigurable<in TParams> { void Configure(TParams); }` 구현 + `.With(params)`.
  - 라이프사이클 훅(오버라이드): `OnInitialize`/`OnBeforeShow`/`OnAfterShow`/`OnBeforeHide`/`OnAfterHide`/`OnDestroyElement`. (OnShow/OnHide 없음)
  - 닫기: `presenter.Hide()`만. 바깥클릭/ESC dismiss 없음.
  - Popup: 항상 모달(표시 중 하위 Page/BelowOverlay/하위팝업 `InputEnabled=false`, AboveOverlay 유지). 같은 타입 이미 활성이면 중복 무시·경고·기존 반환.
  - Overlay: `protected internal virtual bool Above => true` 오버라이드로 Popup 기준 Above/Below.
  - 트랜지션: `FadeTransitionAsset`/`ScaleTransitionAsset`/`SlideTransitionAsset` (ScriptableObject), 기본값은 `UIManagerSettings`.
  - `IResourceService`: `UniTask<T> LoadAsync<T>(string key)`, `T Load<T>(string key)`, `void Release(string key)`, `Dispose()` — 모두 `where T : UnityEngine.Object`.
  - 등록: `builder.RegisterUIManager(UIManagerSettings)` (호출 전 `IResourceService` 등록 필요).
  - View: `[RequireComponent(typeof(CanvasGroup))]` `UIView : MonoBehaviour`. 프리팹 루트에 `Canvas`+`CanvasGroup`+`GraphicRaycaster` 필요(모달 차단·레이캐스트).
- 컴파일·테스트·에셋 생성은 UnityMCP로(컨트롤러 수행). 스크립트 수정 후 `read_console`로 컴파일 에러 0 확인.
- 커밋 메시지 말미: `Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>`.

---

## File Structure

```
Assets/FoundationDI/Samples~/
  Common/
    SampleResourceService.cs        # IResourceService 인스펙터 매핑 로더(MonoBehaviour)
    SampleLifetimeScope.cs          # IResourceService 등록 + RegisterUIManager
    SampleSettings.asset            # UIManagerSettings 인스턴스(모드별 기본 트랜지션)
    Fade.asset / Scale.asset / Slide.asset  # 트랜지션 에셋(샘플 공용)
  01-BasicUsage/   (Scripts/ + Prefabs/ + BasicUsage.unity + README.md)
  02-PageNavigation/
  03-PopupModal/
  04-Overlay/
```
`package.json`에 `samples` 4개 항목 등록.

각 프리팹 루트 컴포넌트(공통): `RectTransform` + `Canvas` + `CanvasGroup` + `GraphicRaycaster` + (해당 `UIView` 파생).

---

## Task 1: 공통 인프라 (로더 + 부트스트랩 + 트랜지션/Settings 에셋)

**Files:**
- Create: `Assets/FoundationDI/Samples~/Common/SampleResourceService.cs`
- Create: `Assets/FoundationDI/Samples~/Common/SampleLifetimeScope.cs`
- Create(에셋, UnityMCP): `Common/Fade.asset`(FadeTransitionAsset), `Common/Scale.asset`(ScaleTransitionAsset), `Common/Slide.asset`(SlideTransitionAsset), `Common/SampleSettings.asset`(UIManagerSettings; Page=Fade, Popup=Scale, Overlay=Fade)

**Interfaces:**
- Produces: `SampleResourceService`(MonoBehaviour, IResourceService) — 인스펙터 `Entry[] _entries`(key, prefab) 매핑. `SampleLifetimeScope`(LifetimeScope) — `[SerializeField] UIManagerSettings _settings; SampleResourceService _resourceService;`.

- [ ] **Step 1: SampleResourceService 작성**

```csharp
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;
using Object = UnityEngine.Object;

namespace DarkNaku.FoundationDI.Samples
{
    /// 샘플용 IResourceService — 인스펙터 (key→prefab) 매핑으로 Addressables 없이 동작.
    public class SampleResourceService : MonoBehaviour, IResourceService
    {
        [System.Serializable]
        public struct Entry { public string key; public GameObject prefab; }

        [SerializeField] private Entry[] _entries;

        private Dictionary<string, GameObject> _map;
        private Dictionary<string, GameObject> Map
        {
            get
            {
                if (_map == null)
                {
                    _map = new Dictionary<string, GameObject>();
                    foreach (var e in _entries) _map[e.key] = e.prefab;
                }
                return _map;
            }
        }

        public UniTask<T> LoadAsync<T>(string key) where T : Object => UniTask.FromResult(Load<T>(key));

        public T Load<T>(string key) where T : Object
        {
            if (Map.TryGetValue(key, out var prefab)) return prefab as T;
            Debug.LogError($"[SampleResourceService] 매핑되지 않은 키: {key}");
            return null;
        }

        public void Release(string key) { }
        public void Dispose() { }
    }
}
```

- [ ] **Step 2: SampleLifetimeScope 작성**

```csharp
using UnityEngine;
using VContainer;
using VContainer.Unity;

namespace DarkNaku.FoundationDI.Samples
{
    public class SampleLifetimeScope : LifetimeScope
    {
        [SerializeField] protected UIManagerSettings _settings;
        [SerializeField] protected SampleResourceService _resourceService;

        protected override void Configure(IContainerBuilder builder)
        {
            builder.RegisterInstance<IResourceService>(_resourceService);
            builder.RegisterUIManager(_settings);
        }
    }
}
```

- [ ] **Step 3: 컴파일 확인**

UnityMCP: `manage_asset`로 `Samples~`는 import 전엔 컴파일 대상이 아니므로, 검증을 위해 **임시로 폴더명을 `Samples`(틸드 없이)로 두고 컴파일** → 확인 후 `Samples~`로 되돌리는 방식 대신, 본 계획에서는 `Samples~`를 유지하되 스크립트 컴파일 검증은 import된 상태를 가정한다. 컨트롤러는 `Samples~`를 일시적으로 `Samples`로 리네임해 `refresh_unity`+`read_console`로 컴파일 에러 0을 확인한 뒤 다시 `Samples~`로 되돌린다.
Expected: 컴파일 에러 0.

- [ ] **Step 4: 트랜지션/Settings 에셋 생성**

UnityMCP `manage_asset`(create, ScriptableObject):
- `Common/Fade.asset` = `FadeTransitionAsset`
- `Common/Scale.asset` = `ScaleTransitionAsset`
- `Common/Slide.asset` = `SlideTransitionAsset`
- `Common/SampleSettings.asset` = `UIManagerSettings`, 필드: `_defaultPageTransition`=Fade, `_defaultPopupTransition`=Scale, `_defaultOverlayTransition`=Fade.

- [ ] **Step 5: 커밋**

```bash
git add Assets/FoundationDI/Samples~/Common
git commit -m "$(printf '%s\n' 'sample: 공통 인프라(SampleResourceService/LifetimeScope/Settings) 추가' '' 'Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>')"
```

---

## Task 2: 01 Basic Usage

**Files:**
- Create: `Samples~/01-BasicUsage/Scripts/` — `MainMenuView.cs`, `MainMenuPage.cs`, `SettingsView.cs`, `SettingsPopup.cs`, `ConfirmView.cs`, `ConfirmPopup.cs`, `HudView.cs`, `HudOverlay.cs`, `BasicUsageDemo.cs`
- Create(UnityMCP): 프리팹 `MainMenu`/`SettingsPopup`/`ConfirmPopup`/`Hud`, 씬 `BasicUsage.unity`, `README.md`

**Interfaces:**
- Consumes: `SampleResourceService`, `SampleLifetimeScope`(Task 1), `IUIManager`.
- Produces: `[UIPrefab]` 키 — `"MainMenu"`, `"SettingsPopup"`, `"ConfirmPopup"`, `"Hud"`.

- [ ] **Step 1: View 스크립트 작성**

```csharp
using UnityEngine;
using UnityEngine.UI;

namespace DarkNaku.FoundationDI.Samples
{
    public class MainMenuView : UIView
    {
        [SerializeField] public Button openPopupButton;
        [SerializeField] public Button openOverlayButton;
    }

    public class SettingsView : UIView
    {
        [SerializeField] public Button openConfirmButton;
        [SerializeField] public Button closeButton;
    }

    public class ConfirmView : UIView
    {
        [SerializeField] public Button closeButton;
    }

    public class HudView : UIView
    {
        [SerializeField] public Text label;
    }
}
```

- [ ] **Step 2: Presenter/Demo 스크립트 작성**

```csharp
using VContainer;
using VContainer.Unity;

namespace DarkNaku.FoundationDI.Samples
{
    [UIPrefab("MainMenu")]
    public class MainMenuPage : UIPagePresenter<MainMenuView>
    {
        [Inject] private IUIManager _ui;
        protected internal override void OnInitialize()
        {
            View.openPopupButton.onClick.AddListener(() => _ui.Popup<SettingsPopup>());
            View.openOverlayButton.onClick.AddListener(() => _ui.Overlay<HudOverlay>());
        }
    }

    [UIPrefab("SettingsPopup")]
    public class SettingsPopup : UIPopupPresenter<SettingsView>
    {
        [Inject] private IUIManager _ui;
        protected internal override void OnInitialize()
        {
            // 서로 다른 타입을 스택에 쌓아 LIFO 시연(같은 타입은 중복 무시됨)
            View.openConfirmButton.onClick.AddListener(() => _ui.Popup<ConfirmPopup>());
            View.closeButton.onClick.AddListener(Hide);
        }
    }

    [UIPrefab("ConfirmPopup")]
    public class ConfirmPopup : UIPopupPresenter<ConfirmView>
    {
        protected internal override void OnInitialize()
            => View.closeButton.onClick.AddListener(Hide);
    }

    [UIPrefab("Hud")]
    public class HudOverlay : UIOverlayPresenter<HudView>
    {
        protected internal override void OnInitialize() => View.label.text = "HUD (Above)";
    }

    public class BasicUsageDemo : IStartable
    {
        private readonly IUIManager _ui;
        public BasicUsageDemo(IUIManager ui) => _ui = ui;
        public void Start() => _ui.Page<MainMenuPage>();
    }
}
```

- [ ] **Step 3: 컴파일 확인**

컨트롤러: `Samples~`→`Samples` 일시 리네임 후 `refresh_unity`+`read_console` 컴파일 에러 0 확인 → `Samples~` 복귀.

- [ ] **Step 4: 프리팹 생성 (UnityMCP)**

각 프리팹 루트: `RectTransform`(stretch full) + `Canvas` + `CanvasGroup` + `GraphicRaycaster` + 해당 View 컴포넌트. 자식 UI:
- `MainMenu`: 세로 배치 Button×2(`openPopupButton`="Open Settings Popup", `openOverlayButton`="Show HUD Overlay") + Title Text. View의 직렬화 필드에 버튼 연결.
- `SettingsPopup`: 중앙 패널(Image, 반투명) + Button×2(`openConfirmButton`="Open Confirm", `closeButton`="Close").
- `ConfirmPopup`: 중앙 패널 + Button(`closeButton`="OK").
- `Hud`: 상단 `Text label`.
프리팹을 `01-BasicUsage/Prefabs/`에 저장.

- [ ] **Step 5: 씬 생성 (UnityMCP)**

`BasicUsage.unity`: Camera + EventSystem + Canvas(또는 UIRoot 자동) + `SampleLifetimeScope` GameObject(컴포넌트: `SampleResourceService`(매핑: 4개 키↔프리팹), `SampleLifetimeScope`(_settings=SampleSettings, _resourceService=위, _entryPoint=BasicUsageDemo는 IStartable이므로 별도 GameObject나 RegisterEntryPoint; 여기서는 SampleLifetimeScope.Configure에서 `builder.RegisterEntryPoint<BasicUsageDemo>()`로 등록하도록 _entryPoint 필드 대신 직접 등록)).

> 구현 메모: `BasicUsageDemo`는 순수 C# IStartable이므로 `SampleLifetimeScope`에 샘플별 등록이 필요하다. Task 1의 `SampleLifetimeScope`를 일반화하기 어렵다면, 각 샘플은 `SampleLifetimeScope`를 상속한 얇은 서브클래스(예: `BasicUsageScope`)에서 `builder.RegisterEntryPoint<BasicUsageDemo>()`를 추가 등록한다. (Task 1 Step 2의 `_entryPoint` 필드는 제거하고 이 서브클래스 방식을 채택.)

- [ ] **Step 6: 동작 검증**

UnityMCP로 `BasicUsage.unity` 로드 → PlayMode 진입 → MainMenu가 표시되는지, 버튼으로 Popup/Overlay가 뜨는지 `read_console`/스크린샷으로 확인(가능 범위). 최소: PlayMode 진입 시 예외 0.

- [ ] **Step 7: README + 커밋**

`01-BasicUsage/README.md`(시연 내용 + 핵심 스니펫). 커밋:
```bash
git add Assets/FoundationDI/Samples~/01-BasicUsage
git commit -m "$(printf '%s\n' 'sample: 01 Basic Usage(Page/Popup/Overlay 기본) 추가' '' 'Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>')"
```

---

## Task 3: 02 Page Navigation

**Files:**
- Create: `Samples~/02-PageNavigation/Scripts/` — `TitleView/Page`, `CharacterListView/Page`, `CharacterDetailView/Page`, `CharacterDetailParams`, `PageNavigationDemo`, `PageNavigationScope`
- Create(UnityMCP): 프리팹 3종 + 씬 + README

**Interfaces:**
- Consumes: Task 1 인프라, `IConfigurable<TParams>`.
- Produces: `[UIPrefab]` 키 `"Title"`, `"CharacterList"`, `"CharacterDetail"`.

- [ ] **Step 1: 스크립트 작성**

```csharp
using UnityEngine;
using UnityEngine.UI;
using VContainer;
using VContainer.Unity;

namespace DarkNaku.FoundationDI.Samples
{
    public struct CharacterDetailParams { public int CharacterId; }

    public class TitleView : UIView { [SerializeField] public Button nextButton; }
    public class CharacterListView : UIView { [SerializeField] public Button[] characterButtons; }
    public class CharacterDetailView : UIView { [SerializeField] public Text idLabel; [SerializeField] public Button backButton; }

    [UIPrefab("Title")]
    public class TitlePage : UIPagePresenter<TitleView>
    {
        [Inject] private IUIManager _ui;
        protected internal override void OnInitialize()
            => View.nextButton.onClick.AddListener(() => _ui.Page<CharacterListPage>());
    }

    [UIPrefab("CharacterList")]
    public class CharacterListPage : UIPagePresenter<CharacterListView>
    {
        [Inject] private IUIManager _ui;
        protected internal override void OnInitialize()
        {
            for (int i = 0; i < View.characterButtons.Length; i++)
            {
                int id = i + 1;
                View.characterButtons[i].onClick.AddListener(() =>
                    _ui.Page<CharacterDetailPage>()
                       .With(new CharacterDetailParams { CharacterId = id })
                       .OnBeforeShow(p => Debug.Log("[Lifecycle] OnBeforeShow"))
                       .OnAfterShow(p => Debug.Log("[Lifecycle] OnAfterShow"))
                       .OnBeforeHide(p => Debug.Log("[Lifecycle] OnBeforeHide"))
                       .OnAfterHide(p => Debug.Log("[Lifecycle] OnAfterHide")));
            }
        }
    }

    [UIPrefab("CharacterDetail")]
    public class CharacterDetailPage : UIPagePresenter<CharacterDetailView>, IConfigurable<CharacterDetailParams>
    {
        [Inject] private IUIManager _ui;
        private CharacterDetailParams _params;
        public void Configure(CharacterDetailParams p) => _params = p;
        protected internal override void OnBeforeShow() => View.idLabel.text = $"Character #{_params.CharacterId}";
        protected internal override void OnInitialize()
            => View.backButton.onClick.AddListener(() => _ui.Page<CharacterListPage>());
    }

    public class PageNavigationDemo : IStartable
    {
        private readonly IUIManager _ui;
        public PageNavigationDemo(IUIManager ui) => _ui = ui;
        public void Start() => _ui.Page<TitlePage>();
    }

    public class PageNavigationScope : SampleLifetimeScope
    {
        protected override void Configure(IContainerBuilder builder)
        {
            base.Configure(builder);
            builder.RegisterEntryPoint<PageNavigationDemo>();
        }
    }
}
```

> 메모: Task 1의 `SampleLifetimeScope.Configure`를 `protected override`로 두고 base 호출 가능하게 한다(이미 그러함). `_entryPoint` 필드는 제거하고 샘플별 Scope 서브클래스에서 `RegisterEntryPoint`로 드라이버 등록.

- [ ] **Step 2: 컴파일 확인** — `Samples~`↔`Samples` 리네임으로 에러 0.

- [ ] **Step 3: 프리팹 3종 생성 (UnityMCP)** — 공통 루트 컴포넌트 + UI:
  - `Title`: Button(`nextButton`="Start").
  - `CharacterList`: Button 배열(`characterButtons`, 3개 "Character 1/2/3").
  - `CharacterDetail`: Text(`idLabel`) + Button(`backButton`="Back").

- [ ] **Step 4: 씬 생성 (UnityMCP)** — `PageNavigation.unity`: EventSystem + `PageNavigationScope`(SampleResourceService 매핑 3키 + _settings).

- [ ] **Step 5: 동작 검증** — PlayMode 진입, Title→List→Detail 전환 + Console에 Lifecycle 로그 4종 + Detail에 CharacterId 표시. 예외 0.

- [ ] **Step 6: README + 커밋**

```bash
git add Assets/FoundationDI/Samples~/02-PageNavigation
git commit -m "$(printf '%s\n' 'sample: 02 Page Navigation(파라미터+라이프사이클 콜백) 추가' '' 'Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>')"
```

---

## Task 4: 03 Popup Modal

**Files:**
- Create: `Samples~/03-PopupModal/Scripts/` — `HostView/Page`, `ConfirmDialogView/Popup`, `PopupModalDemo`, `PopupModalScope`
- Create(UnityMCP): 프리팹 2종 + 씬 + README

**Interfaces:**
- Produces: `[UIPrefab]` 키 `"ModalHost"`, `"ConfirmDialog"`.

- [ ] **Step 1: 스크립트 작성**

```csharp
using UnityEngine;
using UnityEngine.UI;
using VContainer;
using VContainer.Unity;

namespace DarkNaku.FoundationDI.Samples
{
    public class ModalHostView : UIView
    {
        [SerializeField] public Button askButton;   // 모달 표시
        [SerializeField] public Button dummyButton; // 모달 중 비활성 확인용
        [SerializeField] public Text resultLabel;
    }

    public class ConfirmDialogView : UIView
    {
        [SerializeField] public Button yesButton;
        [SerializeField] public Button noButton;
    }

    [UIPrefab("ConfirmDialog")]
    public class ConfirmDialog : UIPopupPresenter<ConfirmDialogView>
    {
        public bool Confirmed { get; private set; }
        protected internal override void OnInitialize()
        {
            View.yesButton.onClick.AddListener(() => { Confirmed = true; Hide(); });
            View.noButton.onClick.AddListener(() => { Confirmed = false; Hide(); });
        }
    }

    [UIPrefab("ModalHost")]
    public class ModalHostPage : UIPagePresenter<ModalHostView>
    {
        [Inject] private IUIManager _ui;
        protected internal override void OnInitialize()
        {
            View.dummyButton.onClick.AddListener(() => Debug.Log("[Modal] dummy clicked"));
            View.askButton.onClick.AddListener(() =>
                _ui.Popup<ConfirmDialog>()
                   .OnAfterHide(p => View.resultLabel.text = p.Confirmed ? "결과: 확인" : "결과: 취소"));
        }
    }

    public class PopupModalDemo : IStartable
    {
        private readonly IUIManager _ui;
        public PopupModalDemo(IUIManager ui) => _ui = ui;
        public void Start() => _ui.Page<ModalHostPage>();
    }

    public class PopupModalScope : SampleLifetimeScope
    {
        protected override void Configure(IContainerBuilder builder)
        {
            base.Configure(builder);
            builder.RegisterEntryPoint<PopupModalDemo>();
        }
    }
}
```

- [ ] **Step 2: 컴파일 확인** — 에러 0.

- [ ] **Step 3: 프리팹 2종 (UnityMCP)**:
  - `ModalHost`: Button(`askButton`="Ask Confirm") + Button(`dummyButton`="Dummy(모달 중 비활성)") + Text(`resultLabel`).
  - `ConfirmDialog`: 중앙 패널 + "정말 삭제할까요?" Text + Button(`yesButton`="확인") + Button(`noButton`="취소"). (모달 차단은 시스템이 보장 — 별도 dim 불필요하나, 풀스크린 반투명 Image를 패널 뒤에 두면 시각적 모달 효과)

- [ ] **Step 4: 씬 (UnityMCP)** — `PopupModal.unity`: EventSystem + `PopupModalScope`(매핑 2키 + settings).

- [ ] **Step 5: 동작 검증** — PlayMode: askButton→다이얼로그 표시, 이때 dummyButton 클릭이 **무반응**(모달 차단 확인), 확인/취소 시 resultLabel 갱신. 예외 0.

- [ ] **Step 6: README + 커밋**

```bash
git add Assets/FoundationDI/Samples~/03-PopupModal
git commit -m "$(printf '%s\n' 'sample: 03 Popup Modal(모달 입력차단+결과 반환) 추가' '' 'Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>')"
```

---

## Task 5: 04 Overlay

**Files:**
- Create: `Samples~/04-Overlay/Scripts/` — `OverlayHostView/Page`, `HudOverlayView/Presenter`(Above), `BackgroundOverlayView/Presenter`(Below), `OverlayDemo`, `OverlayScope`
- Create(UnityMCP): 프리팹 3종 + 씬 + README

**Interfaces:**
- Produces: `[UIPrefab]` 키 `"OverlayHost"`, `"HudAbove"`, `"BackgroundBelow"`.

- [ ] **Step 1: 스크립트 작성**

```csharp
using UnityEngine;
using UnityEngine.UI;
using VContainer;
using VContainer.Unity;

namespace DarkNaku.FoundationDI.Samples
{
    public class OverlayHostView : UIView
    {
        [SerializeField] public Button addScoreButton;
        [SerializeField] public Button popupButton;
    }
    public class HudOverlayView : UIView { [SerializeField] public Text scoreLabel; }
    public class BackgroundOverlayView : UIView { }

    [UIPrefab("HudAbove")]
    public class HudAboveOverlay : UIOverlayPresenter<HudOverlayView>
    {
        private int _score;
        public void AddScore(int amount) { _score += amount; View.scoreLabel.text = $"Score: {_score}"; }
        protected internal override void OnInitialize() => View.scoreLabel.text = "Score: 0";
    }

    [UIPrefab("BackgroundBelow")]
    public class BackgroundBelowOverlay : UIOverlayPresenter<BackgroundOverlayView>
    {
        protected internal override bool Above => false; // Popup 아래(배경)
    }

    [UIPrefab("OverlayHost")]
    public class OverlayHostPage : UIPagePresenter<OverlayHostView>
    {
        [Inject] private IUIManager _ui;
        protected internal override void OnInitialize()
        {
            View.addScoreButton.onClick.AddListener(() =>
            {
                var hud = _ui.Overlay<HudAboveOverlay>(); // 이미 활성이면 기존 인스턴스 반환
                hud.AddScore(10);
            });
            View.popupButton.onClick.AddListener(() =>
                _ui.Popup<ConfirmDialog>()); // Task4의 ConfirmDialog 재사용 시 같은 어셈블리이므로 가능
        }
    }

    public class OverlayDemo : IStartable
    {
        private readonly IUIManager _ui;
        public OverlayDemo(IUIManager ui) => _ui = ui;
        public void Start()
        {
            _ui.Overlay<BackgroundBelowOverlay>().WithTransition(/* Fade */ null); // 기본 트랜지션 사용
            _ui.Page<OverlayHostPage>();
            _ui.Overlay<HudAboveOverlay>();
        }
    }

    public class OverlayScope : SampleLifetimeScope
    {
        protected override void Configure(IContainerBuilder builder)
        {
            base.Configure(builder);
            builder.RegisterEntryPoint<OverlayDemo>();
        }
    }
}
```

> 메모: `popupButton`은 03의 `ConfirmDialog`에 의존한다. 샘플 간 의존을 피하려면 04 전용의 간단 팝업을 별도로 두거나, `popupButton`을 "BelowOverlay 입력 차단 확인용"으로 03 ConfirmDialog 대신 04 내부 팝업으로 만든다. 구현 시 04 내부에 `OverlayConfirm`(닫기 버튼만)을 추가하고 키 `"OverlayConfirm"`을 매핑한다. (`WithTransition(null)`은 오버라이드 없음 의미로, Settings 기본값이 적용되도록 호출 자체를 생략하는 편이 낫다 — 구현 시 `WithTransition` 데모는 Fade/Scale/Slide 에셋을 직접 주입해 보여준다.)

- [ ] **Step 2: 04 전용 팝업 추가 + 트랜지션 데모 정리**

`OverlayConfirmView/Presenter`(닫기 버튼만, `[UIPrefab("OverlayConfirm")]`)를 04에 추가하고 `popupButton`이 이를 띄우게 한다. `OverlayDemo`에서 `WithTransition`을 실제 에셋으로 시연하려면 Scope가 트랜지션 에셋을 주입해 Presenter/Demo에 전달하거나, Demo에서 `Resources`가 아닌 인스펙터로 받은 에셋을 사용한다. 가장 단순하게: `OverlayHostPage`에 버튼 3개(Fade/Scale/Slide)를 두고 각각 `_ui.Popup<OverlayConfirm>().WithTransition(asset)` — 트랜지션 에셋은 `OverlayHostView`의 `[SerializeField] IUITransition`이 불가하므로 `UITransitionAsset` 필드로 받는다.

```csharp
public class OverlayHostView : UIView
{
    [SerializeField] public Button addScoreButton;
    [SerializeField] public Button fadeButton;
    [SerializeField] public Button scaleButton;
    [SerializeField] public Button slideButton;
    [SerializeField] public UITransitionAsset fade;
    [SerializeField] public UITransitionAsset scale;
    [SerializeField] public UITransitionAsset slide;
}
```
`OverlayHostPage.OnInitialize`에서 각 버튼에 `_ui.Popup<OverlayConfirm>().WithTransition(View.fade)` 등 연결.

- [ ] **Step 3: 컴파일 확인** — 에러 0.

- [ ] **Step 4: 프리팹 4종 (UnityMCP)** — `OverlayHost`(버튼들 + 트랜지션 에셋 연결), `HudAbove`(scoreLabel, 상단), `BackgroundBelow`(전체 반투명 Image), `OverlayConfirm`(닫기 버튼).

- [ ] **Step 5: 씬 (UnityMCP)** — `Overlay.unity`: EventSystem + `OverlayScope`(매핑 4키 + settings).

- [ ] **Step 6: 동작 검증** — PlayMode: HUD 상단 표시(Above), Background 배경(Below), addScore로 점수 갱신, Fade/Scale/Slide 버튼으로 팝업 트랜지션 차이, 팝업 표시 시 Background(Below) 입력 차단·HUD(Above) 유지. 예외 0.

- [ ] **Step 7: README + 커밋**

```bash
git add Assets/FoundationDI/Samples~/04-Overlay
git commit -m "$(printf '%s\n' 'sample: 04 Overlay(Above/Below+트랜지션) 추가' '' 'Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>')"
```

---

## Task 6: package.json samples 등록 + 통합 README

**Files:**
- Modify: `Assets/FoundationDI/package.json`
- Create: `Assets/FoundationDI/Samples~/README.md`(선택)

- [ ] **Step 1: package.json에 samples 배열 추가**

`package.json` 루트에 추가:
```json
  "samples": [
    { "displayName": "01 Basic Usage", "description": "Page/Popup/Overlay 기본 표시", "path": "Samples~/01-BasicUsage" },
    { "displayName": "02 Page Navigation", "description": "파라미터 전달과 라이프사이클 콜백", "path": "Samples~/02-PageNavigation" },
    { "displayName": "03 Popup Modal", "description": "모달 입력차단과 결과 반환", "path": "Samples~/03-PopupModal" },
    { "displayName": "04 Overlay", "description": "Above/Below 레이어와 트랜지션", "path": "Samples~/04-Overlay" }
  ]
```
(기존 JSON 유효성 유지 — 마지막 항목 콤마 주의)

- [ ] **Step 2: 검증** — `package.json`이 유효 JSON인지 확인(`python3 -m json.tool Assets/FoundationDI/package.json`).

- [ ] **Step 3: 커밋**

```bash
git add Assets/FoundationDI/package.json Assets/FoundationDI/Samples~/README.md
git commit -m "$(printf '%s\n' 'sample: package.json samples 등록' '' 'Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>')"
```

---

## 완료 기준

- 4개 샘플 씬이 PlayMode에서 예외 없이 동작(각 핵심 시연 확인).
- 컴파일 에러 0(`Samples` 리네임 검증 시).
- `package.json` 유효 JSON + samples 4개 등록.
- 각 샘플 README 존재.

## 알려진 리스크 / 메모

- **Samples~ 컴파일 검증**: Unity는 `Samples~`(틸드)를 import 전 무시하므로, 스크립트 컴파일 검증은 폴더를 일시적으로 `Samples`로 리네임해 수행하고 되돌린다.
- **UnityMCP 프리팹/씬 제작**: uGUI 계층·컴포넌트 연결은 컨트롤러가 `manage_gameobject`/`manage_prefabs`/`manage_scene`/`manage_components`로 수행. 버튼-View 필드 연결(직렬화 참조) 정확성에 주의.
- **PlayMode 진입 타임아웃**: 본 세션에서 PlayMode 테스트 진입이 종종 타임아웃되므로, 검증은 재시도하거나 `read_console` 예외 확인으로 갈음한다.
- **Presenter `[Inject]`**: `UIInstanceFactory`가 `_resolver.Inject(presenter)`로 필드 주입하므로 `[Inject] private IUIManager _ui;`가 동작한다.
