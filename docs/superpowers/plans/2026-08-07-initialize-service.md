# InitializeService Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** ScriptableObject 초기화 항목들을 카탈로그로 선언하고, 카탈로그를 넘겨 세션 내 중복 없이 순차 초기화하는 서비스를 만든다.

**Architecture:** 추상 `InitializeItem`(SO)이 각 초기화 로직을 캡슐화하고, `InitializeCatalog`(SO)가 항목 목록을 담는다. `InitializeService`는 `IObjectResolver`를 주입받아 카탈로그 항목을 선언 순서대로 `await`하며, 아이템/카탈로그 단위 `HashSet`으로 중복 초기화를 방지한다. async 타입은 Unity 내장 `Awaitable`.

**Tech Stack:** Unity 6000.3.17f1, C#, VContainer(IObjectResolver), `UnityEngine.Awaitable`, Unity Test Framework(EditMode) + NSubstitute 5.3.0.

## Global Constraints

- 네임스페이스: `DarkNaku.FoundationDI` (전 파일 공통).
- 위치: 런타임 코드는 `Assets/FoundationDI/Runtime/Services/InitializeService/`, 테스트는 `Assets/FoundationDI/Tests/`.
- async 타입은 `UnityEngine.Awaitable`. UniTask를 신규 표면에 추가하지 않는다. (`async Awaitable` 내부에서 UniTask await는 허용.)
- 컴파일·테스트는 UnityMCP로 수행: 스크립트 생성/수정 후 `read_console`로 컴파일 에러 먼저 확인(`editor_state.isCompiling == false`), 테스트는 `run_tests`(EditMode).
- 테스트 함수 이름은 한국어 `should~` 의도. 테스트는 기존 관례대로 `[UnityTest] public IEnumerator ...() => UniTask.ToCoroutine(async () => { ... });` 형태(파일: `Assets/FoundationDI/Tests/`, asmdef `FoundationDI.Tests`, 이미 NSubstitute 참조).
- STRUCTURAL/BEHAVIORAL 커밋을 섞지 않는다. 커밋 제목에 `[STRUCTURAL]`/`[BEHAVIORAL]` 접두어. (docs 커밋은 접두어 없이 `docs:`.)
- **Awaitable EditMode 펌핑**: 테스트 fake 항목은 반드시 "즉시 완료 Awaitable"(`AwaitableCompletionSource`에 `SetResult()`/`SetException()` 후 `.Awaitable` 반환)을 반환해 continuation이 인라인으로 돌게 한다. `Awaitable.NextFrameAsync` 류를 fake에 쓰지 않는다.

---

## File Structure

- Create: `Assets/FoundationDI/Runtime/Services/InitializeService/InitializeItem.cs` — 추상 SO 베이스. `abstract Awaitable InitializeAsync(IObjectResolver)`.
- Create: `Assets/FoundationDI/Runtime/Services/InitializeService/InitializeCatalog.cs` — 컨테이너 SO. `List<InitializeItem>` + `IReadOnlyList<InitializeItem> Items`.
- Create: `Assets/FoundationDI/Runtime/Services/InitializeService/InitializeService.cs` — `IInitializeService`(interface) + `InitializeService`(impl) + `InitializeServiceVContainerExtensions`(등록 확장).
- Create: `Assets/FoundationDI/Runtime/Services/InitializeService/README.md` — 사용법/API 문서.
- Create: `Assets/FoundationDI/Tests/InitializeServiceTest.cs` — EditMode 테스트 + fake 항목/카탈로그 헬퍼.
- Modify: `Assets/Scripts/LifetimeScopes/RootLifetimeScope.cs` — `builder.RegisterInitializeService()` 추가(호스트 등록 예시).

**공유 테스트 헬퍼** (Task 1에서 `InitializeServiceTest.cs`에 작성, 이후 모든 태스크가 재사용):

