# ResourceService Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Addressables 기반 범용 에셋 로더 서비스(`ResourceService`)를 참조 카운팅 + 캐싱과 함께 구현한다.

**Architecture:** `ResourceService`는 참조 카운팅/캐싱만 담당하고, 실제 Addressables 호출은 `IResourceProvider` seam으로 분리한다. 단위 테스트는 NSubstitute로 provider를 대체해 EditMode에서 검증하고, 실제 Addressables 연동은 얇은 어댑터 `AddressableResourceProvider`에 격리한다.

**Tech Stack:** Unity 6, Addressables, UniTask, Unity Test Framework(NUnit), NSubstitute.

## Global Constraints

- 네임스페이스: `DarkNaku.FoundationDI` (모든 런타임 타입)
- 서비스 패턴: `IXxxService : IDisposable` 인터페이스 + `XxxService` 구현 클래스
- 로딩 백엔드: **Addressables 전용** (Resources.Load 폴백 없음)
- 캐싱/해제: 키 단위 캐싱 + 참조 카운팅 (0이 되면 핸들 해제, 캐시에서 제거)
- 테스트 함수명은 **한글**로 작성한다
- TDD: 한 번에 하나의 테스트만 RED → GREEN → REFACTOR
- 커밋 규율: 구조 변경(asmdef/스캐폴딩)과 동작 변경 커밋을 분리한다
- 커밋 메시지 말미에 `Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>` 추가

### 테스트 실행 방법 (모든 "Run test" 스텝 공통)

1. 스크립트/asmdef 변경 후 Unity를 리프레시하고 컴파일을 기다린다.
   - MCP: `refresh_unity` → `read_console`(error/warning 확인) → `editor_state.isCompiling == false` 대기
2. EditMode 테스트를 실행한다.
   - MCP: `run_tests` 도구, `testMode: "EditMode"`, 필터로 클래스 `ResourceServiceTest`
   - 또는 Unity 에디터: Window > General > Test Runner > EditMode > Run
3. 컴파일 경고가 남아있으면 해결한 뒤 커밋한다.

---

## File Structure

- `Assets/FoundationDI/Runtime/Services/ResourceService/ResourceService.cs` — `IResourceService` + `ResourceService` (참조 카운팅 + 캐싱)
- `Assets/FoundationDI/Runtime/Services/ResourceService/IResourceProvider.cs` — Addressables 호출 추상화 seam
- `Assets/FoundationDI/Runtime/Services/ResourceService/AddressableResourceProvider.cs` — `IResourceProvider`의 실제 Addressables 어댑터(단위 테스트 제외)
- `Assets/FoundationDI/Runtime/FoundationDI.asmdef` — UniTask.Addressables 참조 추가 (수정)
- `Assets/FoundationDI/Tests/FoundationDI.Tests.asmdef` — EditMode 테스트 어셈블리 (신규)
- `Assets/FoundationDI/Tests/ResourceServiceTest.cs` — 단위 테스트

---

## Task 1: 테스트 어셈블리 + seam 스캐폴딩 + 첫 로드 테스트

이 태스크는 후속 모든 태스크가 의존하는 스캐폴딩(테스트 asmdef, `IResourceProvider`, `AddressableResourceProvider` 어댑터, asmdef 참조 추가)을 첫 TDD 사이클과 함께 묶는다.

**Files:**
- Modify: `Assets/FoundationDI/Runtime/FoundationDI.asmdef`
- Create: `Assets/FoundationDI/Runtime/Services/ResourceService/IResourceProvider.cs`
- Create: `Assets/FoundationDI/Runtime/Services/ResourceService/AddressableResourceProvider.cs`
- Create: `Assets/FoundationDI/Runtime/Services/ResourceService/ResourceService.cs`
- Create: `Assets/FoundationDI/Tests/FoundationDI.Tests.asmdef`
- Test: `Assets/FoundationDI/Tests/ResourceServiceTest.cs`

