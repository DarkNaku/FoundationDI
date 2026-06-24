# UIManager 리뉴얼 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** uGUI 기반 UIManager를 메서드 빌더 API + 순차 직렬화(ShowQueue) + 추상 로더 + 트랜지션 추상화를 갖춘 시스템으로 전면 재설계한다.

**Architecture:** 표시 모드(Page/Popup/Overlay)는 Presenter 타입으로 고정(MVP 유지). `UIManager` 파사드가 동기 팩토리로 인스턴스를 즉시 반환하고, 실제 Show/Hide 전환은 `ShowQueue`로 순차 처리한다. prefab은 `[UIPrefab]` 속성 키로 `IUIAssetLoader`(Resources/Addressables)에서 로드하고, 연출은 `IUITransition`(트윈 라이브러리 비의존, UniTask 자체 보간)으로 추상화한다.

**Tech Stack:** Unity 6000.3.17f1, C#, UniTask, VContainer, uGUI, NSubstitute(테스트), Unity Test Framework.

**참고 설계 문서:** `docs/superpowers/specs/2026-06-25-uimanager-renewal-design.md`

## Global Constraints

- 네임스페이스: 모든 신규 타입은 `DarkNaku.FoundationDI` (UIManager 계열도 통일)
- 비동기: UniTask 사용 (`Cysharp.Threading.Tasks`). `Task`/`Awaitable` 직접 사용 금지
- **연출에 트윈 라이브러리(PrimeTween 등) 의존 금지** — `UniTask.Yield` + `Mathf.Lerp` + `AnimationCurve` 자체 보간만
- 테스트 함수명은 한국어 `should~` 의도로 작성 (프로젝트 규칙)
- 모킹은 NSubstitute. 순수 클래스는 EditMode, MonoBehaviour/연출은 PlayMode 테스트
- 테스트 실행: Unity Test Runner — `mcp__UnityMCP__run_tests`(mode: EditMode/PlayMode) 또는 에디터 Test Runner 창
- 스크립트 생성·수정 후 `mcp__UnityMCP__read_console`로 컴파일 에러 확인. `isCompiling == false` 후 진행
- 커밋: 구조적 변경은 `[STRUCTURAL]`, 행동적 변경은 `[BEHAVIORAL]` 접두어. 둘을 섞지 않음
- 위치: 신규 코드는 `Assets/FoundationDI/Runtime/Managers/UIManager/` 하위, 테스트는 `Assets/FoundationDI/Tests/Editor/UIManager/`(EditMode) · `Assets/FoundationDI/Tests/Runtime/UIManager/`(PlayMode)

## 사전 작업: 테스트 어셈블리 준비

신규 코드는 기존 `FoundationDI` 런타임 asmdef에 들어간다. 테스트용 asmdef가 없으므로 먼저 생성한다.

- [ ] **Step 1: EditMode 테스트 asmdef 생성**

`Assets/FoundationDI/Tests/Editor/FoundationDI.Tests.Editor.asmdef`:
```json
{
    "name": "FoundationDI.Tests.Editor",
    "references": ["FoundationDI", "UniTask", "VContainer", "UnityEngine.TestRunner", "UnityEditor.TestRunner"],
    "includePlatforms": ["Editor"],
    "precompiledReferences": ["nunit.framework.dll", "NSubstitute.dll", "Castle.Core.dll"],
    "defineConstraints": ["UNITY_INCLUDE_TESTS"],
    "autoReferenced": false
}
```

- [ ] **Step 2: PlayMode 테스트 asmdef 생성**

`Assets/FoundationDI/Tests/Runtime/FoundationDI.Tests.Runtime.asmdef`: 위와 동일하되 `"name": "FoundationDI.Tests.Runtime"`, `"includePlatforms": []`.

- [ ] **Step 3: 컴파일 확인 후 커밋**

`mcp__UnityMCP__read_console`로 에러 없음 확인.
```bash
git add Assets/FoundationDI/Tests
git commit -m "[STRUCTURAL] 테스트 어셈블리(EditMode/PlayMode) 추가"
```

> 참고: NSubstitute/Castle.Core DLL의 정확한 참조 이름은 `Assets/Packages/`의 실제 DLL 파일명과 일치시킨다. 첫 테스트 컴파일 시 참조 오류가 나면 `precompiledReferences`를 조정한다.

---

## Task 1: ShowQueue — 전환 순차 직렬화

**Files:**
- Create: `Assets/FoundationDI/Runtime/Managers/UIManager/Controllers/ShowQueue.cs`
- Test: `Assets/FoundationDI/Tests/Editor/UIManager/ShowQueueTests.cs`

**Interfaces:**
- Produces: `internal delegate UniTask ShowQueueWork(CancellationToken ct)`; `internal sealed class ShowQueue { int PendingCount; void Enqueue(ShowQueueWork); void CancelAndClear(); }`

- [ ] **Step 1: 실패 테스트 작성**

```csharp
using System.Collections;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine.TestTools;
using DarkNaku.FoundationDI;

public class ShowQueueTests
{
    [UnityTest]
    public IEnumerator 큐는_등록된_작업을_순서대로_직렬화한다() => UniTask.ToCoroutine(async () =>
    {
        var queue = new ShowQueue();
        var order = new List<int>();

        queue.Enqueue(async ct => { await UniTask.Yield(); order.Add(1); });
        queue.Enqueue(async ct => { order.Add(2); await UniTask.CompletedTask; });

        await UniTask.WaitUntil(() => order.Count == 2);

        Assert.AreEqual(new[] { 1, 2 }, order.ToArray());
    });
}
```

- [ ] **Step 2: 테스트 실패 확인**

`mcp__UnityMCP__run_tests`(mode: EditMode, filter: `ShowQueueTests`). Expected: FAIL — `ShowQueue` 타입 없음(컴파일 에러).

- [ ] **Step 3: 최소 구현**

```csharp
using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace DarkNaku.FoundationDI
{
    internal delegate UniTask ShowQueueWork(CancellationToken cancellationToken);

    internal sealed class ShowQueue
    {
        private readonly Queue<ShowQueueWork> _pending = new();
        private bool _processing;
        private CancellationTokenSource _cts = new();

        public int PendingCount => _pending.Count + (_processing ? 1 : 0);

        public void Enqueue(ShowQueueWork work)
        {
            if (work == null) throw new ArgumentNullException(nameof(work));
            _pending.Enqueue(work);
            if (!_processing) ProcessLoopAsync().Forget();
        }

        private async UniTaskVoid ProcessLoopAsync()
        {
            _processing = true;
            try
            {
                while (_pending.Count > 0)
                {
                    var next = _pending.Dequeue();
                    try { await next(_cts.Token); }
                    catch (OperationCanceledException) { }
                    catch (Exception ex) { Debug.LogException(ex); }
                }
            }
            finally { _processing = false; }
        }

        public void CancelAndClear()
        {
            _cts.Cancel();
            _pending.Clear();
            _cts = new CancellationTokenSource();
        }
    }
}
```

- [ ] **Step 4: 테스트 통과 확인**

`run_tests`(EditMode, `ShowQueueTests`). Expected: PASS.

- [ ] **Step 5: 취소 테스트 추가 → 통과 → 커밋**

```csharp
[UnityTest]
public IEnumerator CancelAndClear_후_대기작업은_실행되지_않는다() => UniTask.ToCoroutine(async () =>
{
    var queue = new ShowQueue();
    var ran = false;
    queue.Enqueue(async ct => { await UniTask.Delay(100, cancellationToken: ct); ran = true; });
    queue.CancelAndClear();
    await UniTask.Delay(200);
    Assert.IsFalse(ran);
});
```
통과 확인 후:
```bash
git add Assets/FoundationDI/Runtime/Managers/UIManager/Controllers/ShowQueue.cs Assets/FoundationDI/Tests/Editor/UIManager/ShowQueueTests.cs
git commit -m "[BEHAVIORAL] ShowQueue: 전환 순차 직렬화 구현"
```

---

## Task 2: InstanceCache — 인스턴스 재사용 보관

**Files:**
- Create: `Assets/FoundationDI/Runtime/Managers/UIManager/Controllers/InstanceCache.cs`
- Test: `Assets/FoundationDI/Tests/Editor/UIManager/InstanceCacheTests.cs`

