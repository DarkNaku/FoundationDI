# UIManager Screen Space - Camera + Sorting Layer 정렬 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** UIManager의 Canvas를 Screen Space - Camera로 전환해 `Camera.main` 기준 Sorting Layer 정렬에 참여시키고, UI 수명을 씬 스코프(씬 전환 시 초기화)로 만든다.

**Architecture:** UIManager는 지금처럼 루트 싱글턴으로 두되 내부에서 `SceneManager.activeSceneChanged`를 구독해 씬 전환 시 UIRoot/풀을 리셋한다. UIRoot는 `DontDestroyOnLoad`를 버리고 생성 시점 active 씬에 소속되며, 생성자에서 받은 정렬/거리 값과 `Camera.main`으로 Canvas를 `ScreenSpaceCamera`로 구성한다(카메라 없으면 `ScreenSpaceOverlay` 폴백).

**Tech Stack:** Unity 6000.3.17f1, VContainer, UniTask, NUnit + NSubstitute 5.3.0, Unity Test Framework(EditMode/PlayMode). 컴파일·테스트는 UnityMCP로 수행.

## Global Constraints

- Unity 버전: 6000.3.17f1. CLI 빌드 없음 — **모든 컴파일·테스트는 UnityMCP로**.
- 스크립트 수정 후 `read_console`로 컴파일 에러를 먼저 확인하고, `editor_state.isCompiling == false`가 된 뒤 새 타입을 사용/테스트한다.
- 테스트 실행: UnityMCP `run_tests` (EditMode = `FoundationDI.Tests.Editor`, PlayMode = `FoundationDI.Tests.Runtime`).
- 네임스페이스: `DarkNaku.FoundationDI`.
- 테스트 메서드 이름은 **한국어**, `should~`(해야 한다) 의도로 작성. 기존 파일 스타일(`~한다`)을 따른다.
- **구조적(STRUCTURAL) 변경과 행동적(BEHAVIORAL) 변경을 절대 같은 커밋에 섞지 않는다.** 커밋 제목에 접두어를 단다.
- 모킹: NSubstitute 5.3.0. `IObjectResolver`/`IResourceService`를 `Substitute.For<>()`로 대체.
- 재사용 코드는 `Assets/FoundationDI/` 안에만 둔다. 네임스페이스 `DarkNaku.FoundationDI`.
- 브랜치: `feature/uimanager-screenspace-camera` (이미 생성됨).

## File Structure

- Modify: `Assets/FoundationDI/Runtime/Managers/UIManager/Controllers/UIRoot.cs` — 생성자 파라미터 확장 + 카메라 바인딩 + DDOL 제거.
- Modify: `Assets/FoundationDI/Runtime/Managers/UIManager/Settings/UIManagerSettings.cs` — 정렬/거리 필드 3개 + 프로퍼티.
- Modify: `Assets/FoundationDI/Runtime/Managers/UIManager/UIManager.cs` — `Root`/`Pool` 게터, `ResetSceneState()` 추출, `activeSceneChanged` 구독/해제, `Dispose()` 통합.
- Create: `Assets/FoundationDI/Tests/Editor/UIManager/UIManagerSettingsTests.cs` — 설정 프로퍼티 EditMode 테스트.
- Modify: `Assets/FoundationDI/Tests/Runtime/UIManager/UIRootTests.cs` — 렌더 모드/정렬/씬 소속 PlayMode 테스트 추가.
- Create: `Assets/FoundationDI/Tests/Runtime/UIManager/UIManagerSceneResetTests.cs` — 씬 전환 리셋 PlayMode 테스트.
- Modify: `plan.md` — 이 계획의 테스트 항목으로 갱신.

---

## Task 0: plan.md를 이 계획으로 갱신 (setup)

**Files:**
- Modify: `plan.md`

- [ ] **Step 1: plan.md를 새 활성 계획으로 재작성**

