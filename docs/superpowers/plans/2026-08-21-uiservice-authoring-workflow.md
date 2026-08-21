# UIService 저작 워크플로 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** UI 캔버스 루트를 프리팹으로 만들어 편집 중 보이는 화면이 런타임과 정의상 일치하게 하고, 프리팹 편집 환경과 생성 마법사를 더해 "이름 입력 → 편집 가능한 프리팹이 열린 상태"까지 자동화한다.

**Architecture:** 세 겹이 순서대로 쌓인다. **(1) 런타임** — `UIRoot`를 순수 C# 클래스에서 프리팹 루트에 붙는 MonoBehaviour로 바꾸고, `UIServiceSettings`가 그 프리팹을 들고, `UIService`가 인스턴스화한다. 프리팹 미지정 시 `UIRoot.CreateDefault()`로 폴백한다. **(2) 에디터 환경** — `CreateDefault()`를 재사용해 루트 프리팹을 생성하는 메뉴와, 그 프리팹 인스턴스만 든 씬을 `EditorSettings.prefabUIEnvironment`에 지정하는 메뉴. **(3) 생성 마법사** — 이름/모드를 받아 View·Presenter 스크립트를 쓰고, 도메인 리로드를 건너뛴 뒤 프리팹을 조립하고 프리팹 모드로 진입시킨다.

**Tech Stack:** Unity 6000.3.17f1, uGUI, VContainer(DI), NUnit + NSubstitute 5.3.0, UniTask(테스트 코루틴 어댑터), UnityEditor(`PrefabUtility`/`EditorSceneManager`/`ScriptableSingleton`/`SettingsProvider`/`SessionState`/`DidReloadScripts`).

**Spec:** `docs/superpowers/specs/2026-08-21-uiservice-authoring-workflow-design.md`

## Global Constraints

- 네임스페이스: 런타임 코드는 **`DarkNaku.FoundationDI`**, 에디터 코드는 **`DarkNaku.FoundationDI.Editor`**. 예외 없음.
- 런타임 코드는 `Assets/FoundationDI/Runtime/Services/UIService/` 아래, 에디터 코드는 `Assets/FoundationDI/Editor/UIService/` 아래. `Assets/Scripts/`는 호스트 프로젝트 전용이므로 건드리지 않는다.
- 새 asmdef를 만들지 않는다. 에디터 코드는 기존 **`FoundationDI.Editor`**, 에디터 테스트는 **`FoundationDI.Tests.Editor`**(이미 `FoundationDI.Editor`를 참조함), PlayMode 테스트는 **`FoundationDI.Tests.Runtime`**에 넣는다.
- 테스트 함수 이름은 **한국어**, `should~` 의도. EditMode 동기 테스트는 `[Test]`, 비동기가 필요한 PlayMode 테스트는 `[UnityTest] public IEnumerator 이름() => UniTask.ToCoroutine(async () => { ... });` 형식(기존 `UIServiceFlowTests.cs` 관례).
- **테스트 파일은 부분 수정 대신 `Write`로 통째 교체한다.** UnityMCP 경유 편집에서 부분 편집이 자주 어긋난다.
- **구조적 변경과 행동적 변경을 같은 커밋에 섞지 않는다.** 커밋 제목에 `[STRUCTURAL]` 또는 `[BEHAVIORAL]` 접두어를 단다.
- 한 번에 하나의 테스트만 작성하고, 매번 전체 테스트를 돌린다.
- 컴파일·테스트는 **UnityMCP로만** 가능하다. Unity Editor가 떠 있고 `.mcp.json`의 `http://127.0.0.1:8086/mcp`에 연결되어 있어야 한다. CLI 빌드 명령은 없다.
- 스크립트 생성/수정 후 `read_console`로 컴파일 에러를 먼저 확인한다. `editor_state.isCompiling == false`가 되어야 새 타입을 쓸 수 있다.
- **`UnityEngine.Object`는 `??=`/`?.`로 null 병합하지 않는다.** 파괴된 객체는 "fake-null"이라 `??=`가 의도대로 동작하지 않는다. `if (x == null) x = ...` 형태를 쓴다. `UIRoot`가 MonoBehaviour가 되는 순간 기존 `_root ??= new UIRoot(...)` 코드는 이 함정에 걸린다.
- 메뉴 경로는 기존 관례를 따른다: 도구 창/액션은 `Tools/FoundationDI/UI/...`, 씬 배치는 `GameObject/FoundationDI/...`.
- 작업 브랜치는 **`feature/ui-authoring-workflow`**. 이 프로젝트는 worktree를 쓰지 않는다(UnityMCP가 단일 프로젝트 경로에 붙어 있음).

## 테스트 실행 방법 (모든 Task 공통)

UnityMCP `run_tests` 툴을 쓴다.

```
run_tests(mode="EditMode", testFilter="UIRootPrefabCreatorTests")        # 파일 단위
run_tests(mode="PlayMode", testFilter="UIRootTests.테스트이름")           # 단일 테스트
run_tests(mode="EditMode")                                               # 전체
run_tests(mode="PlayMode")                                               # 전체
```

`Assets/FoundationDI/Tests/Editor/`와 `Assets/FoundationDI/Tests/`(루트)는 **EditMode**, `Assets/FoundationDI/Tests/Runtime/`은 **PlayMode**다. 이 계획에서 `UIRootTests`·`UIServiceRootPrefabTests`는 PlayMode, 나머지 새 테스트는 전부 EditMode다.

**RED 단계의 형태:** 아직 없는 타입/메서드를 참조하는 테스트를 쓰면 테스트 어셈블리가 **컴파일되지 않는다**. 이 리포에서는 그것이 정상적인 RED다. `read_console`에서 "does not contain a definition for ..." 류의 에러를 확인하는 것으로 "실패 확인" 단계를 만족한다.

---

## Task 0: 작업 브랜치 생성

**Files:** 없음

- [ ] **Step 1: 브랜치를 만든다**

```bash
git checkout -b feature/ui-authoring-workflow
git status --short
```

Expected: 브랜치 전환 성공. 작업 트리에 `Assets/Scripts/AdServiceSmokeTest.cs`(기존 미추적 파일)만 남아 있어야 하며, 이 계획에서는 건드리지 않는다.

---

# Phase 1 — 런타임: 프리팹이 단일 진실 소스

## Task 1: `UIRoot`를 MonoBehaviour로 전환하고 `CreateDefault()`를 만든다

**Files:**
- Modify(전체 재작성): `Assets/FoundationDI/Runtime/Services/UIService/Controllers/UIRoot.cs`
- Modify: `Assets/FoundationDI/Runtime/Services/UIService/UIService.cs:34-41` (`Root` getter)
- Test(전체 재작성): `Assets/FoundationDI/Tests/Runtime/UIService/UIRootTests.cs`

**Interfaces:**
- Consumes: 없음
- Produces:
  - `public sealed class UIRoot : MonoBehaviour`
  - `public static Vector2 DefaultReferenceResolution` (= `(1920, 1080)`)
  - `public static UIRoot CreateDefault()` — 새 GameObject에 기본 계층을 조립하고 부착된 `UIRoot`를 반환. **`DontDestroyOnLoad`를 적용하지 않는다.**
  - `public GameObject GO`, `public Transform PageLayer`, `BelowOverlayLayer`, `PopupLayer`, `AboveOverlayLayer` (기존 호출부 시그니처 유지)

- [ ] **Step 1: 실패하는 테스트를 쓴다**

`Assets/FoundationDI/Tests/Runtime/UIService/UIRootTests.cs` 를 아래 내용으로 통째 교체한다. (기존 4개 테스트는 `new UIRoot(...)` 생성자를 쓰므로 더 이상 컴파일되지 않는다. 이 파일이 대체한다.)

```csharp
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
using DarkNaku.FoundationDI;

public class UIRootTests
{
    [Test]
    public void CreateDefault는_4개_레이어가_연결된_UIRoot를_반환한다()
    {
        var root = UIRoot.CreateDefault();

        Assert.IsNotNull(root.PageLayer);
        Assert.IsNotNull(root.BelowOverlayLayer);
        Assert.IsNotNull(root.PopupLayer);
        Assert.IsNotNull(root.AboveOverlayLayer);

        Object.DestroyImmediate(root.GO);
    }
}
```

- [ ] **Step 2: 실패를 확인한다**

Run: `run_tests(mode="PlayMode", testFilter="UIRootTests")`
Expected: 컴파일 실패. `read_console`에 `'UIRoot' does not contain a definition for 'CreateDefault'` 류의 에러가 보인다.

- [ ] **Step 3: 최소 구현 — `UIRoot.cs` 를 통째 교체한다**

```csharp
using UnityEngine;
using UnityEngine.UI;

namespace DarkNaku.FoundationDI
{
    /// <summary>
    /// UI 캔버스 루트. 캔버스 설정과 레이어 구성은 이 컴포넌트가 붙은 "프리팹"이 결정한다.
    /// 인스턴스화 시 코드가 어떤 값도 덮어쓰지 않는다 — 코드가 이 값들을 다루는 유일한 곳은
    /// CreateDefault()이며, 그것은 "기본 프리팹을 조립하는 템플릿"이다.
    /// </summary>
    [RequireComponent(typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster))]
    public sealed class UIRoot : MonoBehaviour
    {
        public static readonly Vector2 DefaultReferenceResolution = new(1920f, 1080f);

        [SerializeField] private RectTransform _pageLayer;
        [SerializeField] private RectTransform _belowOverlayLayer;
        [SerializeField] private RectTransform _popupLayer;
        [SerializeField] private RectTransform _aboveOverlayLayer;

        public GameObject GO => gameObject;
        public Transform PageLayer => _pageLayer;
        public Transform BelowOverlayLayer => _belowOverlayLayer;
        public Transform PopupLayer => _popupLayer;
        public Transform AboveOverlayLayer => _aboveOverlayLayer;

        /// <summary>
        /// 루트 프리팹이 지정되지 않았을 때 쓰는 기본 계층을 조립한다.
        /// 에디터의 "Create UI Root Prefab" 메뉴도 이 메서드를 재사용하므로
        /// 코드 기본값과 프리팹 템플릿이 어긋날 수 없다.
        /// 상주화(DontDestroyOnLoad)는 여기서 하지 않는다 — 에디터에서도 쓰이기 때문이다.
        /// </summary>
        public static UIRoot CreateDefault()
        {
            var go = new GameObject(
                "[UIService]", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));

            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            scaler.referenceResolution = DefaultReferenceResolution;

            var root = go.AddComponent<UIRoot>();

            // 생성 순서 = sibling 순서 = 렌더 순서(아래→위). Overlay는 Popup 기준 Above/Below로 분리된다.
            root._pageLayer = CreateLayer(go.transform, "[Page]");
            root._belowOverlayLayer = CreateLayer(go.transform, "[BelowOverlay]");
            root._popupLayer = CreateLayer(go.transform, "[Popup]");
            root._aboveOverlayLayer = CreateLayer(go.transform, "[AboveOverlay]");

            return root;
        }

        private static RectTransform CreateLayer(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;

            rt.SetParent(parent, false);
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;

            return rt;
        }
    }
}
```