**Interfaces:**
- Consumes: 캐시 항목은 `object`로 다뤄 Presenter 타입 의존 없이 단위 테스트 가능
- Produces: `internal sealed class InstanceCache { bool TryGet(Type, out object); void Register(Type, object); void Remove(Type); IReadOnlyCollection<object> AllInstances; void Clear(); }` — "항상 캐시" 정책: Register는 타입별 스택에 push, TryGet은 pop

- [ ] **Step 1: 실패 테스트 작성**

```csharp
using System;
using NUnit.Framework;
using DarkNaku.FoundationDI;

public class InstanceCacheTests
{
    private class FakeA { }

    [Test]
    public void 등록한_인스턴스를_타입으로_꺼낸다()
    {
        var cache = new InstanceCache();
        var a = new FakeA();
        cache.Register(typeof(FakeA), a);

        Assert.IsTrue(cache.TryGet(typeof(FakeA), out var got));
        Assert.AreSame(a, got);
    }

    [Test]
    public void 꺼낸_인스턴스는_캐시에서_제거된다()
    {
        var cache = new InstanceCache();
        cache.Register(typeof(FakeA), new FakeA());
        cache.TryGet(typeof(FakeA), out _);

        Assert.IsFalse(cache.TryGet(typeof(FakeA), out _));
    }
}
```

- [ ] **Step 2: 실패 확인** — `run_tests`(EditMode, `InstanceCacheTests`). Expected: FAIL(타입 없음).

- [ ] **Step 3: 최소 구현**

```csharp
using System;
using System.Collections.Generic;

namespace DarkNaku.FoundationDI
{
    internal sealed class InstanceCache
    {
        private readonly Dictionary<Type, Stack<object>> _table = new();

        public bool TryGet(Type type, out object instance)
        {
            instance = null;
            if (_table.TryGetValue(type, out var stack) && stack.Count > 0)
            {
                instance = stack.Pop();
                return true;
            }
            return false;
        }

        public void Register(Type type, object instance)
        {
            if (!_table.TryGetValue(type, out var stack))
            {
                stack = new Stack<object>();
                _table[type] = stack;
            }
            stack.Push(instance);
        }

        public void Remove(Type type) => _table.Remove(type);

        public IReadOnlyCollection<object> AllInstances
        {
            get
            {
                var list = new List<object>();
                foreach (var stack in _table.Values) list.AddRange(stack);
                return list;
            }
        }

        public void Clear() => _table.Clear();
    }
}
```

- [ ] **Step 4: 통과 확인** — `run_tests`(EditMode, `InstanceCacheTests`). Expected: PASS.

- [ ] **Step 5: 커밋**

```bash
git add Assets/FoundationDI/Runtime/Managers/UIManager/Controllers/InstanceCache.cs Assets/FoundationDI/Tests/Editor/UIManager/InstanceCacheTests.cs
git commit -m "[BEHAVIORAL] InstanceCache: 타입별 인스턴스 재사용 보관 구현"
```

---

## Task 3: UIPrefabAttribute — prefab 키 해석

**Files:**
- Create: `Assets/FoundationDI/Runtime/Managers/UIManager/Settings/UIPrefabAttribute.cs`
- Create: `Assets/FoundationDI/Runtime/Managers/UIManager/Settings/UIPrefabKeyResolver.cs`
- Test: `Assets/FoundationDI/Tests/Editor/UIManager/UIPrefabKeyResolverTests.cs`

**Interfaces:**
- Produces: `[AttributeUsage(AttributeTargets.Class)] class UIPrefabAttribute { string Key; }`; `static class UIPrefabKeyResolver { string Resolve(Type) }` — 타입→키 1회 캐싱, 속성 없으면 예외

- [ ] **Step 1: 실패 테스트 작성**

```csharp
using System;
using NUnit.Framework;
using DarkNaku.FoundationDI;

public class UIPrefabKeyResolverTests
{
    [UIPrefab("UI/MainMenu")]
    private class Tagged { }
    private class Untagged { }

    [Test]
    public void 속성에_선언된_키를_반환한다()
        => Assert.AreEqual("UI/MainMenu", UIPrefabKeyResolver.Resolve(typeof(Tagged)));

    [Test]
    public void 속성이_없으면_예외를_던진다()
        => Assert.Throws<InvalidOperationException>(() => UIPrefabKeyResolver.Resolve(typeof(Untagged)));
}
```

- [ ] **Step 2: 실패 확인** — Expected: FAIL.

- [ ] **Step 3: 최소 구현**

`UIPrefabAttribute.cs`:
```csharp
using System;

namespace DarkNaku.FoundationDI
{
    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public sealed class UIPrefabAttribute : Attribute
    {
        public string Key { get; }
        public UIPrefabAttribute(string key) => Key = key;
    }
}
```
`UIPrefabKeyResolver.cs`:
```csharp
using System;
using System.Collections.Generic;

namespace DarkNaku.FoundationDI
{
    internal static class UIPrefabKeyResolver
    {
        private static readonly Dictionary<Type, string> _cache = new();

        public static string Resolve(Type type)
        {
            if (_cache.TryGetValue(type, out var key)) return key;

            var attr = (UIPrefabAttribute)Attribute.GetCustomAttribute(type, typeof(UIPrefabAttribute));
            if (attr == null)
                throw new InvalidOperationException($"[UIManager] {type.Name}에 [UIPrefab] 속성이 없습니다.");

            _cache[type] = attr.Key;
            return attr.Key;
        }
    }
}
```

- [ ] **Step 4: 통과 확인** — Expected: PASS.

- [ ] **Step 5: 커밋**

```bash
git add Assets/FoundationDI/Runtime/Managers/UIManager/Settings/UIPrefabAttribute.cs Assets/FoundationDI/Runtime/Managers/UIManager/Settings/UIPrefabKeyResolver.cs Assets/FoundationDI/Tests/Editor/UIManager/UIPrefabKeyResolverTests.cs
git commit -m "[BEHAVIORAL] UIPrefab 속성 + 키 해석(1회 캐싱) 구현"
```

---

## Task 4: IUIAssetLoader + ResourcesUILoader

**Files:**
- Create: `Assets/FoundationDI/Runtime/Managers/UIManager/Loading/IUIAssetLoader.cs`
- Create: `Assets/FoundationDI/Runtime/Managers/UIManager/Loading/ResourcesUILoader.cs`
- Create: `Assets/FoundationDI/Runtime/Managers/UIManager/Loading/AddressablesUILoader.cs`
- Test: `Assets/FoundationDI/Tests/Runtime/UIManager/ResourcesUILoaderTests.cs` (PlayMode — Resources 접근)

**Interfaces:**
- Produces: `interface IUIAssetLoader { GameObject Load(string key); void Release(string key); }`; `ResourcesUILoader`, `AddressablesUILoader` 구현

- [ ] **Step 1: 인터페이스 + Resources 구현 작성 (테스트 우선)**

테스트는 Resources 폴더에 테스트 prefab이 필요하므로, 키 없음 시 예외 동작만 EditMode로 검증한다.

`ResourcesUILoaderTests.cs` (EditMode로 충분 — 존재하지 않는 키):
```csharp
using System.IO;
using NUnit.Framework;
using DarkNaku.FoundationDI;

public class ResourcesUILoaderTests
{
    [Test]
    public void 존재하지_않는_키는_FileNotFound를_던진다()
    {
        var loader = new ResourcesUILoader();
        Assert.Throws<FileNotFoundException>(() => loader.Load("__no_such_ui_prefab__"));
    }
}
```
(파일 위치를 `Tests/Editor/UIManager/`로 두고 EditMode에서 실행)

- [ ] **Step 2: 실패 확인** — Expected: FAIL.

- [ ] **Step 3: 구현**