```csharp
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Cysharp.Threading.Tasks;
using DarkNaku.FoundationDI;
using NSubstitute;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using VContainer;

public class InitializeServiceTest
{
    // 호출 여부/순서/resolver/예외를 기록하는 fake 초기화 항목.
    private class FakeItem : InitializeItem
    {
        public int CallCount;
        public IObjectResolver LastResolver;
        public Exception ToThrow;
        public List<string> OrderLog;
        public string Id;

        public override Awaitable InitializeAsync(IObjectResolver resolver)
        {
            CallCount++;
            LastResolver = resolver;
            OrderLog?.Add(Id);
            var acs = new AwaitableCompletionSource();
            if (ToThrow != null) acs.SetException(ToThrow);
            else acs.SetResult();
            return acs.Awaitable;
        }
    }

    private static FakeItem NewItem(string id = null, List<string> log = null, Exception throwOn = null)
    {
        var item = ScriptableObject.CreateInstance<FakeItem>();
        item.Id = id;
        item.OrderLog = log;
        item.ToThrow = throwOn;
        return item;
    }

    // private 직렬화 필드 _items에 리플렉션으로 항목을 주입 → 런타임 API를 오염시키지 않는다.
    private static InitializeCatalog NewCatalog(params InitializeItem[] items)
    {
        var catalog = ScriptableObject.CreateInstance<InitializeCatalog>();
        typeof(InitializeCatalog)
            .GetField("_items", BindingFlags.NonPublic | BindingFlags.Instance)
            .SetValue(catalog, new List<InitializeItem>(items));
        return catalog;
    }
}
```

> 각 태스크의 테스트 메서드는 위 `InitializeServiceTest` 클래스 안에 추가한다.

---

### Task 1: 초기화 타입 + 순서 보장

카탈로그 항목을 선언 순서대로 초기화하는 최소 골격을 만든다. 이 태스크가 `InitializeItem`, `InitializeCatalog`, `IInitializeService`, `InitializeService`를 모두 생성한다(후속 태스크가 소비).

**Files:**
- Create: `Assets/FoundationDI/Runtime/Services/InitializeService/InitializeItem.cs`
- Create: `Assets/FoundationDI/Runtime/Services/InitializeService/InitializeCatalog.cs`
- Create: `Assets/FoundationDI/Runtime/Services/InitializeService/InitializeService.cs`
- Test: `Assets/FoundationDI/Tests/InitializeServiceTest.cs`

**Interfaces:**
- Produces:
  - `abstract class InitializeItem : ScriptableObject { public abstract Awaitable InitializeAsync(IObjectResolver resolver); }`
  - `class InitializeCatalog : ScriptableObject { [SerializeField] private List<InitializeItem> _items; public IReadOnlyList<InitializeItem> Items => _items; }`
  - `interface IInitializeService : IDisposable { Awaitable InitializeAsync(InitializeCatalog catalog); }`
  - `sealed class InitializeService : IInitializeService { public InitializeService(IObjectResolver resolver); }`
- Consumes: `VContainer.IObjectResolver`.

- [ ] **Step 1: 공유 헬퍼 + 첫 실패 테스트 작성**

위 "공유 테스트 헬퍼" 블록 전체를 `InitializeServiceTest.cs`로 작성하고, 클래스 안에 아래 테스트를 추가한다.

```csharp
[UnityTest]
public IEnumerator 카탈로그_아이템을_선언_순서대로_초기화한다() => UniTask.ToCoroutine(async () =>
{
    var log = new List<string>();
    var a = NewItem("A", log);
    var b = NewItem("B", log);
    var catalog = NewCatalog(a, b);
    var sut = new InitializeService(Substitute.For<IObjectResolver>());

    await sut.InitializeAsync(catalog);

    Assert.AreEqual(new[] { "A", "B" }, log.ToArray());
});
```

- [ ] **Step 2: 컴파일 실패 확인**

UnityMCP `read_console`로 컴파일 에러 확인.
Expected: FAIL — `InitializeItem`/`InitializeCatalog`/`InitializeService` 타입 미정의 컴파일 에러.

- [ ] **Step 3: 최소 구현 작성**

