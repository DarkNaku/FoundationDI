# UIView 입력 차단 — CanvasGroup.interactable 전환 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** UIManager의 입력 차단을 `GraphicRaycaster.enabled`에서 `CanvasGroup.interactable` 기반 요소 단위 통제로 전환하고, 모달 팝업이 하위 레이어(Page/BelowOverlay)까지 차단하게 한다.

**Architecture:** 모든 `UIView`에 `[RequireComponent(typeof(CanvasGroup))]`를 부착해 입력 통제 컴포넌트를 보장한다. `UIView.InputEnabled`는 `CanvasGroup.interactable`을 토글한다. `UIManager`는 활성 집합이 바뀔 때 `RefreshInputBlocking()`으로 각 View의 `InputEnabled`를 규칙에 따라 재계산한다. `blocksRaycasts`/`raycastTarget`은 프리팹 책임으로 두고 시스템은 건드리지 않는다.

**Tech Stack:** Unity 6000.3.17f1, uGUI, UniTask, VContainer, NSubstitute(테스트), Unity Test Framework(EditMode/PlayMode).

## Global Constraints

- 네임스페이스는 `DarkNaku.FoundationDI`.
- 테스트 함수 이름은 한국어로 작성한다.
- **STRUCTURAL 변경과 BEHAVIORAL 변경을 같은 커밋에 섞지 않는다.** 커밋 제목에 `[STRUCTURAL]` 또는 `[BEHAVIORAL]` 접두어를 단다.
- 컴파일·테스트는 UnityMCP로 수행한다. 스크립트 수정 후 `read_console`로 컴파일 에러를 먼저 확인하고(`editor_state.isCompiling == false`), `run_tests`로 EditMode/PlayMode 테스트를 실행한다.
- 모킹은 NSubstitute. 외부 의존(`IResourceService` 등)은 NSubstitute로 대체한다.
- 커밋 메시지 말미에 `Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>` 한 줄을 둔다.

---

## File Structure

- `Assets/FoundationDI/Runtime/Managers/UIManager/Controllers/OverlayController.cs` — Above/Below 상주 집합. **Above/Below 읽기 전용 컬렉션 노출 추가**.
- `Assets/FoundationDI/Runtime/Managers/UIManager/Views/UIView.cs` — **RequireComponent(CanvasGroup) + InputEnabled를 interactable 기반으로 교체, GraphicRaycaster 제거**.
- `Assets/FoundationDI/Runtime/Managers/UIManager/Transitions/UITransitionAsset.cs` — **EnsureCanvasGroup을 GetComponent로 단순화**.
- `Assets/FoundationDI/Runtime/Managers/UIManager/UIManager.cs` — **UpdatePopupModal → RefreshInputBlocking 확장 + 호출 시점 추가**.
- `Assets/FoundationDI/Tests/Editor/UIManager/ModeControllerTests.cs` — OverlayController Above/Below 테스트 추가.
- `Assets/FoundationDI/Tests/Runtime/UIManager/UIViewTests.cs` — InputEnabled가 interactable을 토글하는지로 변경.
- `Assets/FoundationDI/Tests/Runtime/UIManager/UIManagerFlowTests.cs` — 모달 차단 동작 테스트 추가.
- `Assets/FoundationDI/Runtime/Managers/UIManager/README.md`, `CLAUDE.md` — 문서 갱신.

---

## Task 1: OverlayController에 Above/Below 컬렉션 노출 [STRUCTURAL]

**Files:**
- Modify: `Assets/FoundationDI/Runtime/Managers/UIManager/Controllers/OverlayController.cs`
- Test: `Assets/FoundationDI/Tests/Editor/UIManager/ModeControllerTests.cs`

**Interfaces:**
- Produces: `OverlayController.Above` / `OverlayController.Below` (둘 다 `IReadOnlyList<UIPresenterBase>`). Task 4의 `RefreshInputBlocking()`이 이 두 컬렉션을 순회한다.

순수 멤버 추가(읽기 전용 노출)이며 기존 동작을 바꾸지 않으므로 STRUCTURAL.

- [ ] **Step 1: 실패 테스트 작성**

`ModeControllerTests.cs`의 `ModeControllerTests` 클래스 안, 기존 `private class B : UIPopupPresenter<V> { }` 아래에 Overlay용 presenter 타입과 테스트를 추가한다.

