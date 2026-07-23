# PoolManager 의존성 주입 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 풀 아이템 인스턴스가 생성될 때 1회 VContainer 의존성을 주입받게 한다.

**Architecture:** `PoolManager` 생성자에 `IObjectResolver`를 추가하고(VContainer 자동 해결), `ObjectPool`의 create 팩토리에서 인스턴스를 만든 직후 `resolver?.InjectGameObject(go)`로 계층 전체 MonoBehaviour에 주입한다. 재사용(`Get`) 경로에는 주입하지 않는다.

**Tech Stack:** Unity 6000.3.17f1, VContainer(`VContainer` / `VContainer.Unity` 네임스페이스), EditMode Test Framework(NUnit), NSubstitute 5.3.0, UnityMCP(`run_tests`/`read_console`).

## Global Constraints

- 네임스페이스는 `DarkNaku.FoundationDI`.
- 테스트 함수 이름은 한국어, `should~` 의도.
- **STRUCTURAL 커밋과 BEHAVIORAL 커밋을 절대 섞지 않는다.** 커밋 제목에 `[STRUCTURAL]` / `[BEHAVIORAL]` 접두어.
- 모든 컴파일·테스트는 UnityMCP를 통해서만 수행. 스크립트 수정 후 `read_console`로 컴파일 완료(`editor_state.isCompiling == false`) + 에러 0 확인 후 `run_tests`.
- 커밋은 전체 EditMode 테스트가 통과할 때만.
- 모킹은 NSubstitute. EditMode 단위 테스트로 검증.

## File Structure

- **Modify** `Assets/FoundationDI/Runtime/Managers/PoolManager/PoolManager.cs`
  - 생성자에 `IObjectResolver resolver` 파라미터 추가 + 필드 보관 (Task 1)
  - `using VContainer.Unity;` 추가 + create 팩토리에 `resolver?.InjectGameObject(go)` (Task 2)
- **Modify** `Assets/FoundationDI/Runtime/Managers/UIManager/UIManager.cs:34`
  - `new PoolManager(_resource, Root.GO.transform)` → `new PoolManager(_resource, null, Root.GO.transform)` (Task 1)
- **Modify/Test** `Assets/FoundationDI/Tests/PoolManagerTest.cs`
  - 기존 6개 `new PoolManager(...)` 콜사이트를 새 시그니처로 수정 (Task 1)
  - 주입 검증 테스트 추가 (Task 2)
- **Modify** `plan.md` — 활성 계획을 이 작업으로 전환 + 테스트 항목 추가 (Task 2)

---

### Task 1: [STRUCTURAL] 생성자에 IObjectResolver 추가

동작을 바꾸지 않는다. 주입 로직은 아직 넣지 않는다. 생성자 시그니처만 넓히고 모든 콜사이트를 맞춘다. 기존 테스트가 그대로 통과하면 성공.

**Files:**
- Modify: `Assets/FoundationDI/Runtime/Managers/PoolManager/PoolManager.cs:23-40`
- Modify: `Assets/FoundationDI/Runtime/Managers/UIManager/UIManager.cs:34`
- Modify: `Assets/FoundationDI/Tests/PoolManagerTest.cs` (라인 17, 33, 49, 64, 87, 105, 124)

**Interfaces:**
- Produces: `public PoolManager(IResourceService resourceService, IObjectResolver resolver, Transform parent = null)` — 이후 Task 2가 이 `resolver` 필드를 사용한다.
- Consumes: `VContainer.IObjectResolver`(VContainer 런타임이 자동 등록).

- [ ] **Step 1: 생성자에 파라미터·필드 추가**

`PoolManager.cs`의 필드 블록(현재 라인 18-21)에 `_resolver` 필드를 추가한다:

```csharp
        private readonly IResourceService _resourceService;
        private readonly IObjectResolver _resolver;
        private readonly Dictionary<string, PoolData> _table;
        private readonly Transform _root;
        private bool _disposed;
```

생성자(현재 라인 23-40)를 다음으로 바꾼다. 시그니처에 `resolver`를 추가하고 필드에 보관하는 것 외에는 본문 로직을 그대로 둔다:

```csharp
        public PoolManager(IResourceService resourceService, IObjectResolver resolver, Transform parent = null)
        {
            _resourceService = resourceService;
            _resolver = resolver;
            _table = new();

            // 풀 루트는 DontDestroyOnLoad로 두지 않는다.
            // parent(보통 씬 LifetimeScope의 transform)가 주어지면 그 아래에 둬서
            // 풀 루트가 활성 씬이 아니라 스코프가 속한 씬에 확실히 귀속되도록 한다.
            // 그러면 씬 언로드 시 풀도 함께 정리된다. parent가 없으면 활성 씬에 생성된다.
            var root = new GameObject("[PoolManager]");

            _root = root.transform;

            if (parent != null)
            {
                _root.SetParent(parent, false);
            }
        }
```