`InitializeItem.cs`:
```csharp
using UnityEngine;
using VContainer;

namespace DarkNaku.FoundationDI
{
    public abstract class InitializeItem : ScriptableObject
    {
        public abstract Awaitable InitializeAsync(IObjectResolver resolver);
    }
}
```

`InitializeCatalog.cs`:
```csharp
using System.Collections.Generic;
using UnityEngine;

namespace DarkNaku.FoundationDI
{
    public class InitializeCatalog : ScriptableObject
    {
        [SerializeField] private List<InitializeItem> _items = new();

        public IReadOnlyList<InitializeItem> Items => _items;
    }
}
```

`InitializeService.cs`:
```csharp
using System;
using System.Collections.Generic;
using UnityEngine;
using VContainer;

namespace DarkNaku.FoundationDI
{
    public interface IInitializeService : IDisposable
    {
        Awaitable InitializeAsync(InitializeCatalog catalog);
    }

    public sealed class InitializeService : IInitializeService
    {
        private readonly IObjectResolver _resolver;

        public InitializeService(IObjectResolver resolver)
        {
            _resolver = resolver;
        }

        public async Awaitable InitializeAsync(InitializeCatalog catalog)
        {
            foreach (var item in catalog.Items)
            {
                if (item == null) continue;
                await item.InitializeAsync(_resolver);
            }
        }

        public void Dispose()
        {
        }
    }
}
```

- [ ] **Step 4: 컴파일 통과 + 테스트 통과 확인**

`read_console`로 `isCompiling == false` 및 에러 0 확인 → `run_tests`(EditMode, 필터 `InitializeServiceTest`).
Expected: `카탈로그_아이템을_선언_순서대로_초기화한다` PASS.

- [ ] **Step 5: 커밋**

```bash
git add Assets/FoundationDI/Runtime/Services/InitializeService Assets/FoundationDI/Tests/InitializeServiceTest.cs
git commit -m "[BEHAVIORAL] InitializeService 순차 초기화 골격 추가"
```

---

### Task 2: resolver 전달

각 항목에 주입된 `IObjectResolver`가 전달되는지 검증한다.

**Files:**
- Test: `Assets/FoundationDI/Tests/InitializeServiceTest.cs`
- (구현 변경 없음 — Task 1에서 이미 `_resolver` 전달. RED가 바로 GREEN이면 구현은 그대로 두고 커밋한다.)

**Interfaces:**
- Consumes: Task 1의 `InitializeService(IObjectResolver)`, `FakeItem.LastResolver`.

- [ ] **Step 1: 실패 테스트 작성**

```csharp
[UnityTest]
public IEnumerator 각_아이템에_resolver를_전달한다() => UniTask.ToCoroutine(async () =>
{
    var resolver = Substitute.For<IObjectResolver>();
    var a = NewItem("A");
    var catalog = NewCatalog(a);
    var sut = new InitializeService(resolver);

    await sut.InitializeAsync(catalog);

    Assert.AreSame(resolver, a.LastResolver);
});
```

- [ ] **Step 2: 테스트 실행**

`run_tests`(EditMode, 필터 `각_아이템에_resolver를_전달한다`).
Expected: PASS (Task 1 구현이 이미 전달). 만약 FAIL이면 `await item.InitializeAsync(_resolver)`에 `_resolver`가 전달되는지 점검 후 수정.

- [ ] **Step 3: 커밋**

```bash
git add Assets/FoundationDI/Tests/InitializeServiceTest.cs
git commit -m "[BEHAVIORAL] 초기화 항목에 resolver 전달 검증 추가"
```

---

### Task 3: 아이템 단위 중복 방지

같은 항목은 여러 번 초기화 요청해도 세션 내 한 번만 실행한다.

**Files:**
- Modify: `Assets/FoundationDI/Runtime/Services/InitializeService/InitializeService.cs`
- Test: `Assets/FoundationDI/Tests/InitializeServiceTest.cs`