- [ ] **Step 4: `UIService.Root` getter를 고친다**

`UIService.cs`의 아래 블록(현재 34-41행)을

```csharp
        private UIRoot Root
        {
            get
            {
                // 캔버스는 DontDestroyOnLoad라 정상적으로는 파괴되지 않는다.
                // 예외적으로 GO가 파괴되면(fake-null) 참조를 버리고 재구성한다.
                if (_root != null && _root.GO == null) DiscardRoot();
                return _root ??= new UIRoot(_settings != null ? _settings.ReferenceResolution : default);
            }
        }
```

이것으로 교체한다.

```csharp
        private UIRoot Root
        {
            get
            {
                // 캔버스는 DontDestroyOnLoad라 정상적으로는 파괴되지 않는다.
                // 예외적으로 파괴되면(fake-null) 참조를 버리고 재구성한다.
                // UIRoot는 이제 MonoBehaviour다 → ??= 는 fake-null을 못 걸러내므로 쓰지 않는다.
                if (_root == null) DiscardRoot();
                if (_root == null) _root = CreateRoot();
                return _root;
            }
        }

        // 상주화 책임은 서비스가 진다. 루트를 어디서 얻었든(폴백/프리팹) 동일하게 적용한다.
        private UIRoot CreateRoot()
        {
            var root = UIRoot.CreateDefault();
            UnityEngine.Object.DontDestroyOnLoad(root.GO);
            return root;
        }
```

`DiscardRoot()`는 그대로 둔다(`_pool` dispose + `_root = null`). `_root == null`일 때 호출해도 안전하다.

- [ ] **Step 5: 테스트가 통과하는지 확인한다**

Run: `run_tests(mode="PlayMode", testFilter="UIRootTests")`
Expected: PASS (1 test)

- [ ] **Step 6: 전체 테스트를 돌린다**

Run: `run_tests(mode="EditMode")` 그리고 `run_tests(mode="PlayMode")`
Expected: 전부 PASS. 특히 `UIServiceFlowTests`/`UIServiceWithOverlayTests`/`UIServiceSceneResetTests`가 그대로 통과해야 한다(폴백 경로가 기존과 동일한 캔버스를 만들기 때문).

- [ ] **Step 7: 커밋**

```bash
git add Assets/FoundationDI/Runtime/Services/UIService/Controllers/UIRoot.cs \
        Assets/FoundationDI/Runtime/Services/UIService/UIService.cs \
        Assets/FoundationDI/Tests/Runtime/UIService/UIRootTests.cs
git commit -m "[BEHAVIORAL] UIRoot를 프리팹 부착용 MonoBehaviour로 전환

캔버스/레이어 구성을 프리팹이 결정할 수 있도록 UIRoot를 컴포넌트로 바꾸고,
코드 기본값은 CreateDefault() 하나로 모은다. DontDestroyOnLoad 적용 책임은
UIService로 옮긴다(에디터에서도 CreateDefault를 재사용하기 때문)."
```

---

## Task 2: `CreateDefault()`의 캔버스 구성을 특성화 테스트로 고정한다

**Files:**
- Test(전체 재작성): `Assets/FoundationDI/Tests/Runtime/UIService/UIRootTests.cs`

**Interfaces:**
- Consumes: Task 1의 `UIRoot.CreateDefault()`, `UIRoot.DefaultReferenceResolution`
- Produces: 없음(테스트만)

Task 1에서 삭제된 기존 3개 단언(렌더 모드 / 스케일러 / `DontDestroyOnLoad`)을 `CreateDefault()` 기준으로 되살린다. `DontDestroyOnLoad` 단언은 **뒤집힌다** — 이제 `CreateDefault()`는 상주화하지 않는다.

- [ ] **Step 1: 테스트 파일을 통째 교체한다**

```csharp
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
using DarkNaku.FoundationDI;

public class UIRootTests
{
    [Test]
    public void CreateDefault는_4개_레이어가_연결된_UIRoot를_반환한다()
    {
        var root = UIRoot.CreateDefault();

        Assert.IsNotNull(root.PageLayer);
        Assert.IsNotNull(root.BelowOverlayLayer);
        Assert.IsNotNull(root.PopupLayer);
        Assert.IsNotNull(root.AboveOverlayLayer);

        Object.DestroyImmediate(root.GO);
    }

    [Test]
    public void CreateDefault의_레이어는_sibling_순서가_렌더_순서와_같다()
    {
        var root = UIRoot.CreateDefault();

        Assert.AreEqual(0, root.PageLayer.GetSiblingIndex());
        Assert.AreEqual(1, root.BelowOverlayLayer.GetSiblingIndex());
        Assert.AreEqual(2, root.PopupLayer.GetSiblingIndex());
        Assert.AreEqual(3, root.AboveOverlayLayer.GetSiblingIndex());

        Object.DestroyImmediate(root.GO);
    }

    [Test]
    public void CreateDefault는_ScreenSpaceOverlay와_ScaleWithScreenSize_Expand로_구성한다()
    {
        var root = UIRoot.CreateDefault();
        var canvas = root.GO.GetComponent<Canvas>();
        var scaler = root.GO.GetComponent<CanvasScaler>();

        Assert.AreEqual(RenderMode.ScreenSpaceOverlay, canvas.renderMode);
        Assert.AreEqual(CanvasScaler.ScaleMode.ScaleWithScreenSize, scaler.uiScaleMode);
        Assert.AreEqual(CanvasScaler.ScreenMatchMode.Expand, scaler.screenMatchMode);
        Assert.AreEqual(UIRoot.DefaultReferenceResolution, scaler.referenceResolution);

        Object.DestroyImmediate(root.GO);
    }

    [Test]
    public void CreateDefault는_DontDestroyOnLoad를_적용하지_않는다()
    {
        var root = UIRoot.CreateDefault();

        Assert.AreNotEqual("DontDestroyOnLoad", root.GO.scene.name,
            "상주화는 UIService의 책임이다. 에디터 프리팹 조립에도 쓰이므로 여기서 하면 안 된다.");

        Object.DestroyImmediate(root.GO);
    }
}
```

- [ ] **Step 2: 테스트를 돌린다**

Run: `run_tests(mode="PlayMode", testFilter="UIRootTests")`
Expected: PASS (4 tests). Task 1의 구현이 이미 이 계약을 만족하므로 바로 통과해야 한다. 실패하면 Task 1의 `CreateDefault()`를 고친다.

- [ ] **Step 3: 커밋**

```bash
git add Assets/FoundationDI/Tests/Runtime/UIService/UIRootTests.cs
git commit -m "[STRUCTURAL] UIRoot 기본 구성 특성화 테스트 복원

Task 1에서 생성자와 함께 사라진 캔버스/스케일러/상주화 단언을
CreateDefault() 기준으로 되살린다. 프로덕션 코드 변경 없음."
```

---

## Task 3: `UIServiceSettings`가 루트 프리팹을 들고 `UIService`가 그것을 인스턴스화한다

**Files:**
- Modify(전체 재작성): `Assets/FoundationDI/Runtime/Services/UIService/Settings/UIServiceSettings.cs`
- Modify: `Assets/FoundationDI/Runtime/Services/UIService/UIService.cs` (`CreateRoot()`)
- Create: `Assets/FoundationDI/Tests/Runtime/UIService/UIServiceRootPrefabTests.cs`

**Interfaces:**
- Consumes: Task 1의 `UIRoot.CreateDefault()` / `UIRoot.GO`
- Produces:
  - `public UIRoot RootPrefab { get; internal set; }` on `UIServiceSettings` — internal setter는 테스트 전용(`InternalsVisibleTo("FoundationDI.Tests.Runtime")`가 이미 있음)
  - `UIServiceSettings.ReferenceResolution` **삭제됨** — 이후 어떤 Task도 참조하지 않는다

- [ ] **Step 1: 실패하는 테스트를 쓴다**

`Assets/FoundationDI/Tests/Runtime/UIService/UIServiceRootPrefabTests.cs` 를 새로 만든다.

```csharp
using System.Collections;
using Cysharp.Threading.Tasks;
using NSubstitute;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using VContainer;
using DarkNaku.FoundationDI;

public class UIServiceRootPrefabTests
{
    public class V : UIView { }

    [UIPrefab("UI/RootPrefabSample")]
    public class P : UIPagePresenter<V>
    {
        public bool Shown;
        protected internal override void OnAfterShow() => Shown = true;
    }

    // 프리팹 출처를 증명하기 위한 표식. 코드 기본값(1920x1080)과 절대 겹치지 않는 값.
    private static readonly Vector2 Marker = new(1234f, 567f);

    private GameObject _viewPrefab;
    private UIRoot _rootTemplate;

    [SetUp]
    public void SetUp()
    {
        _viewPrefab = new GameObject("view", typeof(RectTransform), typeof(CanvasGroup));
        _viewPrefab.AddComponent<V>();
        _viewPrefab.SetActive(false);

        // Instantiate의 원본은 프리팹 에셋이 아니어도 되므로, 에셋 IO 없이 씬 오브젝트로 대체한다.
        _rootTemplate = UIRoot.CreateDefault();
        _rootTemplate.name = "RootTemplate";
        _rootTemplate.GO.GetComponent<CanvasScaler>().referenceResolution = Marker;
    }

    [TearDown]
    public void TearDown()
    {
        if (_viewPrefab != null) Object.DestroyImmediate(_viewPrefab);
        if (_rootTemplate != null) Object.DestroyImmediate(_rootTemplate.GO);
    }

    private UIService CreateService(UIServiceSettings settings)
    {
        var resource = Substitute.For<IResourceService>();
        resource.Load<GameObject>("UI/RootPrefabSample").Returns(_viewPrefab);
        return new UIService(settings, new UIInstanceFactory(Substitute.For<IObjectResolver>()), resource);
    }

    [UnityTest]
    public IEnumerator Settings에_루트프리팹이_지정되면_그_프리팹을_인스턴스화한다() => UniTask.ToCoroutine(async () =>
    {
        var settings = ScriptableObject.CreateInstance<UIServiceSettings>();
        settings.RootPrefab = _rootTemplate;

        var service = CreateService(settings);
        var p = service.Page<P>();
        await UniTask.WaitUntil(() => p.Shown);

        var clone = p.ViewBase.transform.root.GetComponent<UIRoot>();

        Assert.IsNotNull(clone, "표시된 View는 UIRoot 아래에 있어야 한다");
        Assert.AreNotSame(_rootTemplate, clone, "원본이 아니라 클론이어야 한다");
        Assert.AreEqual(Marker, clone.GO.GetComponent<CanvasScaler>().referenceResolution,
            "캔버스 설정은 프리팹에서 와야 한다(코드가 덮어쓰지 않는다)");
        Assert.AreEqual("DontDestroyOnLoad", clone.GO.scene.name,
            "상주화는 프리팹 경로에서도 적용되어야 한다");

        service.Dispose();
    });

    [UnityTest]
    public IEnumerator Settings에_루트프리팹이_없으면_코드_기본값으로_폴백한다() => UniTask.ToCoroutine(async () =>
    {
        var settings = ScriptableObject.CreateInstance<UIServiceSettings>();

        var service = CreateService(settings);
        var p = service.Page<P>();
        await UniTask.WaitUntil(() => p.Shown);

        var root = p.ViewBase.transform.root.GetComponent<UIRoot>();

        Assert.IsNotNull(root);
        Assert.AreEqual(UIRoot.DefaultReferenceResolution,
            root.GO.GetComponent<CanvasScaler>().referenceResolution);
        Assert.AreEqual("DontDestroyOnLoad", root.GO.scene.name);

        service.Dispose();
    });
}
```

