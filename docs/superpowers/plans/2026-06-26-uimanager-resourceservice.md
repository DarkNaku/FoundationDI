# UIManager → ResourceService 전환 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** UIManager의 프리팹 로딩을 자체 `IUIAssetLoader` 대신 공용 `IResourceService`(Addressables 기반)에 위임한다.

**Architecture:** `UIInstanceFactory`가 `IResourceService`에 직접 의존하도록 바꾸고(동기 `Load<GameObject>`), `IUIAssetLoader`/`ResourcesUILoader`/`AddressablesUILoader`를 제거한다. `IResourceService`는 컴포지션 루트(`RootLifetimeScope`)에 등록하고 `RegisterUIManager`는 그것이 등록되어 있다고 가정한다.

**Tech Stack:** Unity 6, VContainer, Addressables, UniTask, Unity Test Framework(NUnit), NSubstitute.

## Global Constraints

- 네임스페이스: `DarkNaku.FoundationDI` (모든 런타임 타입)
- 로딩 백엔드: **Addressables 전용** — UI 프리팹은 Addressables 항목. Resources 경로 제거.
- `UIInstanceFactory`는 `IResourceService`에 직접 의존한다(어댑터 없음).
- 프리팹 Release는 추가하지 않는다(현행 유지). 정리는 `ResourceService.Dispose`에 위임.
- 테스트 함수명은 **한글**로 작성한다.
- 커밋 규율: 커밋 제목에 `[STRUCTURAL]` 또는 `[BEHAVIORAL]` 접두어를 단다(프로젝트 규약).
- 커밋 메시지 말미에 `Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>` 추가.

### TDD 메모 (중요)

`UIInstanceFactory` 생성자 시그니처 변경은 **컴파일 결합** 변경이다. 변경 즉시 호출처
(UIInstanceFactoryTests, UIManagerFlowTests ×4, DI 해석)가 모두 깨지므로 한 커밋으로 묶어야
프로젝트가 컴파일된다. 따라서 Task 1은 "시그니처 변경 리팩터링"으로 수행한다: **먼저 모든 호출처
(테스트 포함)를 새 시그니처로 바꿔 RED(컴파일 실패)를 만들고**, 그다음 프로덕션 코드를 바꿔
GREEN으로 만든다. 데드코드 삭제는 Task 2에서 분리한다.

### 테스트 실행 방법 (모든 "Run test" 스텝 공통)

스크립트 변경 후:
1. `mcp__UnityMCP__refresh_unity` (compile: request) → `mcp__UnityMCP__read_console` (types ["error","warning"])로 클린 컴파일 확인. 컴파일 완료 대기.
2. 테스트 실행: `mcp__UnityMCP__run_tests`.
   - EditMode: `mode: "EditMode"`, `assembly_names: ["FoundationDI.Tests.Editor"]` (UIManager EditMode) 및 `["FoundationDI.Tests"]`(ResourceServiceTest 회귀).
   - PlayMode: `mode: "PlayMode"`, `assembly_names: ["FoundationDI.Tests.Runtime"]`, `init_timeout: 120000`.
   - `get_test_job`으로 `wait_timeout: 60` 폴링.
3. 새/삭제 파일의 `.meta`도 git에 반영한다.

---

## File Structure

**런타임 (수정/삭제):**
- `Assets/FoundationDI/Runtime/Managers/UIManager/Controllers/UIInstanceFactory.cs` — 의존 교체 (Task 1)
- `Assets/FoundationDI/Runtime/Managers/UIManager/DI/UIManagerVContainerExtensions.cs` — loader 등록 제거 (Task 1)
- `Assets/Scripts/LifetimeScopes/RootLifetimeScope.cs` — IResourceService 등록 추가 (Task 1)
- `Assets/FoundationDI/Runtime/Managers/UIManager/Loading/*.cs` — 3개 삭제 (Task 2)

**테스트 (수정/삭제):**
- `Assets/FoundationDI/Tests/Editor/UIManager/UIInstanceFactoryTests.cs` — 모킹 교체 (Task 1)
- `Assets/FoundationDI/Tests/Editor/UIManager/DIRegistrationTests.cs` — IResourceService 등록 추가 (Task 1)
- `Assets/FoundationDI/Tests/Runtime/UIManager/UIManagerFlowTests.cs` — 모킹 교체 ×4 (Task 1)
- `Assets/FoundationDI/Tests/Editor/UIManager/ResourcesUILoaderTests.cs` — 삭제 (Task 2)