`plan.md`를 아래 내용으로 덮어쓴다:

```markdown
# plan.md

## 활성 계획: UIManager Screen Space - Camera + Sorting Layer 정렬

세부: `docs/superpowers/plans/2026-07-27-uimanager-screenspace-camera-sorting.md`

테스트 목록 (다음 작업 = 첫 번째 미완료 항목):

- [ ] UIManagerSettings는 SortingLayerName/SortingOrder/PlaneDistance를 설정값으로 반환한다
- [ ] UIRoot는 카메라가 있으면 Canvas를 ScreenSpaceCamera와 지정 정렬/거리로 구성한다
- [ ] UIRoot는 카메라가 없으면 Canvas를 ScreenSpaceOverlay로 폴백한다
- [ ] UIRoot의 Canvas GO는 생성 시점 active 씬에 소속된다(DontDestroyOnLoad 아님)
- [ ] active 씬이 바뀌면 활성 presenter가 teardown되고 풀 View가 파괴된다
- [ ] 씬 전환 후 Page 재요청 시 새 씬에서 정상적으로 Show까지 도달한다
```

- [ ] **Step 2: Commit**

```bash
git add plan.md
git commit -m "docs: plan.md를 UIManager Screen Space Camera 계획으로 갱신"
```

---

## Task 1: UIRoot 생성자 파라미터 확장 (STRUCTURAL)

기존 동작·기존 테스트를 그대로 둔 채 생성자에 정렬/거리/카메라 파라미터만 추가한다. 아직 사용하지 않는다.

**Files:**
- Modify: `Assets/FoundationDI/Runtime/Managers/UIManager/Controllers/UIRoot.cs:14`

**Interfaces:**
- Consumes: 없음.
- Produces: `UIRoot(Vector2 referenceResolution = default, string sortingLayerName = "Default", int sortingOrder = 0, float planeDistance = 100f, System.Func<UnityEngine.Camera> cameraProvider = null)` — 이후 Task 3에서 본문이 이 값들을 사용.

- [ ] **Step 1: 생성자 시그니처 확장**

`UIRoot.cs` 상단에 `using System;` 추가(이미 있으면 생략). 생성자 시그니처를 아래로 바꾸고 **본문은 그대로 둔다**(새 파라미터 미사용):

```csharp
public UIRoot(
    Vector2 referenceResolution = default,
    string sortingLayerName = "Default",
    int sortingOrder = 0,
    float planeDistance = 100f,
    Func<Camera> cameraProvider = null)
{
    GO = new GameObject("[UIManager]", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
    var canvas = GO.GetComponent<Canvas>();
    canvas.renderMode = RenderMode.ScreenSpaceOverlay;

    var scaler = GO.GetComponent<CanvasScaler>();
    scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
    scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
    scaler.referenceResolution = (referenceResolution.x > 0f && referenceResolution.y > 0f)
        ? referenceResolution
        : new Vector2(1920f, 1080f);

    Object.DontDestroyOnLoad(GO);

    PageLayer = CreateLayer("[Page]");
    BelowOverlayLayer = CreateLayer("[BelowOverlay]");
    PopupLayer = CreateLayer("[Popup]");
    AboveOverlayLayer = CreateLayer("[AboveOverlay]");
}
```

- [ ] **Step 2: 컴파일 확인**

UnityMCP `read_console`로 컴파일 에러가 없고 `editor_state.isCompiling == false`인지 확인.
Expected: 에러 없음(미사용 파라미터 경고는 무시 가능).

- [ ] **Step 3: 기존 테스트가 그대로 통과하는지 확인**

UnityMCP `run_tests` PlayMode(`FoundationDI.Tests.Runtime`) 실행 — 특히 `UIRootTests`, `UIManagerFlowTests`.
Expected: 전부 PASS(동작 불변).

- [ ] **Step 4: Commit**