**Interfaces:**
- Produces:
  - `interface IResourceProvider` — `UniTask<T> LoadAsync<T>(string key) where T : Object`, `T Load<T>(string key) where T : Object`, `void Release(string key)`
  - `interface IResourceService : IDisposable` — `UniTask<T> LoadAsync<T>(string key) where T : Object`, `T Load<T>(string key) where T : Object`, `void Release(string key)`
  - `class ResourceService : IResourceService` — ctor `ResourceService()` (기본, `AddressableResourceProvider` 주입), ctor `ResourceService(IResourceProvider provider)` (테스트용)
  - `class AddressableResourceProvider : IResourceProvider`

- [ ] **Step 1: asmdef에 UniTask.Addressables 참조 추가**

`Assets/FoundationDI/Runtime/FoundationDI.asmdef` 의 `references` 배열 끝에 항목 하나를 추가한다(`AddressableResourceProvider`의 `.ToUniTask()`에 필요).

수정 후 `references` 전체:

```json
    "references": [
        "GUID:9e24947de15b9834991c9d8411ea37cf",
        "GUID:f51ebe6a0ceec4240a699833d6309b23",
        "GUID:b0214a6008ed146ff8f122a6a9c2f6cc",
        "GUID:84651a3751eca9349aac36a66bba901b",
        "GUID:08b38f39e2d9e594389b7a4cf4c2c338",
        "GUID:77221876cc6b8244180b96e320b1bcd4",
        "GUID:593a5b492d29ac6448b1ebf7f035ef33"
    ],
```

- [ ] **Step 2: IResourceProvider seam 작성**

`Assets/FoundationDI/Runtime/Services/ResourceService/IResourceProvider.cs`:

```csharp
using Cysharp.Threading.Tasks;
using Object = UnityEngine.Object;

namespace DarkNaku.FoundationDI
{
    public interface IResourceProvider
    {
        UniTask<T> LoadAsync<T>(string key) where T : Object;
        T Load<T>(string key) where T : Object;
        void Release(string key);
    }
}
```

- [ ] **Step 3: AddressableResourceProvider 어댑터 작성 (단위 테스트 제외)**

`Assets/FoundationDI/Runtime/Services/ResourceService/AddressableResourceProvider.cs`:

```csharp
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using Object = UnityEngine.Object;

namespace DarkNaku.FoundationDI
{
    public class AddressableResourceProvider : IResourceProvider
    {
        private readonly Dictionary<string, AsyncOperationHandle> _handles = new();

        public async UniTask<T> LoadAsync<T>(string key) where T : Object
        {
            var handle = Addressables.LoadAssetAsync<T>(key);
            _handles[key] = handle;
            return await handle.ToUniTask();
        }

        public T Load<T>(string key) where T : Object
        {
            var handle = Addressables.LoadAssetAsync<T>(key);
            _handles[key] = handle;
            return handle.WaitForCompletion();
        }

        public void Release(string key)
        {
            if (!_handles.TryGetValue(key, out var handle)) return;

            _handles.Remove(key);

            if (handle.IsValid())
            {
                Addressables.Release(handle);
            }
        }
    }
}
```

- [ ] **Step 4: ResourceService 스켈레톤 작성**

`Assets/FoundationDI/Runtime/Services/ResourceService/ResourceService.cs` (테스트 컴파일이 가능하도록 최소 골격만; LoadAsync는 첫 테스트를 통과하는 최소 구현):

```csharp
using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using Object = UnityEngine.Object;

namespace DarkNaku.FoundationDI
{
    public interface IResourceService : IDisposable
    {
        UniTask<T> LoadAsync<T>(string key) where T : Object;
        T Load<T>(string key) where T : Object;
        void Release(string key);
    }

    public class ResourceService : IResourceService
    {
        private sealed class Entry
        {
            public Object Asset;
            public int RefCount;
        }

        private readonly IResourceProvider _provider;
        private readonly Dictionary<string, Entry> _cache = new();

        public ResourceService() : this(new AddressableResourceProvider())
        {
        }

        public ResourceService(IResourceProvider provider)
        {
            _provider = provider;
        }

        public async UniTask<T> LoadAsync<T>(string key) where T : Object
        {
            return await _provider.LoadAsync<T>(key);
        }

        public T Load<T>(string key) where T : Object
        {
            throw new NotImplementedException();
        }

        public void Release(string key)
        {
            throw new NotImplementedException();
        }

        public void Dispose()
        {
        }
    }
}
```