`IUIAssetLoader.cs`:
```csharp
using UnityEngine;

namespace DarkNaku.FoundationDI
{
    public interface IUIAssetLoader
    {
        GameObject Load(string key);
        void Release(string key);
    }
}
```
`ResourcesUILoader.cs`:
```csharp
using System.IO;
using UnityEngine;

namespace DarkNaku.FoundationDI
{
    public sealed class ResourcesUILoader : IUIAssetLoader
    {
        public GameObject Load(string key)
        {
            var prefab = Resources.Load<GameObject>(key);
            if (prefab == null)
                throw new FileNotFoundException($"[UIManager] Resources에서 prefab을 찾을 수 없습니다: {key}");
            return prefab;
        }

        public void Release(string key) { /* Resources 시스템이 수명 관리 */ }
    }
}
```
`AddressablesUILoader.cs`:
```csharp
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace DarkNaku.FoundationDI
{
    public sealed class AddressablesUILoader : IUIAssetLoader
    {
        private readonly Dictionary<string, AsyncOperationHandle<GameObject>> _handles = new();

        public GameObject Load(string key)
        {
            if (_handles.TryGetValue(key, out var existing) && existing.IsValid())
                return existing.Result;

            var handle = Addressables.LoadAssetAsync<GameObject>(key);
            var prefab = handle.WaitForCompletion();
            _handles[key] = handle;
            return prefab;
        }

        public void Release(string key)
        {
            if (_handles.TryGetValue(key, out var handle))
            {
                if (handle.IsValid()) Addressables.Release(handle);
                _handles.Remove(key);
            }
        }
    }
}
```

- [ ] **Step 4: 통과 확인** — Expected: PASS.

- [ ] **Step 5: 커밋**

```bash
git add Assets/FoundationDI/Runtime/Managers/UIManager/Loading Assets/FoundationDI/Tests/Editor/UIManager/ResourcesUILoaderTests.cs
git commit -m "[BEHAVIORAL] IUIAssetLoader + Resources/Addressables 로더 구현"
```

---

## Task 5: IUITransition + NoopTransition + UITransitionAsset

**Files:**
- Create: `Assets/FoundationDI/Runtime/Managers/UIManager/Transitions/IUITransition.cs`
- Create: `Assets/FoundationDI/Runtime/Managers/UIManager/Transitions/NoopTransition.cs`
- Create: `Assets/FoundationDI/Runtime/Managers/UIManager/Transitions/UITransitionAsset.cs`
- Test: `Assets/FoundationDI/Tests/Editor/UIManager/NoopTransitionTests.cs`

**Interfaces:**
- Produces: `interface IUITransition { UniTask PlayShow(RectTransform, CancellationToken); UniTask PlayHide(RectTransform, CancellationToken); }`; `NoopTransition`(즉시 완료); `abstract UITransitionAsset : ScriptableObject, IUITransition` — `float _duration`, `AnimationCurve _ease`, `bool _unscaledTime`, `protected UniTask Animate(float from, float to, Action<float> apply, CancellationToken)`

- [ ] **Step 1: 실패 테스트 작성 (Noop은 즉시 완료)**

```csharp
using System.Collections;
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using DarkNaku.FoundationDI;

public class NoopTransitionTests
{
    [UnityTest]
    public IEnumerator Noop은_즉시_완료된다() => UniTask.ToCoroutine(async () =>
    {
        var go = new GameObject("t", typeof(RectTransform));
        var rt = go.GetComponent<RectTransform>();
        IUITransition noop = new NoopTransition();

        await noop.PlayShow(rt, CancellationToken.None);
        await noop.PlayHide(rt, CancellationToken.None);

        Assert.Pass();
        Object.DestroyImmediate(go);
    });
}
```

- [ ] **Step 2: 실패 확인** — Expected: FAIL.

- [ ] **Step 3: 구현**

`IUITransition.cs`:
```csharp
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace DarkNaku.FoundationDI
{
    public interface IUITransition
    {
        UniTask PlayShow(RectTransform target, CancellationToken ct);
        UniTask PlayHide(RectTransform target, CancellationToken ct);
    }
}
```
`NoopTransition.cs`:
```csharp
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace DarkNaku.FoundationDI
{
    public sealed class NoopTransition : IUITransition
    {
        public UniTask PlayShow(RectTransform target, CancellationToken ct) => UniTask.CompletedTask;
        public UniTask PlayHide(RectTransform target, CancellationToken ct) => UniTask.CompletedTask;
    }
}
```
`UITransitionAsset.cs` (자체 보간 베이스, 트윈 비의존):
```csharp
using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace DarkNaku.FoundationDI
{
    public abstract class UITransitionAsset : ScriptableObject, IUITransition
    {
        [SerializeField] protected float _duration = 0.2f;
        [SerializeField] protected AnimationCurve _ease = AnimationCurve.EaseInOut(0, 0, 1, 1);
        [SerializeField] protected bool _unscaledTime = true;

        public abstract UniTask PlayShow(RectTransform target, CancellationToken ct);
        public abstract UniTask PlayHide(RectTransform target, CancellationToken ct);

        // 트윈 라이브러리 없이 매 프레임 보간
        protected async UniTask Animate(Action<float> apply, CancellationToken ct)
        {
            if (_duration <= 0f) { apply(1f); return; }

            var elapsed = 0f;
            apply(0f);
            while (elapsed < _duration)
            {
                if (ct.IsCancellationRequested) { apply(1f); return; }
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
                elapsed += _unscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
                var t = Mathf.Clamp01(elapsed / _duration);
                apply(_ease.Evaluate(t));
            }
            apply(1f);
        }

        protected static CanvasGroup EnsureCanvasGroup(RectTransform target)
        {
            var cg = target.GetComponent<CanvasGroup>();
            return cg != null ? cg : target.gameObject.AddComponent<CanvasGroup>();
        }
    }
}
```

- [ ] **Step 4: 통과 확인** — Expected: PASS.

- [ ] **Step 5: 커밋**

```bash
git add Assets/FoundationDI/Runtime/Managers/UIManager/Transitions/IUITransition.cs Assets/FoundationDI/Runtime/Managers/UIManager/Transitions/NoopTransition.cs Assets/FoundationDI/Runtime/Managers/UIManager/Transitions/UITransitionAsset.cs Assets/FoundationDI/Tests/Editor/UIManager/NoopTransitionTests.cs
git commit -m "[BEHAVIORAL] IUITransition + Noop + 자체 보간 베이스 구현"
```

---

## Task 6: Fade / Scale / Slide 트랜지션 에셋

**Files:**
- Create: `Assets/FoundationDI/Runtime/Managers/UIManager/Transitions/FadeTransitionAsset.cs`
- Create: `Assets/FoundationDI/Runtime/Managers/UIManager/Transitions/ScaleTransitionAsset.cs`
- Create: `Assets/FoundationDI/Runtime/Managers/UIManager/Transitions/SlideTransitionAsset.cs`
- Test: `Assets/FoundationDI/Tests/Runtime/UIManager/TransitionAssetTests.cs` (PlayMode)

**Interfaces:**
- Consumes: `UITransitionAsset.Animate`, `EnsureCanvasGroup` (Task 5)
- Produces: 세 ScriptableObject 트랜지션. PlayShow는 0→표시상태, PlayHide는 표시상태→0

- [ ] **Step 1: 실패 테스트 작성 (Fade 종료 시 alpha=1/0)**

```csharp
using System.Collections;
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using DarkNaku.FoundationDI;

public class TransitionAssetTests
{
    [UnityTest]
    public IEnumerator Fade_PlayShow_종료시_alpha는_1이다() => UniTask.ToCoroutine(async () =>
    {
        var go = new GameObject("t", typeof(RectTransform), typeof(CanvasGroup));
        var rt = go.GetComponent<RectTransform>();
        var fade = ScriptableObject.CreateInstance<FadeTransitionAsset>();

        await fade.PlayShow(rt, CancellationToken.None);

        Assert.AreEqual(1f, go.GetComponent<CanvasGroup>().alpha, 0.001f);
        Object.DestroyImmediate(go);
    });
}
```

- [ ] **Step 2: 실패 확인** — Expected: FAIL.

- [ ] **Step 3: 구현**