- [ ] **Step 2: 실패를 확인한다**

Run: `run_tests(mode="PlayMode", testFilter="UIServiceRootPrefabTests")`
Expected: 컴파일 실패. `read_console`에 `'UIServiceSettings' does not contain a definition for 'RootPrefab'`.

- [ ] **Step 3: `UIServiceSettings.cs` 를 통째 교체한다**

```csharp
using UnityEngine;

namespace DarkNaku.FoundationDI
{
    [CreateAssetMenu(fileName = "UIServiceSettings", menuName = "DarkNaku/UIServiceSettings")]
    public sealed class UIServiceSettings : ScriptableObject
    {
        // UIService가 런타임에 인스턴스화할 캔버스 루트 프리팹.
        // 캔버스 렌더 모드/CanvasScaler/레이어 구성은 전부 이 프리팹이 결정한다.
        // 비워두면 UIRoot.CreateDefault()로 폴백한다.
        [SerializeField] private UIRoot _rootPrefab;

        // setter는 테스트 전용이다(InternalsVisibleTo). 런타임에는 인스펙터가 유일한 설정 경로다.
        public UIRoot RootPrefab
        {
            get => _rootPrefab;
            internal set => _rootPrefab = value;
        }
    }
}
```

- [ ] **Step 4: `UIService.CreateRoot()` 를 프리팹 우선으로 바꾼다**

Task 1에서 만든 `CreateRoot()`를 이것으로 교체한다.

```csharp
        // 상주화 책임은 서비스가 진다. 루트를 어디서 얻었든(프리팹/폴백) 동일하게 적용한다.
        private UIRoot CreateRoot()
        {
            var prefab = _settings != null ? _settings.RootPrefab : null;
            var root = prefab != null
                ? UnityEngine.Object.Instantiate(prefab)
                : UIRoot.CreateDefault();

            UnityEngine.Object.DontDestroyOnLoad(root.GO);

            return root;
        }
```

- [ ] **Step 5: 테스트가 통과하는지 확인한다**

Run: `run_tests(mode="PlayMode", testFilter="UIServiceRootPrefabTests")`
Expected: PASS (2 tests)

- [ ] **Step 6: 전체 테스트를 돌린다**

Run: `run_tests(mode="EditMode")` 그리고 `run_tests(mode="PlayMode")`
Expected: 전부 PASS. `ReferenceResolution`을 참조하는 코드가 남아 있으면 여기서 컴파일 에러로 드러난다 — 조사 결과 참조처는 `UIService.cs` 한 곳뿐이며 Task 1에서 이미 제거됐다.

- [ ] **Step 7: 커밋**

```bash
git add Assets/FoundationDI/Runtime/Services/UIService/Settings/UIServiceSettings.cs \
        Assets/FoundationDI/Runtime/Services/UIService/UIService.cs \
        Assets/FoundationDI/Tests/Runtime/UIService/UIServiceRootPrefabTests.cs
git commit -m "[BEHAVIORAL] UIServiceSettings가 루트 프리팹을 지정하도록 변경

기준 해상도 필드를 제거하고 UIRoot 프리팹 참조로 대체한다. UIService는
지정된 프리팹을 인스턴스화하고, 미지정 시 CreateDefault()로 폴백한다.
BREAKING: UIServiceSettings.ReferenceResolution 제거."
```

---

# Phase 2 — 에디터: 프리팹 편집 환경

## Task 4: 루트 프리팹 생성 메뉴

**Files:**
- Create: `Assets/FoundationDI/Editor/UIService/UIRootPrefabCreator.cs`
- Create: `Assets/FoundationDI/Tests/Editor/UIService/UIRootPrefabCreatorTests.cs`

**Interfaces:**
- Consumes: Task 1의 `UIRoot.CreateDefault()`
- Produces: `public static UIRoot CreateAt(string assetPath)` in `DarkNaku.FoundationDI.Editor.UIRootPrefabCreator` — 프리팹 에셋을 저장하고 **에셋에 붙은** `UIRoot` 컴포넌트를 반환한다

- [ ] **Step 1: 실패하는 테스트를 쓴다**

`Assets/FoundationDI/Tests/Editor/UIService/UIRootPrefabCreatorTests.cs`

```csharp
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using DarkNaku.FoundationDI;
using DarkNaku.FoundationDI.Editor;

public class UIRootPrefabCreatorTests
{
    private const string Path = "Assets/__UIRootPrefabCreatorTests__.prefab";

    [TearDown]
    public void TearDown() => AssetDatabase.DeleteAsset(Path);

    [Test]
    public void 생성된_프리팹은_4개_레이어가_자기_자식으로_연결되어_있다()
    {
        var asset = UIRootPrefabCreator.CreateAt(Path);

        Assert.IsNotNull(asset, "프리팹 에셋의 UIRoot를 반환해야 한다");
        Assert.IsNotNull(asset.PageLayer);
        Assert.IsNotNull(asset.BelowOverlayLayer);
        Assert.IsNotNull(asset.PopupLayer);
        Assert.IsNotNull(asset.AboveOverlayLayer);

        // SaveAsPrefabAsset이 참조를 에셋 내부로 리매핑했는지 — 씬 오브젝트를 가리키면 안 된다.
        Assert.AreSame(asset.transform, asset.PageLayer.parent);
        Assert.AreSame(asset.transform, asset.AboveOverlayLayer.parent);
    }

    [Test]
    public void 생성된_프리팹은_CreateDefault와_같은_캔버스_구성을_갖는다()
    {
        var asset = UIRootPrefabCreator.CreateAt(Path);
        var scaler = asset.GetComponent<CanvasScaler>();

        Assert.AreEqual(RenderMode.ScreenSpaceOverlay, asset.GetComponent<Canvas>().renderMode);
        Assert.AreEqual(CanvasScaler.ScaleMode.ScaleWithScreenSize, scaler.uiScaleMode);
        Assert.AreEqual(CanvasScaler.ScreenMatchMode.Expand, scaler.screenMatchMode);
        Assert.AreEqual(UIRoot.DefaultReferenceResolution, scaler.referenceResolution);
    }

    [Test]
    public void 프리팹을_만든_뒤_씬에_임시_오브젝트가_남지_않는다()
    {
        UIRootPrefabCreator.CreateAt(Path);

        var leftovers = Object.FindObjectsByType<UIRoot>(FindObjectsSortMode.None);

        Assert.AreEqual(0, leftovers.Length, "조립용 임시 GameObject는 저장 후 파괴되어야 한다");
    }
}
```

- [ ] **Step 2: 실패를 확인한다**

Run: `run_tests(mode="EditMode", testFilter="UIRootPrefabCreatorTests")`
Expected: 컴파일 실패. `The name 'UIRootPrefabCreator' does not exist`.

- [ ] **Step 3: 구현**

`Assets/FoundationDI/Editor/UIService/UIRootPrefabCreator.cs`

```csharp
using UnityEditor;
using UnityEngine;

namespace DarkNaku.FoundationDI.Editor
{
    /// <summary>
    /// UIService의 캔버스 루트 프리팹을 만든다. 계층 조립은 런타임 폴백과 동일한
    /// UIRoot.CreateDefault()를 재사용하므로 코드 기본값과 프리팹이 어긋날 수 없다.
    /// </summary>
    public static class UIRootPrefabCreator
    {
        private const string DefaultFileName = "UIRoot.prefab";

        [MenuItem("Tools/FoundationDI/UI/Create UI Root Prefab", false, 60)]
        private static void CreateFromMenu()
        {
            var path = EditorUtility.SaveFilePanelInProject(
                "Create UI Root Prefab", DefaultFileName, "prefab",
                "UIService가 런타임에 인스턴스화할 캔버스 루트 프리팹을 저장할 위치를 선택하세요.");

            if (string.IsNullOrEmpty(path)) return;

            var asset = CreateAt(path);

            Selection.activeObject = asset;
            EditorGUIUtility.PingObject(asset);
        }

        /// <summary>지정한 경로에 루트 프리팹을 저장하고 에셋의 UIRoot를 반환한다.</summary>
        public static UIRoot CreateAt(string assetPath)
        {
            var temp = UIRoot.CreateDefault();

            try
            {
                var saved = PrefabUtility.SaveAsPrefabAsset(temp.GO, assetPath);
                return saved != null ? saved.GetComponent<UIRoot>() : null;
            }
            finally
            {
                // 조립용 임시 오브젝트는 어떤 경우에도 씬에 남기지 않는다.
                Object.DestroyImmediate(temp.GO);
            }
        }
    }
}
```

- [ ] **Step 4: 테스트가 통과하는지 확인한다**

Run: `run_tests(mode="EditMode", testFilter="UIRootPrefabCreatorTests")`
Expected: PASS (3 tests)

- [ ] **Step 5: 전체 테스트**

Run: `run_tests(mode="EditMode")` 그리고 `run_tests(mode="PlayMode")`
Expected: 전부 PASS

- [ ] **Step 6: 커밋**

```bash
git add Assets/FoundationDI/Editor/UIService/ \
        Assets/FoundationDI/Tests/Editor/UIService/UIRootPrefabCreatorTests.cs
git commit -m "[BEHAVIORAL] UI 루트 프리팹 생성 메뉴 추가

Tools/FoundationDI/UI/Create UI Root Prefab. 계층 조립은 UIRoot.CreateDefault()를
재사용해 런타임 폴백과 동일함을 보장한다."
```

---

## Task 5: 프리팹 편집 환경 씬 생성/지정/해제

**Files:**
- Create: `Assets/FoundationDI/Editor/UIService/UIEditingEnvironment.cs`
- Create: `Assets/FoundationDI/Tests/Editor/UIService/UIEditingEnvironmentTests.cs`