**Interfaces:**
- Produces: `InitializeService`가 성공한 항목을 `_initializedItems`(HashSet<InitializeItem>)에 기록.

- [ ] **Step 1: 실패 테스트 작성**

```csharp
[UnityTest]
public IEnumerator 이미_초기화된_아이템은_다시_초기화하지_않는다() => UniTask.ToCoroutine(async () =>
{
    var a = NewItem("A");
    var catalog = NewCatalog(a);
    var sut = new InitializeService(Substitute.For<IObjectResolver>());

    await sut.InitializeAsync(catalog);
    await sut.InitializeAsync(catalog);

    Assert.AreEqual(1, a.CallCount);
});
```

> 주: 이 테스트는 카탈로그도 재사용하지만, Task 4(카탈로그 dedup) 이전이라 아이템 단위 스킵으로 통과해야 한다. Task 4 구현 후에도 계속 통과한다(카탈로그 스킵이 상위에서 걸러도 CallCount는 1).

- [ ] **Step 2: 테스트 실행 → 실패 확인**

`run_tests`(필터 `이미_초기화된_아이템은_다시_초기화하지_않는다`).
Expected: FAIL — `CallCount == 2` (아직 dedup 없음).

- [ ] **Step 3: 최소 구현**

`InitializeService`에 필드와 스킵/기록 로직 추가:
```csharp
private readonly HashSet<InitializeItem> _initializedItems = new();

public async Awaitable InitializeAsync(InitializeCatalog catalog)
{
    foreach (var item in catalog.Items)
    {
        if (item == null) continue;
        if (_initializedItems.Contains(item)) continue;
        await item.InitializeAsync(_resolver);
        _initializedItems.Add(item);
    }
}
```

- [ ] **Step 4: 테스트 실행 → 통과 확인**

`run_tests`(필터 `InitializeServiceTest`). Expected: 전체 PASS.

- [ ] **Step 5: 커밋**

```bash
git add Assets/FoundationDI/Runtime/Services/InitializeService/InitializeService.cs Assets/FoundationDI/Tests/InitializeServiceTest.cs
git commit -m "[BEHAVIORAL] 아이템 단위 중복 초기화 방지 추가"
```

---

### Task 4: 카탈로그 단위 중복 방지

같은 카탈로그를 두 번 넘기면 두 번째는 즉시 스킵한다(모든 항목 성공 후에만 완료로 표시).

**Files:**
- Modify: `Assets/FoundationDI/Runtime/Services/InitializeService/InitializeService.cs`
- Test: `Assets/FoundationDI/Tests/InitializeServiceTest.cs`

**Interfaces:**
- Produces: `InitializeService`가 전 항목 성공 후 `_initializedCatalogs`(HashSet<InitializeCatalog>)에 카탈로그 기록, 진입 시 조기 반환.

- [ ] **Step 1: 실패 테스트 작성**

```csharp
[UnityTest]
public IEnumerator 이미_초기화된_카탈로그는_다시_순회하지_않는다() => UniTask.ToCoroutine(async () =>
{
    var log = new List<string>();
    var a = NewItem("A", log);
    var catalog = NewCatalog(a);
    var sut = new InitializeService(Substitute.For<IObjectResolver>());

    await sut.InitializeAsync(catalog);
    log.Clear();
    await sut.InitializeAsync(catalog);

    Assert.IsEmpty(log); // 두 번째 호출은 카탈로그 스킵으로 항목 순회 자체가 없음
});
```

- [ ] **Step 2: 테스트 실행 → 실패 확인**

`run_tests`(필터 `이미_초기화된_카탈로그는_다시_순회하지_않는다`).
Expected: FAIL — Task 3에서는 항목 순회는 하되 아이템 스킵만 하므로... 실제로는 `OrderLog?.Add`가 스킵된 항목엔 호출되지 않아 로그는 비어 통과할 수 있다. **먼저 실행해 실제 결과를 확인**하고, 이미 PASS면 카탈로그 dedup의 조기 반환 효과를 직접 검증하도록 아래 대체 어서션으로 교체한다.