`FadeTransitionAsset.cs`:
```csharp
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace DarkNaku.FoundationDI
{
    [CreateAssetMenu(fileName = "FadeTransition", menuName = "DarkNaku/UI Transition/Fade")]
    public sealed class FadeTransitionAsset : UITransitionAsset
    {
        public override UniTask PlayShow(RectTransform target, CancellationToken ct)
        {
            var cg = EnsureCanvasGroup(target);
            return Animate(t => cg.alpha = t, ct);
        }

        public override UniTask PlayHide(RectTransform target, CancellationToken ct)
        {
            var cg = EnsureCanvasGroup(target);
            return Animate(t => cg.alpha = 1f - t, ct);
        }
    }
}
```
`ScaleTransitionAsset.cs`:
```csharp
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace DarkNaku.FoundationDI
{
    [CreateAssetMenu(fileName = "ScaleTransition", menuName = "DarkNaku/UI Transition/Scale")]
    public sealed class ScaleTransitionAsset : UITransitionAsset
    {
        [SerializeField] private float _fromScale = 0.8f;

        public override UniTask PlayShow(RectTransform target, CancellationToken ct)
            => Animate(t => target.localScale = Vector3.one * Mathf.Lerp(_fromScale, 1f, t), ct);

        public override UniTask PlayHide(RectTransform target, CancellationToken ct)
            => Animate(t => target.localScale = Vector3.one * Mathf.Lerp(1f, _fromScale, t), ct);
    }
}
```
`SlideTransitionAsset.cs`:
```csharp
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace DarkNaku.FoundationDI
{
    public enum SlideDirection { Left, Right, Top, Bottom }

    [CreateAssetMenu(fileName = "SlideTransition", menuName = "DarkNaku/UI Transition/Slide")]
    public sealed class SlideTransitionAsset : UITransitionAsset
    {
        [SerializeField] private SlideDirection _direction = SlideDirection.Bottom;

        private Vector2 OffsetFor(RectTransform target)
        {
            var size = target.rect.size;
            return _direction switch
            {
                SlideDirection.Left => new Vector2(-size.x, 0),
                SlideDirection.Right => new Vector2(size.x, 0),
                SlideDirection.Top => new Vector2(0, size.y),
                _ => new Vector2(0, -size.y),
            };
        }

        public override UniTask PlayShow(RectTransform target, CancellationToken ct)
        {
            var home = target.anchoredPosition;
            var off = home + OffsetFor(target);
            return Animate(t => target.anchoredPosition = Vector2.Lerp(off, home, t), ct);
        }

        public override UniTask PlayHide(RectTransform target, CancellationToken ct)
        {
            var home = target.anchoredPosition;
            var off = home + OffsetFor(target);
            return Animate(t => target.anchoredPosition = Vector2.Lerp(home, off, t), ct);
        }
    }
}
```

- [ ] **Step 4: 통과 확인** — Expected: PASS.

- [ ] **Step 5: 커밋**

```bash
git add Assets/FoundationDI/Runtime/Managers/UIManager/Transitions/FadeTransitionAsset.cs Assets/FoundationDI/Runtime/Managers/UIManager/Transitions/ScaleTransitionAsset.cs Assets/FoundationDI/Runtime/Managers/UIManager/Transitions/SlideTransitionAsset.cs Assets/FoundationDI/Tests/Runtime/UIManager/TransitionAssetTests.cs
git commit -m "[BEHAVIORAL] Fade/Scale/Slide 트랜지션 에셋 구현"
```

---

## Task 7: UIView — 공통 뷰 베이스

**Files:**
- Create: `Assets/FoundationDI/Runtime/Managers/UIManager/Views/UIView.cs`
- Test: `Assets/FoundationDI/Tests/Runtime/UIManager/UIViewTests.cs` (PlayMode)

**Interfaces:**
- Consumes: `IUITransition`, `NoopTransition` (Task 5)
- Produces: `abstract class UIView : MonoBehaviour { RectTransform RectTransform; bool InputEnabled; IUITransition ShowTransition; IUITransition HideTransition; virtual void OnInitializeView(); UniTask PlayShow(CancellationToken); UniTask PlayHide(CancellationToken); }`

- [ ] **Step 1: 실패 테스트 작성 (InputEnabled가 GraphicRaycaster 토글)**

```csharp
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
using DarkNaku.FoundationDI;

public class UIViewTests
{
    private class TestView : UIView { }

    [Test]
    public void InputEnabled는_GraphicRaycaster를_토글한다()
    {
        var go = new GameObject("v", typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster));
        var view = go.AddComponent<TestView>();

        view.InputEnabled = false;
        Assert.IsFalse(go.GetComponent<GraphicRaycaster>().enabled);
        view.InputEnabled = true;
        Assert.IsTrue(go.GetComponent<GraphicRaycaster>().enabled);

        Object.DestroyImmediate(go);
    }
}
```

- [ ] **Step 2: 실패 확인** — Expected: FAIL.

- [ ] **Step 3: 구현**

```csharp
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

namespace DarkNaku.FoundationDI
{
    public abstract class UIView : MonoBehaviour
    {
        [SerializeField] private UITransitionAsset _showTransition;
        [SerializeField] private UITransitionAsset _hideTransition;

        private static readonly NoopTransition Noop = new();

        private RectTransform _rectTransform;
        public RectTransform RectTransform => _rectTransform ??= (RectTransform)transform;

        private GraphicRaycaster _raycaster;
        private GraphicRaycaster Raycaster => _raycaster ??= GetComponent<GraphicRaycaster>();

        public bool InputEnabled
        {
            get => Raycaster != null && Raycaster.enabled;
            set { if (Raycaster != null) Raycaster.enabled = value; }
        }

        // 우선순위: per-show 오버라이드 > 인스펙터 에셋 > Noop
        public IUITransition ShowTransition { get; set; }
        public IUITransition HideTransition { get; set; }

        public virtual void OnInitializeView() { }

        public UniTask PlayShow(CancellationToken ct)
            => (ShowTransition ?? _showTransition ?? (IUITransition)Noop).PlayShow(RectTransform, ct);

        public UniTask PlayHide(CancellationToken ct)
            => (HideTransition ?? _hideTransition ?? (IUITransition)Noop).PlayHide(RectTransform, ct);
    }
}
```

- [ ] **Step 4: 통과 확인** — Expected: PASS.

- [ ] **Step 5: 커밋**

```bash
git add Assets/FoundationDI/Runtime/Managers/UIManager/Views/UIView.cs Assets/FoundationDI/Tests/Runtime/UIManager/UIViewTests.cs
git commit -m "[BEHAVIORAL] UIView: 입력 토글 + 트랜지션 위임 구현"
```

---

## Task 8: Presenter 계층 — Base / Page / Popup / Overlay

**Files:**
- Create: `.../Presenters/IUIElementHost.cs`, `IConfigurable.cs`, `UIPresenterBase.cs`, `UIPagePresenter.cs`, `UIPopupPresenter.cs`, `UIOverlayPresenter.cs`
- Test: `Assets/FoundationDI/Tests/Editor/UIManager/PresenterLifecycleTests.cs`

**Interfaces:**
- Consumes: `UIView` (Task 7)
- Produces:
  - `internal interface IUIElementHost { void RequestHide(UIPresenterBase); void RequestDestroy(UIPresenterBase); }`
  - `interface IConfigurable<TParams> { void Configure(TParams p); }`
  - `abstract class UIPresenterBase` : `enum LifecycleEvent`, 8개 `protected virtual void On...()`, `void Bind(UIView, IUIElementHost)`, `Subscribe(LifecycleEvent, Action<UIPresenterBase>)`, `internal void Fire(LifecycleEvent)`, `void Hide()`, `void Close()`, `IUITransition TransitionOverride`, `ResetTransient()`
  - `abstract class UIPresenterBase<TView> : UIPresenterBase where TView : UIView { TView View; }`
  - `abstract class UIPagePresenter<TView>`, `UIPopupPresenter<TView>`, `UIOverlayPresenter<TView>` : 각 모드 마커 + 빌더 메서드(`OnShown`/`OnAfterHidden`/`WithTransition`/`With`)

- [ ] **Step 1: 실패 테스트 작성 (구독자 발화 순서)**