**Interfaces:**
- Consumes: Task 1의 `UIRoot`, Task 3의 `UIServiceSettings.RootPrefab`(메뉴가 루트 프리팹을 찾을 때)
- Produces:
  - `public static void Assign(SceneAsset scene)` — `EditorSettings.prefabUIEnvironment`에 지정
  - `public static void Clear()` — 지정 해제
  - `public static SceneAsset Build(string scenePath, UIRoot rootPrefab)` — 루트 프리팹 인스턴스 하나만 든 씬을 저장하고 `SceneAsset`을 반환

`Build`는 씬 IO를 하므로 EditMode 테스트에서 다루기 까다롭다. **테스트는 `Assign`/`Clear`만 검증**하고, `Build`는 수동 검증 항목이다(Step 6).

- [ ] **Step 1: 실패하는 테스트를 쓴다**

`Assets/FoundationDI/Tests/Editor/UIService/UIEditingEnvironmentTests.cs`

```csharp
using NUnit.Framework;
using UnityEditor;
using DarkNaku.FoundationDI.Editor;

public class UIEditingEnvironmentTests
{
    private SceneAsset _original;

    [SetUp]
    public void SetUp() => _original = EditorSettings.prefabUIEnvironment;

    [TearDown]
    public void TearDown() => EditorSettings.prefabUIEnvironment = _original;

    [Test]
    public void Assign은_프리팹_UI_편집환경을_지정한다()
    {
        // 프로젝트에 이미 존재하는 아무 씬 에셋이나 픽스처로 쓴다.
        var guids = AssetDatabase.FindAssets("t:SceneAsset");

        Assert.Greater(guids.Length, 0, "픽스처로 쓸 씬 에셋이 프로젝트에 하나도 없다");

        var scene = AssetDatabase.LoadAssetAtPath<SceneAsset>(AssetDatabase.GUIDToAssetPath(guids[0]));

        UIEditingEnvironment.Assign(scene);

        Assert.AreSame(scene, EditorSettings.prefabUIEnvironment);
    }

    [Test]
    public void Clear는_프리팹_UI_편집환경_지정을_해제한다()
    {
        var guids = AssetDatabase.FindAssets("t:SceneAsset");
        var scene = AssetDatabase.LoadAssetAtPath<SceneAsset>(AssetDatabase.GUIDToAssetPath(guids[0]));

        UIEditingEnvironment.Assign(scene);
        UIEditingEnvironment.Clear();

        Assert.IsNull(EditorSettings.prefabUIEnvironment);
    }
}
```

- [ ] **Step 2: 실패를 확인한다**

Run: `run_tests(mode="EditMode", testFilter="UIEditingEnvironmentTests")`
Expected: 컴파일 실패. `The name 'UIEditingEnvironment' does not exist`.

- [ ] **Step 3: 구현**

`Assets/FoundationDI/Editor/UIService/UIEditingEnvironment.cs`

```csharp
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace DarkNaku.FoundationDI.Editor
{
    /// <summary>
    /// UI 프리팹을 "런타임과 같은 캔버스 안에서" 편집할 수 있도록,
    /// 루트 프리팹 인스턴스 하나만 든 씬을 만들어 EditorSettings.prefabUIEnvironment에 지정한다.
    /// 이 설정은 프리팹을 "격리(isolation) 모드"로 열 때만 적용된다
    /// (= 프로젝트 창에서 더블클릭했을 때).
    /// 프로젝트 설정을 바꾸므로 자동 실행 없이 명시적 메뉴 실행으로만 동작한다.
    /// </summary>
    public static class UIEditingEnvironment
    {
        private const string DefaultFileName = "UIEditingEnvironment.unity";

        [MenuItem("Tools/FoundationDI/UI/Setup Prefab Editing Environment", false, 61)]
        private static void SetupFromMenu()
        {
            var rootPrefab = PromptForRootPrefab();

            if (rootPrefab == null) return;

            var path = EditorUtility.SaveFilePanelInProject(
                "Create UI Editing Environment Scene", DefaultFileName, "unity",
                "UI 프리팹 편집 환경으로 쓸 씬을 저장할 위치를 선택하세요.");

            if (string.IsNullOrEmpty(path)) return;

            var scene = Build(path, rootPrefab);

            if (scene == null) return;

            Assign(scene);

            Debug.Log($"[FoundationDI] UI 프리팹 편집 환경을 '{path}'로 지정했습니다. " +
                      "이제 UI 프리팹을 더블클릭하면 실제 캔버스 안에서 열립니다.");
        }

        [MenuItem("Tools/FoundationDI/UI/Clear Prefab Editing Environment", false, 62)]
        private static void ClearFromMenu()
        {
            Clear();
            Debug.Log("[FoundationDI] UI 프리팹 편집 환경 지정을 해제했습니다.");
        }

        public static void Assign(SceneAsset scene) => EditorSettings.prefabUIEnvironment = scene;

        public static void Clear() => EditorSettings.prefabUIEnvironment = null;

        /// <summary>루트 프리팹 인스턴스 하나만 든 씬을 저장하고 SceneAsset을 반환한다.</summary>
        public static SceneAsset Build(string scenePath, UIRoot rootPrefab)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);

            try
            {
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(rootPrefab.GO, scene);

                instance.name = rootPrefab.name;

                if (!EditorSceneManager.SaveScene(scene, scenePath)) return null;
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
            }

            AssetDatabase.Refresh();

            return AssetDatabase.LoadAssetAtPath<SceneAsset>(scenePath);
        }

        // Settings 에셋이 하나뿐이면 그것의 루트 프리팹을, 아니면 선택된 프리팹을 쓴다.
        private static UIRoot PromptForRootPrefab()
        {
            var selected = Selection.activeGameObject != null
                ? Selection.activeGameObject.GetComponent<UIRoot>()
                : null;

            if (selected != null) return selected;

            var guids = AssetDatabase.FindAssets("t:UIServiceSettings");

            foreach (var guid in guids)
            {
                var settings = AssetDatabase.LoadAssetAtPath<UIServiceSettings>(
                    AssetDatabase.GUIDToAssetPath(guid));

                if (settings != null && settings.RootPrefab != null) return settings.RootPrefab;
            }

            EditorUtility.DisplayDialog("UI Editing Environment",
                "루트 프리팹을 찾지 못했습니다.\n\n" +
                "Tools/FoundationDI/UI/Create UI Root Prefab 으로 먼저 프리팹을 만들고 " +
                "UIServiceSettings에 연결하거나, 프로젝트 창에서 루트 프리팹을 선택한 뒤 다시 실행하세요.",
                "확인");

            return null;
        }
    }
}
```

- [ ] **Step 4: 테스트가 통과하는지 확인한다**

Run: `run_tests(mode="EditMode", testFilter="UIEditingEnvironmentTests")`
Expected: PASS (2 tests)

- [ ] **Step 5: 전체 테스트**

Run: `run_tests(mode="EditMode")` 그리고 `run_tests(mode="PlayMode")`
Expected: 전부 PASS

- [ ] **Step 6: 수동 검증 — 실제로 편해졌는지 확인한다 (이 Task의 진짜 목표)**

Unity Editor에서 직접:

1. `Tools/FoundationDI/UI/Create UI Root Prefab` → `Assets/Prefabs/UIRoot.prefab` 저장
2. `Assets/Settings/UIServiceSettings.asset`의 Root Prefab에 연결
3. `Tools/FoundationDI/UI/Setup Prefab Editing Environment` 실행
4. `Assets/Resources/MenuPage.prefab`을 프로젝트 창에서 **더블클릭**

확인할 것:
- 프리팹이 실제 캔버스 안에서 **올바른 크기/스케일로** 보인다
- 씬에는 아무 오브젝트도 생기지 않는다
- **프리팹이 어느 노드 아래에 배치되는지 기록한다** (첫 `Canvas` 바로 아래로 예상되나 공식 문서에 명시가 없다). `[Page]` 레이어 아래가 아니어도 캔버스 스케일이 정확하면 목표 달성이다. 결과를 아래 Task 10의 README에 적는다.

- [ ] **Step 7: 커밋**

```bash
git add Assets/FoundationDI/Editor/UIService/UIEditingEnvironment.cs \
        Assets/FoundationDI/Tests/Editor/UIService/UIEditingEnvironmentTests.cs
git commit -m "[BEHAVIORAL] UI 프리팹 편집 환경 씬 생성/지정 메뉴 추가

루트 프리팹 인스턴스만 든 씬을 만들어 EditorSettings.prefabUIEnvironment에
지정한다. UI 프리팹을 더블클릭하면 런타임과 동일한 캔버스 안에서 열리고,
씬에 임시 오브젝트를 만들 필요가 사라진다."
```

---

# Phase 3 — 에디터: UI 요소 생성 마법사

## Task 6: 이름 검증 · 경로 · 로드 키 (순수 함수)

**Files:**
- Create: `Assets/FoundationDI/Editor/UIService/UIElementMode.cs`
- Create: `Assets/FoundationDI/Editor/UIService/UIElementNaming.cs`
- Create: `Assets/FoundationDI/Tests/Editor/UIService/UIElementNamingTests.cs`

**Interfaces:**
- Consumes: 없음
- Produces:
  - `public enum UIElementMode { Page, Popup, Overlay }`
  - `public static bool UIElementNaming.TryValidate(string name, out string error)`
  - `public static string UIElementNaming.ResolveResourceKey(string prefabAssetPath)`

- [ ] **Step 1: 실패하는 테스트를 쓴다**

`Assets/FoundationDI/Tests/Editor/UIService/UIElementNamingTests.cs`

```csharp
using NUnit.Framework;
using DarkNaku.FoundationDI.Editor;

public class UIElementNamingTests
{
    [TestCase("Shop")]
    [TestCase("ShopPopup")]
    [TestCase("_Shop")]
    [TestCase("Shop2")]
    public void 유효한_이름은_통과한다(string name)
    {
        Assert.IsTrue(UIElementNaming.TryValidate(name, out var error), error);
        Assert.IsEmpty(error);
    }

    [TestCase("", "비어")]
    [TestCase("   ", "비어")]
    [TestCase("2Shop", "숫자")]
    [TestCase("My Shop", "식별자")]
    [TestCase("Shop-1", "식별자")]
    [TestCase("class", "예약어")]
    public void 유효하지_않은_이름은_이유와_함께_거부된다(string name, string reasonKeyword)
    {
        Assert.IsFalse(UIElementNaming.TryValidate(name, out var error));
        StringAssert.Contains(reasonKeyword, error);
    }

    [Test]
    public void Resources_아래_프리팹은_Resources_기준_상대경로가_키가_된다()
    {
        Assert.AreEqual("UI/Shop",
            UIElementNaming.ResolveResourceKey("Assets/Resources/UI/Shop.prefab"));
    }

    [Test]
    public void 중첩된_Resources_폴더도_마지막_Resources를_기준으로_한다()
    {
        Assert.AreEqual("Shop",
            UIElementNaming.ResolveResourceKey("Assets/Game/Resources/Shop.prefab"));
    }

    [Test]
    public void Resources_밖의_프리팹은_경로_전체가_Addressables_주소가_된다()
    {
        Assert.AreEqual("Assets/UI/Shop.prefab",
            UIElementNaming.ResolveResourceKey("Assets/UI/Shop.prefab"));
    }
}
```