```csharp
    private class OverlayAbove : UIOverlayPresenter<V> { }
    private class OverlayBelow : UIOverlayPresenter<V> { protected internal override bool Above => false; }

    [Test]
    public void Overlay는_Above와_Below_집합으로_분리된다()
    {
        var c = new OverlayController();
        var above = new OverlayAbove();
        var below = new OverlayBelow();

        c.Register(above, true);
        c.Register(below, false);

        Assert.AreEqual(1, c.Above.Count);
        Assert.AreSame(above, c.Above[0]);
        Assert.AreEqual(1, c.Below.Count);
        Assert.AreSame(below, c.Below[0]);
    }
```

- [ ] **Step 2: 테스트 실행 — 실패 확인**

UnityMCP `run_tests`(mode: EditMode, test_names: `ModeControllerTests.Overlay는_Above와_Below_집합으로_분리된다`).
Expected: FAIL — `OverlayController`에 `Above`/`Below` 멤버가 없어 컴파일 에러.

- [ ] **Step 3: 최소 구현**

`OverlayController.cs`에 두 읽기 전용 프로퍼티를 추가한다. `using System.Collections.Generic;`은 이미 있음.

```csharp
        public IReadOnlyList<UIPresenterBase> Above => _above;
        public IReadOnlyList<UIPresenterBase> Below => _below;
```

(`private readonly List<UIPresenterBase> _above`/`_below` 선언 바로 아래에 둔다.)

- [ ] **Step 4: 컴파일 확인 후 테스트 실행 — 통과 확인**

`read_console`로 컴파일 에러 0 확인 후, `run_tests`(EditMode, `ModeControllerTests`). Expected: PASS.

- [ ] **Step 5: 커밋**

```bash
git add Assets/FoundationDI/Runtime/Managers/UIManager/Controllers/OverlayController.cs Assets/FoundationDI/Tests/Editor/UIManager/ModeControllerTests.cs
git commit -m "$(printf '%s\n' '[STRUCTURAL] OverlayController에 Above/Below 컬렉션 노출' '' 'Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>')"
```

---

## Task 2: UIView 입력 통제를 CanvasGroup.interactable로 전환 [BEHAVIORAL]

**Files:**
- Modify: `Assets/FoundationDI/Runtime/Managers/UIManager/Views/UIView.cs`
- Test: `Assets/FoundationDI/Tests/Runtime/UIManager/UIViewTests.cs`

**Interfaces:**
- Produces: `UIView.InputEnabled` (bool, get/set) — `CanvasGroup.interactable`을 토글. Task 4가 사용.
- `UIView`에 `[RequireComponent(typeof(CanvasGroup))]`가 부착되어, `AddComponent<TView>()` 시 CanvasGroup이 자동 부착된다.

`InputEnabled`가 토글하는 대상이 `GraphicRaycaster.enabled`에서 `CanvasGroup.interactable`로 바뀌는 동작 변경이므로 BEHAVIORAL.

- [ ] **Step 1: 실패 테스트로 교체**

`UIViewTests.cs` 전체를 아래로 교체한다. (기존은 Canvas+GraphicRaycaster를 부착하고 `GraphicRaycaster.enabled`를 검증한다 — 새 버전은 CanvasGroup만 부착하고 `interactable`을 검증한다.)

```csharp
using NUnit.Framework;
using UnityEngine;
using DarkNaku.FoundationDI;

public class UIViewTests
{
    private class TestView : UIView { }

    [Test]
    public void InputEnabled는_CanvasGroup_interactable을_토글한다()
    {
        var go = new GameObject("v", typeof(RectTransform), typeof(CanvasGroup));
        var view = go.AddComponent<TestView>();

        view.InputEnabled = false;
        Assert.IsFalse(go.GetComponent<CanvasGroup>().interactable);
        view.InputEnabled = true;
        Assert.IsTrue(go.GetComponent<CanvasGroup>().interactable);

        Object.DestroyImmediate(go);
    }
}
```

- [ ] **Step 2: 테스트 실행 — 실패 확인**

`run_tests`(mode: PlayMode, assembly_names: `FoundationDI.Tests.Runtime`, test_names: `UIViewTests.InputEnabled는_CanvasGroup_interactable을_토글한다`).
Expected: FAIL — 현재 `InputEnabled`는 GraphicRaycaster 기반이라 CanvasGroup이 없으면(이 테스트는 CanvasGroup만 부착) setter가 no-op이 되어 `interactable`이 기본값 `true`로 남고 `Assert.IsFalse`에서 실패.