- [ ] **Step 5: 테스트 어셈블리 정의 작성**

`Assets/FoundationDI/Tests/FoundationDI.Tests.asmdef`:

```json
{
    "name": "FoundationDI.Tests",
    "rootNamespace": "",
    "references": [
        "FoundationDI",
        "UniTask",
        "UnityEngine.TestRunner",
        "UnityEditor.TestRunner"
    ],
    "includePlatforms": [
        "Editor"
    ],
    "excludePlatforms": [],
    "allowUnsafeCode": false,
    "overrideReferences": true,
    "precompiledReferences": [
        "nunit.framework.dll",
        "NSubstitute.dll",
        "Castle.Core.dll"
    ],
    "autoReferenced": false,
    "defineConstraints": [
        "UNITY_INCLUDE_TESTS"
    ],
    "versionDefines": [],
    "noEngineReferences": false
}
```

- [ ] **Step 6: 첫 실패 테스트 작성**

`Assets/FoundationDI/Tests/ResourceServiceTest.cs`:

```csharp
using System.Collections;
using Cysharp.Threading.Tasks;
using DarkNaku.FoundationDI;
using NSubstitute;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

public class ResourceServiceTest
{
    [UnityTest]
    public IEnumerator 첫_로드시_provider를_호출하고_에셋을_반환한다() => UniTask.ToCoroutine(async () =>
    {
        var asset = new GameObject("asset");
        var provider = Substitute.For<IResourceProvider>();
        provider.LoadAsync<GameObject>("key").Returns(UniTask.FromResult(asset));
        var sut = new ResourceService(provider);

        var result = await sut.LoadAsync<GameObject>("key");

        Assert.AreEqual(asset, result);
        _ = provider.Received(1).LoadAsync<GameObject>("key");
    });
}
```

- [ ] **Step 7: 테스트 실행 — 실패 확인**

위 "테스트 실행 방법"에 따라 EditMode 테스트를 실행한다.
Expected: 처음에는 컴파일이 통과하고 테스트가 RED가 되어야 한다. (스켈레톤 LoadAsync가 provider를 그대로 호출하므로 실제로는 PASS할 수 있다. PASS하면 그대로 GREEN으로 간주하고 Step 9로 진행한다.)

- [ ] **Step 8: 최소 구현 확인**

Step 4의 `LoadAsync`가 이미 `await _provider.LoadAsync<T>(key)`를 반환하므로 추가 구현이 필요 없다.

- [ ] **Step 9: 테스트 실행 — 통과 확인**

Expected: `첫_로드시_provider를_호출하고_에셋을_반환한다` PASS

- [ ] **Step 10: 구조 변경 커밋 (스캐폴딩)**

```bash
git add Assets/FoundationDI/Runtime/FoundationDI.asmdef \
        Assets/FoundationDI/Runtime/Services/ResourceService/IResourceProvider.cs \
        Assets/FoundationDI/Runtime/Services/ResourceService/AddressableResourceProvider.cs \
        Assets/FoundationDI/Tests/FoundationDI.Tests.asmdef
git commit -m "chore: ResourceService seam 및 테스트 어셈블리 스캐폴딩 (구조 변경)

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

- [ ] **Step 11: 동작 변경 커밋 (첫 로드)**

```bash
git add Assets/FoundationDI/Runtime/Services/ResourceService/ResourceService.cs \
        Assets/FoundationDI/Tests/ResourceServiceTest.cs