---

## Task 1: UIInstanceFactory를 IResourceService로 전환 (원자적 시그니처 변경 + DI + 모든 테스트)

이 태스크는 컴파일 결합 때문에 한 커밋으로 처리한다. 먼저 모든 호출처를 새 시그니처로 바꿔 RED(컴파일 실패)를 만들고, 그다음 프로덕션 코드와 DI를 바꿔 GREEN으로 만든다.

**Files:**
- Modify: `Assets/FoundationDI/Runtime/Managers/UIManager/Controllers/UIInstanceFactory.cs`
- Modify: `Assets/FoundationDI/Runtime/Managers/UIManager/DI/UIManagerVContainerExtensions.cs`
- Modify: `Assets/Scripts/LifetimeScopes/RootLifetimeScope.cs`
- Test: `Assets/FoundationDI/Tests/Editor/UIManager/UIInstanceFactoryTests.cs`
- Test: `Assets/FoundationDI/Tests/Editor/UIManager/DIRegistrationTests.cs`
- Test: `Assets/FoundationDI/Tests/Runtime/UIManager/UIManagerFlowTests.cs`

**Interfaces:**
- Consumes: `IResourceService.Load<T>(string key) where T : Object`, `ResourceService`(기본 생성자), `UIInstanceFactory.Create(Type, IUIElementHost)`, `RegisterUIManager(this IContainerBuilder, UIManagerSettings)`
- Produces: `UIInstanceFactory(IObjectResolver resolver, IResourceService resource)` 생성자

- [ ] **Step 1: UIInstanceFactoryTests를 IResourceService 모킹으로 교체**

`Assets/FoundationDI/Tests/Editor/UIManager/UIInstanceFactoryTests.cs` 전체를 다음으로 교체:

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
    public void ResourceService가_제공한_prefab으로_Presenter를_생성하고_바인딩한다()
    {
        var prefab = new GameObject("prefab", typeof(RectTransform));
        prefab.AddComponent<V>();

        var resource = Substitute.For<IResourceService>();
        resource.Load<GameObject>("UI/Sample").Returns(prefab);

        var resolver = Substitute.For<IObjectResolver>();
        var host = Substitute.For<IUIElementHost>();

        var factory = new UIInstanceFactory(resolver, resource);
        var presenter = factory.Create(typeof(P), host);

        Assert.IsInstanceOf<P>(presenter);
        Assert.IsNotNull(((P)presenter).ViewBaseForTest);   // Bind 확인용 internal 노출

        Object.DestroyImmediate(prefab);
    }
}
```

- [ ] **Step 2: DIRegistrationTests에 IResourceService 등록 추가**

`Assets/FoundationDI/Tests/Editor/UIManager/DIRegistrationTests.cs` 전체를 다음으로 교체:

```csharp
using NUnit.Framework;
using UnityEngine;
using VContainer;
using DarkNaku.FoundationDI;

public class DIRegistrationTests
{
    [Test]
    public void 컨테이너에서_IUIManager를_해석할_수_있다()
    {
        var builder = new ContainerBuilder();
        builder.Register<IResourceService, ResourceService>(Lifetime.Singleton);
        builder.RegisterUIManager(ScriptableObject.CreateInstance<UIManagerSettings>());

        var container = builder.Build();
        Assert.IsNotNull(container.Resolve<IUIManager>());
    }
}
```

- [ ] **Step 3: UIManagerFlowTests의 로더 모킹 ×4를 IResourceService로 교체**

`Assets/FoundationDI/Tests/Runtime/UIManager/UIManagerFlowTests.cs`의 4개 `[UnityTest]` 메서드에서, 각 메서드 첫머리의

```csharp
        var loader = Substitute.For<IUIAssetLoader>();
        loader.Load("<키>").Returns(<프리팹>);
        var resolver = Substitute.For<IObjectResolver>();
        var settings = ScriptableObject.CreateInstance<UIManagerSettings>();
        var factory = new UIInstanceFactory(resolver, loader);
