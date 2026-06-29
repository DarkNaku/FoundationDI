# ResourceService 백엔드별 구체 클래스 분리 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** `ResourceService`를 코어 + 백엔드별 구체 클래스(`AddressableResourceService`/`DefaultResourceService`)로 분리해 `Register<IResourceService, AddressableResourceService>(...)` 형태로 등록할 수 있게 한다.

**Architecture:** 코어 `ResourceService`는 `IResourceProvider` 단일 생성자로 캐싱·참조 카운팅을 담당하고, 무파라미터 단일 생성자 sealed 구체 두 개가 각자 백엔드 provider를 고정 주입한다. 신규 `ResourcesProvider`가 Resources.Load 백엔드를 제공한다.

**Tech Stack:** Unity 6000.3(C# 9), VContainer, UniTask, NSubstitute, Unity Test Framework(EditMode).

## Global Constraints

- 네임스페이스: `DarkNaku.FoundationDI`.
- 에셋 로딩 백엔드는 `IResourceProvider` seam 뒤에 둔다. 코어 `ResourceService`는 직접 `Resources`/`Addressables`를 호출하지 않는다.
- 테스트는 EditMode, NSubstitute로 seam 대체, 테스트 함수명은 한국어. DI 등록 테스트는 `Assets/FoundationDI/Tests/Editor/UIManager/DIRegistrationTests.cs`(asmdef `FoundationDI.Tests.Editor`, VContainer 참조).
- 컴파일·테스트는 UnityMCP로만: 변경 후 `refresh_unity(compile=request, mode=force)` → `read_console(types=[error])` → `run_tests` + `get_test_job`.
- 커밋: STRUCTURAL/BEHAVIORAL 분리, 제목 접두어. 끝에 `Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>`. 모든 테스트 통과 시에만 커밋. `.meta`는 Unity가 생성.

## 파일 구조

- Modify: `Assets/FoundationDI/Runtime/Services/ResourceService/ResourceService.cs` — 무파라미터 생성자 제거.
- Create: `Assets/FoundationDI/Runtime/Services/ResourceService/AddressableResourceService.cs` — Addressables 백엔드 고정 구체.
- Create: `Assets/FoundationDI/Runtime/Services/ResourceService/ResourcesProvider.cs` — Resources.Load 백엔드 provider.
- Create: `Assets/FoundationDI/Runtime/Services/ResourceService/DefaultResourceService.cs` — Resources 백엔드 고정 구체.
- Modify: `Assets/FoundationDI/Tests/Editor/UIManager/DIRegistrationTests.cs` — 등록 패턴 갱신 + resolve 검증.
- Modify(docs): 메인/루트/ResourceService/SoundService/UIManager README.

`new ResourceService()`(무파라미터) 호출처는 `DIRegistrationTests.cs` 1곳뿐임을 확인했다(Task 1에서 함께 갱신).

---

## Task 1: 코어 생성자 제거 + AddressableResourceService

**Files:**
- Modify: `Assets/FoundationDI/Runtime/Services/ResourceService/ResourceService.cs`
- Create: `Assets/FoundationDI/Runtime/Services/ResourceService/AddressableResourceService.cs`
- Test: `Assets/FoundationDI/Tests/Editor/UIManager/DIRegistrationTests.cs`

**Interfaces:**
- Consumes: `IResourceService`, `IResourceProvider`, `AddressableResourceProvider`(기존), VContainer `ContainerBuilder`.
- Produces: `sealed class AddressableResourceService : ResourceService`(무파라미터 생성자). 코어 `ResourceService`는 `ResourceService(IResourceProvider)` 단일 생성자만 보유.

- [ ] **Step 1: 실패 테스트 작성** — `DIRegistrationTests.cs`를 아래로 교체

기존 람다 등록을 구체 클래스 등록으로 바꾸고, greedy 문제 해결을 검증하는 테스트를 추가한다.

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
        builder.Register<IResourceService, AddressableResourceService>(Lifetime.Singleton);
        builder.RegisterUIManager(ScriptableObject.CreateInstance<UIManagerSettings>());

        var container = builder.Build();
        Assert.IsNotNull(container.Resolve<IUIManager>());
    }

    [Test]
    public void AddressableResourceService로_등록하면_IResourceProvider_없이_해석된다()
    {
        var builder = new ContainerBuilder();
        builder.Register<IResourceService, AddressableResourceService>(Lifetime.Singleton);

        var container = builder.Build();
        Assert.IsNotNull(container.Resolve<IResourceService>());
    }
}
```

- [ ] **Step 2: 컴파일/실패 확인**

UnityMCP: `refresh_unity(compile=request, mode=force)` → `read_console(types=[error])`. `AddressableResourceService` 미정의로 컴파일 에러 = RED.

- [ ] **Step 3: 구현** — 코어 생성자 제거 + 구체 클래스 추가

(a) `ResourceService.cs`에서 무파라미터 생성자를 제거한다. 아래 블록
```csharp
        public ResourceService() : this(new AddressableResourceProvider())
        {
        }

        public ResourceService(IResourceProvider provider)
        {
            _provider = provider;
        }