git commit -m "feat: ResourceService 첫 로드 시 provider 위임 (동작 변경)

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

> 참고: 새로 생성된 `.cs`/`.asmdef`에 대해 Unity가 생성한 `.meta` 파일도 함께 add 한다 (`git add -A` 또는 해당 `.meta` 경로 포함). 이하 모든 커밋에 동일 적용.

---

## Task 2: 같은 키 재로드 시 캐싱

**Files:**
- Modify: `Assets/FoundationDI/Runtime/Services/ResourceService/ResourceService.cs`
- Test: `Assets/FoundationDI/Tests/ResourceServiceTest.cs`

**Interfaces:**
- Consumes: `IResourceProvider`, `ResourceService(IResourceProvider)` (Task 1)

- [ ] **Step 1: 실패 테스트 추가**

`ResourceServiceTest` 클래스에 추가:

```csharp
    [UnityTest]
    public IEnumerator 같은_키_재로드시_provider를_다시_호출하지_않고_캐시에서_반환한다() => UniTask.ToCoroutine(async () =>
    {
        var asset = new GameObject("asset");
        var provider = Substitute.For<IResourceProvider>();
        provider.LoadAsync<GameObject>("key").Returns(UniTask.FromResult(asset));
        var sut = new ResourceService(provider);

        var first = await sut.LoadAsync<GameObject>("key");
        var second = await sut.LoadAsync<GameObject>("key");

        Assert.AreEqual(asset, second);
        Assert.AreEqual(first, second);
        _ = provider.Received(1).LoadAsync<GameObject>("key");
    });
```

- [ ] **Step 2: 테스트 실행 — 실패 확인**

Expected: FAIL — provider가 2번 호출되어 `Received(1)` 검증 실패.

- [ ] **Step 3: 캐시 + 참조 카운팅 구현**

`ResourceService.LoadAsync`를 다음으로 교체:

```csharp
        public async UniTask<T> LoadAsync<T>(string key) where T : Object
        {
            if (_cache.TryGetValue(key, out var entry))
            {
                entry.RefCount++;
                return entry.Asset as T;
            }

            var asset = await _provider.LoadAsync<T>(key);
            _cache[key] = new Entry { Asset = asset, RefCount = 1 };
            return asset;
        }
```

- [ ] **Step 4: 테스트 실행 — 통과 확인**

Expected: 두 테스트 모두 PASS

- [ ] **Step 5: 커밋**

```bash
git add Assets/FoundationDI/Runtime/Services/ResourceService/ResourceService.cs \
        Assets/FoundationDI/Tests/ResourceServiceTest.cs
git commit -m "feat: ResourceService 키 단위 캐싱 및 참조 카운트 증가 (동작 변경)

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Task 3: Release 시 참조 카운트 감소 (잔여 참조 시 해제 안 함)

**Files:**
- Modify: `Assets/FoundationDI/Runtime/Services/ResourceService/ResourceService.cs`
- Test: `Assets/FoundationDI/Tests/ResourceServiceTest.cs`

**Interfaces:**
- Consumes: `ResourceService.LoadAsync`, `ResourceService.Release` (Task 1-2)

- [ ] **Step 1: 실패 테스트 추가**

```csharp
    [UnityTest]
    public IEnumerator Release시_참조가_남아있으면_provider_Release를_호출하지_않는다() => UniTask.ToCoroutine(async () =>
    {
        var asset = new GameObject("asset");
        var provider = Substitute.For<IResourceProvider>();
        provider.LoadAsync<GameObject>("key").Returns(UniTask.FromResult(asset));
        var sut = new ResourceService(provider);

        await sut.LoadAsync<GameObject>("key");
        await sut.LoadAsync<GameObject>("key");   // RefCount = 2
        sut.Release("key");                        // RefCount = 1

        provider.DidNotReceive().Release("key");
    });