- [ ] **Step 2: 실패를 확인한다**

Run: `run_tests(mode="EditMode", testFilter="UIElementNamingTests")`
Expected: 컴파일 실패. `The name 'UIElementNaming' does not exist`.

- [ ] **Step 3: 구현**

`Assets/FoundationDI/Editor/UIService/UIElementMode.cs`

```csharp
namespace DarkNaku.FoundationDI.Editor
{
    /// <summary>생성할 UI 요소의 표시 모드. Presenter 기반 클래스와 프리팹 템플릿을 결정한다.</summary>
    public enum UIElementMode
    {
        Page,
        Popup,
        Overlay,
    }
}
```

`Assets/FoundationDI/Editor/UIService/UIElementNaming.cs`

```csharp
using System;
using System.Text.RegularExpressions;

namespace DarkNaku.FoundationDI.Editor
{
    /// <summary>UI 요소 이름 검증과 프리팹 경로 → 로드 키 도출.</summary>
    public static class UIElementNaming
    {
        private const string ResourcesFolder = "/Resources/";
        private static readonly Regex Identifier = new(@"^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.Compiled);

        // C# 예약어 중 타입 이름으로 쓰일 만한 것들. 전체 목록이 필요하면 CodeDomProvider를 쓸 수 있으나
        // 에디터 어셈블리에서 System.CodeDom 의존을 늘리지 않기 위해 최소 집합으로 둔다.
        private static readonly string[] Keywords =
        {
            "abstract", "as", "base", "bool", "break", "byte", "case", "catch", "char", "checked",
            "class", "const", "continue", "decimal", "default", "delegate", "do", "double", "else",
            "enum", "event", "explicit", "extern", "false", "finally", "fixed", "float", "for",
            "foreach", "goto", "if", "implicit", "in", "int", "interface", "internal", "is", "lock",
            "long", "namespace", "new", "null", "object", "operator", "out", "override", "params",
            "private", "protected", "public", "readonly", "ref", "return", "sbyte", "sealed",
            "short", "sizeof", "stackalloc", "static", "string", "struct", "switch", "this", "throw",
            "true", "try", "typeof", "uint", "ulong", "unchecked", "unsafe", "ushort", "using",
            "virtual", "void", "volatile", "while",
        };

        public static bool TryValidate(string name, out string error)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                error = "이름이 비어 있습니다.";
                return false;
            }

            if (char.IsDigit(name[0]))
            {
                error = "이름은 숫자로 시작할 수 없습니다.";
                return false;
            }

            if (!Identifier.IsMatch(name))
            {
                error = "이름은 영문/숫자/밑줄만 쓸 수 있는 C# 식별자여야 합니다(공백·기호 불가).";
                return false;
            }

            if (Array.IndexOf(Keywords, name) >= 0)
            {
                error = $"'{name}'은(는) C# 예약어라 타입 이름으로 쓸 수 없습니다.";
                return false;
            }

            error = string.Empty;

            return true;
        }

        /// <summary>
        /// 프리팹 에셋 경로에서 IResourceService 로드 키를 도출한다.
        /// Resources 폴더 아래면 그 기준 상대 경로(확장자 제거), 아니면 경로 전체(Addressables 주소).
        /// </summary>
        public static string ResolveResourceKey(string prefabAssetPath)
        {
            var normalized = prefabAssetPath.Replace('\\', '/');
            var index = normalized.LastIndexOf(ResourcesFolder, StringComparison.Ordinal);

            if (index < 0) return normalized;

            var relative = normalized[(index + ResourcesFolder.Length)..];
            var dot = relative.LastIndexOf('.');

            return dot >= 0 ? relative[..dot] : relative;
        }
    }
}
```

- [ ] **Step 4: 테스트가 통과하는지 확인한다**

Run: `run_tests(mode="EditMode", testFilter="UIElementNamingTests")`
Expected: PASS (13 tests — 유효한 이름 4건, 거부 6건, 키 도출 3건)

- [ ] **Step 5: 커밋**

```bash
git add Assets/FoundationDI/Editor/UIService/UIElementMode.cs \
        Assets/FoundationDI/Editor/UIService/UIElementNaming.cs \
        Assets/FoundationDI/Tests/Editor/UIService/UIElementNamingTests.cs
git commit -m "[BEHAVIORAL] UI 요소 이름 검증과 로드 키 도출 추가"
```

---

## Task 7: 스크립트 템플릿 생성 (순수 함수)

**Files:**
- Create: `Assets/FoundationDI/Editor/UIService/UIElementTemplates.cs`
- Create: `Assets/FoundationDI/Tests/Editor/UIService/UIElementTemplatesTests.cs`

**Interfaces:**
- Consumes: Task 6의 `UIElementMode`
- Produces:
  - `public static string UIElementTemplates.View(string ns, string name)`
  - `public static string UIElementTemplates.Presenter(string ns, string name, UIElementMode mode, string resourceKey)`

- [ ] **Step 1: 실패하는 테스트를 쓴다**

`Assets/FoundationDI/Tests/Editor/UIService/UIElementTemplatesTests.cs`

```csharp
using NUnit.Framework;
using DarkNaku.FoundationDI.Editor;

public class UIElementTemplatesTests
{
    [Test]
    public void View_템플릿은_UIView를_상속한_클래스를_만든다()
    {
        var code = UIElementTemplates.View("MyGame.UI", "Shop");

        StringAssert.Contains("namespace MyGame.UI", code);
        StringAssert.Contains("public class ShopView : UIView", code);
        StringAssert.Contains("using DarkNaku.FoundationDI;", code);
    }

    [Test]
    public void 네임스페이스가_비면_네임스페이스_블록_없이_만든다()
    {
        var code = UIElementTemplates.View("", "Shop");

        StringAssert.DoesNotContain("namespace", code);
        StringAssert.Contains("public class ShopView : UIView", code);
    }

    [TestCase(UIElementMode.Page, "UIPagePresenter<ShopView>")]
    [TestCase(UIElementMode.Popup, "UIPopupPresenter<ShopView>")]
    [TestCase(UIElementMode.Overlay, "UIOverlayPresenter<ShopView>")]
    public void Presenter_템플릿은_모드에_맞는_기반_클래스를_쓴다(UIElementMode mode, string expectedBase)
    {
        var code = UIElementTemplates.Presenter("MyGame.UI", "Shop", mode, "UI/Shop");

        StringAssert.Contains($"public class ShopPresenter : {expectedBase}", code);
    }

    [Test]
    public void Presenter_템플릿은_로드_키를_UIPrefab_속성으로_붙인다()
    {
        var code = UIElementTemplates.Presenter("MyGame.UI", "Shop", UIElementMode.Popup, "UI/Shop");

        StringAssert.Contains("[UIPrefab(\"UI/Shop\")]", code);
    }

    [Test]
    public void Presenter_템플릿은_OnInitialize_오버라이드_자리를_남긴다()
    {
        var code = UIElementTemplates.Presenter("MyGame.UI", "Shop", UIElementMode.Page, "UI/Shop");

        StringAssert.Contains("protected internal override void OnInitialize()", code);
    }
}
```

> `OnInitialize`는 `protected internal virtual`이다(`UIPresenter.cs:68`). 패키지 외부 어셈블리에서 파생하면 `protected internal override`로 선언해야 컴파일된다.

- [ ] **Step 2: 실패를 확인한다**

Run: `run_tests(mode="EditMode", testFilter="UIElementTemplatesTests")`
Expected: 컴파일 실패. `The name 'UIElementTemplates' does not exist`.

- [ ] **Step 3: 구현**

`Assets/FoundationDI/Editor/UIService/UIElementTemplates.cs`

```csharp
using System;
using System.Text;

namespace DarkNaku.FoundationDI.Editor
{
    /// <summary>UI 요소 생성 마법사가 쓰는 스크립트 템플릿.</summary>
    public static class UIElementTemplates
    {
        public static string View(string ns, string name)
        {
            var body = $"public class {name}View : UIView\n{{\n}}\n";

            return Compose(ns, body);
        }

        public static string Presenter(string ns, string name, UIElementMode mode, string resourceKey)
        {
            var body = new StringBuilder()
                .Append($"[UIPrefab(\"{resourceKey}\")]\n")
                .Append($"public class {name}Presenter : {BaseTypeOf(mode)}<{name}View>\n")
                .Append("{\n")
                .Append("    // 패키지를 다른 어셈블리에서 파생하므로 protected internal override로 선언한다.\n")
                .Append("    protected internal override void OnInitialize()\n")
                .Append("    {\n")
                .Append("    }\n")
                .Append("}\n")
                .ToString();

            return Compose(ns, body);
        }

        private static string BaseTypeOf(UIElementMode mode) => mode switch
        {
            UIElementMode.Page => "UIPagePresenter",
            UIElementMode.Popup => "UIPopupPresenter",
            UIElementMode.Overlay => "UIOverlayPresenter",
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null),
        };

        private static string Compose(string ns, string body)
        {
            var sb = new StringBuilder().Append("using DarkNaku.FoundationDI;\n\n");

            if (string.IsNullOrWhiteSpace(ns)) return sb.Append(body).ToString();

            sb.Append($"namespace {ns}\n{{\n");

            foreach (var line in body.TrimEnd('\n').Split('\n'))
            {
                sb.Append(line.Length > 0 ? "    " + line : line).Append('\n');
            }

            return sb.Append("}\n").ToString();
        }
    }
}
```

- [ ] **Step 4: 테스트가 통과하는지 확인한다**

Run: `run_tests(mode="EditMode", testFilter="UIElementTemplatesTests")`
Expected: PASS (7 tests)

- [ ] **Step 5: 커밋**

```bash
git add Assets/FoundationDI/Editor/UIService/UIElementTemplates.cs \
        Assets/FoundationDI/Tests/Editor/UIService/UIElementTemplatesTests.cs
git commit -m "[BEHAVIORAL] UI 요소 스크립트 템플릿 생성 추가"
```

---

## Task 8: 모드별 프리팹 조립

**Files:**
- Create: `Assets/FoundationDI/Editor/UIService/UIElementPrefabBuilder.cs`
- Create: `Assets/FoundationDI/Tests/Editor/UIService/UIElementPrefabBuilderTests.cs`

**Interfaces:**
- Consumes: Task 6의 `UIElementMode`, 런타임의 `UIView`
- Produces: `public static GameObject UIElementPrefabBuilder.Build(Type viewType, UIElementMode mode)` — 저장되지 않은 씬 GameObject를 반환한다. 저장은 호출자 책임.