```

를 메서드별 키/프리팹에 맞춰 아래로 교체한다(나머지 본문·SetUp·Teardown 불변).

- `Page_호출시_OnShow까지_도달한다` → 키 `"UI/Sample"`, 프리팹 `_prefab`
- `Popup_호출시_스택_Top이_된다` → 키 `"UI/SamplePopup"`, 프리팹 `_popupPrefab`
- `Overlay_호출시_OnShow까지_도달한다` → 키 `"UI/SampleOverlay"`, 프리팹 `_overlayPrefab`
- `재Show시_GameObject가_다시_활성화된다` → 키 `"UI/ReshowSample"`, 프리팹 `_reshowPrefab`

교체 형태(예: Page):

```csharp
        var resource = Substitute.For<IResourceService>();
        resource.Load<GameObject>("UI/Sample").Returns(_prefab);
        var resolver = Substitute.For<IObjectResolver>();
        var settings = ScriptableObject.CreateInstance<UIManagerSettings>();
        var factory = new UIInstanceFactory(resolver, resource);
```

- [ ] **Step 4: 테스트 실행 — 컴파일 실패(RED) 확인**

EditMode(`FoundationDI.Tests.Editor`) 실행을 시도한다.
Expected: **컴파일 실패** — `UIInstanceFactory` 생성자가 아직 `IUIAssetLoader`를 받으므로 위 테스트들의 `new UIInstanceFactory(resolver, resource)`가 타입 불일치. (RED)

- [ ] **Step 5: UIInstanceFactory 의존 교체**

`Assets/FoundationDI/Runtime/Managers/UIManager/Controllers/UIInstanceFactory.cs`에서 필드/생성자를 다음으로 변경한다.

```csharp
        private readonly IObjectResolver _resolver;
        private readonly IResourceService _resource;

        public UIInstanceFactory(IObjectResolver resolver, IResourceService resource)
        {
            _resolver = resolver;
            _resource = resource;
        }
```

`Create()` 내부의 prefab 로드 한 줄을 변경한다:

```csharp
            var prefab = _resource.Load<GameObject>(key);
```

(나머지 `Create()` 본문 — Instantiate/UIView 확인/Activator/Inject/Bind/초기화 — 은 그대로.)

- [ ] **Step 6: RegisterUIManager에서 loader 등록 제거**

`Assets/FoundationDI/Runtime/Managers/UIManager/DI/UIManagerVContainerExtensions.cs` 전체를 다음으로 교체:

```csharp
using VContainer;

namespace DarkNaku.FoundationDI
{
    public static class UIManagerVContainerExtensions
    {
        /// <summary>
        /// UIManager를 컨테이너에 등록한다.
        /// 전제: 호출 전에 <see cref="IResourceService"/>가 이미 등록되어 있어야 한다
        /// (UIInstanceFactory가 프리팹 로드를 IResourceService에 위임함).
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

- [ ] **Step 7: RootLifetimeScope에 IResourceService 등록 추가**

`Assets/Scripts/LifetimeScopes/RootLifetimeScope.cs` 전체를 다음으로 교체:

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
        builder.Register<IResourceService, ResourceService>(Lifetime.Singleton);
        builder.RegisterUIManager(_uiSettings);
    }
}
```

- [ ] **Step 8: 테스트 실행 — GREEN 확인**

`refresh_unity` → `read_console`로 컴파일 에러 0 확인 후:
- EditMode `FoundationDI.Tests.Editor` 실행 → UIInstanceFactoryTests, DIRegistrationTests + 기존 UIManager EditMode 테스트 모두 PASS.
- EditMode `FoundationDI.Tests` 실행 → ResourceServiceTest 회귀 PASS.
- PlayMode `FoundationDI.Tests.Runtime` (`init_timeout: 120000`) 실행 → UIManagerFlowTests 4개 PASS.

Expected: 모두 PASS, 컴파일 경고 없음.

> 참고: 이 시점에 `Loading/`의 로더 파일들은 미참조 상태로 남아 있으나 컴파일·테스트는 통과한다(Task 2에서 삭제).

- [ ] **Step 9: 커밋**

```bash
git add Assets/FoundationDI/Runtime/Managers/UIManager/Controllers/UIInstanceFactory.cs \
        Assets/FoundationDI/Runtime/Managers/UIManager/DI/UIManagerVContainerExtensions.cs \
        Assets/Scripts/LifetimeScopes/RootLifetimeScope.cs \
        Assets/FoundationDI/Tests/Editor/UIManager/UIInstanceFactoryTests.cs \
        Assets/FoundationDI/Tests/Editor/UIManager/DIRegistrationTests.cs \
        Assets/FoundationDI/Tests/Runtime/UIManager/UIManagerFlowTests.cs