```

- [ ] **Step 2: 테스트 실행 — 실패 확인**

Expected: FAIL — 현재 `Release`는 `NotImplementedException`을 던진다.

- [ ] **Step 3: Release 최소 구현 (감소만)**

`ResourceService.Release`를 다음으로 교체:

```csharp
        public void Release(string key)
        {
            var entry = _cache[key];
            entry.RefCount--;
        }
```

- [ ] **Step 4: 테스트 실행 — 통과 확인**

Expected: PASS (provider.Release 미호출)

- [ ] **Step 5: 커밋**

```bash
git add Assets/FoundationDI/Runtime/Services/ResourceService/ResourceService.cs \
        Assets/FoundationDI/Tests/ResourceServiceTest.cs
git commit -m "feat: ResourceService Release 시 참조 카운트 감소 (동작 변경)

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Task 4: 참조 카운트 0이 되면 provider.Release 호출

**Files:**
- Modify: `Assets/FoundationDI/Runtime/Services/ResourceService/ResourceService.cs`
- Test: `Assets/FoundationDI/Tests/ResourceServiceTest.cs`

- [ ] **Step 1: 실패 테스트 추가**

```csharp
    [UnityTest]
    public IEnumerator 참조가_0이_되면_provider_Release를_호출한다() => UniTask.ToCoroutine(async () =>
    {
        var asset = new GameObject("asset");
        var provider = Substitute.For<IResourceProvider>();
        provider.LoadAsync<GameObject>("key").Returns(UniTask.FromResult(asset));
        var sut = new ResourceService(provider);

        await sut.LoadAsync<GameObject>("key");   // RefCount = 1
        sut.Release("key");                        // RefCount = 0

        provider.Received(1).Release("key");
    });
```

- [ ] **Step 2: 테스트 실행 — 실패 확인**

Expected: FAIL — provider.Release가 호출되지 않음.

- [ ] **Step 3: 0 도달 시 provider.Release 호출 구현**

`ResourceService.Release`를 다음으로 교체:

```csharp
        public void Release(string key)
        {
            var entry = _cache[key];
            entry.RefCount--;

            if (entry.RefCount <= 0)
            {
                _provider.Release(key);
            }
        }
```

- [ ] **Step 4: 테스트 실행 — 통과 확인**

Expected: 이 테스트 PASS, Task 3 테스트도 여전히 PASS.

- [ ] **Step 5: 커밋**

```bash
git add Assets/FoundationDI/Runtime/Services/ResourceService/ResourceService.cs \
        Assets/FoundationDI/Tests/ResourceServiceTest.cs
git commit -m "feat: ResourceService 참조 0 도달 시 provider 해제 (동작 변경)

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Task 5: 해제된 키를 다시 로드하면 provider 재호출 (캐시에서 제거)

**Files:**
- Modify: `Assets/FoundationDI/Runtime/Services/ResourceService/ResourceService.cs`
- Test: `Assets/FoundationDI/Tests/ResourceServiceTest.cs`

- [ ] **Step 1: 실패 테스트 추가**

```csharp
    [UnityTest]
    public IEnumerator 해제된_키를_다시_로드하면_provider를_재호출한다() => UniTask.ToCoroutine(async () =>
    {
        var asset = new GameObject("asset");
        var provider = Substitute.For<IResourceProvider>();
        provider.LoadAsync<GameObject>("key").Returns(UniTask.FromResult(asset));
        var sut = new ResourceService(provider);

        await sut.LoadAsync<GameObject>("key");   // RefCount = 1
        sut.Release("key");                        // RefCount = 0, 해제
        await sut.LoadAsync<GameObject>("key");    // 재로드

        _ = provider.Received(2).LoadAsync<GameObject>("key");
    });
```

- [ ] **Step 2: 테스트 실행 — 실패 확인**

Expected: FAIL — Task 4 구현은 캐시에서 제거하지 않아 재로드가 캐시 히트(RefCount 0→1)되어 provider가 1번만 호출됨.

- [ ] **Step 3: 0 도달 시 캐시에서 제거**

`ResourceService.Release`를 다음으로 교체:

```csharp
        public void Release(string key)
        {
            var entry = _cache[key];
            entry.RefCount--;

            if (entry.RefCount <= 0)
            {
                _cache.Remove(key);
                _provider.Release(key);
            }
        }