대체(항목 순회 자체를 스킵함을 확실히 검증): 카탈로그 dedup이 없으면 `Contains` 순회 비용이 남지만 관측이 어려우므로, 다음처럼 **미완료 항목이 남은 카탈로그를 재호출해도 신규 항목이 실행되지 않음**으로 검증한다.

```csharp
[UnityTest]
public IEnumerator 완료된_카탈로그_재호출은_조기반환한다() => UniTask.ToCoroutine(async () =>
{
    var a = NewItem("A");
    var catalog = NewCatalog(a);
    var sut = new InitializeService(Substitute.For<IObjectResolver>());
    await sut.InitializeAsync(catalog);

    // 완료 후 카탈로그에 새 항목을 추가해도, 카탈로그가 완료로 표시되어 순회하지 않는다.
    var b = NewItem("B");
    typeof(InitializeCatalog).GetField("_items", BindingFlags.NonPublic | BindingFlags.Instance)
        .SetValue(catalog, new List<InitializeItem> { a, b });

    await sut.InitializeAsync(catalog);

    Assert.AreEqual(0, b.CallCount); // 조기 반환 → b는 실행되지 않음
});
```

Task 3까지의 구현에서는 카탈로그 조기 반환이 없어 `b.CallCount == 1`이 되므로 FAIL. 이 테스트를 채택한다(위 `이미_초기화된_카탈로그는...`는 삭제).

- [ ] **Step 3: 최소 구현**

```csharp
private readonly HashSet<InitializeCatalog> _initializedCatalogs = new();

public async Awaitable InitializeAsync(InitializeCatalog catalog)
{
    if (_initializedCatalogs.Contains(catalog)) return;

    foreach (var item in catalog.Items)
    {
        if (item == null) continue;
        if (_initializedItems.Contains(item)) continue;
        await item.InitializeAsync(_resolver);
        _initializedItems.Add(item);
    }

    _initializedCatalogs.Add(catalog);
}
```

- [ ] **Step 4: 테스트 실행 → 통과 확인**

`run_tests`(필터 `InitializeServiceTest`). Expected: 전체 PASS.

- [ ] **Step 5: 커밋**

```bash
git add Assets/FoundationDI/Runtime/Services/InitializeService/InitializeService.cs Assets/FoundationDI/Tests/InitializeServiceTest.cs
git commit -m "[BEHAVIORAL] 카탈로그 단위 중복 초기화 방지 추가"
```

---

### Task 5: 카탈로그 간 겹치는 아이템 한 번만

서로 다른 두 카탈로그가 같은 항목을 참조하면, 그 항목은 세션 내 한 번만 실행한다.

**Files:**
- Test: `Assets/FoundationDI/Tests/InitializeServiceTest.cs`
- (구현 변경 없음 — 아이템 단위 dedup으로 이미 성립. RED가 바로 GREEN이면 커밋만.)

**Interfaces:**
- Consumes: Task 3의 `_initializedItems`.

- [ ] **Step 1: 실패 테스트 작성**

```csharp
[UnityTest]
public IEnumerator 두_카탈로그에_겹치는_아이템은_한번만_초기화된다() => UniTask.ToCoroutine(async () =>
{
    var shared = NewItem("S");
    var catalog1 = NewCatalog(shared);
    var catalog2 = NewCatalog(shared);
    var sut = new InitializeService(Substitute.For<IObjectResolver>());

    await sut.InitializeAsync(catalog1);
    await sut.InitializeAsync(catalog2);

    Assert.AreEqual(1, shared.CallCount);
});
```

- [ ] **Step 2: 테스트 실행**

`run_tests`(필터 `두_카탈로그에_겹치는_아이템은_한번만_초기화된다`).
Expected: PASS (아이템 단위 dedup으로 성립). FAIL이면 `_initializedItems`가 카탈로그 간 공유되는지 점검.

- [ ] **Step 3: 커밋**