- [ ] **Step 1: 실패하는 테스트를 쓴다**

`Assets/FoundationDI/Tests/Editor/UIService/UIElementPrefabBuilderTests.cs`

```csharp
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
using DarkNaku.FoundationDI;
using DarkNaku.FoundationDI.Editor;

public class UIElementPrefabBuilderTests
{
    public class DummyView : UIView { }

    private GameObject _built;

    [TearDown]
    public void TearDown()
    {
        if (_built != null) Object.DestroyImmediate(_built);
    }

    [Test]
    public void 모든_모드의_루트는_스트레치_RectTransform과_CanvasGroup과_View를_갖는다()
    {
        _built = UIElementPrefabBuilder.Build(typeof(DummyView), UIElementMode.Page);

        var rt = (RectTransform)_built.transform;

        Assert.IsNotNull(_built.GetComponent<CanvasGroup>());
        Assert.IsNotNull(_built.GetComponent<DummyView>());
        Assert.AreEqual(Vector2.zero, rt.anchorMin);
        Assert.AreEqual(Vector2.one, rt.anchorMax);
        Assert.AreEqual(Vector2.zero, rt.offsetMin);
        Assert.AreEqual(Vector2.zero, rt.offsetMax);
    }

    [Test]
    public void Page는_자식_없이_루트만_만든다()
    {
        _built = UIElementPrefabBuilder.Build(typeof(DummyView), UIElementMode.Page);

        Assert.AreEqual(0, _built.transform.childCount);
    }

    [Test]
    public void Overlay도_자식_없이_루트만_만들고_blocksRaycasts를_끄지_않는다()
    {
        _built = UIElementPrefabBuilder.Build(typeof(DummyView), UIElementMode.Overlay);

        Assert.AreEqual(0, _built.transform.childCount);
        Assert.IsTrue(_built.GetComponent<CanvasGroup>().blocksRaycasts,
            "blocksRaycasts를 끄면 오버레이 안의 버튼까지 죽는다. 전면 배경이 없으므로 입력은 자연히 통과한다.");
    }

    [Test]
    public void Popup은_Background와_Content_자식을_만든다()
    {
        _built = UIElementPrefabBuilder.Build(typeof(DummyView), UIElementMode.Popup);

        var background = _built.transform.Find("Background");
        var content = _built.transform.Find("Content");

        Assert.IsNotNull(background, "모달 배경");
        Assert.IsNotNull(content, "실제 팝업 내용이 들어갈 자리");
        Assert.AreEqual(0, background.GetSiblingIndex(), "배경은 내용보다 아래에 그려져야 한다");

        var image = background.GetComponent<Image>();

        Assert.IsNotNull(image);
        Assert.IsTrue(image.raycastTarget, "모달이므로 뒤쪽 입력을 막아야 한다");
    }
}
```

- [ ] **Step 2: 실패를 확인한다**

Run: `run_tests(mode="EditMode", testFilter="UIElementPrefabBuilderTests")`
Expected: 컴파일 실패. `The name 'UIElementPrefabBuilder' does not exist`.

- [ ] **Step 3: 구현**

`Assets/FoundationDI/Editor/UIService/UIElementPrefabBuilder.cs`

```csharp
using System;
using UnityEngine;
using UnityEngine.UI;

namespace DarkNaku.FoundationDI.Editor
{
    /// <summary>모드별 UI 프리팹 계층을 조립한다. 저장은 호출자 책임이다.</summary>
    public static class UIElementPrefabBuilder
    {
        public static GameObject Build(Type viewType, UIElementMode mode)
        {
            if (viewType == null) throw new ArgumentNullException(nameof(viewType));

            if (!typeof(UIView).IsAssignableFrom(viewType))
            {
                throw new ArgumentException($"'{viewType.Name}'은(는) UIView 파생 타입이 아니다.", nameof(viewType));
            }

            var go = new GameObject(viewType.Name, typeof(RectTransform), typeof(CanvasGroup));

            Stretch((RectTransform)go.transform);

            // UIView는 [RequireComponent(typeof(CanvasGroup))]라 CanvasGroup이 먼저 있어야 한다.
            go.AddComponent(viewType);

            if (mode == UIElementMode.Popup) BuildPopupChildren(go.transform);

            return go;
        }

        private static void BuildPopupChildren(Transform parent)
        {
            var background = new GameObject("Background", typeof(RectTransform), typeof(Image));

            background.transform.SetParent(parent, false);
            Stretch((RectTransform)background.transform);

            var image = background.GetComponent<Image>();

            image.color = new Color(0f, 0f, 0f, 0.5f);
            image.raycastTarget = true; // 모달: 뒤쪽 입력 차단

            var content = new GameObject("Content", typeof(RectTransform));

            content.transform.SetParent(parent, false);

            var rt = (RectTransform)content.transform;

            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = new Vector2(800f, 600f);
        }

        private static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }
    }
}
```

- [ ] **Step 4: 테스트가 통과하는지 확인한다**

Run: `run_tests(mode="EditMode", testFilter="UIElementPrefabBuilderTests")`
Expected: PASS (4 tests)

- [ ] **Step 5: 커밋**

```bash
git add Assets/FoundationDI/Editor/UIService/UIElementPrefabBuilder.cs \
        Assets/FoundationDI/Tests/Editor/UIService/UIElementPrefabBuilderTests.cs
git commit -m "[BEHAVIORAL] 모드별 UI 프리팹 조립 추가

Page/Overlay는 스트레치 루트만, Popup은 Background(모달 차단) + Content로
조립한다. Slide/Scale 트랜지션의 _background/_content 필드와 맞물린다."
```

---

## Task 9: 프로젝트 기본값 (스크립트 루트 / 네임스페이스 / 프리팹 루트)

**Files:**
- Create: `Assets/FoundationDI/Editor/UIService/UIElementCreationSettings.cs`
- Create: `Assets/FoundationDI/Tests/Editor/UIService/UIElementCreationSettingsTests.cs`

**Interfaces:**
- Consumes: 없음
- Produces:
  - `public sealed class UIElementCreationSettings : ScriptableSingleton<UIElementCreationSettings>` — `ScriptRoot`, `Namespace`, `PrefabRoot` 프로퍼티 + `Save()`
  - `public static string UIElementCreationSettings.CombineAssetPath(string root, string fileName)` — 순수 함수(테스트 대상)
  - `Project Settings/FoundationDI/UI` SettingsProvider

- [ ] **Step 1: 실패하는 테스트를 쓴다**

`Assets/FoundationDI/Tests/Editor/UIService/UIElementCreationSettingsTests.cs`

```csharp
using NUnit.Framework;
using DarkNaku.FoundationDI.Editor;

public class UIElementCreationSettingsTests
{
    [Test]
    public void 경로_결합은_슬래시_중복을_만들지_않는다()
    {
        Assert.AreEqual("Assets/Scripts/UI/ShopView.cs",
            UIElementCreationSettings.CombineAssetPath("Assets/Scripts/UI/", "ShopView.cs"));
        Assert.AreEqual("Assets/Scripts/UI/ShopView.cs",
            UIElementCreationSettings.CombineAssetPath("Assets/Scripts/UI", "ShopView.cs"));
    }

    [Test]
    public void 경로_결합은_역슬래시를_슬래시로_정규화한다()
    {
        Assert.AreEqual("Assets/Scripts/UI/ShopView.cs",
            UIElementCreationSettings.CombineAssetPath(@"Assets\Scripts\UI", "ShopView.cs"));
    }

    [Test]
    public void 기본값은_Assets_아래_경로로_시작한다()
    {
        var settings = UIElementCreationSettings.instance;

        StringAssert.StartsWith("Assets/", settings.ScriptRoot);
        StringAssert.StartsWith("Assets/", settings.PrefabRoot);
        StringAssert.Contains("Resources", settings.PrefabRoot,
            "기본 백엔드는 ResourcesProvider이므로 기본 프리팹 루트는 Resources 아래여야 한다");
    }
}
```

- [ ] **Step 2: 실패를 확인한다**

Run: `run_tests(mode="EditMode", testFilter="UIElementCreationSettingsTests")`
Expected: 컴파일 실패. `The name 'UIElementCreationSettings' does not exist`.

- [ ] **Step 3: 구현**

`Assets/FoundationDI/Editor/UIService/UIElementCreationSettings.cs`

```csharp
using UnityEditor;
using UnityEngine;

namespace DarkNaku.FoundationDI.Editor
{
    /// <summary>
    /// UI 요소 생성 마법사의 프로젝트 기본값.
    /// EditorPrefs(머신 로컬)가 아니라 ProjectSettings에 저장한다 — 팀원 간 공유·커밋이 되어야
    /// 프로젝트 규약이 유지되기 때문이다.
    /// </summary>
    [FilePath("ProjectSettings/FoundationDIUIEditor.asset", FilePathAttribute.Location.ProjectFolder)]
    public sealed class UIElementCreationSettings : ScriptableSingleton<UIElementCreationSettings>
    {
        [SerializeField] private string _scriptRoot = "Assets/Scripts/UI";
        [SerializeField] private string _namespace = "";
        [SerializeField] private string _prefabRoot = "Assets/Resources/UI";

        public string ScriptRoot { get => _scriptRoot; set => _scriptRoot = value; }
        public string Namespace { get => _namespace; set => _namespace = value; }
        public string PrefabRoot { get => _prefabRoot; set => _prefabRoot = value; }

        public void Save() => Save(true);

        /// <summary>에셋 경로를 결합한다. 역슬래시를 정규화하고 슬래시 중복을 만들지 않는다.</summary>
        public static string CombineAssetPath(string root, string fileName)
        {
            var normalized = (root ?? string.Empty).Replace('\\', '/').TrimEnd('/');

            return normalized.Length == 0 ? fileName : $"{normalized}/{fileName}";
        }
    }

    internal static class UIElementCreationSettingsProvider
    {
        [SettingsProvider]
        private static SettingsProvider Create()
        {
            return new SettingsProvider("Project/FoundationDI/UI", SettingsScope.Project)
            {
                label = "UI",
                keywords = new[] { "FoundationDI", "UI", "UIService", "Page", "Popup", "Overlay" },
                guiHandler = _ =>
                {
                    var settings = UIElementCreationSettings.instance;

                    EditorGUI.BeginChangeCheck();

                    settings.ScriptRoot = EditorGUILayout.TextField("Script Root", settings.ScriptRoot);
                    settings.Namespace = EditorGUILayout.TextField("Namespace", settings.Namespace);
                    settings.PrefabRoot = EditorGUILayout.TextField("Prefab Root", settings.PrefabRoot);

                    EditorGUILayout.HelpBox(
                        "Prefab Root가 Resources 폴더 아래면 로드 키는 Resources 기준 상대 경로가 되고, " +
                        "그렇지 않으면 경로 전체가 Addressables 주소로 쓰입니다.",
                        MessageType.Info);

                    if (EditorGUI.EndChangeCheck()) settings.Save();
                },
            };
        }
    }
}
```