```csharp
using System.Collections.Generic;
using NUnit.Framework;
using DarkNaku.FoundationDI;

public class PresenterLifecycleTests
{
    private class V : UIView { }
    [UIPrefab("UI/Sample")]
    private class P : UIPagePresenter<V> { }

    [Test]
    public void OnShown_구독자는_Shown_발화시_호출된다()
    {
        var p = new P();
        var called = false;
        p.OnShown(_ => called = true);

        p.FireForTest(UIPresenterBase.LifecycleEvent.Shown);

        Assert.IsTrue(called);
    }
}
```
> `FireForTest`는 `Fire`를 호출하는 `internal` 테스트 보조. 테스트 어셈블리에 `[assembly: InternalsVisibleTo("FoundationDI.Tests.Editor")]`를 `FoundationDI` asmdef 측 `AssemblyInfo.cs`에 추가한다(이 task에서 함께 생성).

- [ ] **Step 2: 실패 확인** — Expected: FAIL.

- [ ] **Step 3: 구현**

`IUIElementHost.cs`, `IConfigurable.cs`:
```csharp
namespace DarkNaku.FoundationDI
{
    internal interface IUIElementHost
    {
        void RequestHide(UIPresenterBase element);
        void RequestDestroy(UIPresenterBase element);
    }

    public interface IConfigurable<in TParams>
    {
        void Configure(TParams parameters);
    }
}
```
`UIPresenterBase.cs`:
```csharp
using System;
using System.Collections.Generic;
using UnityEngine;

namespace DarkNaku.FoundationDI
{
    public abstract class UIPresenterBase
    {
        public enum LifecycleEvent { BeforeShown, Shown, AfterShown, BeforeHidden, Hidden, AfterHidden, Destroyed }

        internal UIView ViewBase { get; private set; }
        internal IUIElementHost Host { get; private set; }
        internal IUITransition TransitionOverride { get; private set; }

        private Dictionary<LifecycleEvent, List<Action<UIPresenterBase>>> _subscribers;

        internal void Bind(UIView view, IUIElementHost host) { ViewBase = view; Host = host; }

        internal void Subscribe(LifecycleEvent ev, Action<UIPresenterBase> handler)
        {
            if (handler == null) return;
            _subscribers ??= new();
            if (!_subscribers.TryGetValue(ev, out var list)) { list = new(); _subscribers[ev] = list; }
            list.Add(handler);
        }

        internal void Fire(LifecycleEvent ev)
        {
            if (_subscribers == null || !_subscribers.TryGetValue(ev, out var list)) return;
            for (int i = 0; i < list.Count; i++)
            {
                try { list[i](this); } catch (Exception e) { Debug.LogException(e); }
            }
        }

        internal void FireForTest(LifecycleEvent ev) => Fire(ev);   // 테스트 보조

        internal void SetTransitionOverride(IUITransition t) => TransitionOverride = t;

        internal virtual void ResetTransient()
        {
            _subscribers?.Clear();
            TransitionOverride = null;
        }

        // 라이프사이클 훅
        protected internal virtual void OnInitialize() { }
        protected internal virtual void OnBeforeShow() { }
        protected internal virtual void OnShow() { }
        protected internal virtual void OnAfterShow() { }
        protected internal virtual void OnBeforeHide() { }
        protected internal virtual void OnHide() { }
        protected internal virtual void OnAfterHide() { }
        protected internal virtual void OnDestroyElement() { }

        // 커맨드
        public void Hide() => Host?.RequestHide(this);
        public void Close() => Host?.RequestDestroy(this);
    }

    public abstract class UIPresenterBase<TView> : UIPresenterBase where TView : UIView
    {
        protected TView View => (TView)ViewBase;
    }
}
```
`UIPagePresenter.cs` (빌더 메서드 — Page; Popup/Overlay도 동일 패턴):
```csharp
using System;

namespace DarkNaku.FoundationDI
{
    public abstract class UIPagePresenter<TView> : UIPresenterBase<TView> where TView : UIView
    {
        public UIPagePresenter<TView> OnShown(Action<UIPagePresenter<TView>> cb)
        { Subscribe(LifecycleEvent.Shown, p => cb((UIPagePresenter<TView>)p)); return this; }

        public UIPagePresenter<TView> OnAfterHidden(Action<UIPagePresenter<TView>> cb)
        { Subscribe(LifecycleEvent.AfterHidden, p => cb((UIPagePresenter<TView>)p)); return this; }

        public UIPagePresenter<TView> WithTransition(IUITransition t) { SetTransitionOverride(t); return this; }

        public UIPagePresenter<TView> With<TParams>(TParams p)
        { if (this is IConfigurable<TParams> c) c.Configure(p); return this; }
    }
}
```
`UIPopupPresenter.cs`, `UIOverlayPresenter.cs`: 위 `UIPagePresenter`와 동일한 빌더 메서드를, 자기 타입(`UIPopupPresenter<TView>` / `UIOverlayPresenter<TView>`)을 반환하도록 복제한다. `UIOverlayPresenter`는 추가로 `protected internal virtual bool Above => true;`를 둔다.

`Assets/FoundationDI/Runtime/Managers/UIManager/AssemblyInfo.cs`:
```csharp
using System.Runtime.CompilerServices;
[assembly: InternalsVisibleTo("FoundationDI.Tests.Editor")]
[assembly: InternalsVisibleTo("FoundationDI.Tests.Runtime")]
```

- [ ] **Step 4: 통과 확인** — Expected: PASS.

- [ ] **Step 5: 커밋**

```bash
git add Assets/FoundationDI/Runtime/Managers/UIManager/Presenters Assets/FoundationDI/Runtime/Managers/UIManager/AssemblyInfo.cs Assets/FoundationDI/Tests/Editor/UIManager/PresenterLifecycleTests.cs
git commit -m "[BEHAVIORAL] Presenter 계층(Base/Page/Popup/Overlay) + 빌더 구현"
```

---

## Task 9: 모드별 컨트롤러 — Page / Popup / Overlay

**Files:**
- Create: `.../Controllers/PageController.cs`, `PopupController.cs`, `OverlayController.cs`
- Test: `Assets/FoundationDI/Tests/Editor/UIManager/ModeControllerTests.cs`

**Interfaces:**
- Consumes: `UIPresenterBase`
- Produces:
  - `PageController { UIPresenterBase Active; void SetActive(UIPresenterBase); void Clear(); }`
  - `PopupController { void Push(UIPresenterBase); void Remove(UIPresenterBase); UIPresenterBase Top; IReadOnlyList<UIPresenterBase> All; }`
  - `OverlayController { void Register(UIPresenterBase, bool above); void Unregister(UIPresenterBase); IReadOnlyList<UIPresenterBase> All; }`

- [ ] **Step 1: 실패 테스트 작성**

```csharp
using NUnit.Framework;
using DarkNaku.FoundationDI;

public class ModeControllerTests
{
    private class V : UIView { }
    private class A : UIPopupPresenter<V> { }
    private class B : UIPopupPresenter<V> { }

    [Test]
    public void Popup_스택은_LIFO로_Top을_반환한다()
    {
        var c = new PopupController();
        var a = new A(); var b = new B();
        c.Push(a); c.Push(b);
        Assert.AreSame(b, c.Top);
        c.Remove(b);
        Assert.AreSame(a, c.Top);
    }
}
```

- [ ] **Step 2: 실패 확인** — Expected: FAIL.

- [ ] **Step 3: 구현**

`PageController.cs`:
```csharp
namespace DarkNaku.FoundationDI
{
    internal sealed class PageController
    {
        public UIPresenterBase Active { get; private set; }
        public void SetActive(UIPresenterBase page) => Active = page;
        public void Clear() => Active = null;
    }
}
```
`PopupController.cs`:
```csharp
using System.Collections.Generic;

namespace DarkNaku.FoundationDI
{
    internal sealed class PopupController
    {
        private readonly List<UIPresenterBase> _stack = new();
        public IReadOnlyList<UIPresenterBase> All => _stack;
        public UIPresenterBase Top => _stack.Count > 0 ? _stack[^1] : null;
        public void Push(UIPresenterBase popup) => _stack.Add(popup);
        public void Remove(UIPresenterBase popup) => _stack.Remove(popup);
        public void Clear() => _stack.Clear();
    }
}
```
`OverlayController.cs`:
```csharp
using System.Collections.Generic;

namespace DarkNaku.FoundationDI
{
    internal sealed class OverlayController
    {
        private readonly List<UIPresenterBase> _above = new();
        private readonly List<UIPresenterBase> _below = new();

        public IReadOnlyList<UIPresenterBase> All
        {
            get { var l = new List<UIPresenterBase>(_above); l.AddRange(_below); return l; }
        }

        public void Register(UIPresenterBase o, bool above) => (above ? _above : _below).Add(o);
        public void Unregister(UIPresenterBase o) { _above.Remove(o); _below.Remove(o); }
        public bool IsAbove(UIPresenterBase o) => _above.Contains(o);
        public void Clear() { _above.Clear(); _below.Clear(); }
    }
}
```