```bash
git add Assets/FoundationDI/Runtime/Managers/UIManager/Controllers/UIRoot.cs
git commit -m "[STRUCTURAL] UIRoot 생성자에 정렬/거리/카메라 파라미터 추가(미사용)"
```

---

## Task 2: UIManagerSettings 정렬/거리 프로퍼티 (BEHAVIORAL)

**Files:**
- Modify: `Assets/FoundationDI/Runtime/Managers/UIManager/Settings/UIManagerSettings.cs`
- Test: `Assets/FoundationDI/Tests/Editor/UIManager/UIManagerSettingsTests.cs` (신규)

**Interfaces:**
- Consumes: 없음.
- Produces: `UIManagerSettings.SortingLayerName` (string), `.SortingOrder` (int), `.PlaneDistance` (float) — Task 3에서 UIManager가 UIRoot 생성 시 전달.

- [ ] **Step 1: 실패하는 EditMode 테스트 작성**

`Assets/FoundationDI/Tests/Editor/UIManager/UIManagerSettingsTests.cs` 생성. private `[SerializeField]` 필드는 `SerializedObject`로 세팅한다:

```csharp
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using DarkNaku.FoundationDI;

public class UIManagerSettingsTests
{
    [Test]
    public void UIManagerSettings는_정렬레이어_정렬순서_평면거리를_설정값으로_반환한다()
    {
        var settings = ScriptableObject.CreateInstance<UIManagerSettings>();
        var so = new SerializedObject(settings);
        so.FindProperty("_sortingLayerName").stringValue = "UI";
        so.FindProperty("_sortingOrder").intValue = 5;
        so.FindProperty("_planeDistance").floatValue = 42f;
        so.ApplyModifiedPropertiesWithoutUndo();

        Assert.AreEqual("UI", settings.SortingLayerName);
        Assert.AreEqual(5, settings.SortingOrder);
        Assert.AreEqual(42f, settings.PlaneDistance);

        Object.DestroyImmediate(settings);
    }
}
```

- [ ] **Step 2: 테스트 실패 확인**

UnityMCP `run_tests` EditMode(`FoundationDI.Tests.Editor`), 필터 `UIManagerSettingsTests`.
Expected: 컴파일 에러 또는 FAIL(프로퍼티/필드 없음).

- [ ] **Step 3: 최소 구현 — 필드/프로퍼티 추가**

`UIManagerSettings.cs`를 아래로 갱신:

```csharp
using UnityEngine;

namespace DarkNaku.FoundationDI
{
    [CreateAssetMenu(fileName = "UIManagerSettings", menuName = "DarkNaku/UIManagerSettings")]
    public sealed class UIManagerSettings : ScriptableObject
    {
        [SerializeField] private Vector2 _referenceResolution = new Vector2(1920f, 1080f);

        [Tooltip("UI Canvas가 얹힐 Sorting Layer 이름. ScreenSpaceCamera일 때 월드 스프라이트와의 정렬에 사용.")]
        [SerializeField] private string _sortingLayerName = "Default";

        [Tooltip("같은 Sorting Layer 내 정렬 순서.")]
        [SerializeField] private int _sortingOrder = 0;

        [Tooltip("ScreenSpaceCamera에서 카메라로부터 UI 평면까지의 거리.")]
        [SerializeField] private float _planeDistance = 100f;

        // CanvasScaler(Scale With Screen Size, Expand)의 기준 해상도
        public Vector2 ReferenceResolution => _referenceResolution;

        public string SortingLayerName => _sortingLayerName;

        public int SortingOrder => _sortingOrder;

        public float PlaneDistance => _planeDistance;
    }
}
```

- [ ] **Step 4: 테스트 통과 확인**

UnityMCP `run_tests` EditMode, 필터 `UIManagerSettingsTests`.
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add Assets/FoundationDI/Runtime/Managers/UIManager/Settings/UIManagerSettings.cs \
        Assets/FoundationDI/Tests/Editor/UIManager/UIManagerSettingsTests.cs