```bash
git add Assets/FoundationDI/Tests/InitializeServiceTest.cs
git commit -m "[BEHAVIORAL] 카탈로그 간 공유 아이템 단일 초기화 검증 추가"
```

---

### Task 6: 예외 즉시 중단 + 전파

항목이 예외를 던지면 전체 초기화를 중단하고 예외를 호출측으로 전파한다. 뒤 항목은 실행하지 않는다.

**Files:**
- Test: `Assets/FoundationDI/Tests/InitializeServiceTest.cs`
- (구현 변경 없음 — `await`가 예외를 자연 전파, `_initializedItems.Add`는 예외 시 실행 안 됨. RED가 바로 GREEN이면 커밋만.)

**Interfaces:**
- Consumes: `FakeItem.ToThrow`, `FakeItem.CallCount`.

- [ ] **Step 1: 실패 테스트 작성**

```csharp
[UnityTest]
public IEnumerator 아이템이_예외를_던지면_중단하고_예외를_전파한다() => UniTask.ToCoroutine(async () =>
{
    var boom = new InvalidOperationException("boom");
    var a = NewItem("A", throwOn: boom);
    var b = NewItem("B");
    var catalog = NewCatalog(a, b);
    var sut = new InitializeService(Substitute.For<IObjectResolver>());

    Exception caught = null;
    try { await sut.InitializeAsync(catalog); }
    catch (Exception e) { caught = e; }

    Assert.AreSame(boom, caught);   // 예외 전파
    Assert.AreEqual(0, b.CallCount); // 뒤 항목 미실행(즉시 중단)
});
```

- [ ] **Step 2: 테스트 실행**

`run_tests`(필터 `아이템이_예외를_던지면_중단하고_예외를_전파한다`).
Expected: PASS. FAIL 시 `await item.InitializeAsync(...)` 예외가 `_initializedItems.Add` 전에 전파되는지(add 미실행) 확인.

- [ ] **Step 3: 커밋**

```bash
git add Assets/FoundationDI/Tests/InitializeServiceTest.cs
git commit -m "[BEHAVIORAL] 초기화 예외 즉시 중단/전파 검증 추가"
```

---

### Task 7: 실패 후 재호출 시 실패 지점부터 재개

항목 실패로 카탈로그가 미완료로 남은 뒤 재호출하면, 완료된 항목은 스킵하고 실패했던 항목부터 재개한다.

**Files:**
- Test: `Assets/FoundationDI/Tests/InitializeServiceTest.cs`
- (구현 변경 없음 — 아이템/카탈로그 dedup 조합으로 이미 성립. RED가 바로 GREEN이면 커밋만.)

**Interfaces:**
- Consumes: `FakeItem.ToThrow`(재호출 전 null로 변경), `CallCount`.

- [ ] **Step 1: 실패 테스트 작성**

```csharp
[UnityTest]
public IEnumerator 실패후_재호출하면_완료된_아이템은_스킵하고_실패지점부터_재개한다() => UniTask.ToCoroutine(async () =>
{
    var a = NewItem("A");
    var b = NewItem("B", throwOn: new InvalidOperationException("boom"));
    var c = NewItem("C");
    var catalog = NewCatalog(a, b, c);
    var sut = new InitializeService(Substitute.For<IObjectResolver>());

    try { await sut.InitializeAsync(catalog); } catch { /* b에서 중단 */ }

    Assert.AreEqual(1, a.CallCount);
    Assert.AreEqual(1, b.CallCount);
    Assert.AreEqual(0, c.CallCount);

    b.ToThrow = null; // b가 이제 성공하도록 수정
    await sut.InitializeAsync(catalog);

    Assert.AreEqual(1, a.CallCount); // 완료된 A는 스킵
    Assert.AreEqual(2, b.CallCount); // 실패했던 B부터 재개
    Assert.AreEqual(1, c.CallCount); // 이어서 C 실행
});
```

- [ ] **Step 2: 테스트 실행**

