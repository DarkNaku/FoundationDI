# UIManager View 풀링 전환 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** UIManager의 `InstanceCache`(Presenter+View 쌍 캐시)를 제거하고, View는 기존 `PoolManager`(`IPoolItem`)로 풀 재사용, Presenter는 매 show마다 새로 생성하는 구조로 전환한다.

**Architecture:** 무거운 View(GameObject)는 UIManager가 소유한 전용 `PoolManager`로 풀링하고(수명을 UIManager Canvas=DontDestroyOnLoad에 귀속), 가벼운 Presenter는 매번 `Activator.CreateInstance`+`Inject`로 새로 만든다. Presenter 수명이 `create→show→hide→버림`으로 선형화되어 잔류 상태 버그 부류(`ResetTransient`, `_active↔_cache` invariant)가 사라진다.

**Tech Stack:** Unity 6000.3.17f1, VContainer, UniTask, R3, NSubstitute(테스트), NUnit, UnityMCP(컴파일/테스트).

## Global Constraints

- 네임스페이스는 `DarkNaku.FoundationDI` 단일.
- 테스트 함수 이름은 **한국어**로 `should~` 의도를 표현한다.
- **STRUCTURAL 변경과 BEHAVIORAL 변경을 같은 커밋에 섞지 않는다.** 커밋 제목에 `[STRUCTURAL]`/`[BEHAVIORAL]` 접두어.
- 스크립트 생성/수정 후 UnityMCP `read_console`로 컴파일 에러 확인, `editor_state.isCompiling==false` 확인 후 새 타입 사용.
- 모킹은 NSubstitute. 외부 의존은 인터페이스 seam으로 대체.
- 매 사이클(장시간 테스트 제외) 전체 테스트 실행.
- 커밋 메시지 말미:
  `Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>`
- 테스트 실행: EditMode → UnityMCP `run_tests(mode="EditMode")`, PlayMode → `run_tests(mode="PlayMode", assembly_names=["FoundationDI.Tests.Runtime"])`.
- 브랜치 `uimanager-view-pooling`에서 작업(이미 생성됨).

---

## 파일 구조

**생성:**
- `Assets/FoundationDI/Tests/Editor/UIManager/UIViewPoolLifecycleTests.cs` — UIView의 IPoolItem 훅 검증(Task 1)

**수정:**
- `Assets/FoundationDI/Runtime/Managers/UIManager/Views/UIView.cs` — `IPoolItem` 구현 + `OnDestroyView` 추가(Task 1)
- `Assets/FoundationDI/Runtime/Managers/UIManager/Controllers/UIInstanceFactory.cs` — presenter 생성 전용으로 축소(Task 2)
- `Assets/FoundationDI/Runtime/Managers/UIManager/Presenters/UIPresenter.cs` — `Destroyed`/`OnDestroyElement`/`ResetTransient` 제거(Task 2)
- `Assets/FoundationDI/Runtime/Managers/UIManager/UIManager.cs` — 전용 풀 소유, fresh presenter, `_active` 집합화, Dispose 재작성(Task 2)
- `Assets/FoundationDI/Tests/Editor/UIManager/UIInstanceFactoryTests.cs` — 새 팩토리 시그니처(Task 2)
- `Assets/FoundationDI/Tests/Runtime/UIManager/UIManagerFlowTests.cs` — 생성자 갱신 + 재Show 테스트 재작성 + 신규 behavior 테스트(Task 2)
- UIManager `README.md` + 관련 샘플 — 구독 해제 컨벤션 문서화(Task 3)

**삭제:**
- `Assets/FoundationDI/Runtime/Managers/UIManager/Controllers/InstanceCache.cs` (+ `.meta`)(Task 2)
- `Assets/FoundationDI/Tests/Editor/UIManager/InstanceCacheTests.cs` (+ `.meta`)(Task 2)

---

## Task 1: UIView가 IPoolItem을 구현 [STRUCTURAL]

`UIView`에 `IPoolItem` 구현을 **추가**한다. 아직 UIManager는 풀을 쓰지 않으므로 순수 구조적 추가(기존 동작 불변). 풀 어휘는 명시적 인터페이스 구현으로 감추고, subclass에는 `OnInitializeView`(기존)와 `OnDestroyView`(신규)만 노출한다.

**Files:**
- Modify: `Assets/FoundationDI/Runtime/Managers/UIManager/Views/UIView.cs`
- Create: `Assets/FoundationDI/Tests/Editor/UIManager/UIViewPoolLifecycleTests.cs`

**Interfaces:**
- Consumes: `IPoolItem`(기존 `Managers/PoolManager/PoolItem.cs`: `GameObject GO`, `PoolData PD {get;set;}`, `OnCreateItem/OnGetItem/OnReleaseItem/OnDestroyItem`, `Release(float)`).
- Produces:
  - `UIView : MonoBehaviour, IPoolItem`
  - `public virtual void OnInitializeView()` (기존 유지)
  - `protected virtual void OnDestroyView()` (신규)
  - `IPoolItem.OnCreateItem()`은 `OnInitializeView()` 호출 후 `SetActive(false)`
  - `IPoolItem.OnDestroyItem()`은 `OnDestroyView()` 호출