- [ ] **Step 4: 통과 확인** — Expected: PASS.

- [ ] **Step 5: 커밋**

```bash
git add Assets/FoundationDI/Runtime/Managers/UIManager/Controllers/PageController.cs Assets/FoundationDI/Runtime/Managers/UIManager/Controllers/PopupController.cs Assets/FoundationDI/Runtime/Managers/UIManager/Controllers/OverlayController.cs Assets/FoundationDI/Tests/Editor/UIManager/ModeControllerTests.cs
git commit -m "[BEHAVIORAL] Page/Popup/Overlay 모드 컨트롤러 구현"
```

---

## Task 10: UIManagerSettings + UIRoot (레이어 컨테이너)

**Files:**
- Create: `.../Settings/UIManagerSettings.cs`
- Create: `.../Controllers/UIRoot.cs`
- Test: `Assets/FoundationDI/Tests/Runtime/UIManager/UIRootTests.cs` (PlayMode)

**Interfaces:**
- Produces:
  - `UIManagerSettings : ScriptableObject { Canvas RootCanvasPrefab; UITransitionAsset DefaultPageTransition; DefaultPopupTransition; DefaultOverlayTransition; }`
  - `UIRoot { Transform PageLayer; PopupLayer; AboveOverlayLayer; BelowOverlayLayer; RectTransform CreatePopupModalWrapper(); }` — 루트 Canvas와 4개 레이어 RectTransform 구성, Popup용 dim+모달 래퍼 생성

- [ ] **Step 1: 실패 테스트 작성 (레이어 4개 생성)**

```csharp
using NUnit.Framework;
using UnityEngine;
using DarkNaku.FoundationDI;

public class UIRootTests
{
    [Test]
    public void UIRoot는_4개_레이어를_생성한다()
    {
        var root = new UIRoot();
        Assert.IsNotNull(root.PageLayer);
        Assert.IsNotNull(root.PopupLayer);
        Assert.IsNotNull(root.AboveOverlayLayer);
        Assert.IsNotNull(root.BelowOverlayLayer);
        Object.DestroyImmediate(root.CanvasObject);
    }
}
```

- [ ] **Step 2: 실패 확인** — Expected: FAIL.

- [ ] **Step 3: 구현**

`UIManagerSettings.cs`:
```csharp
using UnityEngine;

namespace DarkNaku.FoundationDI
{
    [CreateAssetMenu(fileName = "UIManagerSettings", menuName = "DarkNaku/UIManagerSettings")]
    public sealed class UIManagerSettings : ScriptableObject
    {
        [SerializeField] private UITransitionAsset _defaultPageTransition;
        [SerializeField] private UITransitionAsset _defaultPopupTransition;
        [SerializeField] private UITransitionAsset _defaultOverlayTransition;

        public IUITransition DefaultPageTransition => _defaultPageTransition;
        public IUITransition DefaultPopupTransition => _defaultPopupTransition;
        public IUITransition DefaultOverlayTransition => _defaultOverlayTransition;
    }
}
```
`UIRoot.cs` (레이어 순서: Below Overlay → Page → Popup → Above Overlay):
```csharp
using UnityEngine;
using UnityEngine.UI;

namespace DarkNaku.FoundationDI
{
    internal sealed class UIRoot
    {
        public GameObject CanvasObject { get; }
        public Transform BelowOverlayLayer { get; }
        public Transform PageLayer { get; }
        public Transform PopupLayer { get; }
        public Transform AboveOverlayLayer { get; }

        public UIRoot()
        {
            CanvasObject = new GameObject("[UIManager]", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = CanvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            Object.DontDestroyOnLoad(CanvasObject);

            BelowOverlayLayer = CreateLayer("BelowOverlay");
            PageLayer = CreateLayer("Page");
            PopupLayer = CreateLayer("Popup");
            AboveOverlayLayer = CreateLayer("AboveOverlay");
        }

        private Transform CreateLayer(string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(CanvasObject.transform, false);
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
            return rt;
        }
    }
}
```

- [ ] **Step 4: 통과 확인** — Expected: PASS.

- [ ] **Step 5: 커밋**

```bash
git add Assets/FoundationDI/Runtime/Managers/UIManager/Settings/UIManagerSettings.cs Assets/FoundationDI/Runtime/Managers/UIManager/Controllers/UIRoot.cs Assets/FoundationDI/Tests/Runtime/UIManager/UIRootTests.cs
git commit -m "[BEHAVIORAL] UIManagerSettings + UIRoot 레이어 컨테이너 구현"
```

---

## Task 11: UIInstanceFactory — prefab 로드·생성·주입

**Files:**
- Create: `.../Controllers/UIInstanceFactory.cs`
- Test: `Assets/FoundationDI/Tests/Editor/UIManager/UIInstanceFactoryTests.cs`

**Interfaces:**
- Consumes: `IUIAssetLoader`(Task 4), `UIPrefabKeyResolver`(Task 3), `UIView`(Task 7), `UIPresenterBase`(Task 8), VContainer `IObjectResolver`
- Produces: `UIInstanceFactory(IObjectResolver, IUIAssetLoader)`; `UIPresenterBase Create(Type presenterType, IUIElementHost host)` — 키 해석 → prefab 로드 → Instantiate → `UIView` 추출 → Presenter 생성(`Activator`)+`Inject` → `Bind` → `OnInitialize`+`OnInitializeView`

- [ ] **Step 1: 실패 테스트 작성 (NSubstitute로 로더 모킹)**

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
    public void 로더가_제공한_prefab으로_Presenter를_생성하고_바인딩한다()
    {
        var prefab = new GameObject("prefab", typeof(RectTransform));
        prefab.AddComponent<V>();

        var loader = Substitute.For<IUIAssetLoader>();
        loader.Load("UI/Sample").Returns(prefab);

        var resolver = Substitute.For<IObjectResolver>();
        var host = Substitute.For<IUIElementHost>();

        var factory = new UIInstanceFactory(resolver, loader);
        var presenter = factory.Create(typeof(P), host);

        Assert.IsInstanceOf<P>(presenter);
        Assert.IsNotNull(((P)presenter).ViewBaseForTest);   // Bind 확인용 internal 노출

        Object.DestroyImmediate(prefab);
    }
}
```
> `ViewBaseForTest`는 `internal UIView ViewBaseForTest => ViewBase;`를 `UIPresenterBase`에 추가(테스트 보조). `IUIElementHost`는 internal이므로 `InternalsVisibleTo`로 테스트에서 접근 가능(Task 8에서 설정 완료).

- [ ] **Step 2: 실패 확인** — Expected: FAIL.

- [ ] **Step 3: 구현**

```csharp
using System;
using UnityEngine;
using VContainer;

namespace DarkNaku.FoundationDI
{
    internal sealed class UIInstanceFactory
    {
        private readonly IObjectResolver _resolver;
        private readonly IUIAssetLoader _loader;

        public UIInstanceFactory(IObjectResolver resolver, IUIAssetLoader loader)
        {
            _resolver = resolver;
            _loader = loader;
        }