`IObjectResolver`는 `VContainer` 네임스페이스에 있고 `using VContainer;`(현재 라인 6)가 이미 있으므로 using 추가는 불필요하다.

- [ ] **Step 2: 런타임 콜사이트(UIManager) 수정**

`UIManager.cs:34`를 수정한다. UIManager는 resolver를 보유하지 않으므로 `null`을 넘겨 기존 동작(주입 없음)을 그대로 유지한다:

```csharp
        private PoolManager Pool => _pool ??= new PoolManager(_resource, null, Root.GO.transform);
```

- [ ] **Step 3: 기존 테스트 콜사이트 수정**

`PoolManagerTest.cs`의 7개 콜사이트를 새 시그니처에 맞춘다. 이 테스트들은 주입을 검증하지 않으므로 `resolver`에 `null`을 넘긴다.

- 라인 17, 33, 64, 105, 124: `var sut = new PoolManager(resource);` → `var sut = new PoolManager(resource, null);`
- 라인 49: `var sut = new PoolManager(resource, parent.transform);` → `var sut = new PoolManager(resource, null, parent.transform);`
- 라인 87: `var sut = new PoolManager(resource, scope.transform);` → `var sut = new PoolManager(resource, null, scope.transform);`

- [ ] **Step 4: 컴파일 확인**

UnityMCP `read_console`로 컴파일 완료(`editor_state.isCompiling == false`)와 에러 0을 확인한다.
Expected: 컴파일 에러 없음.

- [ ] **Step 5: 기존 테스트 통과 확인**

UnityMCP `run_tests`(EditMode, 필터 `PoolManagerTest`) 실행.
Expected: 기존 7개 테스트 모두 PASS(동작 불변).

- [ ] **Step 6: 커밋**

```bash
git add Assets/FoundationDI/Runtime/Managers/PoolManager/PoolManager.cs \
        Assets/FoundationDI/Runtime/Managers/UIManager/UIManager.cs \
        Assets/FoundationDI/Tests/PoolManagerTest.cs
git commit -m "[STRUCTURAL] PoolManager 생성자에 IObjectResolver 파라미터 추가

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

### Task 2: [BEHAVIORAL] 생성 시 계층 전체에 의존성 주입

**Files:**
- Test: `Assets/FoundationDI/Tests/PoolManagerTest.cs` (새 테스트 추가)
- Modify: `Assets/FoundationDI/Runtime/Managers/PoolManager/PoolManager.cs` (using + create 팩토리)
- Modify: `plan.md`

**Interfaces:**
- Consumes: Task 1의 `_resolver` 필드, 확장 메서드 `VContainer.Unity.ObjectResolverUnityExtensions.InjectGameObject(this IObjectResolver, GameObject)` — 계층의 각 MonoBehaviour에 `resolver.Inject(mb)`를 호출한다.

- [ ] **Step 1: 실패하는 테스트 작성 (RED)**

`PoolManagerTest.cs` 상단 using에 `using VContainer;`를 추가한다(없으면). 그리고 다음 테스트를 클래스에 추가한다.

`Object.Instantiate`된 프리팹은 `IPoolItem`이 없으므로 `PoolManager`가 `PoolItem`(MonoBehaviour) 1개를 붙인다. 따라서 `InjectGameObject`는 그 컴포넌트 1개에 대해 `Inject`를 1회 호출한다:

```csharp
    [Test]
    public void Get은_새_인스턴스_생성시_resolver로_컴포넌트에_주입한다()
    {
        var prefab = new GameObject("prefab");
        var resource = Substitute.For<IResourceService>();
        resource.Load<GameObject>("enemy").Returns(prefab);
        var resolver = Substitute.For<IObjectResolver>();
        var sut = new PoolManager(resource, resolver);

        sut.Get("enemy");

        resolver.Received(1).Inject(Arg.Any<object>());

        sut.Dispose();
        Object.DestroyImmediate(prefab);
    }