- [ ] **Step 3: UIView 구현 교체**

`UIView.cs` 전체를 아래로 교체한다. (`UnityEngine.UI` using 제거, `_raycaster`/`Raycaster` 제거, CanvasGroup 도입, `[RequireComponent(typeof(CanvasGroup))]` 추가.)

```csharp
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace DarkNaku.FoundationDI
{
    [RequireComponent(typeof(CanvasGroup))]
    public abstract class UIView : MonoBehaviour
    {
        [SerializeField] private UITransitionAsset _showTransition;
        [SerializeField] private UITransitionAsset _hideTransition;

        private static readonly NoopTransition Noop = new();

        private RectTransform _rectTransform;
        public RectTransform RectTransform => _rectTransform ??= (RectTransform)transform;

        private CanvasGroup _canvasGroup;
        private CanvasGroup CanvasGroup => _canvasGroup ??= GetComponent<CanvasGroup>();

        public bool InputEnabled
        {
            get => CanvasGroup.interactable;
            set => CanvasGroup.interactable = value;
        }

        // 우선순위: per-show 오버라이드 > 인스펙터 에셋 > settings 모드 기본값 > Noop
        public IUITransition ShowTransition { get; set; }
        public IUITransition HideTransition { get; set; }
        public IUITransition DefaultTransition { get; set; }

        public virtual void OnInitializeView() { }

        public UniTask PlayShow(CancellationToken ct)
            => (ShowTransition ?? _showTransition ?? DefaultTransition ?? (IUITransition)Noop).PlayShow(RectTransform, ct);

        public UniTask PlayHide(CancellationToken ct)
            => (HideTransition ?? _hideTransition ?? DefaultTransition ?? (IUITransition)Noop).PlayHide(RectTransform, ct);
    }
}
```

- [ ] **Step 4: 컴파일 확인 후 테스트 실행 — 통과 확인**

`read_console`로 컴파일 에러 0 확인. 그다음:
- `run_tests`(PlayMode, `FoundationDI.Tests.Runtime`) → `UIViewTests` 신규 테스트 PASS, 기존 `UIManagerFlowTests`도 PASS(테스트 프리팹은 `AddComponent<V>()` 시 RequireComponent로 CanvasGroup이 자동 부착되므로 영향 없음).
- `run_tests`(EditMode, `FoundationDI.Tests.Editor`) → 전체 PASS.

Expected: 모두 PASS.

- [ ] **Step 5: 커밋**

```bash
git add Assets/FoundationDI/Runtime/Managers/UIManager/Views/UIView.cs Assets/FoundationDI/Tests/Runtime/UIManager/UIViewTests.cs
git commit -m "$(printf '%s\n' '[BEHAVIORAL] UIView 입력 통제를 CanvasGroup.interactable로 전환' '' 'GraphicRaycaster 의존을 제거하고 RequireComponent(CanvasGroup)로 입력 통제' '컴포넌트를 보장한다. InputEnabled는 interactable을 토글한다.' '' 'Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>')"
```

---

## Task 3: 트랜지션 EnsureCanvasGroup 단순화 [STRUCTURAL]

**Files:**
- Modify: `Assets/FoundationDI/Runtime/Managers/UIManager/Transitions/UITransitionAsset.cs`
- Test: `Assets/FoundationDI/Tests/Runtime/UIManager/TransitionAssetTests.cs` (기존 통과 유지)

**Interfaces:**
- Consumes: Task 2가 보장한 `[RequireComponent(typeof(CanvasGroup))]` — 트랜지션 대상(UIView 루트)에 CanvasGroup이 항상 존재.

`AddComponent` 폴백을 제거해도 RequireComponent로 CanvasGroup이 항상 존재하므로 동작이 보존된다 → STRUCTURAL.

- [ ] **Step 1: 구현 단순화**

`UITransitionAsset.cs`의 `EnsureCanvasGroup` 메서드를 아래로 교체한다.

변경 전:
```csharp
        protected static CanvasGroup EnsureCanvasGroup(RectTransform target)
        {
            var cg = target.GetComponent<CanvasGroup>();
            return cg != null ? cg : target.gameObject.AddComponent<CanvasGroup>();
        }
```