git commit -m "[BEHAVIORAL] UIManager 프리팹 로딩을 IResourceService 위임으로 전환

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Task 2: 미사용 로더 코드 및 테스트 제거 (구조 정리)

Task 1 완료 후 `IUIAssetLoader`/`ResourcesUILoader`/`AddressablesUILoader`는 어디서도 참조되지 않는다. 안전하게 삭제한다.

**Files:**
- Delete: `Assets/FoundationDI/Runtime/Managers/UIManager/Loading/IUIAssetLoader.cs` (+ `.meta`)
- Delete: `Assets/FoundationDI/Runtime/Managers/UIManager/Loading/AddressablesUILoader.cs` (+ `.meta`)
- Delete: `Assets/FoundationDI/Runtime/Managers/UIManager/Loading/ResourcesUILoader.cs` (+ `.meta`)
- Delete: `Assets/FoundationDI/Tests/Editor/UIManager/ResourcesUILoaderTests.cs` (+ `.meta`)

- [ ] **Step 1: 미참조 확인**

```bash
cd /Users/darknaku/Projects/FoundationDI
grep -rn "IUIAssetLoader\|ResourcesUILoader\|AddressablesUILoader" Assets/FoundationDI --include="*.cs"
```
Expected: 출력 없음. 출력이 있으면 삭제를 멈추고 해당 참조를 먼저 처리한다.

- [ ] **Step 2: 파일 삭제 (.meta 포함)**

```bash
cd /Users/darknaku/Projects/FoundationDI
git rm Assets/FoundationDI/Runtime/Managers/UIManager/Loading/IUIAssetLoader.cs \
       Assets/FoundationDI/Runtime/Managers/UIManager/Loading/IUIAssetLoader.cs.meta \
       Assets/FoundationDI/Runtime/Managers/UIManager/Loading/AddressablesUILoader.cs \
       Assets/FoundationDI/Runtime/Managers/UIManager/Loading/AddressablesUILoader.cs.meta \
       Assets/FoundationDI/Runtime/Managers/UIManager/Loading/ResourcesUILoader.cs \
       Assets/FoundationDI/Runtime/Managers/UIManager/Loading/ResourcesUILoader.cs.meta \
       Assets/FoundationDI/Tests/Editor/UIManager/ResourcesUILoaderTests.cs \
       Assets/FoundationDI/Tests/Editor/UIManager/ResourcesUILoaderTests.cs.meta
```

`Loading/` 폴더가 비면 폴더 `.meta`도 제거한다:
```bash
rmdir Assets/FoundationDI/Runtime/Managers/UIManager/Loading 2>/dev/null && \
  git rm Assets/FoundationDI/Runtime/Managers/UIManager/Loading.meta 2>/dev/null || true
```

- [ ] **Step 3: 테스트 실행 — 회귀 확인**

`refresh_unity` → `read_console`로 컴파일 에러 0 확인. 그다음:
- EditMode `FoundationDI.Tests.Editor` + `FoundationDI.Tests` → 모두 PASS.
- PlayMode `FoundationDI.Tests.Runtime` (`init_timeout: 120000`) → 모두 PASS.

Expected: 모든 테스트 PASS, 컴파일 에러/경고 없음.

- [ ] **Step 4: 커밋**

```bash
cd /Users/darknaku/Projects/FoundationDI
git add -A
git commit -m "[STRUCTURAL] 미사용 IUIAssetLoader/로더 구현 및 테스트 제거

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## 완료 기준

- `grep -rn "IUIAssetLoader" Assets` 결과가 비어 있다.
- UIInstanceFactory가 `IResourceService.Load<GameObject>(key)`로 프리팹을 로드한다.
- `RootLifetimeScope`가 `IResourceService`를 등록하고, `RegisterUIManager`는 loader를 등록하지 않는다.
- EditMode(`FoundationDI.Tests.Editor`, `FoundationDI.Tests`) + PlayMode(`FoundationDI.Tests.Runtime`) 전체 통과.
- 컴파일 경고 없음. 동작/구조 변경 커밋이 분리됨.

## 범위 외 (후속)

- 프리팹 Release를 UI 요소 생명주기에 맞춰 호출하는 참조 카운팅 활용.
- PoolService/SoundService의 ResourceService 위임.