`run_tests`(필터 `실패후_재호출하면_완료된_아이템은_스킵하고_실패지점부터_재개한다`).
Expected: PASS. FAIL 시 실패한 카탈로그가 `_initializedCatalogs`에 추가되지 않았는지(조기 반환 안 됨), 실패 항목이 `_initializedItems`에 없는지 확인.

- [ ] **Step 3: 커밋**

```bash
git add Assets/FoundationDI/Tests/InitializeServiceTest.cs
git commit -m "[BEHAVIORAL] 실패 지점부터 재개 동작 검증 추가"
```

---

### Task 8: Dispose 후 세션 상태 초기화

`Dispose()`가 아이템/카탈로그 추적을 비워, 이후 초기화가 다시 실행되게 한다.

**Files:**
- Modify: `Assets/FoundationDI/Runtime/Services/InitializeService/InitializeService.cs`
- Test: `Assets/FoundationDI/Tests/InitializeServiceTest.cs`

**Interfaces:**
- Produces: `InitializeService.Dispose()`가 `_initializedItems`/`_initializedCatalogs`를 clear.

- [ ] **Step 1: 실패 테스트 작성**

```csharp
[UnityTest]
public IEnumerator Dispose후에는_세션상태가_초기화되어_다시_실행된다() => UniTask.ToCoroutine(async () =>
{
    var a = NewItem("A");
    var catalog = NewCatalog(a);
    var sut = new InitializeService(Substitute.For<IObjectResolver>());

    await sut.InitializeAsync(catalog);
    sut.Dispose();
    await sut.InitializeAsync(catalog);

    Assert.AreEqual(2, a.CallCount);
});
```

- [ ] **Step 2: 테스트 실행 → 실패 확인**

`run_tests`(필터 `Dispose후에는_세션상태가_초기화되어_다시_실행된다`).
Expected: FAIL — `CallCount == 1` (빈 `Dispose()`).

- [ ] **Step 3: 최소 구현**

```csharp
public void Dispose()
{
    _initializedItems.Clear();
    _initializedCatalogs.Clear();
}
```

- [ ] **Step 4: 테스트 실행 → 통과 확인**

`run_tests`(필터 `InitializeServiceTest`). Expected: 전체 PASS.

- [ ] **Step 5: 커밋**

```bash
git add Assets/FoundationDI/Runtime/Services/InitializeService/InitializeService.cs Assets/FoundationDI/Tests/InitializeServiceTest.cs
git commit -m "[BEHAVIORAL] Dispose 시 세션 상태 초기화 추가"
```

---

### Task 9: DI 등록 확장 + CreateAssetMenu (STRUCTURAL)

호스트가 서비스를 등록할 확장 메서드와, 카탈로그 에셋 생성 메뉴를 추가한다. 동작 변경이 아니므로 STRUCTURAL 커밋.

**Files:**
- Modify: `Assets/FoundationDI/Runtime/Services/InitializeService/InitializeService.cs` (등록 확장 클래스 추가)
- Modify: `Assets/FoundationDI/Runtime/Services/InitializeService/InitializeCatalog.cs` (`[CreateAssetMenu]`)
- Modify: `Assets/Scripts/LifetimeScopes/RootLifetimeScope.cs` (`RegisterInitializeService()` 호출)

**Interfaces:**
- Produces: `InitializeServiceVContainerExtensions.RegisterInitializeService(this IContainerBuilder)` → `builder.Register<IInitializeService, InitializeService>(Lifetime.Singleton)`.

- [ ] **Step 1: 등록 확장 추가**

`InitializeService.cs` 파일 끝(네임스페이스 내부)에 추가. `using VContainer;`는 이미 있음.
```csharp
public static class InitializeServiceVContainerExtensions
{
    /// <summary>
    /// InitializeService를 컨테이너에 싱글턴으로 등록한다.
    /// IObjectResolver는 VContainer가 자동 주입한다.
    /// </summary>
    public static void RegisterInitializeService(this IContainerBuilder builder)
    {
        builder.Register<IInitializeService, InitializeService>(Lifetime.Singleton);
    }
}
```