```

- [ ] **Step 4: 테스트 실행 — 통과 확인**

Expected: 이 테스트 PASS, 이전 모든 테스트 PASS.

- [ ] **Step 5: 커밋**

```bash
git add Assets/FoundationDI/Runtime/Services/ResourceService/ResourceService.cs \
        Assets/FoundationDI/Tests/ResourceServiceTest.cs
git commit -m "feat: ResourceService 참조 0 도달 시 캐시에서 제거 (동작 변경)

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Task 6: 과다 Release 안전 무시

**Files:**
- Modify: `Assets/FoundationDI/Runtime/Services/ResourceService/ResourceService.cs`
- Test: `Assets/FoundationDI/Tests/ResourceServiceTest.cs`

- [ ] **Step 1: 실패 테스트 추가**

```csharp
    [UnityTest]
    public IEnumerator 보유_참조보다_많이_Release하면_안전하게_무시한다() => UniTask.ToCoroutine(async () =>
    {
        var asset = new GameObject("asset");
        var provider = Substitute.For<IResourceProvider>();
        provider.LoadAsync<GameObject>("key").Returns(UniTask.FromResult(asset));
        var sut = new ResourceService(provider);

        await sut.LoadAsync<GameObject>("key");   // RefCount = 1
        sut.Release("key");                        // 0, 제거됨

        Assert.DoesNotThrow(() => sut.Release("key"));   // 추가 Release는 무시
        provider.Received(1).Release("key");
    });
```

- [ ] **Step 2: 테스트 실행 — 실패 확인**

Expected: FAIL — Task 5의 `Release`는 `_cache[key]` 인덱싱에서 `KeyNotFoundException`을 던져 `Assert.DoesNotThrow` 실패.

- [ ] **Step 3: 존재하지 않는 키 가드 추가**

`ResourceService.Release`를 다음으로 교체:

```csharp
        public void Release(string key)
        {
            if (!_cache.TryGetValue(key, out var entry)) return;

            entry.RefCount--;

            if (entry.RefCount <= 0)
            {
                _cache.Remove(key);
                _provider.Release(key);
            }
        }
```

- [ ] **Step 4: 테스트 실행 — 통과 확인**

Expected: 이 테스트 PASS, 이전 모든 테스트 PASS.

- [ ] **Step 5: 커밋**

```bash
git add Assets/FoundationDI/Runtime/Services/ResourceService/ResourceService.cs \
        Assets/FoundationDI/Tests/ResourceServiceTest.cs
git commit -m "feat: ResourceService 미보유 키 과다 Release 안전 무시 (동작 변경)

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Task 7: Dispose 시 남은 모든 핸들 해제

**Files:**
- Modify: `Assets/FoundationDI/Runtime/Services/ResourceService/ResourceService.cs`
- Test: `Assets/FoundationDI/Tests/ResourceServiceTest.cs`

- [ ] **Step 1: 실패 테스트 추가**

```csharp
    [UnityTest]
    public IEnumerator Dispose시_남은_모든_키의_핸들을_해제한다() => UniTask.ToCoroutine(async () =>
    {
        var assetA = new GameObject("assetA");
        var assetB = new GameObject("assetB");
        var provider = Substitute.For<IResourceProvider>();
        provider.LoadAsync<GameObject>("a").Returns(UniTask.FromResult(assetA));
        provider.LoadAsync<GameObject>("b").Returns(UniTask.FromResult(assetB));
        var sut = new ResourceService(provider);

        await sut.LoadAsync<GameObject>("a");
        await sut.LoadAsync<GameObject>("b");
        sut.Dispose();

        provider.Received(1).Release("a");
        provider.Received(1).Release("b");
    });