- [ ] **Step 1: 실패하는 테스트 작성**

`Assets/FoundationDI/Tests/Editor/UIManager/UIViewPoolLifecycleTests.cs` 생성:

```csharp
using NUnit.Framework;
using UnityEngine;
using DarkNaku.FoundationDI;

public class UIViewPoolLifecycleTests
{
    private class CountingView : UIView
    {
        public int InitCount;
        public int DestroyCount;
        public override void OnInitializeView() => InitCount++;
        protected override void OnDestroyView() => DestroyCount++;
    }

    [Test]
    public void OnCreateItem은_OnInitializeView를_1회_호출하고_비활성화한다()
    {
        var go = new GameObject("v", typeof(RectTransform), typeof(CanvasGroup));
        var view = go.AddComponent<CountingView>();
        IPoolItem item = view;

        item.OnCreateItem();

        Assert.AreEqual(1, view.InitCount);
        Assert.IsFalse(go.activeSelf);

        Object.DestroyImmediate(go);
    }

    [Test]
    public void OnDestroyItem은_OnDestroyView를_호출한다()
    {
        var go = new GameObject("v", typeof(RectTransform), typeof(CanvasGroup));
        var view = go.AddComponent<CountingView>();
        IPoolItem item = view;

        item.OnDestroyItem();

        Assert.AreEqual(1, view.DestroyCount);

        Object.DestroyImmediate(go);
    }
}
```

- [ ] **Step 2: 컴파일 에러(=미구현) 확인**

UnityMCP `refresh_unity(compile="request")` 후 `read_console(types=["error"])`.
Expected: `CountingView`가 `OnDestroyView`를 override 못 함 / `UIView`가 `IPoolItem` 아님 → 컴파일 에러.

- [ ] **Step 3: UIView에 IPoolItem 구현 추가**

`Views/UIView.cs` 전체를 아래로 교체:

```csharp
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace DarkNaku.FoundationDI
{
    [RequireComponent(typeof(CanvasGroup))]
    public abstract class UIView : MonoBehaviour, IPoolItem
    {
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

        // 해석 우선순위: per-show 오버라이드(Transition) > 부착된 트랜지션 컴포넌트 > Noop
        public IUITransition Transition { get; set; }

        private IUITransition _componentTransition;
        private bool _resolvedComponent;

        private IUITransition ResolveComponent()
        {
            if (!_resolvedComponent)
            {
                _componentTransition = GetComponent<IUITransition>();
                _resolvedComponent = true;
            }
            return _componentTransition;
        }

        private IUITransition Resolve() => Transition ?? ResolveComponent() ?? Noop;

        // 뷰 자체 초기화(물리 인스턴스당 1회) / 파괴 훅. presenter 존재와 무관.
        public virtual void OnInitializeView() { }
        protected virtual void OnDestroyView() { }

        public UniTask ShowAsync(CancellationToken ct) => Resolve().ShowAsync(RectTransform, ct);
        public UniTask HideAsync(CancellationToken ct) => Resolve().HideAsync(RectTransform, ct);

        // === IPoolItem (풀 세부는 명시적 구현으로 감춘다) ===
        GameObject IPoolItem.GO => this != null ? gameObject : null;
        PoolData IPoolItem.PD { get; set; }

        void IPoolItem.OnCreateItem()
        {
            OnInitializeView();
            gameObject.SetActive(false); // 풀 상주 중 비활성
        }

        void IPoolItem.OnGetItem() { } // 활성화는 UIManager show 흐름이 제어

        void IPoolItem.OnReleaseItem()
        {
            if (this == null) return;
            if (gameObject != null) gameObject.SetActive(false);
        }

        void IPoolItem.OnDestroyItem() => OnDestroyView();

        // UI 풀은 지연 반환을 쓰지 않는다(delay 무시).
        void IPoolItem.Release(float delay) => ((IPoolItem)this).PD?.Release(this);
    }
}
```

- [ ] **Step 4: 테스트 통과 확인**

`refresh_unity` → `read_console` 에러 없음 확인 후 `run_tests(mode="EditMode")`.
Expected: `UIViewPoolLifecycleTests` 2개 PASS, 기존 테스트 전부 PASS(동작 불변).

- [ ] **Step 5: 커밋**