변경 후:
```csharp
        // UIView에 RequireComponent(CanvasGroup)가 부착되어 트랜지션 대상에는 항상 CanvasGroup이 존재한다.
        protected static CanvasGroup GetCanvasGroup(RectTransform target) => target.GetComponent<CanvasGroup>();
```

- [ ] **Step 2: 호출처 갱신**

`FadeTransitionAsset.cs`에서 `EnsureCanvasGroup(target)` 호출 2곳을 `GetCanvasGroup(target)`으로 바꾼다.

Run(검색): `grep -n "EnsureCanvasGroup" Assets/FoundationDI/Runtime/Managers/UIManager/Transitions/FadeTransitionAsset.cs`
각 라인의 `EnsureCanvasGroup` → `GetCanvasGroup`으로 치환한다.

- [ ] **Step 3: 컴파일 확인 후 테스트 실행 — 통과 확인**

`read_console`로 컴파일 에러 0 확인 후 `run_tests`(PlayMode, `FoundationDI.Tests.Runtime`, test_names: `TransitionAssetTests`).
Expected: PASS (Fade 트랜지션 동작 동일).

- [ ] **Step 4: 커밋**

```bash
git add Assets/FoundationDI/Runtime/Managers/UIManager/Transitions/UITransitionAsset.cs Assets/FoundationDI/Runtime/Managers/UIManager/Transitions/FadeTransitionAsset.cs
git commit -m "$(printf '%s\n' '[STRUCTURAL] 트랜지션 CanvasGroup 확보를 GetComponent로 단순화' '' 'RequireComponent(CanvasGroup)로 항상 존재하므로 AddComponent 폴백 제거.' '' 'Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>')"
```

---

## Task 4: 모달 팝업이 하위 레이어를 차단 (RefreshInputBlocking) [BEHAVIORAL]

**Files:**
- Modify: `Assets/FoundationDI/Runtime/Managers/UIManager/UIManager.cs`
- Test: `Assets/FoundationDI/Tests/Runtime/UIManager/UIManagerFlowTests.cs`

**Interfaces:**
- Consumes: `OverlayController.Above`/`Below`(Task 1), `UIView.InputEnabled`(Task 2), 기존 `PageController.Current`, `PopupController.All`.
- Produces: `UIManager.RefreshInputBlocking()` (private) — 활성 집합을 기준으로 각 View의 `InputEnabled`를 재계산.

모달 표시 시 하위 레이어 입력을 차단하는 새 동작이므로 BEHAVIORAL.

- [ ] **Step 1: 실패 테스트 작성 (Page 차단)**

`UIManagerFlowTests.cs`의 마지막 테스트(`재Show시_GameObject가_다시_활성화된다`) 아래, 클래스 닫는 `}` 앞에 추가한다.

```csharp
    [UnityTest]
    public IEnumerator 팝업_표시시_하위_Page_입력이_차단되고_팝업은_활성이다() => UniTask.ToCoroutine(async () =>
    {
        var resource = Substitute.For<IResourceService>();
        resource.Load<GameObject>("UI/Sample").Returns(_prefab);
        resource.Load<GameObject>("UI/SamplePopup").Returns(_popupPrefab);
        var resolver = Substitute.For<IObjectResolver>();
        var settings = ScriptableObject.CreateInstance<UIManagerSettings>();
        var factory = new UIInstanceFactory(resolver, resource);
        var manager = new UIManager(settings, factory);

        var page = manager.Page<P>();
        await UniTask.WaitUntil(() => page.Shown);
        Assert.IsTrue(page.ViewBase.InputEnabled, "팝업 없을 때 Page 입력 활성");

        var popup = manager.Popup<PopupP>();
        await UniTask.WaitUntil(() => popup.Shown);
        Assert.IsFalse(page.ViewBase.InputEnabled, "팝업 표시 시 Page 입력 차단");
        Assert.IsTrue(popup.ViewBase.InputEnabled, "최상단 팝업은 입력 활성");

        manager.Dispose();
    });
```

- [ ] **Step 2: 테스트 실행 — 실패 확인**

`run_tests`(PlayMode, `FoundationDI.Tests.Runtime`, test_names: `UIManagerFlowTests.팝업_표시시_하위_Page_입력이_차단되고_팝업은_활성이다`).
Expected: FAIL — 현재 `UpdatePopupModal`은 Page를 건드리지 않아 `page.ViewBase.InputEnabled`가 `true`로 남고 `Assert.IsFalse`에서 실패.