```
를 다음으로 변경:
```csharp
        public ResourceService(IResourceProvider provider)
        {
            _provider = provider;
        }
```

(b) `AddressableResourceService.cs` 생성:
```csharp
namespace DarkNaku.FoundationDI
{
    /// <summary>
    /// Addressables 백엔드를 사용하는 ResourceService 구체 구현.
    /// 무파라미터 단일 생성자라 Register&lt;IResourceService, AddressableResourceService&gt;로 등록할 수 있다.
    /// </summary>
    public sealed class AddressableResourceService : ResourceService
    {
        public AddressableResourceService() : base(new AddressableResourceProvider())
        {
        }
    }
}
```

- [ ] **Step 4: 컴파일 확인 후 테스트 통과 확인**

UnityMCP: `refresh_unity` → `read_console(types=[error])`(에러 0) → `run_tests(mode=EditMode, assembly_names=[FoundationDI.Tests.Editor])` → `get_test_job`. DIRegistrationTests 2개 PASS 기대. 이어 전체 회귀 확인: `run_tests(mode=EditMode, assembly_names=[FoundationDI.Tests])`도 통과(코어 로직 불변, 기존 41개).

- [ ] **Step 5: 커밋**

```bash
git add Assets/FoundationDI/Runtime/Services/ResourceService/ResourceService.cs Assets/FoundationDI/Runtime/Services/ResourceService/AddressableResourceService.cs Assets/FoundationDI/Runtime/Services/ResourceService/AddressableResourceService.cs.meta Assets/FoundationDI/Tests/Editor/UIManager/DIRegistrationTests.cs
git commit -m "$(cat <<'EOF'
[BEHAVIORAL] AddressableResourceService 추가 및 코어 기본 생성자 제거

- ResourceService 무파라미터 생성자 제거(greedy 생성자 모호성 해소)
- AddressableResourceService(무파라미터 단일 생성자)로 Register<I,C> 등록 가능
- DIRegistrationTests를 구체 클래스 등록으로 갱신 + resolve 검증 추가

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 2: ResourcesProvider + DefaultResourceService

**Files:**
- Create: `Assets/FoundationDI/Runtime/Services/ResourceService/ResourcesProvider.cs`
- Create: `Assets/FoundationDI/Runtime/Services/ResourceService/DefaultResourceService.cs`
- Test: `Assets/FoundationDI/Tests/Editor/UIManager/DIRegistrationTests.cs`

**Interfaces:**
- Consumes: `IResourceProvider`, 코어 `ResourceService`(Task 1).
- Produces: `class ResourcesProvider : IResourceProvider`, `sealed class DefaultResourceService : ResourceService`(무파라미터 생성자).

- [ ] **Step 1: 실패 테스트 작성** — `DIRegistrationTests.cs`에 테스트 추가

`AddressableResourceService로_등록하면...` 테스트 아래에 추가:

```csharp
    [Test]
    public void DefaultResourceService로_등록하면_IResourceProvider_없이_해석된다()
    {
        var builder = new ContainerBuilder();
        builder.Register<IResourceService, DefaultResourceService>(Lifetime.Singleton);

        var container = builder.Build();
        Assert.IsNotNull(container.Resolve<IResourceService>());
    }
```

- [ ] **Step 2: 컴파일/실패 확인**

UnityMCP: `refresh_unity` → `read_console(types=[error])`. `DefaultResourceService` 미정의로 컴파일 에러 = RED.

- [ ] **Step 3: 구현** — provider + 구체 클래스 추가

(a) `ResourcesProvider.cs` 생성:
```csharp
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;
using Object = UnityEngine.Object;

namespace DarkNaku.FoundationDI
{
    /// <summary>
    /// Resources.Load 기반 IResourceProvider 구현.
    /// </summary>
    /// <remarks>
    /// Resources는 Addressables 같은 핸들 해제가 없어 메모리 반환이 제한적이다.
    /// GameObject(프리팹) 등은 개별 언로드가 불가하며, 확실한 회수는
    /// Resources.UnloadUnusedAssets()(호출자 책임)로만 가능하다.
    /// </remarks>
    public class ResourcesProvider : IResourceProvider
    {
        private readonly Dictionary<string, Object> _assets = new();

        public async UniTask<T> LoadAsync<T>(string key) where T : Object
        {
            var request = Resources.LoadAsync<T>(key);
            await request.ToUniTask();
            var asset = request.asset as T;
            if (asset != null) _assets[key] = asset;
            return asset;
        }

        public T Load<T>(string key) where T : Object
        {
            var asset = Resources.Load<T>(key);
            if (asset != null) _assets[key] = asset;
            return asset;
        }

        public void Release(string key)
        {
            if (!_assets.TryGetValue(key, out var asset)) return;

            _assets.Remove(key);

            // Resources.UnloadAsset은 GameObject/Component에 사용할 수 없다.
            if (asset != null && asset is not GameObject && asset is not Component)
            {
                Resources.UnloadAsset(asset);
            }
        }
    }
}
```

(b) `DefaultResourceService.cs` 생성:
```csharp
namespace DarkNaku.FoundationDI
{
    /// <summary>
    /// Resources.Load 백엔드를 사용하는 ResourceService 구체 구현.
    /// 무파라미터 단일 생성자라 Register&lt;IResourceService, DefaultResourceService&gt;로 등록할 수 있다.
    /// </summary>
    public sealed class DefaultResourceService : ResourceService
    {
        public DefaultResourceService() : base(new ResourcesProvider())
        {
        }
    }
}
```

- [ ] **Step 4: 컴파일 확인 후 테스트 통과 확인**

UnityMCP: `refresh_unity` → `read_console(types=[error])`(에러 0) → `run_tests(mode=EditMode, assembly_names=[FoundationDI.Tests.Editor])` → `get_test_job`. DIRegistrationTests 3개 PASS 기대.

- [ ] **Step 5: 커밋**