```bash
git add Assets/FoundationDI/Runtime/Managers/UIManager/Views/UIView.cs \
        Assets/FoundationDI/Tests/Editor/UIManager/UIViewPoolLifecycleTests.cs
git commit -m "$(cat <<'EOF'
[STRUCTURAL] UIView가 IPoolItem 구현 (OnDestroyView 추가)

풀 어휘는 명시적 인터페이스 구현으로 감추고 subclass에는
OnInitializeView/OnDestroyView만 노출. UIManager 전환 준비(동작 불변).

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 2: 전용 풀 + fresh Presenter로 전환 [BEHAVIORAL]

UIManager를 풀 기반 View 획득 + 매 show fresh Presenter로 재작성한다. `InstanceCache`/`ResetTransient`/`Destroyed`/`OnDestroyElement`를 제거하고 `_active`를 인스턴스 집합으로 바꾼다. 하나의 원자적 행동 변경이므로 단일 커밋.

**Files:**
- Modify: `Assets/FoundationDI/Runtime/Managers/UIManager/Controllers/UIInstanceFactory.cs`
- Modify: `Assets/FoundationDI/Runtime/Managers/UIManager/Presenters/UIPresenter.cs`
- Modify: `Assets/FoundationDI/Runtime/Managers/UIManager/UIManager.cs`
- Delete: `Assets/FoundationDI/Runtime/Managers/UIManager/Controllers/InstanceCache.cs` (+ `.meta`)
- Delete: `Assets/FoundationDI/Tests/Editor/UIManager/InstanceCacheTests.cs` (+ `.meta`)
- Modify: `Assets/FoundationDI/Tests/Editor/UIManager/UIInstanceFactoryTests.cs`
- Modify: `Assets/FoundationDI/Tests/Runtime/UIManager/UIManagerFlowTests.cs`

**Interfaces:**
- Consumes: Task 1의 `UIView : IPoolItem`; 기존 `PoolManager(IResourceService, Transform)`, `IPoolManager.Get<T>(key, parent)`, `IPoolManager.Release(GameObject, float)`, `IPoolManager.Dispose()`; `UIPrefabKeyResolver.Resolve(Type)`.
- Produces:
  - `UIInstanceFactory(IObjectResolver resolver)` — 생성자에서 `IResourceService` 제거
  - `UIPresenter CreatePresenter(Type presenterType, UIView view, IUIElementHost host)`
  - `UIManager(UIManagerSettings settings, UIInstanceFactory factory, IResourceService resource)` — `IResourceService` 추가
  - `UIPresenter.LifecycleEvent` = `{ BeforeShow, AfterShow, BeforeHide, AfterHide }` (`Destroyed` 제거)
  - `UIPresenter`에서 `OnDestroyElement`, `ResetTransient` 제거

- [ ] **Step 1: UIManagerFlowTests 재작성(생성자 갱신 + 재Show 테스트 교체 + 신규 behavior 테스트)**

`Tests/Runtime/UIManager/UIManagerFlowTests.cs`에서:

(1) 프리팹 타입 선언부의 `ReshowV`에 `OnInitializeView` 카운터 추가 — 클래스 선언을 아래로 교체:

```csharp
    // 재Show 테스트용: OnAfterShow / OnInitializeView 호출 횟수 추적
    public class ReshowV : UIView { public int InitCount; public override void OnInitializeView() => InitCount++; }
    [UIPrefab("UI/ReshowSample")]
    public class ReshowP : UIPagePresenter<ReshowV> { public int ShowCount; protected internal override void OnAfterShow() => ShowCount++; }
```

(2) Dispose teardown 검증용 타입을 클래스 필드 선언부(예: `P2` 선언 아래)에 추가:

```csharp
    public class HideTrackV : UIView { public static int DestroyCount; protected override void OnDestroyView() => DestroyCount++; }
    [UIPrefab("UI/HideTrack")]
    public class HideTrackP : UIPagePresenter<HideTrackV>
    {
        public bool Shown; public bool AfterHideCalled;
        protected internal override void OnAfterShow() => Shown = true;
        protected internal override void OnAfterHide() => AfterHideCalled = true;
    }
```

(3) `_prefab`~`_reshowPrefab` 필드 아래에 프리팹 필드 추가 + `Setup`/`Teardown`에 등록:

```csharp
    private GameObject _hideTrackPrefab;
```
Setup 마지막에:
```csharp
        _hideTrackPrefab = new GameObject("hideTrackPrefab", typeof(RectTransform));
        _hideTrackPrefab.AddComponent<HideTrackV>();
```
Teardown 마지막에:
```csharp
        Object.DestroyImmediate(_hideTrackPrefab);