```

- [ ] **Step 2: 테스트 실행 — 실패 확인**

Expected: FAIL — 현재 `Dispose`는 비어 있어 provider.Release가 호출되지 않음.

- [ ] **Step 3: Dispose 구현**

`ResourceService.Dispose`를 다음으로 교체:

```csharp
        public void Dispose()
        {
            foreach (var key in new List<string>(_cache.Keys))
            {
                _provider.Release(key);
            }

            _cache.Clear();
        }
```

- [ ] **Step 4: 테스트 실행 — 통과 확인**

Expected: 이 테스트 PASS, 이전 모든 테스트 PASS.

- [ ] **Step 5: 커밋**

```bash
git add Assets/FoundationDI/Runtime/Services/ResourceService/ResourceService.cs \
        Assets/FoundationDI/Tests/ResourceServiceTest.cs
git commit -m "feat: ResourceService Dispose 시 잔여 핸들 일괄 해제 (동작 변경)

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Task 8: 동기 Load<T> — 동일 참조 카운팅

**Files:**
- Modify: `Assets/FoundationDI/Runtime/Services/ResourceService/ResourceService.cs`
- Test: `Assets/FoundationDI/Tests/ResourceServiceTest.cs`

- [ ] **Step 1: 실패 테스트 추가**

동기 API이므로 `[Test]`(비코루틴)로 작성:

```csharp
    [Test]
    public void 동기_Load도_동일한_참조_카운팅을_따른다()
    {
        var asset = new GameObject("asset");
        var provider = Substitute.For<IResourceProvider>();
        provider.Load<GameObject>("key").Returns(asset);
        var sut = new ResourceService(provider);

        var first = sut.Load<GameObject>("key");
        var second = sut.Load<GameObject>("key");   // RefCount = 2, 캐시 히트
        sut.Release("key");                          // RefCount = 1
        sut.Release("key");                          // RefCount = 0, 해제

        Assert.AreEqual(asset, first);
        Assert.AreEqual(first, second);
        provider.Received(1).Load<GameObject>("key");
        provider.Received(1).Release("key");
    }
```

- [ ] **Step 2: 테스트 실행 — 실패 확인**

Expected: FAIL — 현재 `Load`는 `NotImplementedException`을 던진다.

- [ ] **Step 3: 동기 Load 구현**

`ResourceService.Load`를 다음으로 교체:

```csharp
        public T Load<T>(string key) where T : Object
        {
            if (_cache.TryGetValue(key, out var entry))
            {
                entry.RefCount++;
                return entry.Asset as T;
            }

            var asset = _provider.Load<T>(key);
            _cache[key] = new Entry { Asset = asset, RefCount = 1 };
            return asset;
        }
```

- [ ] **Step 4: 테스트 실행 — 통과 확인**

Expected: 이 테스트 PASS, 이전 모든 테스트 PASS.

- [ ] **Step 5: 커밋**

```bash
git add Assets/FoundationDI/Runtime/Services/ResourceService/ResourceService.cs \
        Assets/FoundationDI/Tests/ResourceServiceTest.cs
git commit -m "feat: ResourceService 동기 Load 참조 카운팅 (동작 변경)

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Task 9: 진행 중(in-flight) 비동기 로드 중복 제거

같은 키의 `LoadAsync`가 완료되기 전 재호출되면 provider를 중복 호출하지 않도록 진행 중 task를 공유한다.

**Files:**
- Modify: `Assets/FoundationDI/Runtime/Services/ResourceService/ResourceService.cs`
- Test: `Assets/FoundationDI/Tests/ResourceServiceTest.cs`

- [ ] **Step 1: 실패 테스트 추가**

`UniTaskCompletionSource`로 로드 완료 시점을 제어한다:

```csharp
    [UnityTest]
    public IEnumerator 로드_진행중_재호출시_provider를_중복_호출하지_않는다() => UniTask.ToCoroutine(async () =>
    {
        var asset = new GameObject("asset");
        var source = new UniTaskCompletionSource<GameObject>();
        var provider = Substitute.For<IResourceProvider>();
        provider.LoadAsync<GameObject>("key").Returns(source.Task);
        var sut = new ResourceService(provider);

        var t1 = sut.LoadAsync<GameObject>("key");   // 로드 시작 (in-flight)
        var t2 = sut.LoadAsync<GameObject>("key");   // 진행 중 task 재사용해야 함
        source.TrySetResult(asset);
        await UniTask.WhenAll(t1, t2);

        _ = provider.Received(1).LoadAsync<GameObject>("key");
    });