git commit -m "[BEHAVIORAL] UIManagerSettings에 SortingLayerName/SortingOrder/PlaneDistance 추가"
```

---

## Task 3: UIRoot 카메라 바인딩 + DontDestroyOnLoad 제거 (BEHAVIORAL)

**Files:**
- Modify: `Assets/FoundationDI/Runtime/Managers/UIManager/Controllers/UIRoot.cs`
- Modify: `Assets/FoundationDI/Runtime/Managers/UIManager/UIManager.cs:31` (Root 게터 호출부)
- Test: `Assets/FoundationDI/Tests/Runtime/UIManager/UIRootTests.cs`

**Interfaces:**
- Consumes: `UIRoot(..., Func<Camera> cameraProvider)` (Task 1), `UIManagerSettings.SortingLayerName/SortingOrder/PlaneDistance` (Task 2).
- Produces: 카메라가 있으면 `ScreenSpaceCamera`로, 없으면 `ScreenSpaceOverlay`로 구성된 Canvas. UIManager는 settings 값을 UIRoot에 전달.

- [ ] **Step 1: 실패하는 PlayMode 테스트 3개 작성**

`UIRootTests.cs` 상단 using에 `using UnityEngine.SceneManagement;` 추가. 아래 테스트 추가:

```csharp
    [Test]
    public void UIRoot는_카메라가_있으면_ScreenSpaceCamera와_지정정렬거리로_구성한다()
    {
        var camGo = new GameObject("cam", typeof(Camera));
        var cam = camGo.GetComponent<Camera>();

        var root = new UIRoot(default, "Default", 7, 33f, () => cam);
        var canvas = root.GO.GetComponent<Canvas>();

        Assert.AreEqual(RenderMode.ScreenSpaceCamera, canvas.renderMode);
        Assert.AreSame(cam, canvas.worldCamera);
        Assert.AreEqual(7, canvas.sortingOrder);
        Assert.AreEqual(SortingLayer.NameToID("Default"), canvas.sortingLayerID);
        Assert.AreEqual(33f, canvas.planeDistance);

        Object.DestroyImmediate(root.GO);
        Object.DestroyImmediate(camGo);
    }

    [Test]
    public void UIRoot는_카메라가_없으면_ScreenSpaceOverlay로_폴백한다()
    {
        var root = new UIRoot(default, "Default", 0, 100f, () => null);
        var canvas = root.GO.GetComponent<Canvas>();

        Assert.AreEqual(RenderMode.ScreenSpaceOverlay, canvas.renderMode);

        Object.DestroyImmediate(root.GO);
    }

    [Test]
    public void UIRoot의_CanvasGO는_생성시점_active씬에_소속된다()
    {
        var root = new UIRoot(default, "Default", 0, 100f, () => null);

        Assert.AreEqual(SceneManager.GetActiveScene(), root.GO.scene,
            "DontDestroyOnLoad가 아니라 active 씬에 소속되어야 한다");

        Object.DestroyImmediate(root.GO);
    }
```

- [ ] **Step 2: 테스트 실패 확인**

UnityMCP `run_tests` PlayMode, 필터 `UIRootTests`.
Expected: 위 3개 FAIL(현재 Overlay + DDOL 고정).

- [ ] **Step 3: UIRoot 본문 구현**

`UIRoot.cs` 생성자 본문에서 `canvas.renderMode = RenderMode.ScreenSpaceOverlay;` 줄과 `Object.DontDestroyOnLoad(GO);` 줄을 제거하고, `Object.DontDestroyOnLoad(GO);` 자리(레이어 생성 직전)에 아래를 넣는다:

```csharp
    // DontDestroyOnLoad를 하지 않는다 → GO는 생성 시점의 active 씬에 소속되어
    // 그 씬의 카메라(Screen Space - Camera)와 함께 수명을 같이한다.
    var camera = cameraProvider != null ? cameraProvider() : Camera.main;
    if (camera != null)
    {
        canvas.renderMode = RenderMode.ScreenSpaceCamera;
        canvas.worldCamera = camera;
        canvas.planeDistance = planeDistance;
        canvas.sortingLayerID = SortingLayer.NameToID(sortingLayerName);
        canvas.sortingOrder = sortingOrder;
    }
    else
    {
        // 로딩 화면 등 MainCamera 태그 카메라가 없는 순간엔 최상단 Overlay로 폴백.
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        Debug.LogWarning("[UIManager] Camera.main이 없어 UI Canvas를 ScreenSpaceOverlay로 폴백합니다. Sorting Layer 정렬이 적용되지 않습니다.");
    }