```

(4) 7곳의 생성자 호출을 일괄 치환:
- `new UIInstanceFactory(resolver, resource)` → `new UIInstanceFactory(resolver)`
- `new UIManager(settings, factory)` → `new UIManager(settings, factory, resource)`

(5) 기존 `재Show시_GameObject가_다시_활성화된다` 테스트 전체를 아래로 교체(spec 1·2·3):

```csharp
    [UnityTest]
    public IEnumerator 재Show시_새_Presenter가_생성되고_View는_풀에서_재사용된다() => UniTask.ToCoroutine(async () =>
    {
        var resource = Substitute.For<IResourceService>();
        resource.Load<GameObject>("UI/ReshowSample").Returns(_reshowPrefab);
        var resolver = Substitute.For<IObjectResolver>();
        var settings = ScriptableObject.CreateInstance<UIManagerSettings>();
        var factory = new UIInstanceFactory(resolver);
        var manager = new UIManager(settings, factory, resource);

        var p = manager.Page<ReshowP>();
        await UniTask.WaitUntil(() => p.ShowCount >= 1);
        var view1 = p.ViewBase;
        Assert.AreEqual(1, ((ReshowV)view1).InitCount, "OnInitializeView 1차 1회");

        p.Hide();
        await UniTask.WaitUntil(() => !view1.gameObject.activeSelf);

        var p2 = manager.Page<ReshowP>();
        Assert.AreNotSame(p, p2, "fresh presenter: 새 인스턴스");
        await UniTask.WaitUntil(() => p2.ShowCount >= 1);

        Assert.AreEqual(1, p2.ShowCount, "fresh presenter: ShowCount는 1부터");
        Assert.AreSame(view1, p2.ViewBase, "View는 풀에서 재사용");
        Assert.AreEqual(1, ((ReshowV)p2.ViewBase).InitCount, "OnInitializeView는 물리 1회(재호출 안 됨)");

        manager.Dispose();
    });
```

(6) 파일 끝(마지막 `}` 앞)에 신규 behavior 테스트 3개 추가(spec 5·6·7):

```csharp
    [UnityTest]
    public IEnumerator 같은_타입_팝업을_두번_열면_스택된다() => UniTask.ToCoroutine(async () =>
    {
        var resource = Substitute.For<IResourceService>();
        resource.Load<GameObject>("UI/SamplePopup").Returns(_popupPrefab);
        var resolver = Substitute.For<IObjectResolver>();
        var settings = ScriptableObject.CreateInstance<UIManagerSettings>();
        var factory = new UIInstanceFactory(resolver);
        var manager = new UIManager(settings, factory, resource);

        var p1 = manager.Popup<PopupP>();
        await UniTask.WaitUntil(() => p1.Shown);
        var p2 = manager.Popup<PopupP>();
        await UniTask.WaitUntil(() => p2.Shown);

        Assert.AreNotSame(p1, p2, "같은 타입도 새 인스턴스");
        Assert.IsFalse(p1.ViewBase.InputEnabled, "하위 팝업 입력 차단");
        Assert.IsTrue(p2.ViewBase.InputEnabled, "상단 팝업 입력 활성");

        manager.Dispose();
    });

    [UnityTest]
    public IEnumerator 같은_타입_Page_재요청은_새_인스턴스로_교체된다() => UniTask.ToCoroutine(async () =>
    {
        var resource = Substitute.For<IResourceService>();
        resource.Load<GameObject>("UI/Sample").Returns(_prefab);
        var resolver = Substitute.For<IObjectResolver>();
        var settings = ScriptableObject.CreateInstance<UIManagerSettings>();
        var factory = new UIInstanceFactory(resolver);
        var manager = new UIManager(settings, factory, resource);

        var a = manager.Page<P>();
        await UniTask.WaitUntil(() => a.Shown);
        var viewA = a.ViewBase;

        var a2 = manager.Page<P>();
        await UniTask.WaitUntil(() => a2.Shown);

        Assert.AreNotSame(a, a2, "같은 타입 Page 재요청 = 새 인스턴스(새로고침)");
        Assert.AreSame(viewA, a2.ViewBase, "이전 View는 풀 반환 후 재사용");

        manager.Dispose();
    });

    [UnityTest]
    public IEnumerator Dispose시_활성_presenter의_OnAfterHide발화와_View파괴가_일어난다() => UniTask.ToCoroutine(async () =>
    {
        HideTrackV.DestroyCount = 0;
        var resource = Substitute.For<IResourceService>();
        resource.Load<GameObject>("UI/HideTrack").Returns(_hideTrackPrefab);
        var resolver = Substitute.For<IObjectResolver>();
        var settings = ScriptableObject.CreateInstance<UIManagerSettings>();
        var factory = new UIInstanceFactory(resolver);
        var manager = new UIManager(settings, factory, resource);

        var p = manager.Page<HideTrackP>();
        await UniTask.WaitUntil(() => p.Shown);

        manager.Dispose();
        await UniTask.Yield(); // Object.Destroy 반영

        Assert.IsTrue(p.AfterHideCalled, "Dispose 시 활성 presenter OnAfterHide 발화");
        Assert.AreEqual(1, HideTrackV.DestroyCount, "풀 View 파괴 시 OnDestroyView 호출");
    });
```

- [ ] **Step 2: UIInstanceFactoryTests 재작성**

`Tests/Editor/UIManager/UIInstanceFactoryTests.cs` 전체를 아래로 교체:

```csharp
using NSubstitute;
using NUnit.Framework;
using UnityEngine;
using VContainer;
using DarkNaku.FoundationDI;