```

- [ ] **Step 2: 테스트 실행 — 실패 확인**

Expected: FAIL — 현재 `LoadAsync`는 캐시 미스 시 매번 provider를 호출하므로 in-flight 동안 2번 호출되어 `Received(1)` 검증 실패(또는 단일 소비 UniTask 재await 예외).

- [ ] **Step 3: in-flight 공유 구현 (REFACTOR)**

`_loading` 필드를 추가하고 `LoadAsync`를 in-flight task 공유 방식으로 교체한다. `Entry` 초기 `RefCount`는 0으로 만들고 각 awaiter가 1씩 증가시킨다(순차/동시 모두 일관).

`_cache` 필드 아래에 추가:

```csharp
        private readonly Dictionary<string, UniTask<Object>> _loading = new();
```

`LoadAsync`를 다음으로 교체하고, private 헬퍼 `LoadAndCacheAsync`를 추가:

```csharp
        public async UniTask<T> LoadAsync<T>(string key) where T : Object
        {
            if (_cache.TryGetValue(key, out var entry))
            {
                entry.RefCount++;
                return entry.Asset as T;
            }

            if (!_loading.TryGetValue(key, out var loading))
            {
                loading = LoadAndCacheAsync<T>(key).Preserve();
                _loading[key] = loading;
            }

            var asset = await loading;
            _loading.Remove(key);
            _cache[key].RefCount++;
            return asset as T;
        }

        private async UniTask<Object> LoadAndCacheAsync<T>(string key) where T : Object
        {
            var asset = await _provider.LoadAsync<T>(key);

            if (!_cache.ContainsKey(key))
            {
                _cache[key] = new Entry { Asset = asset, RefCount = 0 };
            }

            return asset;
        }
```

> 설명: 첫 awaiter가 `_loading`에 preserved task를 등록하고, in-flight 동안 도착한 awaiter는 같은 task를 await한다. 완료 시 `LoadAndCacheAsync`가 `Entry`(RefCount=0)를 한 번만 생성하고, 각 awaiter가 `RefCount++` 한다. 단일 로드 → 1, 동시 2회 → 2로 기존 동작과 일관된다.

- [ ] **Step 4: 테스트 실행 — 통과 확인**

Expected: 이 테스트 PASS, 그리고 Task 1-8의 모든 기존 테스트가 여전히 PASS (회귀 없음).

- [ ] **Step 5: 커밋**

```bash
git add Assets/FoundationDI/Runtime/Services/ResourceService/ResourceService.cs \
        Assets/FoundationDI/Tests/ResourceServiceTest.cs
git commit -m "feat: ResourceService in-flight 비동기 로드 중복 제거 (동작 변경)

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## 완료 기준

- Task 1-9의 모든 테스트가 EditMode에서 PASS
- 컴파일러/린터 경고 없음
- 구조 변경 커밋(Task 1 스캐폴딩)과 동작 변경 커밋이 분리됨

## 범위 외 (후속 계획)

- `PoolService.Load()` / `SoundService.Load()` 의 중복 로딩 로직을 `IResourceService` 호출로 교체하는 작업은 **별도 스펙/플랜**으로 진행한다 (Tidy First: 본 플랜은 ResourceService 자체까지).
- `AddressableResourceProvider`의 실제 Addressables 연동 검증은 PlayMode + 빌드된 Addressables 카탈로그가 필요하므로 별도 PlayMode 테스트로 다룬다.