        public UIPresenterBase Create(Type presenterType, IUIElementHost host)
        {
            var key = UIPrefabKeyResolver.Resolve(presenterType);
            var prefab = _loader.Load(key);

            var go = UnityEngine.Object.Instantiate(prefab);
            var view = go.GetComponent<UIView>();
            if (view == null)
            {
                UnityEngine.Object.Destroy(go);
                throw new InvalidOperationException($"[UIManager] {key} prefab 루트에 UIView가 없습니다.");
            }

            var presenter = (UIPresenterBase)Activator.CreateInstance(presenterType);
            _resolver.Inject(presenter);

            presenter.Bind(view, host);
            view.OnInitializeView();
            presenter.OnInitialize();

            return presenter;
        }
    }
}
```

- [ ] **Step 4: 통과 확인** — Expected: PASS.

- [ ] **Step 5: 커밋**

```bash
git add Assets/FoundationDI/Runtime/Managers/UIManager/Controllers/UIInstanceFactory.cs Assets/FoundationDI/Runtime/Managers/UIManager/Presenters/UIPresenterBase.cs Assets/FoundationDI/Tests/Editor/UIManager/UIInstanceFactoryTests.cs
git commit -m "[BEHAVIORAL] UIInstanceFactory: prefab 로드·생성·주입 구현"
```

---

## Task 12: UIManager 파사드 — 팩토리 + Show/Hide 흐름

**Files:**
- Create: `.../IUIManager.cs`, `.../UIManager.cs`
- Test: `Assets/FoundationDI/Tests/Runtime/UIManager/UIManagerFlowTests.cs` (PlayMode)

**Interfaces:**
- Consumes: 모든 이전 task 산출물
- Produces:
  - `interface IUIManager { T Page<T>() where T : UIPresenterBase; T Popup<T>(); T Overlay<T>(); }` (+ 내부 `IUIElementHost`)
  - `class UIManager : IUIManager, IUIElementHost, IDisposable` — `Page/Popup/Overlay<T>()`가 캐시/팩토리로 인스턴스 확보 후 Show를 `ShowQueue`에 enqueue; Hide/Destroy도 큐 경유; Show flow는 `UniTask.Yield` 1프레임 후 레이어 부착·트랜지션·라이프사이클·구독자 발화; Hide flow는 트랜지션 역재생 후 `InstanceCache.Register`(항상 캐시)

- [ ] **Step 1: 실패 테스트 작성 (Page 호출 시 OnShow까지 도달)**

```csharp
using System.Collections;
using Cysharp.Threading.Tasks;
using NSubstitute;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using VContainer;
using DarkNaku.FoundationDI;

public class UIManagerFlowTests
{
    public class V : UIView { }
    [UIPrefab("UI/Sample")]
    public class P : UIPagePresenter<V> { public bool Shown; protected internal override void OnShow() => Shown = true; }

    private GameObject _prefab;

    [SetUp] public void Setup()
    {
        _prefab = new GameObject("prefab", typeof(RectTransform));
        _prefab.AddComponent<V>();
    }
    [TearDown] public void Teardown() => Object.DestroyImmediate(_prefab);

    [UnityTest]
    public IEnumerator Page_호출시_OnShow까지_도달한다() => UniTask.ToCoroutine(async () =>
    {
        var loader = Substitute.For<IUIAssetLoader>();
        loader.Load("UI/Sample").Returns(_prefab);
        var resolver = Substitute.For<IObjectResolver>();
        var settings = ScriptableObject.CreateInstance<UIManagerSettings>();
        var factory = new UIInstanceFactory(resolver, loader);

        var manager = new UIManager(settings, factory);
        var p = manager.Page<P>();

        await UniTask.WaitUntil(() => p.Shown);
        Assert.IsTrue(p.Shown);

        manager.Dispose();
    });
}
```

- [ ] **Step 2: 실패 확인** — Expected: FAIL.

- [ ] **Step 3: 구현**

`IUIManager.cs`:
```csharp
namespace DarkNaku.FoundationDI
{
    public interface IUIManager
    {
        T Page<T>() where T : UIPresenterBase;
        T Popup<T>() where T : UIPresenterBase;
        T Overlay<T>() where T : UIPresenterBase;
    }
}
```
`UIManager.cs`:
```csharp
using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace DarkNaku.FoundationDI
{
    public sealed class UIManager : IUIManager, IUIElementHost, IDisposable
    {
        private readonly UIManagerSettings _settings;
        private readonly UIInstanceFactory _factory;
        private readonly ShowQueue _queue = new();
        private readonly InstanceCache _cache = new();
        private readonly PageController _pages = new();
        private readonly PopupController _popups = new();
        private readonly OverlayController _overlays = new();
        private readonly Dictionary<Type, UIPresenterBase> _active = new();
        private readonly UIRoot _root;
        private bool _disposed;

        public UIManager(UIManagerSettings settings, UIInstanceFactory factory)
        {
            _settings = settings;
            _factory = factory;
            _root = new UIRoot();
        }

        public T Page<T>() where T : UIPresenterBase
            => Acquire<T>(inst => _queue.Enqueue(ct => ShowPageAsync(inst, ct)));
        public T Popup<T>() where T : UIPresenterBase
            => Acquire<T>(inst => _queue.Enqueue(ct => ShowPopupAsync(inst, ct)));
        public T Overlay<T>() where T : UIPresenterBase
            => Acquire<T>(inst => _queue.Enqueue(ct => ShowOverlayAsync(inst, ct)));

        private T Acquire<T>(Action<UIPresenterBase> enqueueShow) where T : UIPresenterBase
        {
            if (_disposed) throw new ObjectDisposedException(nameof(UIManager));
            var type = typeof(T);

            if (_active.TryGetValue(type, out var existing))
            {
                Debug.LogWarning($"[UIManager] {type.Name} 이미 활성. 중복 요청 무시.");
                return (T)existing;
            }

            UIPresenterBase instance;
            if (_cache.TryGet(type, out var cached)) instance = (UIPresenterBase)cached;
            else instance = _factory.Create(type, this);

            _active[type] = instance;
            enqueueShow(instance);
            return (T)instance;
        }

        private async UniTask ShowPageAsync(UIPresenterBase inst, CancellationToken ct)
        {
            await UniTask.Yield(PlayerLoopTiming.Update, ct);   // 체인 등록 보장
            if (_pages.Active != null && _pages.Active != inst)
            {
                await HideCoreAsync(_pages.Active, _root.PageLayer, ct);
                _pages.Clear();
            }
            _pages.SetActive(inst);
            AttachTo(inst, _root.PageLayer);
            await ShowCoreAsync(inst, ct);
        }

        private async UniTask ShowPopupAsync(UIPresenterBase inst, CancellationToken ct)
        {
            await UniTask.Yield(PlayerLoopTiming.Update, ct);
            _popups.Push(inst);
            AttachTo(inst, _root.PopupLayer);
            UpdatePopupModal();
            await ShowCoreAsync(inst, ct);
        }

        private async UniTask ShowOverlayAsync(UIPresenterBase inst, CancellationToken ct)
        {
            await UniTask.Yield(PlayerLoopTiming.Update, ct);
            var above = (inst as IOverlayPlacement)?.Above ?? true;
            _overlays.Register(inst, above);
            AttachTo(inst, above ? _root.AboveOverlayLayer : _root.BelowOverlayLayer);
            await ShowCoreAsync(inst, ct);
        }

        private void AttachTo(UIPresenterBase inst, Transform layer)
            => inst.ViewBase.RectTransform.SetParent(layer, false);

        private async UniTask ShowCoreAsync(UIPresenterBase inst, CancellationToken ct)
        {
            if (inst.TransitionOverride != null) inst.ViewBase.ShowTransition = inst.TransitionOverride;
            inst.OnBeforeShow(); inst.Fire(UIPresenterBase.LifecycleEvent.BeforeShown);
            await inst.ViewBase.PlayShow(ct);
            inst.OnShow(); inst.Fire(UIPresenterBase.LifecycleEvent.Shown);
            inst.OnAfterShow(); inst.Fire(UIPresenterBase.LifecycleEvent.AfterShown);
        }

        private async UniTask HideCoreAsync(UIPresenterBase inst, Transform layer, CancellationToken ct)
        {
            inst.OnBeforeHide(); inst.Fire(UIPresenterBase.LifecycleEvent.BeforeHidden);
            await inst.ViewBase.PlayHide(ct);
            inst.OnHide(); inst.Fire(UIPresenterBase.LifecycleEvent.Hidden);
            inst.ViewBase.RectTransform.SetParent(null, false);
            inst.OnAfterHide(); inst.Fire(UIPresenterBase.LifecycleEvent.AfterHidden);

            _active.Remove(inst.GetType());
            inst.ResetTransient();
            _cache.Register(inst.GetType(), inst);   // 항상 캐시
            inst.ViewBase.gameObject.SetActive(false);
        }

        private void UpdatePopupModal()
        {
            for (int i = 0; i < _popups.All.Count; i++)
                _popups.All[i].ViewBase.InputEnabled = (i == _popups.All.Count - 1);
        }

        void IUIElementHost.RequestHide(UIPresenterBase e) => _queue.Enqueue(ct => HandleHideAsync(e, ct));
        void IUIElementHost.RequestDestroy(UIPresenterBase e) => _queue.Enqueue(ct => HandleDestroyAsync(e, ct));

        private async UniTask HandleHideAsync(UIPresenterBase e, CancellationToken ct)
        {
            var layer = LayerOf(e);
            await HideCoreAsync(e, layer, ct);
            if (_pages.Active == e) _pages.Clear();
            _popups.Remove(e); UpdatePopupModal();
            _overlays.Unregister(e);
        }

        private async UniTask HandleDestroyAsync(UIPresenterBase e, CancellationToken ct)
        {
            await HandleHideAsync(e, ct);
            _cache.Remove(e.GetType());
            e.OnDestroyElement(); e.Fire(UIPresenterBase.LifecycleEvent.Destroyed);
            if (e.ViewBase != null) UnityEngine.Object.Destroy(e.ViewBase.gameObject);
        }

        private Transform LayerOf(UIPresenterBase e)
        {
            if (_pages.Active == e) return _root.PageLayer;
            if (_popups.All.Contains(e)) return _root.PopupLayer;
            return _overlays.IsAbove(e) ? _root.AboveOverlayLayer : _root.BelowOverlayLayer;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _queue.CancelAndClear();
            if (_root.CanvasObject != null) UnityEngine.Object.Destroy(_root.CanvasObject);
        }
    }

    internal interface IOverlayPlacement { bool Above { get; } }
}
```
> `UIOverlayPresenter<TView>`가 `IOverlayPlacement`를 구현하도록 Task 8 산출물에 `bool IOverlayPlacement.Above => Above;`를 추가한다(이 task에서 보강).

- [ ] **Step 4: 통과 확인** — Expected: PASS.

- [ ] **Step 5: Popup/Overlay 흐름 테스트 추가 → 통과 → 커밋**

`Popup_호출시_스택_Top이_된다`, `Overlay_호출시_OnShow까지_도달한다`를 같은 패턴으로 추가하고 통과 확인 후:
```bash
git add Assets/FoundationDI/Runtime/Managers/UIManager/IUIManager.cs Assets/FoundationDI/Runtime/Managers/UIManager/UIManager.cs Assets/FoundationDI/Runtime/Managers/UIManager/Presenters/UIOverlayPresenter.cs Assets/FoundationDI/Tests/Runtime/UIManager/UIManagerFlowTests.cs
git commit -m "[BEHAVIORAL] UIManager 파사드: 팩토리 + Show/Hide 큐 흐름 구현"
```

---

## Task 13: 기존 UI 코드 제거 + DI 등록

**Files:**
- Delete: `.../UIManager/UIManager.cs`(구버전), `UIPresenter.cs`, `UIView.cs`, `UISetting.cs` — ※ 신규 파일과 경로가 겹치지 않도록, 신규는 `Views/`·`Presenters/` 하위에 있으므로 구 루트 파일만 삭제
- Modify: `Assets/Scripts/LifetimeScopes/RootLifetimeScope.cs`
- Test: `Assets/FoundationDI/Tests/Editor/UIManager/DIRegistrationTests.cs`

**Interfaces:**
- Consumes: `UIManager`, `IUIAssetLoader`, `ResourcesUILoader`, `UIInstanceFactory`, `UIManagerSettings`

- [ ] **Step 1: 구버전 UI 파일 삭제**

```bash
git rm Assets/FoundationDI/Runtime/Managers/UIManager/UIManager.cs.meta 2>/dev/null
```
구 `UIManager.cs`/`UIPresenter.cs`/`UIView.cs`/`UISetting.cs`와 각 `.meta`를 삭제(신규 파일은 하위 폴더에 있어 충돌 없음). `mcp__UnityMCP__read_console`로 컴파일 에러(참조 끊김) 확인 — 사용처가 없어야 정상.

- [ ] **Step 2: DI 등록 테스트 작성**

```csharp
using NSubstitute;
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
        builder.RegisterInstance(ScriptableObject.CreateInstance<UIManagerSettings>());
        builder.Register<IUIAssetLoader, ResourcesUILoader>(Lifetime.Singleton);
        builder.Register<UIInstanceFactory>(Lifetime.Singleton);
        builder.Register<UIManager>(Lifetime.Singleton).As<IUIManager>();

        var container = builder.Build();
        Assert.IsNotNull(container.Resolve<IUIManager>());
    }
}
```

- [ ] **Step 3: 실패 확인** — Expected: FAIL 또는 컴파일 통과 후 PASS. (해석 실패 시 등록 코드 조정)

- [ ] **Step 4: RootLifetimeScope에 등록 추가**

```csharp
using VContainer;
using VContainer.Unity;
using DarkNaku.FoundationDI;