public class UIInstanceFactoryTests
{
    private class V : UIView { }
    [UIPrefab("UI/Sample")]
    private class P : UIPagePresenter<V> { }

    [Test]
    public void 주어진_view로_Presenter를_생성하고_바인딩한다()
    {
        var go = new GameObject("v", typeof(RectTransform), typeof(CanvasGroup));
        var view = go.AddComponent<V>();
        var resolver = Substitute.For<IObjectResolver>();
        var host = Substitute.For<IUIElementHost>();

        var factory = new UIInstanceFactory(resolver);
        var presenter = factory.CreatePresenter(typeof(P), view, host);

        Assert.IsInstanceOf<P>(presenter);
        Assert.AreSame(view, ((P)presenter).ViewBase);

        Object.DestroyImmediate(go);
    }
}
```

- [ ] **Step 3: InstanceCache 및 그 테스트 삭제**

```bash
git rm Assets/FoundationDI/Runtime/Managers/UIManager/Controllers/InstanceCache.cs \
       Assets/FoundationDI/Runtime/Managers/UIManager/Controllers/InstanceCache.cs.meta \
       Assets/FoundationDI/Tests/Editor/UIManager/InstanceCacheTests.cs \
       Assets/FoundationDI/Tests/Editor/UIManager/InstanceCacheTests.cs.meta
```

- [ ] **Step 4: 테스트가 실패(컴파일 에러)하는지 확인**

`refresh_unity(compile="request")` → `read_console(types=["error"])`.
Expected: `UIInstanceFactory`에 `CreatePresenter` 없음 / `UIManager` 생성자 인자 불일치 등 컴파일 에러. (=구현 필요)

- [ ] **Step 5: UIInstanceFactory 축소**

`Controllers/UIInstanceFactory.cs` 전체를 아래로 교체:

```csharp
using System;
using VContainer;

namespace DarkNaku.FoundationDI
{
    internal sealed class UIInstanceFactory
    {
        private readonly IObjectResolver _resolver;

        public UIInstanceFactory(IObjectResolver resolver)
        {
            _resolver = resolver;
        }

        // View는 풀이 제공한다. 여기서는 presenter만 생성/주입/바인딩한다.
        public UIPresenter CreatePresenter(Type presenterType, UIView view, IUIElementHost host)
        {
            var presenter = (UIPresenter)Activator.CreateInstance(presenterType);
            _resolver.Inject(presenter);
            presenter.Bind(view, host);
            return presenter;
        }
    }
}
```

- [ ] **Step 6: UIPresenter에서 파괴 훅/ResetTransient 제거**

`Presenters/UIPresenter.cs`에서:

(a) enum에서 `Destroyed` 제거:
```csharp
        public enum LifecycleEvent { BeforeShow, AfterShow, BeforeHide, AfterHide }
```

(b) `ResetTransient` 메서드 전체 삭제:
```csharp
        internal virtual void ResetTransient()
        {
            _subscribers?.Clear();
            TransitionOverride = null;
        }
```
(위 블록 삭제)

(c) `OnDestroyElement` 훅 삭제:
```csharp
        protected internal virtual void OnDestroyElement() { }
```
(위 라인 삭제)

- [ ] **Step 7: UIManager 재작성**

`UIManager.cs` 전체를 아래로 교체:

```csharp
using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using VContainer;

namespace DarkNaku.FoundationDI
{
    public sealed class UIManager : IUIManager, IUIElementHost, IDisposable
    {
        private readonly UIManagerSettings _settings;
        private readonly UIInstanceFactory _factory;
        private readonly IResourceService _resource;
        private readonly OperationQueue _queue = new();
        private readonly PageController _pages = new();
        private readonly PopupController _popups = new();
        private readonly OverlayController _overlays = new();
        private readonly HashSet<UIPresenter> _active = new();
        private UIRoot _root;
        private PoolManager _pool;
        private bool _disposed;

        internal UIManager(UIManagerSettings settings, UIInstanceFactory factory, IResourceService resource)
        {
            _settings = settings;
            _factory = factory;
            _resource = resource;
        }

        private UIRoot Root => _root ??= new UIRoot(_settings != null ? _settings.ReferenceResolution : default);

        // 전용 풀: 루트를 Canvas(DontDestroyOnLoad) 아래에 둬 UIManager와 수명을 함께한다.
        private PoolManager Pool => _pool ??= new PoolManager(_resource, Root.GO.transform);

        public T Page<T>() where T : UIPresenter => Acquire<T>(presenter => _queue.Enqueue(ct => ShowPageAsync(presenter, ct)));
        public T Popup<T>() where T : UIPresenter => Acquire<T>(presenter => _queue.Enqueue(ct => ShowPopupAsync(presenter, ct)));
        public T Overlay<T>() where T : UIPresenter => Acquire<T>(presenter => _queue.Enqueue(ct => ShowOverlayAsync(presenter, ct)));