```bash
git add Assets/FoundationDI/Runtime/Services/ResourceService/ResourcesProvider.cs Assets/FoundationDI/Runtime/Services/ResourceService/ResourcesProvider.cs.meta Assets/FoundationDI/Runtime/Services/ResourceService/DefaultResourceService.cs Assets/FoundationDI/Runtime/Services/ResourceService/DefaultResourceService.cs.meta Assets/FoundationDI/Tests/Editor/UIManager/DIRegistrationTests.cs
git commit -m "$(cat <<'EOF'
[BEHAVIORAL] ResourcesProvider + DefaultResourceService 추가

- Resources.Load 백엔드 provider(언로드 한계 문서화)
- DefaultResourceService(무파라미터 단일 생성자)로 Register<I,C> 등록 가능
- DIRegistrationTests에 resolve 검증 추가

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 3: README 갱신

**Files:**
- Modify: `Assets/FoundationDI/README.md`, `README.md`(루트), `Runtime/Services/ResourceService/README.md`, `Runtime/Services/SoundService/README.md`, `Runtime/Managers/UIManager/README.md`

**Interfaces:** 문서 전용(런타임 API 없음).

> 문서 변경이므로 컴파일/테스트 대상이 아니다. 검증은 **등록 예제가 새 패턴과 일치**하는지 + 컴파일 그린 유지로 한다.

- [ ] **Step 1: 등록 예제 통일**

다음 파일들에서 `IResourceService` 등록 예제를 `builder.Register<IResourceService, AddressableResourceService>(Lifetime.Singleton);`로 교체한다.

- `Assets/FoundationDI/README.md:56` — `builder.Register<IResourceService, ResourceService>(Lifetime.Singleton);` → 위 형태
- 루트 `README.md`의 동일 라인(빠른 시작 코드 블록)
- `Runtime/Services/ResourceService/README.md:26` — `builder.Register<IResourceService, ResourceService>(Lifetime.Singleton);` → 위 형태
- `Runtime/Services/SoundService/README.md:40` — `builder.Register<IResourceService, ResourceService>(Lifetime.Singleton);` → 위 형태
- `Runtime/Managers/UIManager/README.md:31` — `builder.Register<IResourceService>(_ => new ResourceService(), Lifetime.Singleton);` → 위 형태

- [ ] **Step 2: ResourceService README에 백엔드 설명 추가**

`Runtime/Services/ResourceService/README.md`의 개요/사용법에 다음을 반영한다(정확한 위치는 문서 흐름에 맞춰 삽입):

- 개요에 한 줄: "구체 구현은 백엔드별로 둘 — `AddressableResourceService`(Addressables) / `DefaultResourceService`(Resources.Load). 코어 `ResourceService`는 `IResourceProvider`를 받는 단일 생성자이며 직접 등록 대신 구체 클래스를 등록한다."
- DI 등록 예제를 구체 클래스로 교체(Step 1과 동일).
- 한계 절에 한 줄: "`DefaultResourceService`(Resources)는 Addressables 같은 핸들 해제가 없어 메모리 반환이 제한적이다(`GameObject` 등 개별 언로드 불가, 확실한 회수는 `Resources.UnloadUnusedAssets()`)."

- [ ] **Step 3: 컴파일 그린 확인 후 커밋**

UnityMCP: `refresh_unity` → `read_console(types=[error])`(에러 0, 문서 변경이라 영향 없음 확인).

```bash
git add Assets/FoundationDI/README.md README.md "Assets/FoundationDI/Runtime/Services/ResourceService/README.md" "Assets/FoundationDI/Runtime/Services/SoundService/README.md" "Assets/FoundationDI/Runtime/Managers/UIManager/README.md"
git commit -m "$(cat <<'EOF'
docs: ResourceService 등록 예제를 구체 클래스로 통일

- Register<IResourceService, AddressableResourceService>로 등록 예제 갱신(메인/루트/각 서비스 README)
- ResourceService README에 백엔드 2종(Addressable/Default)·Resources 한계 설명 추가

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## 자체 검토 결과

- **스펙 커버리지**: 3.1 코어+구체 → Task1(코어 생성자 제거, AddressableResourceService)·Task2(DefaultResourceService); 3.2 ResourcesProvider → Task2; 3.3 DI 등록 → Task1·Task2 테스트; 4 영향 갱신(DIRegistrationTests/README) → Task1·Task3; 5 에러처리(Release) → Task2; 6 테스트전략 → 각 Task. 5(ResourcesProvider 자동 테스트 생략)·7(범위 밖)은 의도적. 누락 없음.
- **플레이스홀더**: 없음(모든 코드/명령 구체적).
- **타입 일관성**: `ResourceService(IResourceProvider)` 단일 생성자, `AddressableResourceService()`/`DefaultResourceService()` 무파라미터, `ResourcesProvider`(`LoadAsync`/`Load`/`Release`)가 전 태스크에서 일치. 등록 패턴 `Register<IResourceService, AddressableResourceService>`가 테스트·README에서 동일.