- [ ] **Step 3: RefreshInputBlocking 구현 + 호출처 교체**

`UIManager.cs`에서 다음을 수행한다.

(a) `UpdatePopupModal()` 메서드(현재 157~163줄 근처)를 아래 `RefreshInputBlocking()`으로 **교체**한다.

```csharp
        private void RefreshInputBlocking()
        {
            // 모달 기준선: 현재는 "활성 팝업이 1개 이상이면 모달". 향후 더 위에 뜨는 모달을 추가하면
            // 이 기준선을 그 요소의 렌더 순서로 일반화한다.
            bool hasModal = _popups.All.Count > 0;

            if (_pages.Current != null)
            {
                _pages.Current.ViewBase.InputEnabled = !hasModal;
            }

            var below = _overlays.Below;
            for (int i = 0; i < below.Count; i++)
            {
                below[i].ViewBase.InputEnabled = !hasModal;
            }

            var above = _overlays.Above;
            for (int i = 0; i < above.Count; i++)
            {
                above[i].ViewBase.InputEnabled = true;
            }

            var popups = _popups.All;
            for (int i = 0; i < popups.Count; i++)
            {
                popups[i].ViewBase.InputEnabled = (i == popups.Count - 1);
            }
        }
```

(b) `ShowPageAsync`에서 `AttachTo(presenter, Root.PageLayer);` 다음 줄에 `RefreshInputBlocking();`을 추가한다(`await ShowAsync(...)` 앞).

```csharp
            _pages.SetCurrent(presenter);
            AttachTo(presenter, Root.PageLayer);
            RefreshInputBlocking();

            await ShowAsync(presenter, _settings?.DefaultPageTransition, ct);
```

(c) `ShowOverlayAsync`에서 `AttachTo(...)` 다음에 `RefreshInputBlocking();`을 추가한다.

```csharp
            _overlays.Register(presenter, above);
            AttachTo(presenter, above ? Root.AboveOverlayLayer : Root.BelowOverlayLayer);
            RefreshInputBlocking();
            await ShowAsync(presenter, _settings?.DefaultOverlayTransition, ct);
```

(d) `ShowPopupAsync`에서 기존 `UpdatePopupModal();` 호출을 `RefreshInputBlocking();`으로 바꾼다.

```csharp
            _popups.Add(presenter);
            AttachTo(presenter, Root.PopupLayer);
            RefreshInputBlocking();
            await ShowAsync(presenter, _settings?.DefaultPopupTransition, ct);
```

(e) `HandleHideAsync`에서 기존 `_popups.Remove(e); UpdatePopupModal();` 와 `_overlays.Unregister(e);` 부분을, Unregister를 먼저 한 뒤 Refresh하도록 바꾼다.

변경 전:
```csharp
            if (_pages.Current == e) _pages.Clear();
            _popups.Remove(e); UpdatePopupModal();
            _overlays.Unregister(e);
```
변경 후:
```csharp
            if (_pages.Current == e) _pages.Clear();
            _popups.Remove(e);
            _overlays.Unregister(e);
            RefreshInputBlocking();
```

- [ ] **Step 4: 컴파일 확인 후 테스트 실행 — 통과 확인**

`read_console`로 컴파일 에러 0 확인 후:
- `run_tests`(PlayMode, `FoundationDI.Tests.Runtime`) → 신규 테스트 + 기존 flow 테스트 전부 PASS.
- `run_tests`(EditMode, `FoundationDI.Tests.Editor`) → 전체 PASS.

Expected: 모두 PASS.

- [ ] **Step 5: 실패 테스트 추가 (AboveOverlay 유지)**

같은 파일에 두 번째 테스트를 추가한다. `OverlayP`는 `Above => true`(기본)이다.