- [ ] **Step 2: CreateAssetMenu 추가**

`InitializeCatalog.cs`의 클래스 선언 위에 속성 추가(관례상 menuName `DarkNaku/...`):
```csharp
[CreateAssetMenu(fileName = "InitializeCatalog", menuName = "DarkNaku/InitializeCatalog")]
public class InitializeCatalog : ScriptableObject
```

- [ ] **Step 3: 호스트 등록**

`RootLifetimeScope.Configure`의 기존 등록 옆에 한 줄 추가:
```csharp
builder.RegisterInitializeService();
```

- [ ] **Step 4: 컴파일 + 전체 테스트 확인**

`read_console`로 에러 0 확인 → `run_tests`(EditMode, `FoundationDI.Tests`).
Expected: 전체 PASS(회귀 없음).

- [ ] **Step 5: 커밋**

```bash
git add Assets/FoundationDI/Runtime/Services/InitializeService Assets/Scripts/LifetimeScopes/RootLifetimeScope.cs
git commit -m "[STRUCTURAL] InitializeService DI 등록 확장 및 카탈로그 생성 메뉴 추가"
```

---

### Task 10: README 문서 (STRUCTURAL/docs)

다른 서비스(`ResourceService`, `HapticService`)와 같은 형식의 사용법 문서를 추가한다.

**Files:**
- Create: `Assets/FoundationDI/Runtime/Services/InitializeService/README.md`

- [ ] **Step 1: README 작성**

다음을 담는다:
- 개요: SO 항목(`InitializeItem`) → 카탈로그(`InitializeCatalog`) → `IInitializeService.InitializeAsync(catalog)` 순차 초기화, 세션 내 아이템/카탈로그 단위 중복 방지.
- 항목 작성 예시: `InitializeItem` 상속, `public override async Awaitable InitializeAsync(IObjectResolver resolver)` 안에서 `resolver.Resolve<T>()`로 서비스 접근(내부에서 UniTask await 가능).
- 카탈로그 에셋 생성: `Create > DarkNaku > InitializeCatalog`, 인스펙터에서 항목 SO 등록.
- 등록: `builder.RegisterInitializeService();`
- 호출 예시: 부트스트랩에서 `await _initializeService.InitializeAsync(catalog);`
- 실패 처리: 예외 즉시 전파, 실패 항목 미완료 → 재호출 시 실패 지점부터 재개.
- 범위 밖: 병렬/우선순위/진행률/취소/스레드 안전성 미지원, 엔트리포인트 자동 실행 미포함.

- [ ] **Step 2: 커밋**

```bash
git add Assets/FoundationDI/Runtime/Services/InitializeService/README.md
git commit -m "docs: InitializeService README 추가"
```

---

## Self-Review

- **Spec coverage**: 순차(Task1)·resolver 전달(Task2)·아이템 dedup(Task3)·카탈로그 dedup(Task4)·카탈로그 간 공유(Task5)·예외 전파(Task6)·재개(Task7)·Dispose(Task8)·DI 등록(Task9)·README(Task10) — 스펙 테스트 목록 8개 + DI 등록 + 문서 전부 매핑됨.
- **Awaitable 펌핑 리스크**: 전 fake가 `AwaitableCompletionSource` 즉시 완료를 반환 → EditMode 인라인. Task 1 Step 4에서 조기 검증됨.
- **타입 일관성**: `InitializeItem.InitializeAsync(IObjectResolver)`, `InitializeCatalog.Items`/`_items`, `IInitializeService.InitializeAsync(InitializeCatalog)`, `InitializeService(IObjectResolver)`, `RegisterInitializeService` — 전 태스크에서 이름/시그니처 일치.
- **주의(실행자)**: Task 4 Step 2는 첫 테스트 문구가 dedup 없이도 통과할 수 있어, 조기 반환을 확실히 검증하는 대체 테스트(`완료된_카탈로그_재호출은_조기반환한다`)로 교체하도록 명시했다. 실행 시 대체 테스트를 채택할 것.