        private T Acquire<T>(Action<UIPresenter> enqueueShow) where T : UIPresenter
        {
            if (_disposed) throw new ObjectDisposedException(nameof(UIManager));
            var type = typeof(T);
            var key = UIPrefabKeyResolver.Resolve(type);

            var view = Pool.Get<UIView>(key);
            if (view == null)
                throw new InvalidOperationException($"[UIManager] '{key}' View 로드 실패(프리팹 없음 또는 UIView 부재). ({type.Name})");

            var presenter = _factory.CreatePresenter(type, view, this);
            presenter.OnInitialize();       // fresh presenter: 매 show 1회
            _active.Add(presenter);
            enqueueShow(presenter);
            return (T)presenter;
        }

        private async UniTask ShowPageAsync(UIPresenter presenter, CancellationToken ct)
        {
            await UniTask.Yield(PlayerLoopTiming.Update, ct);   // 빌더 체인 등록 보장

            if (_pages.Current != null && _pages.Current != presenter)
            {
                await HideAsync(_pages.Current, ct);
                _pages.Clear();
            }

            _pages.SetCurrent(presenter);
            AttachTo(presenter, Root.PageLayer);
            RefreshInputBlocking();

            await ShowAsync(presenter, ct);
        }

        private async UniTask ShowOverlayAsync(UIPresenter presenter, CancellationToken ct)
        {
            await UniTask.Yield(PlayerLoopTiming.Update, ct);
            var above = (presenter as IOverlayPlacement)?.Above ?? true;
            _overlays.Register(presenter, above);
            AttachTo(presenter, above ? Root.AboveOverlayLayer : Root.BelowOverlayLayer);
            RefreshInputBlocking();
            await ShowAsync(presenter, ct);
        }

        private async UniTask ShowPopupAsync(UIPresenter presenter, CancellationToken ct)
        {
            await UniTask.Yield(PlayerLoopTiming.Update, ct);
            _popups.Add(presenter);
            AttachTo(presenter, Root.PopupLayer);
            RefreshInputBlocking();
            await ShowAsync(presenter, ct);
        }

        private void AttachTo(UIPresenter presenter, Transform layer) => presenter.ViewBase.RectTransform.SetParent(layer, false);

        private async UniTask ShowAsync(UIPresenter presenter, CancellationToken ct)
        {
            presenter.ViewBase.gameObject.SetActive(true); // 풀에서 나온 비활성 View 활성화
            presenter.ViewBase.Transition = presenter.TransitionOverride;
            presenter.OnBeforeShow();
            presenter.Fire(UIPresenter.LifecycleEvent.BeforeShow);
            await presenter.ViewBase.ShowAsync(ct);
            presenter.OnAfterShow();
            presenter.Fire(UIPresenter.LifecycleEvent.AfterShow);
        }

        private async UniTask HideAsync(UIPresenter presenter, CancellationToken ct)
        {
            presenter.OnBeforeHide(); presenter.Fire(UIPresenter.LifecycleEvent.BeforeHide);
            await presenter.ViewBase.HideAsync(ct);
            presenter.OnAfterHide(); presenter.Fire(UIPresenter.LifecycleEvent.AfterHide); // teardown 지점

            _active.Remove(presenter);
            Pool.Release(presenter.ViewBase.gameObject);   // OnReleaseItem: SetActive(false)
        }

        private void RefreshInputBlocking()
        {
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

        void IUIElementHost.RequestHide(UIPresenter presenter) => _queue.Enqueue(ct => HandleHideAsync(presenter, ct));

        private async UniTask HandleHideAsync(UIPresenter presenter, CancellationToken ct)
        {
            // 이미 숨겨졌거나 교체된 경우 중복 Hide 무시.
            if (!_active.Contains(presenter)) return;
            await HideAsync(presenter, ct);
            if (_pages.Current == presenter) _pages.Clear();
            _popups.Remove(presenter);
            _overlays.Unregister(presenter);
            RefreshInputBlocking();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _queue.CancelAndClear();

            // 활성 presenter teardown: 트랜지션 없이 OnBeforeHide→OnAfterHide 동기 발화(구독 해제).
            foreach (var p in _active)
            {
                p.OnBeforeHide(); p.Fire(UIPresenter.LifecycleEvent.BeforeHide);
                p.OnAfterHide(); p.Fire(UIPresenter.LifecycleEvent.AfterHide);
            }

            _active.Clear();
            _pages.Clear();
            _popups.Clear();
            _overlays.Clear();

            _pool?.Dispose(); // OnDestroyItem으로 전 View 파괴 + IResourceService.Release
            if (_root != null && _root.GO != null) UnityEngine.Object.Destroy(_root.GO);
        }
    }

    internal interface IOverlayPlacement { bool Above { get; } }