```csharp
    [UnityTest]
    public IEnumerator 팝업_표시중_AboveOverlay_입력은_유지된다() => UniTask.ToCoroutine(async () =>
    {
        var resource = Substitute.For<IResourceService>();
        resource.Load<GameObject>("UI/SampleOverlay").Returns(_overlayPrefab);
        resource.Load<GameObject>("UI/SamplePopup").Returns(_popupPrefab);
        var resolver = Substitute.For<IObjectResolver>();
        var settings = ScriptableObject.CreateInstance<UIManagerSettings>();
        var factory = new UIInstanceFactory(resolver, resource);
        var manager = new UIManager(settings, factory);

        var overlay = manager.Overlay<OverlayP>();
        await UniTask.WaitUntil(() => overlay.Shown);

        var popup = manager.Popup<PopupP>();
        await UniTask.WaitUntil(() => popup.Shown);

        Assert.IsTrue(overlay.ViewBase.InputEnabled, "AboveOverlay는 모달 팝업 중에도 입력 유지");

        manager.Dispose();
    });
```

- [ ] **Step 6: 테스트 실행 — 통과 확인**

`run_tests`(PlayMode, `FoundationDI.Tests.Runtime`, test_names: `UIManagerFlowTests.팝업_표시중_AboveOverlay_입력은_유지된다`).
Expected: PASS (Step 3의 구현이 Above를 항상 `true`로 두므로 바로 통과). 회귀가 없는지 `FoundationDI.Tests.Runtime` 전체도 PASS 확인.

- [ ] **Step 7: 커밋**

```bash
git add Assets/FoundationDI/Runtime/Managers/UIManager/UIManager.cs Assets/FoundationDI/Tests/Runtime/UIManager/UIManagerFlowTests.cs
git commit -m "$(printf '%s\n' '[BEHAVIORAL] 모달 팝업이 하위 레이어 입력을 차단' '' 'RefreshInputBlocking으로 Page/BelowOverlay/하위 팝업을 차단하고' 'AboveOverlay와 최상단 팝업은 입력을 유지한다.' '' 'Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>')"
```

---

## Task 5: 문서 갱신 [STRUCTURAL]

**Files:**
- Modify: `Assets/FoundationDI/Runtime/Managers/UIManager/README.md`
- Modify: `CLAUDE.md`

문서만 변경 → STRUCTURAL.

- [ ] **Step 1: README의 InputEnabled 설명 갱신**

`README.md`에서 `InputEnabled` 설명 줄(약 146줄)을 찾아 교체한다.

변경 전:
```
| `bool InputEnabled` | GraphicRaycaster 활성/비활성(모달 입력 차단에 사용) |
```
변경 후:
```
| `bool InputEnabled` | CanvasGroup.interactable 활성/비활성(모달 입력 차단에 사용) |
```

- [ ] **Step 2: README의 Popup 모달 설명 갱신**

`README.md`에서 Popup 모달 설명 줄(약 171줄)을 찾아 차단 범위를 명확히 한다.

변경 전:
```
- **Popup** — LIFO 스택. 여러 개가 쌓이며, **최상단만 입력 활성**(`InputEnabled`)이고 하위는 입력 차단(모달).
```
변경 후:
```
- **Popup** — LIFO 스택. 여러 개가 쌓이며, **최상단 팝업만 입력 활성**이고 하위 팝업·Page·BelowOverlay는 입력 차단(모달). AboveOverlay는 팝업 표시 중에도 입력을 유지한다. 입력 차단은 `CanvasGroup.interactable` 토글이며, 클릭 흡수/통과(`blocksRaycasts`/`raycastTarget`)는 프리팹 책임이다.
```

- [ ] **Step 3: CLAUDE.md의 InputBlocker 관련 설명 확인·갱신**

Run(검색): `grep -n "InputEnabled\|InputBlocker\|GraphicRaycaster" CLAUDE.md`
해당 줄이 있으면 `CanvasGroup.interactable` 기반 설명으로 맞춘다. (없으면 이 스텝은 변경 없이 넘어간다 — 그 경우 README 변경만 커밋.)

- [ ] **Step 4: 커밋**

```bash
git add Assets/FoundationDI/Runtime/Managers/UIManager/README.md CLAUDE.md
git commit -m "$(printf '%s\n' '[STRUCTURAL] 문서: 입력 차단을 CanvasGroup.interactable 기준으로 갱신' '' 'Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>')"
```

---

## 완료 기준

- EditMode(`FoundationDI.Tests.Editor`) 전체 PASS.
- PlayMode(`FoundationDI.Tests.Runtime`) 전체 PASS — 특히 신규 모달 차단 테스트 2종, 기존 flow 테스트 4종, UIViewTests 1종.
- 컴파일 에러/경고 없음.
- STRUCTURAL/BEHAVIORAL 커밋이 섞이지 않음.