- [ ] **Step 4: 테스트가 통과하는지 확인한다**

Run: `run_tests(mode="EditMode", testFilter="UIElementCreationSettingsTests")`
Expected: PASS (3 tests)

- [ ] **Step 5: 수동 확인**

`Edit > Project Settings > FoundationDI > UI`가 보이고 세 필드를 편집할 수 있는지 확인한다. 값을 바꾸면 `ProjectSettings/FoundationDIUIEditor.asset`이 생성된다.

- [ ] **Step 6: 커밋**

```bash
git add Assets/FoundationDI/Editor/UIService/UIElementCreationSettings.cs \
        Assets/FoundationDI/Tests/Editor/UIService/UIElementCreationSettingsTests.cs
git commit -m "[BEHAVIORAL] UI 요소 생성 기본값을 ProjectSettings에 추가

스크립트 루트/네임스페이스/프리팹 루트를 프로젝트 단위로 저장해 팀원 간
규약이 유지되게 한다. Project Settings/FoundationDI/UI 에 노출."
```

---

## Task 10: 생성 마법사 — 2단계 파이프라인

**Files:**
- Create: `Assets/FoundationDI/Editor/UIService/UIElementCreationRequest.cs`
- Create: `Assets/FoundationDI/Editor/UIService/UIElementCreator.cs`
- Create: `Assets/FoundationDI/Editor/UIService/UIElementWizard.cs`
- Create: `Assets/FoundationDI/Tests/Editor/UIService/UIElementCreationRequestTests.cs`

**Interfaces:**
- Consumes: Task 6 `UIElementMode`/`UIElementNaming`, Task 7 `UIElementTemplates`, Task 8 `UIElementPrefabBuilder`, Task 9 `UIElementCreationSettings`
- Produces:
  - `[Serializable] public sealed class UIElementCreationRequest { string Name; UIElementMode Mode; string Namespace; string PrefabPath; }` + `ToJson()` / `FromJson(string)`
  - `public static void UIElementCreator.Begin(UIElementCreationRequest request)` — 스크립트를 쓰고 대기 작업을 `SessionState`에 저장한 뒤 리프레시
  - `[DidReloadScripts] private static void UIElementCreator.Resume()` — 프리팹을 조립·저장하고 프리팹 모드로 진입

**왜 2단계인가:** 스크립트를 만든 직후에는 그 타입이 아직 컴파일되지 않아 `AddComponent`가 불가능하다. 도메인 리로드를 건너뛴 뒤에야 타입이 존재한다. `SessionState`는 도메인 리로드를 넘어 살아남는 유일한 간단한 저장소다(에디터 세션 범위).

- [ ] **Step 1: 실패하는 테스트를 쓴다 (직렬화 왕복만 단위 테스트한다)**

`Assets/FoundationDI/Tests/Editor/UIService/UIElementCreationRequestTests.cs`

```csharp
using NUnit.Framework;
using DarkNaku.FoundationDI.Editor;

public class UIElementCreationRequestTests
{
    [Test]
    public void 요청은_JSON_왕복에서_모든_필드를_보존한다()
    {
        var original = new UIElementCreationRequest
        {
            Name = "Shop",
            Mode = UIElementMode.Popup,
            Namespace = "MyGame.UI",
            PrefabPath = "Assets/Resources/UI/Shop.prefab",
        };

        var restored = UIElementCreationRequest.FromJson(original.ToJson());

        Assert.AreEqual(original.Name, restored.Name);
        Assert.AreEqual(original.Mode, restored.Mode);
        Assert.AreEqual(original.Namespace, restored.Namespace);
        Assert.AreEqual(original.PrefabPath, restored.PrefabPath);
    }

    [Test]
    public void 잘못된_JSON은_null을_돌려준다()
    {
        Assert.IsNull(UIElementCreationRequest.FromJson(""));
        Assert.IsNull(UIElementCreationRequest.FromJson("not json"));
    }
}
```

- [ ] **Step 2: 실패를 확인한다**

Run: `run_tests(mode="EditMode", testFilter="UIElementCreationRequestTests")`
Expected: 컴파일 실패. `The name 'UIElementCreationRequest' does not exist`.

- [ ] **Step 3: `UIElementCreationRequest.cs` 구현**

```csharp
using System;
using UnityEngine;

namespace DarkNaku.FoundationDI.Editor
{
    /// <summary>도메인 리로드를 넘어 전달되는 생성 요청. SessionState에 JSON으로 보관된다.</summary>
    [Serializable]
    public sealed class UIElementCreationRequest
    {
        public string Name;
        public UIElementMode Mode;
        public string Namespace;
        public string PrefabPath;

        public string ToJson() => JsonUtility.ToJson(this);

        public static UIElementCreationRequest FromJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;

            try
            {
                var request = JsonUtility.FromJson<UIElementCreationRequest>(json);

                return string.IsNullOrEmpty(request?.Name) ? null : request;
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
```

- [ ] **Step 4: 테스트가 통과하는지 확인한다**

Run: `run_tests(mode="EditMode", testFilter="UIElementCreationRequestTests")`
Expected: PASS (2 tests)

- [ ] **Step 5: `UIElementCreator.cs` 구현 (2단계 파이프라인)**

```csharp
using System;
using System.IO;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEngine;

namespace DarkNaku.FoundationDI.Editor
{
    /// <summary>
    /// UI 요소(View 스크립트 + Presenter 스크립트 + 프리팹)를 생성한다.
    ///
    /// 스크립트를 만든 직후에는 그 타입이 아직 컴파일되지 않아 AddComponent가 불가능하다.
    /// 그래서 도메인 리로드를 경계로 2단계로 나눈다:
    ///   Begin()  — 스크립트를 쓰고 요청을 SessionState에 남긴 뒤 Refresh
    ///   Resume() — 리로드 후 [DidReloadScripts]에서 프리팹을 조립하고 프리팹 모드로 진입
    /// </summary>
    public static class UIElementCreator
    {
        private const string PendingKey = "DarkNaku.FoundationDI.UIElementCreator.Pending";

        public static void Begin(UIElementCreationRequest request)
        {
            var scriptRoot = UIElementCreationSettings.instance.ScriptRoot;
            var viewPath = UIElementCreationSettings.CombineAssetPath(scriptRoot, $"{request.Name}View.cs");
            var presenterPath = UIElementCreationSettings.CombineAssetPath(scriptRoot, $"{request.Name}Presenter.cs");
            var resourceKey = UIElementNaming.ResolveResourceKey(request.PrefabPath);

            EnsureFolder(scriptRoot);
            EnsureFolder(Path.GetDirectoryName(request.PrefabPath)?.Replace('\\', '/'));

            File.WriteAllText(viewPath, UIElementTemplates.View(request.Namespace, request.Name));
            File.WriteAllText(presenterPath,
                UIElementTemplates.Presenter(request.Namespace, request.Name, request.Mode, resourceKey));

            // 리로드 후에도 살아남아야 한다.
            SessionState.SetString(PendingKey, request.ToJson());

            AssetDatabase.Refresh();
        }

        [DidReloadScripts]
        private static void Resume()
        {
            var json = SessionState.GetString(PendingKey, string.Empty);

            if (string.IsNullOrEmpty(json)) return;

            // 성공하든 실패하든 대기 작업은 여기서 지운다. 좀비 상태로 남기지 않는다.
            SessionState.EraseString(PendingKey);

            var request = UIElementCreationRequest.FromJson(json);

            if (request == null)
            {
                Debug.LogError("[FoundationDI] UI 요소 생성 요청을 복원하지 못했습니다. 마법사를 다시 실행하세요.");
                return;
            }

            var viewType = FindViewType(request);

            if (viewType == null)
            {
                Debug.LogError(
                    $"[FoundationDI] '{request.Name}View' 타입을 찾지 못해 프리팹 생성을 중단했습니다. " +
                    "컴파일 에러가 있는지 확인한 뒤 마법사를 다시 실행하세요.");
                return;
            }

            var go = UIElementPrefabBuilder.Build(viewType, request.Mode);

            try
            {
                var prefab = PrefabUtility.SaveAsPrefabAsset(go, request.PrefabPath);

                if (prefab == null)
                {
                    Debug.LogError($"[FoundationDI] 프리팹 저장에 실패했습니다: {request.PrefabPath}");
                    return;
                }

                Selection.activeObject = prefab;

                // 격리 프리팹 모드로 진입 → UI 편집 환경이 적용된 상태로 바로 작업 가능.
                AssetDatabase.OpenAsset(prefab);

                Debug.Log($"[FoundationDI] '{request.Name}' {request.Mode} 생성 완료: {request.PrefabPath}");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        private static Type FindViewType(UIElementCreationRequest request)
        {
            var typeName = string.IsNullOrWhiteSpace(request.Namespace)
                ? $"{request.Name}View"
                : $"{request.Namespace}.{request.Name}View";

            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                var type = assembly.GetType(typeName, false);

                if (type != null) return type;
            }

            return null;
        }

        private static void EnsureFolder(string assetFolder)
        {
            if (string.IsNullOrEmpty(assetFolder) || AssetDatabase.IsValidFolder(assetFolder)) return;

            Directory.CreateDirectory(assetFolder);
            AssetDatabase.Refresh();
        }
    }
}
```

- [ ] **Step 6: `UIElementWizard.cs` 구현 (입력 창)**