public class RootLifetimeScope : LifetimeScope
{
    [SerializeField] private UIManagerSettings _uiSettings;

    protected override void Configure(IContainerBuilder builder)
    {
        builder.RegisterInstance(_uiSettings);
        builder.Register<IUIAssetLoader, ResourcesUILoader>(Lifetime.Singleton);
        builder.Register<UIInstanceFactory>(Lifetime.Singleton);
        builder.Register<UIManager>(Lifetime.Singleton).As<IUIManager>();
    }
}
```

- [ ] **Step 5: 통과 확인 후 커밋**

`run_tests`(EditMode, `DIRegistrationTests`) PASS, 전체 테스트 회귀 없음 확인:
```bash
git add -A
git commit -m "[BEHAVIORAL] 구 UI 시스템 제거 + UIManager DI 등록"
```

---

## Task 14: 통합 스모크 + 문서 갱신

**Files:**
- Modify: `CLAUDE.md` (UIManager 아키텍처 설명 갱신)
- Modify: `docs/superpowers/specs/2026-06-25-uimanager-renewal-design.md` (상태: 구현 완료)

- [ ] **Step 1: 전체 테스트 실행**

`mcp__UnityMCP__run_tests`(EditMode), 이어서 (PlayMode). Expected: 전부 PASS.

- [ ] **Step 2: CLAUDE.md의 UIManager 단락 갱신**

기존 "UIManager … MVP 패턴 … `Activator.CreateInstance`" 설명을 신규 설계(빌더 API `Page/Popup/Overlay<T>()`, `ShowQueue` 직렬화, `IUIAssetLoader`, `[UIPrefab]`, `IUITransition`)로 교체.

- [ ] **Step 3: 커밋**

```bash
git add CLAUDE.md docs/superpowers/specs/2026-06-25-uimanager-renewal-design.md
git commit -m "[STRUCTURAL] UIManager 리뉴얼 문서 갱신"
```

---

## Self-Review 결과

- **Spec 커버리지**: §3 빌더 API→Task 8·12 / §4.1 Presenter→Task 8 / §4.2 View→Task 7 / §4.3 컨트롤러·큐·캐시·팩토리→Task 1·2·9·11·12 / §4.4 로더·키→Task 3·4 / §4.5 트랜지션→Task 5·6 / §6 DI→Task 13 / §7 테스트 전략→전 task EditMode·PlayMode 분리. 누락 없음.
- **타입 일관성**: `UIPresenterBase`/`ViewBase`/`Bind`/`Fire`/`ResetTransient`, `IUIAssetLoader.Load`, `UIPrefabKeyResolver.Resolve`, `ShowQueueWork`가 task 간 동일 시그니처로 사용됨.
- **주의**: Task 12에서 `IOverlayPlacement` 보강과 `UIOverlayPresenter`의 `Above` 노출을 Task 8 산출물에 추가하는 의존이 있음 — 실행 시 Task 8 파일을 함께 수정.