```

- [ ] **Step 2: 테스트 실패 확인 (RED)**

UnityMCP `read_console`로 컴파일 확인 후 `run_tests`(EditMode, 필터 `Get은_새_인스턴스_생성시_resolver로_컴포넌트에_주입한다`).
Expected: FAIL — 주입 코드가 없어 `Inject`가 호출되지 않음(`Received(1)` 불만족).

- [ ] **Step 3: 최소 구현 (GREEN)**

`PoolManager.cs` 상단 using에 `using VContainer.Unity;`를 추가한다(`InjectGameObject` 확장 메서드용). 현재 라인 5-6 부근:

```csharp
using Object = UnityEngine.Object;
using VContainer;
using VContainer.Unity;
```

`Register`의 `ObjectPool` create 람다(현재 라인 137-152)에서 `item` 확보 직후, `OnCreateItem()` 호출 직전에 주입을 넣는다:

```csharp
                () =>
                {
                    var go = Object.Instantiate(prefab);

                    var item = go.GetComponent<IPoolItem>();

                    if (item == null)
                    {
                        item = go.AddComponent<PoolItem>();
                    }

                    // 생성 시 1회 계층 전체 MonoBehaviour에 DI 주입.
                    // resolver가 없으면(테스트/컨테이너 미사용) 조용히 건너뛴다.
                    _resolver?.InjectGameObject(go);

                    item.OnCreateItem();

                    return item;
                },
```

- [ ] **Step 4: 테스트 통과 확인 (GREEN)**

UnityMCP `read_console`(컴파일 에러 0) 후 `run_tests`(EditMode, 필터 `PoolManagerTest`).
Expected: 신규 테스트 포함 전체 PASS.

- [ ] **Step 5: plan.md 갱신**

`plan.md`의 활성 계획을 이 작업으로 바꾸고, 완료 항목으로 기록한다. `## 활성 계획: HapticService` 이하 블록을 다음으로 교체한다:

```markdown
## 활성 계획: PoolManager 의존성 주입

세부: `docs/superpowers/plans/2026-07-23-poolmanager-di-injection.md`

테스트 목록 (다음 작업 = 첫 번째 미완료 항목):

- [x] Get은 새 인스턴스 생성 시 resolver로 컴포넌트에 주입한다
```

- [ ] **Step 6: 커밋**

```bash
git add Assets/FoundationDI/Runtime/Managers/PoolManager/PoolManager.cs \
        Assets/FoundationDI/Tests/PoolManagerTest.cs \
        plan.md
git commit -m "[BEHAVIORAL] PoolManager가 생성 시 풀 아이템에 의존성 주입

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

### Task 3: [BEHAVIORAL] 재사용 시 재주입하지 않음 검증 (회귀 방지)

생성 시 1회 주입을 못박는 회귀 방지 테스트. 이미 Task 2 구현으로 통과할 가능성이 높으므로, RED가 안 나오면 "이미 충족됨"으로 간주하고 테스트만 남긴다.

**Files:**
- Test: `Assets/FoundationDI/Tests/PoolManagerTest.cs`
- Modify: `plan.md`

**Interfaces:**
- Consumes: Task 2에서 구현한 생성 팩토리 주입.

- [ ] **Step 1: 테스트 작성**

같은 키를 두 번 `Get`하되, 첫 아이템을 반환(Release)해 풀에서 재사용되게 한 뒤 총 주입 횟수가 1회인지 확인한다. `PoolItem.Release`는 즉시 반환(delay=0)이므로 동기적으로 풀에 돌아간다:

```csharp
    [Test]
    public void 재사용된_아이템은_다시_주입하지_않는다()
    {
        var prefab = new GameObject("prefab");
        var resource = Substitute.For<IResourceService>();
        resource.Load<GameObject>("enemy").Returns(prefab);
        var resolver = Substitute.For<IObjectResolver>();
        var sut = new PoolManager(resource, resolver);

        var first = sut.Get("enemy");
        sut.Release(first);       // 풀로 반환
        sut.Get("enemy");         // 같은 인스턴스 재사용

        resolver.Received(1).Inject(Arg.Any<object>());

        sut.Dispose();
        Object.DestroyImmediate(prefab);
    }
```

- [ ] **Step 2: 테스트 실행**

UnityMCP `read_console`(컴파일 에러 0) 후 `run_tests`(EditMode, 필터 `재사용된_아이템은_다시_주입하지_않는다`).
Expected: PASS (Task 2 구현이 생성 시 1회만 주입하므로). 만약 FAIL이면 systematic-debugging으로 원인(재사용 경로 주입) 조사.

- [ ] **Step 3: 전체 테스트 확인**

UnityMCP `run_tests`(EditMode, 필터 `PoolManagerTest`).
Expected: 전체 PASS.

- [ ] **Step 4: plan.md 갱신 + 커밋**

`plan.md` 테스트 목록에 `- [x] 재사용된 아이템은 다시 주입하지 않는다` 추가 후:

```bash
git add Assets/FoundationDI/Tests/PoolManagerTest.cs plan.md
git commit -m "[BEHAVIORAL] 풀 아이템 재사용 시 재주입하지 않음 회귀 테스트 추가

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```