```

`cameraProvider` 기본 해석은 `Camera.main`이다. `CreateLayer` 호출부는 그대로 둔다.

- [ ] **Step 4: UIManager 호출부가 settings 값을 전달하도록 수정**

`UIManager.cs:31`의 `Root` 게터를 아래로 바꾼다(이 시점에는 게터 방어 없이 값 전달만; 방어는 Task 4):

```csharp
        private UIRoot Root => _root ??= new UIRoot(
            _settings != null ? _settings.ReferenceResolution : default,
            _settings != null ? _settings.SortingLayerName : "Default",
            _settings != null ? _settings.SortingOrder : 0,
            _settings != null ? _settings.PlaneDistance : 100f);
```

- [ ] **Step 5: 컴파일 + 테스트 통과 확인**

UnityMCP `read_console`로 컴파일 확인 후 `run_tests` PlayMode(`FoundationDI.Tests.Runtime`) 전체 실행.
Expected: 새 `UIRootTests` 3개 PASS. `UIManagerFlowTests`도 PASS(테스트 씬에 카메라가 없어 Overlay 폴백 + LogWarning이 나지만, LogWarning은 테스트를 실패시키지 않는다).

- [ ] **Step 6: plan.md 체크 + Commit**

`plan.md`에서 해당 3개 항목을 `[x]`로 표시.

```bash
git add Assets/FoundationDI/Runtime/Managers/UIManager/Controllers/UIRoot.cs \
        Assets/FoundationDI/Runtime/Managers/UIManager/UIManager.cs \
        Assets/FoundationDI/Tests/Runtime/UIManager/UIRootTests.cs plan.md
git commit -m "[BEHAVIORAL] UIRoot를 ScreenSpaceCamera로 구성하고 DontDestroyOnLoad 제거"
```

---

## Task 4: UIManager 씬 전환 리셋 (BEHAVIORAL)

`activeSceneChanged` 구독으로 씬 전환 시 UI/풀을 초기화하고, 게터 방어로 파괴된 루트를 재구성한다. `Dispose()`를 `ResetSceneState()`로 통합한다.

**Files:**
- Modify: `Assets/FoundationDI/Runtime/Managers/UIManager/UIManager.cs`
- Test: `Assets/FoundationDI/Tests/Runtime/UIManager/UIManagerSceneResetTests.cs` (신규)

**Interfaces:**
- Consumes: `UIManager(UIManagerSettings, UIInstanceFactory, IResourceService)` (기존), `OperationQueue.CancelAndClear()` (기존), `PoolManager.Dispose()` (기존).
- Produces: 씬 전환 시 `_active` presenter teardown + 풀 dispose + `_root`/`_pool` 재구성.

- [ ] **Step 1: 실패하는 PlayMode 테스트 작성**

`Assets/FoundationDI/Tests/Runtime/UIManager/UIManagerSceneResetTests.cs` 생성:

```csharp
using System.Collections;
using Cysharp.Threading.Tasks;
using NSubstitute;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using VContainer;
using DarkNaku.FoundationDI;