    public static class UIManagerVContainerExtensions
    {
        /// <summary>
        /// UIManager를 컨테이너에 등록한다.
        /// 전제: 호출 전에 <see cref="IResourceService"/>가 이미 등록되어 있어야 한다
        /// (UIManager 전용 풀과 UIInstanceFactory가 이를 사용).
        /// </summary>
        public static void RegisterUIManager(this IContainerBuilder builder, UIManagerSettings settings)
        {
            builder.RegisterInstance(settings);
            builder.Register<UIInstanceFactory>(Lifetime.Singleton);
            builder.Register<UIManager>(Lifetime.Singleton).As<IUIManager>();
        }
    }
}
```

- [ ] **Step 8: 컴파일 에러 없음 확인**

`refresh_unity(compile="request")` → `read_console(types=["error"])`. Expected: 에러 0건.
(참고: 새 `.cs` 미임포트로 타입 미발견이 나면 `refresh_unity(mode="force", scope="all")` 후 재확인.)

- [ ] **Step 9: 전체 테스트 실행**

- `run_tests(mode="EditMode")` — `UIInstanceFactoryTests.주어진_view로...`, `UIViewPoolLifecycleTests`, `PresenterLifecycleTests`, `ModeControllerTests` 등 PASS.
- `run_tests(mode="PlayMode", assembly_names=["FoundationDI.Tests.Runtime"])` — 재작성/신규 테스트 포함 전부 PASS.

Expected: 모든 테스트 PASS. 실패 시 원인 수정 후 재실행(구현 완료 전까지 커밋 금지).

- [ ] **Step 10: 커밋**

```bash
git add -A
git commit -m "$(cat <<'EOF'
[BEHAVIORAL] UIManager를 전용 풀 + fresh Presenter 구조로 전환

InstanceCache 제거, View는 IPoolItem 풀 재사용, Presenter는 매 show 새로 생성.
_active를 인스턴스 집합화(타입 dedup/경고 제거, 같은 타입 스택 허용).
Destroyed/OnDestroyElement/ResetTransient 제거, teardown은 OnAfterHide로 일원화.

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 3: 구독 해제 컨벤션 검증 + 문서화 [BEHAVIORAL]

fresh presenter가 pooled View 위젯에 건 구독은 `OnAfterHide`에서 해제해야 한다는 계약을, 누수 가드 테스트와 README/샘플로 못박는다.

**Files:**
- Modify: `Assets/FoundationDI/Tests/Runtime/UIManager/UIManagerFlowTests.cs`
- Modify: `Assets/FoundationDI/Runtime/Managers/UIManager/README.md`
- Modify: 해당 샘플(예: `Assets/FoundationDI/Samples~/03-PopupModal/Scripts/PopupModalPresenters.cs`) — 컨벤션 시연

**Interfaces:**
- Consumes: Task 2의 fresh presenter + 풀 재사용 동작.
- Produces: 누수 가드 테스트 `Show_Hide_반복시_View위젯에_중복핸들러가_없다`.

- [ ] **Step 1: 실패 가능한 누수 가드 테스트 작성**

`UIManagerFlowTests.cs`에 타입 선언 추가(클래스 필드 선언부):

```csharp
    // 구독 해제 컨벤션 검증용: OnBeforeShow에서 카운터에 구독, OnAfterHide에서 해제.
    public class SubV : UIView { public System.Action OnTick; public void Tick() => OnTick?.Invoke(); }
    [UIPrefab("UI/Sub")]
    public class SubP : UIPopupPresenter<SubV>
    {
        public static int TickHandlerCalls;
        private System.Action _handler;
        public bool Shown;
        protected internal override void OnAfterShow() => Shown = true;
        protected internal override void OnBeforeShow()
        {
            _handler = () => TickHandlerCalls++;
            View.OnTick += _handler;              // pooled View 위젯에 구독
        }
        protected internal override void OnAfterHide()
        {
            View.OnTick -= _handler;              // 컨벤션: OnAfterHide에서 해제
            _handler = null;
        }
    }
```

프리팹 필드/Setup/Teardown 추가:
```csharp
    private GameObject _subPrefab;
```
Setup:
```csharp
        _subPrefab = new GameObject("subPrefab", typeof(RectTransform));
        _subPrefab.AddComponent<SubV>();
```
Teardown:
```csharp
        Object.DestroyImmediate(_subPrefab);
```

파일 끝에 테스트 추가:
```csharp
    [UnityTest]
    public IEnumerator Show_Hide_반복시_View위젯에_중복핸들러가_없다() => UniTask.ToCoroutine(async () =>
    {
        SubP.TickHandlerCalls = 0;
        var resource = Substitute.For<IResourceService>();
        resource.Load<GameObject>("UI/Sub").Returns(_subPrefab);
        var resolver = Substitute.For<IObjectResolver>();
        var settings = ScriptableObject.CreateInstance<UIManagerSettings>();
        var factory = new UIInstanceFactory(resolver);
        var manager = new UIManager(settings, factory, resource);

        // 1회차 Show → Hide
        var s1 = manager.Popup<SubP>();
        await UniTask.WaitUntil(() => s1.Shown);
        var view = (SubV)s1.ViewBase;
        s1.Hide();
        await UniTask.WaitUntil(() => !view.gameObject.activeSelf);

        // 2회차 Show(같은 View 재사용) → Tick 1회
        var s2 = manager.Popup<SubP>();
        await UniTask.WaitUntil(() => s2.Shown);
        Assert.AreSame(view, s2.ViewBase, "View 풀 재사용 전제");

        SubP.TickHandlerCalls = 0;
        view.Tick();
        Assert.AreEqual(1, SubP.TickHandlerCalls, "이전 presenter 구독이 남지 않아 핸들러는 1회만 호출");

        manager.Dispose();
    });
```

- [ ] **Step 2: 테스트 실행(통과 확인)**

`run_tests(mode="PlayMode", assembly_names=["FoundationDI.Tests.Runtime"])`.
Expected: PASS. (`OnAfterHide` 해제 컨벤션을 지키므로 중복 핸들러 없음. 만약 해제를 빼면 2가 되어 FAIL — 컨벤션의 필요성 증명.)

- [ ] **Step 3: README/샘플에 컨벤션 명시**

`Managers/UIManager/README.md`의 라이프사이클 섹션에 아래 취지를 추가:

```markdown
### 구독 해제 컨벤션 (필수)

Presenter는 매 Show마다 새로 생성되고 View는 풀에서 재사용된다. 따라서
Presenter가 View 위젯(버튼 onClick, R3 Subscribe 등)에 건 구독은 반드시
`OnAfterHide`에서 해제해야 한다. 해제하지 않으면 다음 Show에서 재사용된
View에 이전 구독이 남아 중복/유령 핸들러가 쌓인다.
```

관련 샘플 Presenter에 `OnAfterHide` 해제 예시를 1개 이상 추가(예: `PopupModalPresenters.cs`에서 버튼 구독을 `OnBeforeShow`에 걸고 `OnAfterHide`에서 해제).

- [ ] **Step 4: 전체 테스트 재실행**

`run_tests(mode="EditMode")` + `run_tests(mode="PlayMode", assembly_names=["FoundationDI.Tests.Runtime"])` 모두 PASS 확인.

- [ ] **Step 5: 커밋**

```bash
git add -A
git commit -m "$(cat <<'EOF'
[BEHAVIORAL] 구독 해제 컨벤션 누수 가드 테스트 + 문서화

fresh presenter가 pooled View에 건 구독은 OnAfterHide에서 해제해야 함을
누수 가드 테스트로 검증하고 README/샘플에 명시.

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Self-Review

**Spec 커버리지:**
- 테스트 1(새 인스턴스)/2(View 재사용)/3(OnInitializeView 1회 vs OnInitialize 매번) → Task 2 Step 1(5).
- 테스트 4(OnAfterHide 매 hide) → Task 2 재Show/Dispose 테스트에서 간접 검증 + Task 3에서 직접 활용.
- 테스트 5(같은 타입 팝업 스택) → Task 2 Step 1(6).
- 테스트 6(Page 재요청 교체) → Task 2 Step 1(6).
- 테스트 7(Dispose teardown + View 파괴) → Task 2 Step 1(6).
- 테스트 8(누수 가드) → Task 3.
- 삭제(InstanceCache/ResetTransient/Destroyed/OnDestroyElement) → Task 2 Step 3·6·7.
- 풀 소유/수명(Canvas 아래 전용 PoolManager) → Task 2 Step 7(`Pool` 프로퍼티).
- UIView 훅(OnInitializeView/OnDestroyView, 내부 SetActive) → Task 1.
- 활성화/부모 정책(비활성 상주, show에서 SetActive) → Task 1(OnCreateItem) + Task 2(ShowAsync).
- 구독 해제 컨벤션 문서 → Task 3.

**타입 일관성:** `CreatePresenter(Type, UIView, IUIElementHost)`, `new UIManager(settings, factory, resource)`, `LifecycleEvent{BeforeShow,AfterShow,BeforeHide,AfterHide}`, `Pool.Get<UIView>(key)`, `Pool.Release(GameObject)` — Task 간 시그니처 일치 확인함.

**Placeholder 스캔:** 없음(모든 코드 블록 실제 내용 포함).

**알려진 위험:** 누수 가드는 컨벤션 준수를 전제로 통과 — 컨벤션 미준수 시 FAIL하도록 설계(가치 있음). PoolManager 재사용 시 `Get`이 매번 pool 루트로 reparent하므로 hide에서 별도 detach 불필요(released View는 layer 아래 비활성으로 남아도 Canvas=DontDestroyOnLoad 귀속 유지).