```csharp
using UnityEditor;
using UnityEngine;

namespace DarkNaku.FoundationDI.Editor
{
    /// <summary>이름과 모드만 받아 UI 요소를 생성하는 마법사.</summary>
    public sealed class UIElementWizard : EditorWindow
    {
        private string _name = "";
        private UIElementMode _mode = UIElementMode.Page;

        [MenuItem("Tools/FoundationDI/UI/Create UI Element...", false, 70)]
        private static void Open()
        {
            var window = GetWindow<UIElementWizard>(true, "Create UI Element", true);

            window.minSize = new Vector2(460f, 220f);
            window.ShowUtility();
        }

        private void OnGUI()
        {
            var settings = UIElementCreationSettings.instance;

            EditorGUILayout.LabelField("새 UI 요소", EditorStyles.boldLabel);

            _name = EditorGUILayout.TextField("Name", _name);
            _mode = (UIElementMode)EditorGUILayout.EnumPopup("Mode", _mode);

            EditorGUILayout.Space();

            var valid = UIElementNaming.TryValidate(_name, out var error);
            var prefabPath = UIElementCreationSettings.CombineAssetPath(settings.PrefabRoot, $"{_name}.prefab");
            var scriptRoot = settings.ScriptRoot;

            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.TextField("View", UIElementCreationSettings.CombineAssetPath(scriptRoot, $"{_name}View.cs"));
                EditorGUILayout.TextField("Presenter", UIElementCreationSettings.CombineAssetPath(scriptRoot, $"{_name}Presenter.cs"));
                EditorGUILayout.TextField("Prefab", prefabPath);
                EditorGUILayout.TextField("Key", UIElementNaming.ResolveResourceKey(prefabPath));
            }

            EditorGUILayout.Space();

            if (!valid && !string.IsNullOrEmpty(_name))
            {
                EditorGUILayout.HelpBox(error, MessageType.Error);
            }

            var exists = !string.IsNullOrEmpty(_name)
                         && AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath) != null;

            if (exists)
            {
                EditorGUILayout.HelpBox($"이미 존재합니다: {prefabPath}", MessageType.Error);
            }

            using (new EditorGUI.DisabledScope(!valid || exists))
            {
                if (GUILayout.Button("Create", GUILayout.Height(28f)))
                {
                    UIElementCreator.Begin(new UIElementCreationRequest
                    {
                        Name = _name,
                        Mode = _mode,
                        Namespace = settings.Namespace,
                        PrefabPath = prefabPath,
                    });

                    Close();
                }
            }

            EditorGUILayout.HelpBox(
                "경로와 네임스페이스 기본값은 Project Settings > FoundationDI > UI 에서 바꿉니다.",
                MessageType.Info);
        }
    }
}
```

- [ ] **Step 7: 전체 테스트를 돌린다**

Run: `run_tests(mode="EditMode")` 그리고 `run_tests(mode="PlayMode")`
Expected: 전부 PASS

- [ ] **Step 8: 수동 검증 — 도메인 리로드 왕복 (이 Task의 핵심)**

Unity Editor에서 `Tools/FoundationDI/UI/Create UI Element...` 실행:

1. Name `SmokePopup`, Mode `Popup` → Create
2. 스크립트 2개가 생성되고 컴파일이 도는지 확인
3. 컴파일 후 **자동으로** `Assets/Resources/UI/SmokePopup.prefab`이 만들어지고 **프리팹 격리 모드로 열리는지** 확인
4. 열린 프리팹이 Task 5의 편집 환경(실제 캔버스) 안에 올바른 크기로 보이는지 확인
5. `Background`/`Content` 자식이 있는지 확인
6. 확인이 끝나면 생성물 3개(스크립트 2 + 프리팹 1)를 삭제한다 — 커밋하지 않는다

실패 경로도 확인한다: 존재하는 이름으로 다시 실행하면 Create 버튼이 비활성화되는지.

- [ ] **Step 9: 커밋**

```bash
git add Assets/FoundationDI/Editor/UIService/UIElementCreationRequest.cs \
        Assets/FoundationDI/Editor/UIService/UIElementCreator.cs \
        Assets/FoundationDI/Editor/UIService/UIElementWizard.cs \
        Assets/FoundationDI/Tests/Editor/UIService/UIElementCreationRequestTests.cs
git commit -m "[BEHAVIORAL] UI 요소 생성 마법사 추가

이름과 모드만 입력하면 View/Presenter 스크립트와 프리팹을 만들고 연결한 뒤
프리팹 격리 모드로 진입한다. 스크립트 컴파일 전에는 타입이 없으므로
SessionState + DidReloadScripts로 도메인 리로드를 건너뛰어 2단계로 처리한다."
```

---

# Phase 4 — 문서와 버전

## Task 11: README 갱신, 마이그레이션 안내, 버전 업

**Files:**
- Modify: `Assets/FoundationDI/Runtime/Services/UIService/README.md` (저작 워크플로 절 추가 + 상단 요약/DI 예제 갱신 + 마이그레이션 절)
- Modify: `Assets/FoundationDI/package.json:4` (`"version": "0.3.0"` → `"0.4.0"`)
- Modify: `Assets/FoundationDI/README.md:4` (버전 배지 `version-0.3.0-blue` → `version-0.4.0-blue`)
- Modify: `CLAUDE.md` (UIService 절)

**Interfaces:**
- Consumes: Task 1-10의 모든 공개 API
- Produces: 없음

- [ ] **Step 1: UIService README에 저작 워크플로 절을 추가한다**

`## 사용법` 앞이나 뒤에 아래 내용을 넣는다. Task 5 Step 6에서 기록한 "프리팹이 붙는 위치" 실측 결과를 반영한다.

````markdown
## 에디터 워크플로 (디자이너용)

### 1) 루트 프리팹 만들기 (프로젝트당 1회)

`Tools/FoundationDI/UI/Create UI Root Prefab` → 저장 위치 선택 → 생성된 프리팹을
`UIServiceSettings`의 **Root Prefab**에 연결합니다. 캔버스 렌더 모드, `CanvasScaler`,
기준 해상도, 레이어 구성은 전부 이 프리팹이 결정합니다. 비워두면 코드 기본값
(ScreenSpaceOverlay / ScaleWithScreenSize / Expand / 1920x1080)으로 폴백합니다.

### 2) 프리팹 편집 환경 지정하기 (프로젝트당 1회)

`Tools/FoundationDI/UI/Setup Prefab Editing Environment` → 씬 저장 위치 선택.
이후 **UI 프리팹을 프로젝트 창에서 더블클릭**하면 런타임과 동일한 캔버스 안에서
올바른 크기/스케일로 열립니다. 씬에 임시 캔버스를 만들 필요도, 작업 후 지울 필요도 없습니다.

> 이 설정(`EditorSettings.prefabUIEnvironment`)은 프리팹을 **격리 모드**로 열 때만
> 적용됩니다. 씬의 프리팹 인스턴스에서 "Open"으로 들어가는 "in context" 모드에는
> 적용되지 않습니다.

해제는 `Tools/FoundationDI/UI/Clear Prefab Editing Environment`.

### 3) 새 UI 요소 만들기

`Tools/FoundationDI/UI/Create UI Element...` → 이름과 모드(Page/Popup/Overlay) 입력 → Create.

생성되는 것:

| 산출물 | 예시(이름 `Shop`, Popup) |
|---|---|
| View 스크립트 | `<Script Root>/ShopView.cs` — `public class ShopView : UIView` |
| Presenter 스크립트 | `<Script Root>/ShopPresenter.cs` — `[UIPrefab("UI/Shop")] class ShopPresenter : UIPopupPresenter<ShopView>` |
| 프리팹 | `<Prefab Root>/Shop.prefab` — 루트(stretch + CanvasGroup + ShopView) + `Background` + `Content` |

스크립트 컴파일이 끝나면 프리팹이 자동으로 조립되고 **프리팹 편집 모드로 열립니다.**

경로/네임스페이스 기본값은 `Project Settings > FoundationDI > UI`에서 바꿉니다.
`Prefab Root`가 `Resources` 폴더 아래면 로드 키는 Resources 기준 상대 경로가 되고,
아니면 경로 전체가 Addressables 주소로 쓰입니다.

모드별 프리팹 템플릿:

| 모드 | 루트 | 자식 |
|---|---|---|
| Page | RectTransform(stretch) + CanvasGroup + View | 없음 |
| Popup | 위와 동일 | `Background`(Image, 검정 α=0.5, 모달 입력 차단), `Content`(중앙 정렬) |
| Overlay | 위와 동일 | 없음 |

Page와 Overlay는 전면 배경이 없으므로 빈 영역의 입력이 자연히 아래로 통과합니다.
`CanvasGroup.blocksRaycasts`는 끄지 않습니다 — 끄면 오버레이 안의 버튼까지 죽습니다.
````

- [ ] **Step 2: README의 기존 "상주 캔버스" 설명과 DI 등록 예제를 갱신한다**

`### 1) DI 등록 (VContainer)` 절과 상단 요약에서 기준 해상도가 Settings에 있다는 서술을
프리팹 기반으로 바꾼다. 아래 마이그레이션 절을 README 끝에 추가한다.

```markdown
## 마이그레이션 (0.3.0 → 0.4.0)

**BREAKING:** `UIServiceSettings.ReferenceResolution`이 제거되고 `RootPrefab`으로 대체되었습니다.

1. `Tools/FoundationDI/UI/Create UI Root Prefab`으로 루트 프리팹을 만듭니다.
2. 그 프리팹의 `CanvasScaler`에 기존에 쓰던 기준 해상도를 설정합니다.
3. `UIServiceSettings`의 **Root Prefab**에 연결합니다.

연결하지 않아도 동작은 하지만, 기준 해상도가 코드 기본값(1920x1080)으로 폴백합니다.
```

- [ ] **Step 3: 패키지 버전을 올린다**

`Assets/FoundationDI/package.json`의 `"version": "0.3.0"` → `"version": "0.4.0"`.

`Assets/FoundationDI/README.md:4`의 배지도 함께 바꾼다.

```bash
grep -rn "0\.3\.0" Assets/FoundationDI/package.json Assets/FoundationDI/README.md
```
Expected: 두 파일 모두 매치가 없어야 한다(=전부 0.4.0으로 갱신됨).

- [ ] **Step 4: `CLAUDE.md`의 UIService 절을 갱신한다**

`**상주 캔버스**` 관련 서술에 "루트는 `UIServiceSettings.RootPrefab`으로 지정한 프리팹을
인스턴스화하며, 미지정 시 `UIRoot.CreateDefault()`로 폴백한다"를 추가하고,
에디터 도구 3종(`Tools/FoundationDI/UI/...`)을 한 줄로 적는다.

- [ ] **Step 5: 전체 테스트를 마지막으로 돌린다**

Run: `run_tests(mode="EditMode")` 그리고 `run_tests(mode="PlayMode")`
Expected: 전부 PASS

- [ ] **Step 6: 커밋**

```bash
git add Assets/FoundationDI/Runtime/Services/UIService/README.md \
        Assets/FoundationDI/package.json \
        Assets/FoundationDI/README.md \
        CLAUDE.md
git commit -m "[STRUCTURAL] UIService 저작 워크플로 문서화 및 0.4.0 버전 업

에디터 워크플로 3단계(루트 프리팹 → 편집 환경 → 생성 마법사)와
ReferenceResolution 제거에 대한 마이그레이션 안내를 추가한다."
```

---

## 완료 조건

- [ ] `run_tests(mode="EditMode")` 전부 PASS
- [ ] `run_tests(mode="PlayMode")` 전부 PASS
- [ ] UI 프리팹을 더블클릭하면 실제 캔버스 안에서 올바른 크기로 열린다 (Task 5 Step 6 수동 검증)
- [ ] `Create UI Element...`로 만든 UI가 컴파일 후 자동으로 프리팹 모드까지 열린다 (Task 10 Step 8 수동 검증)
- [ ] 수동 검증에서 만든 임시 산출물이 리포에 남아 있지 않다 (`git status --short`가 깨끗하다)