public class UIManagerSceneResetTests
{
    public class ResetTrackV : UIView { public static int DestroyCount; protected override void OnDestroyView() => DestroyCount++; }
    [UIPrefab("UI/ResetTrack")]
    public class ResetTrackP : UIPagePresenter<ResetTrackV>
    {
        public bool Shown; public bool AfterHideCalled;
        protected internal override void OnAfterShow() => Shown = true;
        protected internal override void OnAfterHide() => AfterHideCalled = true;
    }

    private GameObject _prefab;

    [SetUp] public void Setup()
    {
        _prefab = new GameObject("resetPrefab", typeof(RectTransform));
        _prefab.AddComponent<ResetTrackV>();
    }

    [TearDown] public void Teardown()
    {
        Object.DestroyImmediate(_prefab);
    }

    [UnityTest]
    public IEnumerator active씬_전환시_활성presenter가_teardown되고_풀View가_파괴된다() => UniTask.ToCoroutine(async () =>
    {
        ResetTrackV.DestroyCount = 0;
        var resource = Substitute.For<IResourceService>();
        resource.Load<GameObject>("UI/ResetTrack").Returns(_prefab);
        var resolver = Substitute.For<IObjectResolver>();
        var settings = ScriptableObject.CreateInstance<UIManagerSettings>();
        var factory = new UIInstanceFactory(resolver);
        var manager = new UIManager(settings, factory, resource);

        var p = manager.Page<ResetTrackP>();
        await UniTask.WaitUntil(() => p.Shown);

        var previous = SceneManager.GetActiveScene();
        var temp = SceneManager.CreateScene("temp_reset_scene");
        SceneManager.SetActiveScene(temp);      // activeSceneChanged 발화 → 리셋
        await UniTask.Yield();                    // Object.Destroy 반영

        Assert.IsTrue(p.AfterHideCalled, "씬 전환 시 활성 presenter OnAfterHide 발화");
        Assert.AreEqual(1, ResetTrackV.DestroyCount, "풀 View 파괴 시 OnDestroyView 호출");

        // 정리
        SceneManager.SetActiveScene(previous);
        manager.Dispose();
        await SceneManager.UnloadSceneAsync(temp).ToUniTask();
    });

    [UnityTest]
    public IEnumerator 씬전환후_Page재요청시_새씬에서_Show까지_도달한다() => UniTask.ToCoroutine(async () =>
    {
        var resource = Substitute.For<IResourceService>();
        resource.Load<GameObject>("UI/ResetTrack").Returns(_prefab);
        var resolver = Substitute.For<IObjectResolver>();
        var settings = ScriptableObject.CreateInstance<UIManagerSettings>();
        var factory = new UIInstanceFactory(resolver);
        var manager = new UIManager(settings, factory, resource);

        var p1 = manager.Page<ResetTrackP>();
        await UniTask.WaitUntil(() => p1.Shown);

        var previous = SceneManager.GetActiveScene();
        var temp = SceneManager.CreateScene("temp_reset_scene2");
        SceneManager.SetActiveScene(temp);
        await UniTask.Yield();

        var p2 = manager.Page<ResetTrackP>();
        await UniTask.WhenAny(UniTask.WaitUntil(() => p2.Shown), UniTask.Delay(3000));
        Assert.IsTrue(p2.Shown, "씬 전환 후 재구성된 UIManager에서 Page가 표시되어야 한다");
        Assert.AreNotSame(p1, p2, "씬 전환 후엔 새 presenter 인스턴스");

        SceneManager.SetActiveScene(previous);
        manager.Dispose();
        await SceneManager.UnloadSceneAsync(temp).ToUniTask();
    });
}
```

- [ ] **Step 2: 테스트 실패 확인**

UnityMCP `run_tests` PlayMode, 필터 `UIManagerSceneResetTests`.
Expected: FAIL(씬 전환 시 리셋이 없어 `AfterHideCalled == false` / `DestroyCount == 0`, 또는 두 번째 테스트에서 파괴된 루트 참조로 hang/에러).

- [ ] **Step 3: UIManager에 씬 생명주기 구현**

`UIManager.cs` 상단 using에 `using UnityEngine.SceneManagement;` 추가.

`Root` 게터를 블록 형태로 바꾸고 게터 방어 추가:

```csharp
        private UIRoot Root
        {
            get
            {
                if (_root != null && _root.GO == null) ResetSceneState(); // 씬 파괴로 fake-null → 재구성
                return _root ??= new UIRoot(
                    _settings != null ? _settings.ReferenceResolution : default,
                    _settings != null ? _settings.SortingLayerName : "Default",
                    _settings != null ? _settings.SortingOrder : 0,
                    _settings != null ? _settings.PlaneDistance : 100f);
            }
        }
```

생성자에 구독 추가(기존 `internal UIManager(...)` 본문 끝에):

```csharp
            SceneManager.activeSceneChanged += OnActiveSceneChanged;
```

씬 전환 핸들러 + 리셋 로직 추가(클래스 내 아무 곳, 예: `Dispose` 위):

```csharp
        private void OnActiveSceneChanged(Scene previous, Scene next)
        {
            if (_disposed || _root == null) return;
            ResetSceneState();
        }

        // 씬 전환/Dispose 공통: 활성 UI teardown + 풀/루트 파괴 후 참조를 비워 다음 접근에 재구성.
        private void ResetSceneState()
        {
            _queue.CancelAndClear();

            foreach (var p in _active)
            {
                p.OnBeforeHide(); p.Fire(UIPresenter.LifecycleEvent.BeforeHide);
                p.OnAfterHide(); p.Fire(UIPresenter.LifecycleEvent.AfterHide);
            }

            _active.Clear();
            _pages.Clear();
            _popups.Clear();
            _overlays.Clear();

            _pool?.Dispose();
            _pool = null;

            if (_root != null && _root.GO != null) UnityEngine.Object.Destroy(_root.GO);
            _root = null;
        }
```

`Dispose()`를 아래로 교체(리셋 통합 + 구독 해제):

```csharp
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            SceneManager.activeSceneChanged -= OnActiveSceneChanged;
            ResetSceneState();
        }
```

- [ ] **Step 4: 컴파일 + 대상 테스트 통과 확인**

UnityMCP `read_console` 후 `run_tests` PlayMode, 필터 `UIManagerSceneResetTests`.
Expected: 2개 PASS.

- [ ] **Step 5: 전체 회귀 테스트**

UnityMCP `run_tests` PlayMode(`FoundationDI.Tests.Runtime`) + EditMode(`FoundationDI.Tests.Editor`) 전체.
Expected: 전부 PASS. 특히 `UIManagerFlowTests`의 `Dispose시_...` 테스트가 리셋 통합 후에도 PASS인지 확인.

- [ ] **Step 6: plan.md 체크 + Commit**

`plan.md`에서 씬 전환 관련 2개 항목을 `[x]`로 표시.

```bash
git add Assets/FoundationDI/Runtime/Managers/UIManager/UIManager.cs \
        Assets/FoundationDI/Tests/Runtime/UIManager/UIManagerSceneResetTests.cs plan.md
git commit -m "[BEHAVIORAL] UIManager가 씬 전환 시 UI/풀을 초기화하고 재구성"
```

---

## 완료 후 확인

- 실제 씬에 `MainCamera` 태그 카메라가 있는 환경에서 UI가 지정한 Sorting Layer 위치에 월드 스프라이트와 섞여 렌더되는지 수동 확인(자동 테스트는 카메라 유무 분기까지만 커버).
- `README`(UIManager) 갱신 여부 검토 — 정렬/카메라 설정과 "씬마다 초기화" 수명이 문서화 대상이면 별도 `docs:` 커밋으로 추가.
