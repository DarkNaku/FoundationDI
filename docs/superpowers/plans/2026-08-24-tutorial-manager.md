# TutorialManager Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 게임 조건(레벨 시작, 아이템 등장 등)에 따라 발동하는 튜토리얼 시퀀스를 `ITutorialManager` 하나로 진행·영속화한다. 게임 코드는 원래 발행하던 메시지만 발행하고 튜토리얼을 모른다.

**Architecture:** 두 층으로 가른다. 진행 규칙은 순수 C#(`TutorialManager`/`TutorialSequence`/`TutorialStep`)이라 EditMode에서 씬 없이 전부 테스트되고, 씬 오써링은 얇은 MonoBehaviour 어댑터(`TutorialSequenceBehaviour`/`TutorialStepBehaviour`)가 인스펙터 데이터를 엔진에 넘기는 역할만 한다. 시퀀스는 순차 리스트가 아니라 **조건부 후보 집합**이며, 진행도는 인덱스가 아니라 **시퀀스 ID**로 저장한다.

**Tech Stack:** Unity 6000.3.17f1, VContainer, `UnityEngine.Awaitable`, uGUI, NUnit + NSubstitute(EditMode), `AwaitableTest`(TestSupport).

**Spec:** `docs/superpowers/specs/2026-08-24-tutorial-manager-design.md`

## Global Constraints

- 네임스페이스는 `DarkNaku.FoundationDI` 하나. 새 asmdef를 만들지 않는다(기존 `FoundationDI`).
- 런타임 코드는 `Assets/FoundationDI/Runtime/Managers/TutorialManager/`, 테스트는 `Assets/FoundationDI/Tests/`(플랫 배치).
- 신규 async는 `UnityEngine.Awaitable`. UniTask/R3는 쓰지 않는다.
- **`await` 뒤에 그 `Awaitable`의 `.IsCompleted`를 읽지 않는다** — Unity 6의 `Awaitable`은 단일 사용/풀 반환이라 detached 상태가 된다. 테스트는 `await` 이전에 단언할 것을 단언한다.
- EditMode에서 `Awaitable.NextFrameAsync()` / `WaitForSecondsAsync()`는 영원히 완료되지 않는다. **프레임/시간 대기는 반드시 주입된 seam을 통해서 한다** (Task 3의 `ITutorialClock`). 테스트 본문의 대기는 `AwaitableTest.NextFrame/Delay/WaitUntil`.
- 테스트는 `[UnityTest] [Timeout(5000)] public IEnumerator 한국어이름() => AwaitableTest.Run(async () => { ... });` 형태. 동기 테스트는 `[Test] public void 한국어이름()`.
- 테스트 이름은 한국어 `should~` 의도 서술(밑줄로 띄어쓰기).
- 테스트 파일 수정은 Write로 통째 교체(UnityMCP 관례).
- 순수 C# 엔진(`TutorialManager`/`TutorialSequence`/`TutorialStep`/트리거/저장소/레지스트리)에 `UnityEngine.Object` 생성이나 프레임 펌프를 넣지 않는다. `Transform` 참조는 값으로만 다룬다.
- 구조적 변경과 행동적 변경을 같은 커밋에 섞지 않는다. 제목에 `[STRUCTURAL]`/`[BEHAVIORAL]` 접두어.
- 매 태스크 종료 시 UnityMCP `run_tests(EditMode)` 전체를 돌리고 초록일 때만 커밋한다. 그 전에 `read_console`로 컴파일 에러가 없고 `editor_state.isCompiling == false`인지 확인한다.

## File Structure

| 경로 | 책임 |
| --- | --- |
| `TutorialManager/TutorialTypes.cs` | `TutorialState`, `ResumeMode`, `TutorialTriggerContext` |
| `TutorialManager/ITutorialManager.cs` | 공개 계약 |
| `TutorialManager/TutorialManager.cs` | 진행 엔진 — 등록·조건 arm·시퀀스 실행·대기열·영속화 |
| `TutorialManager/TutorialSequence.cs` | Step 목록 + 시작 조건 + 재개 모드 + 타임아웃 |
| `TutorialManager/TutorialStep.cs` | 트리거 쌍 + 지연 + 모듈 목록 + 타깃 |
| `TutorialManager/ITutorialClock.cs` `TutorialClock.cs` | 프레임/시간 대기 seam (엔진이 Unity 시간에 직접 안 붙게) |
| `TutorialManager/TutorialTriggerAwaiter.cs` | arm/disarm 트리거를 `Awaitable`로 잇는 내부 어댑터 |
| `TutorialManager/TutorialManagerRegistration.cs` | `builder.RegisterTutorialManager(saveKey)` |
| `TutorialManager/README.md` | 사용 매뉴얼 |
| `TutorialManager/Storage/ITutorialProgressStorage.cs` | 영속화 seam |
| `TutorialManager/Storage/PlayerPrefsTutorialProgressStorage.cs` | 기본 구현 |
| `TutorialManager/Targets/TutorialTargetRef.cs` | 직접 참조 \| 키 |
| `TutorialManager/Targets/TutorialTargetHandle.cs` | 살아있는 타깃 핸들 |
| `TutorialManager/Targets/ITutorialTargetRegistry.cs` | 타깃 해석 seam |
| `TutorialManager/Targets/TutorialTargetRegistry.cs` | 기본 구현 (LIFO 스택 + 대기) |
| `TutorialManager/Targets/TutorialTarget.cs` | MonoBehaviour — 키 등록/해제 |
| `TutorialManager/Targets/TutorialScreenRect.cs` | UI/3D 공통 스크린 rect 계산 |
| `TutorialManager/Triggers/ITutorialTrigger.cs` | 트리거 seam |
| `TutorialManager/Triggers/AutoTrigger.cs` | 즉시 발동 |
| `TutorialManager/Triggers/ManualTrigger.cs` | `Complete(id)` 발동 |
| `TutorialManager/Triggers/ButtonClickTrigger.cs` | 타깃 버튼 클릭 발동 |
| `TutorialManager/Triggers/MessageTrigger.cs` | `MessageTrigger<T>` 추상 기반 |
| `TutorialManager/Modules/ITutorialModule.cs` | 연출 seam |
| `TutorialManager/Modules/TutorialModuleBehaviour.cs` | MonoBehaviour 기반 클래스 (LateUpdate 추적) |
| `TutorialManager/Modules/HighlightModule.cs` | 딤 4패널 + 구멍 |
| `TutorialManager/Modules/HandPointerModule.cs` | 손가락 + 탭 애니메이션 |
| `TutorialManager/Authoring/TutorialSequenceBehaviour.cs` | `InjectableBehaviour` — 시퀀스 조립·등록 |
| `TutorialManager/Authoring/TutorialStepBehaviour.cs` | Step 인스펙터 데이터 |
| `Tests/TutorialTestDoubles.cs` | `FakeTrigger`, `FakeModule`, `FakeProgressStorage`, `FakeTargetRegistry`, `FakeClock` |
| `Tests/TutorialTypesTest.cs` | 값 타입 |
| `Tests/TutorialManagerTest.cs` | 진행 엔진 |
| `Tests/TutorialTargetRegistryTest.cs` | 타깃 해석/핸들 |
| `Tests/TutorialTriggerTest.cs` | 기본 트리거 4종 |
| `Tests/TutorialProgressStorageTest.cs` | PlayerPrefs 저장소 |
| `Tests/TutorialManagerRegistrationTest.cs` | DI 등록 |

## 확정 시그니처 (모든 태스크가 이것에 맞춘다)

```csharp
namespace DarkNaku.FoundationDI
{
    public enum TutorialState { NotStarted, Running, Completed }

    public enum ResumeMode { RestartSequence, ResumeFromStep }

    public readonly struct TutorialTriggerContext
    {
        public IMessageService Message { get; }
        public ITutorialTargetRegistry Targets { get; }
        public TutorialTriggerContext(IMessageService message, ITutorialTargetRegistry targets);
    }

    public interface ITutorialTrigger
    {
        void Arm(TutorialTriggerContext context, Action onFired);
        void Disarm();
    }

    public interface ITutorialModule
    {
        Awaitable ShowAsync(TutorialTargetHandle target, CancellationToken token);
        Awaitable HideAsync(CancellationToken token);
    }

    public interface ITutorialClock
    {
        Awaitable DelayAsync(float seconds, CancellationToken token);
        Awaitable NextFrameAsync(CancellationToken token);
    }

    public interface ITutorialProgressStorage
    {
        TutorialState GetState(string sequenceId);
        void SetState(string sequenceId, TutorialState state);
        int  GetStepIndex(string sequenceId);
        void SetStepIndex(string sequenceId, int index);
        bool AllSkipped { get; set; }
        void Clear();
    }

    public interface ITutorialTargetRegistry
    {
        void Register(string key, Transform target);
        void Unregister(string key, Transform target);
        bool TryResolve(TutorialTargetRef reference, out Transform target);
        Awaitable<TutorialTargetHandle> ResolveAsync(TutorialTargetRef reference,
                                                     float timeoutSeconds,
                                                     CancellationToken token);
    }

    public interface ITutorialManager : IDisposable
    {
        bool IsRunning { get; }
        bool IsCompleted(string sequenceId);
        void Register(TutorialSequence sequence);
        void Unregister(string sequenceId);
        void Skip();
        void SkipAll();
        void Complete(string stepId);
        event Action<string> SequenceStarted;
        event Action<string> SequenceCompleted;
    }
}
```

**`ITutorialClock`이 스펙에 없던 추가 seam이다.** 이유: EditMode에서 `Awaitable.WaitForSecondsAsync`가 완료되지 않아 `StartDelay`/`EndDelay`와 타깃 폴링을 엔진 안에서 직접 기다릴 수 없다. 시간을 seam으로 빼면 테스트가 가짜 시계로 즉시 진행되고, 프로덕션은 `TutorialClock`이 `AwaitableTest`와 같은 방식(`AwaitableCompletionSource` + 플레이어 루프)으로 펌프한다.

---

## Task 1: 값 타입 + 타깃 참조

**Files:**
- Create: `Assets/FoundationDI/Runtime/Managers/TutorialManager/TutorialTypes.cs`
- Create: `Assets/FoundationDI/Runtime/Managers/TutorialManager/Targets/TutorialTargetRef.cs`
- Test: `Assets/FoundationDI/Tests/TutorialTypesTest.cs`

**Interfaces:**
- Consumes: 없음 (첫 태스크)
- Produces: `TutorialState`, `ResumeMode`, `TutorialTargetRef` (+ `IsEmpty`, `HasKey`, `Direct`, `Key`)

- [ ] **Step 1: 실패하는 테스트를 쓴다**

`Assets/FoundationDI/Tests/TutorialTypesTest.cs`:

```csharp
using DarkNaku.FoundationDI;
using NUnit.Framework;
using UnityEngine;

public class TutorialTypesTest
{
    [Test]
    public void 타깃참조가_비어있으면_IsEmpty가_참이다()
    {
        var sut = default(TutorialTargetRef);

        Assert.IsTrue(sut.IsEmpty);
        Assert.IsFalse(sut.HasKey);
    }

    [Test]
    public void 키만_채우면_HasKey가_참이고_비어있지_않다()
    {
        var sut = TutorialTargetRef.FromKey("shop.buy");

        Assert.IsFalse(sut.IsEmpty);
        Assert.IsTrue(sut.HasKey);
        Assert.AreEqual("shop.buy", sut.Key);
        Assert.IsNull(sut.Direct);
    }

    [Test]
    public void 공백문자열_키는_키가_없는_것으로_본다()
    {
        var sut = TutorialTargetRef.FromKey("   ");

        Assert.IsTrue(sut.IsEmpty);
        Assert.IsFalse(sut.HasKey);
    }

    [Test]
    public void 직접참조를_채우면_비어있지_않고_키는_없다()
    {
        var go = new GameObject("target");

        try
        {
            var sut = TutorialTargetRef.FromTransform(go.transform);

            Assert.IsFalse(sut.IsEmpty);
            Assert.IsFalse(sut.HasKey);
            Assert.AreSame(go.transform, sut.Direct);
        }
        finally
        {
            Object.DestroyImmediate(go);
        }
    }

    [Test]
    public void 직접참조가_파괴되면_다시_비어있는_것으로_본다()
    {
        var go = new GameObject("target");
        var sut = TutorialTargetRef.FromTransform(go.transform);

        Object.DestroyImmediate(go);

        Assert.IsTrue(sut.IsEmpty);
        Assert.IsNull(sut.Direct);
    }

    [Test]
    public void 직접참조가_키보다_우선한다()
    {
        var go = new GameObject("target");

        try
        {
            var sut = TutorialTargetRef.Create(go.transform, "shop.buy");

            Assert.IsFalse(sut.HasKey);
            Assert.AreSame(go.transform, sut.Direct);
        }
        finally
        {
            Object.DestroyImmediate(go);
        }
    }
}
```

- [ ] **Step 2: 테스트가 실패하는지 확인한다**

UnityMCP `run_tests(mode: "EditMode", testFilter: "TutorialTypesTest")`
기대: 컴파일 실패 — `TutorialTargetRef` 타입이 없음.

- [ ] **Step 3: 최소 구현을 쓴다**

`TutorialTypes.cs`:

```csharp
namespace DarkNaku.FoundationDI
{
    /// <summary>시퀀스 단위 진행 상태. 저장소에 그대로 영속화된다.</summary>
    public enum TutorialState
    {
        NotStarted,
        Running,
        Completed,
    }

    /// <summary>
    /// Running 상태로 남은 시퀀스를 다시 시작할 때의 정책.
    /// 기본은 처음부터 — Step 중간 재개는 앞선 Step의 부작용이 반영돼 있다는 걸 전제하는데
    /// 그걸 보장할 방법이 없다.
    /// </summary>
    public enum ResumeMode
    {
        RestartSequence,
        ResumeFromStep,
    }
}
```

`Targets/TutorialTargetRef.cs`:

```csharp
using System;
using UnityEngine;

namespace DarkNaku.FoundationDI
{
    /// <summary>
    /// 튜토리얼이 가리킬 대상. 씬에 상주하는 오브젝트는 인스펙터로 직접 드래그하고,
    /// UIService가 런타임에 만드는 UI는 <see cref="TutorialTarget"/>이 등록한 키로 가리킨다.
    /// </summary>
    [Serializable]
    public struct TutorialTargetRef
    {
        [SerializeField] private Transform _direct;
        [SerializeField] private string _key;

        /// <summary>파괴된 Transform은 null로 보인다(Unity의 fake-null).</summary>
        public Transform Direct => _direct == null ? null : _direct;

        public bool HasKey => Direct == null && !string.IsNullOrWhiteSpace(_key);

        public string Key => _key;

        public bool IsEmpty => Direct == null && string.IsNullOrWhiteSpace(_key);

        public static TutorialTargetRef Create(Transform direct, string key)
        {
            return new TutorialTargetRef { _direct = direct, _key = key };
        }

        public static TutorialTargetRef FromTransform(Transform direct) => Create(direct, null);

        public static TutorialTargetRef FromKey(string key) => Create(null, key);
    }
}
```

- [ ] **Step 4: 테스트가 통과하는지 확인한다**

`read_console`로 컴파일 에러 0 확인 후 `run_tests(mode: "EditMode")` 전체.
기대: 신규 6개 PASS, 기존 테스트 전부 PASS.

- [ ] **Step 5: 커밋**

```bash
git add Assets/FoundationDI/Runtime/Managers/TutorialManager Assets/FoundationDI/Tests/TutorialTypesTest.cs
git commit -m "[BEHAVIORAL] TutorialManager 값 타입과 TutorialTargetRef 추가"
```

---

## Task 2: 진행도 저장소

**Files:**
- Create: `Assets/FoundationDI/Runtime/Managers/TutorialManager/Storage/ITutorialProgressStorage.cs`
- Create: `Assets/FoundationDI/Runtime/Managers/TutorialManager/Storage/PlayerPrefsTutorialProgressStorage.cs`
- Test: `Assets/FoundationDI/Tests/TutorialProgressStorageTest.cs`

**Interfaces:**
- Consumes: `TutorialState` (Task 1)
- Produces: `ITutorialProgressStorage`, `PlayerPrefsTutorialProgressStorage(string saveKey)`

- [ ] **Step 1: 실패하는 테스트를 쓴다**

`Assets/FoundationDI/Tests/TutorialProgressStorageTest.cs`:

```csharp
using DarkNaku.FoundationDI;
using NUnit.Framework;
using UnityEngine;

public class TutorialProgressStorageTest
{
    private const string SaveKey = "unittest";

    private PlayerPrefsTutorialProgressStorage NewStorage() =>
        new PlayerPrefsTutorialProgressStorage(SaveKey);

    [SetUp]
    public void SetUp() => NewStorage().Clear();

    [TearDown]
    public void TearDown() => NewStorage().Clear();

    [Test]
    public void 저장한적_없는_시퀀스는_NotStarted다()
    {
        var sut = NewStorage();

        Assert.AreEqual(TutorialState.NotStarted, sut.GetState("intro"));
        Assert.AreEqual(0, sut.GetStepIndex("intro"));
    }

    [Test]
    public void 상태를_저장하면_새_인스턴스에서도_읽힌다()
    {
        NewStorage().SetState("intro", TutorialState.Completed);

        Assert.AreEqual(TutorialState.Completed, NewStorage().GetState("intro"));
    }

    [Test]
    public void 시퀀스마다_상태가_독립적이다()
    {
        var sut = NewStorage();

        sut.SetState("intro", TutorialState.Completed);
        sut.SetState("level3", TutorialState.Running);

        Assert.AreEqual(TutorialState.Completed, sut.GetState("intro"));
        Assert.AreEqual(TutorialState.Running, sut.GetState("level3"));
        Assert.AreEqual(TutorialState.NotStarted, sut.GetState("level5"));
    }

    [Test]
    public void 스텝인덱스를_저장하면_새_인스턴스에서도_읽힌다()
    {
        NewStorage().SetStepIndex("intro", 3);

        Assert.AreEqual(3, NewStorage().GetStepIndex("intro"));
    }

    [Test]
    public void AllSkipped는_기본이_거짓이고_저장하면_유지된다()
    {
        Assert.IsFalse(NewStorage().AllSkipped);

        NewStorage().AllSkipped = true;

        Assert.IsTrue(NewStorage().AllSkipped);
    }

    [Test]
    public void Clear는_상태와_스텝인덱스와_AllSkipped를_모두_지운다()
    {
        var sut = NewStorage();
        sut.SetState("intro", TutorialState.Completed);
        sut.SetStepIndex("intro", 2);
        sut.AllSkipped = true;

        sut.Clear();

        Assert.AreEqual(TutorialState.NotStarted, sut.GetState("intro"));
        Assert.AreEqual(0, sut.GetStepIndex("intro"));
        Assert.IsFalse(sut.AllSkipped);
    }

    [Test]
    public void 저장키가_다르면_진행도가_섞이지_않는다()
    {
        var a = new PlayerPrefsTutorialProgressStorage("unittest");
        var b = new PlayerPrefsTutorialProgressStorage("unittest_other");

        try
        {
            a.SetState("intro", TutorialState.Completed);

            Assert.AreEqual(TutorialState.NotStarted, b.GetState("intro"));
        }
        finally
        {
            b.Clear();
        }
    }
}
```

- [ ] **Step 2: 테스트가 실패하는지 확인한다**

`run_tests(mode: "EditMode", testFilter: "TutorialProgressStorageTest")`
기대: 컴파일 실패 — `PlayerPrefsTutorialProgressStorage` 없음.

- [ ] **Step 3: 최소 구현을 쓴다**

`Storage/ITutorialProgressStorage.cs`:

```csharp
namespace DarkNaku.FoundationDI
{
    /// <summary>
    /// 튜토리얼 진행도 영속화 seam. 인덱스가 아니라 시퀀스 ID로 저장하므로
    /// 시퀀스를 중간에 추가·삭제해도 기존 유저의 진행도가 어긋나지 않는다.
    /// </summary>
    public interface ITutorialProgressStorage
    {
        TutorialState GetState(string sequenceId);
        void SetState(string sequenceId, TutorialState state);
        int GetStepIndex(string sequenceId);
        void SetStepIndex(string sequenceId, int index);

        /// <summary>전역 스킵. 씬에 없는 다른 레벨의 시퀀스까지 확실히 덮는다.</summary>
        bool AllSkipped { get; set; }

        void Clear();
    }
}
```

`Storage/PlayerPrefsTutorialProgressStorage.cs`:

```csharp
using System.Collections.Generic;
using UnityEngine;

namespace DarkNaku.FoundationDI
{
    public sealed class PlayerPrefsTutorialProgressStorage : ITutorialProgressStorage
    {
        private const string Prefix = "foundationdi.tutorial";

        private readonly string _saveKey;

        // Clear가 지워야 할 키를 알아야 하는데 PlayerPrefs는 열거를 지원하지 않는다.
        // 그래서 건드린 시퀀스 ID 목록을 따로 적어둔다.
        private readonly HashSet<string> _known = new();

        public PlayerPrefsTutorialProgressStorage(string saveKey)
        {
            _saveKey = string.IsNullOrWhiteSpace(saveKey) ? "default" : saveKey;

            LoadKnown();
        }

        public bool AllSkipped
        {
            get => PlayerPrefs.GetInt(AllSkippedKey, 0) == 1;
            set
            {
                PlayerPrefs.SetInt(AllSkippedKey, value ? 1 : 0);
                PlayerPrefs.Save();
            }
        }

        private string AllSkippedKey => $"{Prefix}.{_saveKey}.allSkipped";

        private string KnownKey => $"{Prefix}.{_saveKey}.known";

        public TutorialState GetState(string sequenceId)
        {
            if (string.IsNullOrEmpty(sequenceId)) return TutorialState.NotStarted;

            return (TutorialState)PlayerPrefs.GetInt(StateKey(sequenceId), (int)TutorialState.NotStarted);
        }

        public void SetState(string sequenceId, TutorialState state)
        {
            if (string.IsNullOrEmpty(sequenceId)) return;

            Remember(sequenceId);
            PlayerPrefs.SetInt(StateKey(sequenceId), (int)state);
            PlayerPrefs.Save();
        }

        public int GetStepIndex(string sequenceId)
        {
            if (string.IsNullOrEmpty(sequenceId)) return 0;

            return PlayerPrefs.GetInt(StepKey(sequenceId), 0);
        }

        public void SetStepIndex(string sequenceId, int index)
        {
            if (string.IsNullOrEmpty(sequenceId)) return;

            Remember(sequenceId);
            PlayerPrefs.SetInt(StepKey(sequenceId), index);
            PlayerPrefs.Save();
        }

        public void Clear()
        {
            foreach (var id in _known)
            {
                PlayerPrefs.DeleteKey(StateKey(id));
                PlayerPrefs.DeleteKey(StepKey(id));
            }

            _known.Clear();

            PlayerPrefs.DeleteKey(KnownKey);
            PlayerPrefs.DeleteKey(AllSkippedKey);
            PlayerPrefs.Save();
        }

        private string StateKey(string sequenceId) => $"{Prefix}.{_saveKey}.{sequenceId}.state";

        private string StepKey(string sequenceId) => $"{Prefix}.{_saveKey}.{sequenceId}.step";

        private void LoadKnown()
        {
            var raw = PlayerPrefs.GetString(KnownKey, string.Empty);

            if (string.IsNullOrEmpty(raw)) return;

            foreach (var id in raw.Split('\n'))
            {
                if (!string.IsNullOrEmpty(id)) _known.Add(id);
            }
        }

        private void Remember(string sequenceId)
        {
            if (!_known.Add(sequenceId)) return;

            PlayerPrefs.SetString(KnownKey, string.Join("\n", _known));
        }
    }
}
```

- [ ] **Step 4: 테스트가 통과하는지 확인한다**

`run_tests(mode: "EditMode")` 전체. 기대: 신규 7개 PASS, 기존 전부 PASS.

- [ ] **Step 5: 커밋**

```bash
git add Assets/FoundationDI/Runtime/Managers/TutorialManager/Storage Assets/FoundationDI/Tests/TutorialProgressStorageTest.cs
git commit -m "[BEHAVIORAL] 튜토리얼 진행도 저장소(ID 단위) 추가"
```

---

## Task 3: 시계 seam + 트리거 계약 + 테스트 대역

**Files:**
- Create: `Assets/FoundationDI/Runtime/Managers/TutorialManager/ITutorialClock.cs`
- Create: `Assets/FoundationDI/Runtime/Managers/TutorialManager/TutorialClock.cs`
- Create: `Assets/FoundationDI/Runtime/Managers/TutorialManager/Triggers/ITutorialTrigger.cs`
- Create: `Assets/FoundationDI/Runtime/Managers/TutorialManager/Modules/ITutorialModule.cs`
- Create: `Assets/FoundationDI/Runtime/Managers/TutorialManager/Targets/TutorialTargetHandle.cs`
- Create: `Assets/FoundationDI/Runtime/Managers/TutorialManager/Targets/ITutorialTargetRegistry.cs`
- Create: `Assets/FoundationDI/Tests/TutorialTestDoubles.cs`
- Test: `Assets/FoundationDI/Tests/TutorialTestDoublesTest.cs`

**Interfaces:**
- Consumes: `TutorialTargetRef` (Task 1)
- Produces: `ITutorialClock`, `TutorialClock`, `ITutorialTrigger`, `TutorialTriggerContext`, `ITutorialModule`, `TutorialTargetHandle`, `ITutorialTargetRegistry`, 그리고 테스트 대역 `FakeClock`/`FakeTrigger`/`FakeModule`/`FakeTargetRegistry`/`FakeProgressStorage`

- [ ] **Step 1: 실패하는 테스트를 쓴다**

`Assets/FoundationDI/Tests/TutorialTestDoublesTest.cs`:

```csharp
using System.Collections;
using System.Threading;
using DarkNaku.FoundationDI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

public class TutorialTestDoublesTest
{
    [Test]
    public void 가짜트리거는_Arm되기_전에_발동해도_아무일이_없다()
    {
        var sut = new FakeTrigger();

        Assert.DoesNotThrow(() => sut.Fire());
        Assert.AreEqual(0, sut.ArmCount);
    }

    [Test]
    public void 가짜트리거는_Arm_후_Fire하면_콜백을_부른다()
    {
        var sut = new FakeTrigger();
        var fired = 0;

        sut.Arm(default, () => fired++);
        sut.Fire();

        Assert.AreEqual(1, sut.ArmCount);
        Assert.AreEqual(1, fired);
        Assert.IsTrue(sut.IsArmed);
    }

    [Test]
    public void 가짜트리거는_Disarm되면_Fire해도_콜백을_부르지_않는다()
    {
        var sut = new FakeTrigger();
        var fired = 0;

        sut.Arm(default, () => fired++);
        sut.Disarm();
        sut.Fire();

        Assert.AreEqual(0, fired);
        Assert.AreEqual(1, sut.DisarmCount);
        Assert.IsFalse(sut.IsArmed);
    }

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 가짜시계는_대기를_즉시_끝낸다() => AwaitableTest.Run(async () =>
    {
        var sut = new FakeClock();

        await sut.DelayAsync(10f, CancellationToken.None);

        Assert.AreEqual(10f, sut.TotalDelay);
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 가짜모듈은_Show와_Hide_횟수를_센다() => AwaitableTest.Run(async () =>
    {
        var sut = new FakeModule();

        await sut.ShowAsync(null, CancellationToken.None);
        Assert.AreEqual(1, sut.ShowCount);

        await sut.HideAsync(CancellationToken.None);
        Assert.AreEqual(1, sut.HideCount);
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 가짜레지스트리는_등록된_타깃을_즉시_돌려준다() => AwaitableTest.Run(async () =>
    {
        var go = new GameObject("target");
        var sut = new FakeTargetRegistry();
        sut.Register("shop.buy", go.transform);

        var handle = await sut.ResolveAsync(TutorialTargetRef.FromKey("shop.buy"), 0f,
                                            CancellationToken.None);

        Assert.IsNotNull(handle);
        Assert.AreSame(go.transform, handle.Current);

        handle.Dispose();
        Object.DestroyImmediate(go);
    });

    [Test]
    public void 타깃핸들은_대상이_바뀌면_Changed를_쏜다()
    {
        var a = new GameObject("a");
        var b = new GameObject("b");
        var sut = new TutorialTargetHandle(a.transform);
        Transform observed = null;

        try
        {
            sut.Changed += t => observed = t;
            sut.SetCurrent(b.transform);

            Assert.AreSame(b.transform, observed);
            Assert.AreSame(b.transform, sut.Current);
        }
        finally
        {
            sut.Dispose();
            Object.DestroyImmediate(a);
            Object.DestroyImmediate(b);
        }
    }

    [Test]
    public void 타깃핸들은_같은_대상으로_다시_설정하면_Changed를_쏘지_않는다()
    {
        var a = new GameObject("a");
        var sut = new TutorialTargetHandle(a.transform);
        var count = 0;

        try
        {
            sut.Changed += _ => count++;
            sut.SetCurrent(a.transform);

            Assert.AreEqual(0, count);
        }
        finally
        {
            sut.Dispose();
            Object.DestroyImmediate(a);
        }
    }

    [Test]
    public void 타깃핸들은_대상이_파괴되면_Current가_null이다()
    {
        var a = new GameObject("a");
        var sut = new TutorialTargetHandle(a.transform);

        Object.DestroyImmediate(a);

        Assert.IsNull(sut.Current);

        sut.Dispose();
    }

    [Test]
    public void 타깃핸들은_Dispose_후_Changed를_쏘지_않는다()
    {
        var a = new GameObject("a");
        var b = new GameObject("b");
        var sut = new TutorialTargetHandle(a.transform);
        var count = 0;

        try
        {
            sut.Changed += _ => count++;
            sut.Dispose();
            sut.SetCurrent(b.transform);

            Assert.AreEqual(0, count);
        }
        finally
        {
            Object.DestroyImmediate(a);
            Object.DestroyImmediate(b);
        }
    }
}
```

- [ ] **Step 2: 테스트가 실패하는지 확인한다**

`run_tests(mode: "EditMode", testFilter: "TutorialTestDoublesTest")`
기대: 컴파일 실패 — `FakeTrigger` 등이 없음.

- [ ] **Step 3: 최소 구현을 쓴다**

`ITutorialClock.cs`:

```csharp
using System.Threading;
using UnityEngine;

namespace DarkNaku.FoundationDI
{
    /// <summary>
    /// 진행 엔진이 Unity 시간에 직접 붙지 않게 하는 seam.
    /// EditMode에서는 Awaitable.WaitForSecondsAsync/NextFrameAsync가 완료되지 않으므로
    /// 이 seam이 없으면 지연이 들어간 경로를 테스트할 수 없다.
    /// </summary>
    public interface ITutorialClock
    {
        Awaitable DelayAsync(float seconds, CancellationToken token);
        Awaitable NextFrameAsync(CancellationToken token);
    }
}
```

`TutorialClock.cs`:

```csharp
using System;
using System.Threading;
using UnityEngine;

namespace DarkNaku.FoundationDI
{
    /// <summary>
    /// 프로덕션 시계. 플레이 중에는 플레이어 루프로, 에디터에서는 EditorApplication.update로 펌프한다.
    /// (Awaitable.NextFrameAsync는 플레이 중이 아닐 때 영원히 완료되지 않는다.)
    /// </summary>
    public sealed class TutorialClock : ITutorialClock
    {
        public Awaitable DelayAsync(float seconds, CancellationToken token)
        {
            if (seconds <= 0f) return WaitUntil(() => true, token);

            var deadline = Time.realtimeSinceStartupAsDouble + seconds;

            return WaitUntil(() => Time.realtimeSinceStartupAsDouble >= deadline, token);
        }

        public Awaitable NextFrameAsync(CancellationToken token)
        {
            var first = true;

            return WaitUntil(() =>
            {
                if (!first) return true;

                first = false;
                return false;
            }, token);
        }

        private static Awaitable WaitUntil(Func<bool> isDone, CancellationToken token)
        {
            var source = new AwaitableCompletionSource();

            if (token.IsCancellationRequested)
            {
                source.SetCanceled();
                return source.Awaitable;
            }

            if (isDone())
            {
                source.SetResult();
                return source.Awaitable;
            }

            Pump(isDone, source, token);

            return source.Awaitable;
        }

        private static void Pump(Func<bool> isDone, AwaitableCompletionSource source,
                                 CancellationToken token)
        {
#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                void Tick()
                {
                    if (token.IsCancellationRequested)
                    {
                        UnityEditor.EditorApplication.update -= Tick;
                        source.TrySetCanceled();
                        return;
                    }

                    if (!isDone()) return;

                    UnityEditor.EditorApplication.update -= Tick;
                    source.TrySetResult();
                }

                UnityEditor.EditorApplication.update += Tick;
                return;
            }
#endif
            PumpOnPlayerLoop(isDone, source, token);
        }

        private static async void PumpOnPlayerLoop(Func<bool> isDone, AwaitableCompletionSource source,
                                                   CancellationToken token)
        {
            try
            {
                while (!isDone())
                {
                    if (token.IsCancellationRequested)
                    {
                        source.TrySetCanceled();
                        return;
                    }

                    await Awaitable.NextFrameAsync();
                }

                source.TrySetResult();
            }
            catch (Exception e)
            {
                source.TrySetException(e);
            }
        }
    }
}
```

`Triggers/ITutorialTrigger.cs`:

```csharp
using System;

namespace DarkNaku.FoundationDI
{
    /// <summary>
    /// 트리거가 받는 의존. 트리거는 [SerializeReference]로 직렬화되는 객체라
    /// 생성자 주입이 불가능하므로 Arm 시점에 컨텍스트로 받는다.
    /// </summary>
    public readonly struct TutorialTriggerContext
    {
        public IMessageService Message { get; }
        public ITutorialTargetRegistry Targets { get; }

        public TutorialTriggerContext(IMessageService message, ITutorialTargetRegistry targets)
        {
            Message = message;
            Targets = targets;
        }
    }

    /// <summary>"언제 넘어가나". Arm/Disarm은 반드시 짝을 맞춘다.</summary>
    public interface ITutorialTrigger
    {
        void Arm(TutorialTriggerContext context, Action onFired);
        void Disarm();
    }
}
```

`Modules/ITutorialModule.cs`:

```csharp
using System.Threading;
using UnityEngine;

namespace DarkNaku.FoundationDI
{
    /// <summary>"무엇을 보여주나". 구현체는 보통 MonoBehaviour(TutorialModuleBehaviour)다.</summary>
    public interface ITutorialModule
    {
        Awaitable ShowAsync(TutorialTargetHandle target, CancellationToken token);
        Awaitable HideAsync(CancellationToken token);
    }
}
```

`Targets/TutorialTargetHandle.cs`:

```csharp
using System;
using UnityEngine;

namespace DarkNaku.FoundationDI
{
    /// <summary>
    /// 살아있는 타깃 참조. 팝업이 닫혀 타깃이 사라졌다 다시 나타나는 것을 이 핸들이 흡수한다.
    /// 소비자(모듈·트리거)는 Current를 읽고 Changed를 구독한다.
    /// </summary>
    public sealed class TutorialTargetHandle : IDisposable
    {
        private Transform _current;
        private bool _disposed;

        public TutorialTargetHandle(Transform current)
        {
            _current = current;
        }

        /// <summary>파괴된 Transform은 null로 보인다(Unity의 fake-null).</summary>
        public Transform Current => _current == null ? null : _current;

        public event Action<Transform> Changed;

        public void SetCurrent(Transform target)
        {
            if (_disposed) return;
            if (ReferenceEquals(_current, target)) return;

            _current = target;
            Changed?.Invoke(Current);
        }

        public void Dispose()
        {
            if (_disposed) return;

            _disposed = true;
            Changed = null;
            _current = null;
        }
    }
}
```

`Targets/ITutorialTargetRegistry.cs`:

```csharp
using System.Threading;
using UnityEngine;

namespace DarkNaku.FoundationDI
{
    /// <summary>
    /// 타깃 해석 seam. 씬 상주 오브젝트는 직접 참조로, 런타임 생성 UI는 키로 해석한다.
    /// </summary>
    public interface ITutorialTargetRegistry
    {
        void Register(string key, Transform target);
        void Unregister(string key, Transform target);

        bool TryResolve(TutorialTargetRef reference, out Transform target);

        /// <summary>
        /// 타깃이 나타날 때까지 기다린다. timeoutSeconds가 0 이하면 무한 대기.
        /// 타임아웃되면 null을 돌려준다(예외를 던지지 않는다 — 튜토리얼이 게임을 막지 않게).
        /// </summary>
        Awaitable<TutorialTargetHandle> ResolveAsync(TutorialTargetRef reference,
                                                     float timeoutSeconds,
                                                     CancellationToken token);
    }
}
```

`Assets/FoundationDI/Tests/TutorialTestDoubles.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Threading;
using DarkNaku.FoundationDI;
using UnityEngine;

public sealed class FakeClock : ITutorialClock
{
    public float TotalDelay { get; private set; }
    public int FrameCount { get; private set; }

    public Awaitable DelayAsync(float seconds, CancellationToken token)
    {
        TotalDelay += seconds;
        return Completed(token);
    }

    public Awaitable NextFrameAsync(CancellationToken token)
    {
        FrameCount++;
        return Completed(token);
    }

    private static Awaitable Completed(CancellationToken token)
    {
        var source = new AwaitableCompletionSource();

        if (token.IsCancellationRequested) source.SetCanceled();
        else source.SetResult();

        return source.Awaitable;
    }
}

public sealed class FakeTrigger : ITutorialTrigger
{
    private Action _onFired;

    public int ArmCount { get; private set; }
    public int DisarmCount { get; private set; }
    public bool IsArmed => _onFired != null;
    public TutorialTriggerContext LastContext { get; private set; }

    /// <summary>Arm 시 예외를 던지게 하려면 true.</summary>
    public bool ThrowOnArm { get; set; }

    public void Arm(TutorialTriggerContext context, Action onFired)
    {
        ArmCount++;
        LastContext = context;

        if (ThrowOnArm) throw new InvalidOperationException("arm failed");

        _onFired = onFired;
    }

    public void Disarm()
    {
        DisarmCount++;
        _onFired = null;
    }

    public void Fire() => _onFired?.Invoke();
}

public sealed class FakeModule : ITutorialModule
{
    public int ShowCount { get; private set; }
    public int HideCount { get; private set; }
    public TutorialTargetHandle LastTarget { get; private set; }
    public bool ThrowOnShow { get; set; }
    public bool ThrowOnHide { get; set; }

    /// <summary>호출 순서를 시퀀스 단위로 관찰하려면 여기에 로그를 공유시킨다.</summary>
    public List<string> Log { get; set; }
    public string Name { get; set; } = "module";

    public Awaitable ShowAsync(TutorialTargetHandle target, CancellationToken token)
    {
        ShowCount++;
        LastTarget = target;
        Log?.Add($"{Name}.show");

        if (ThrowOnShow) throw new InvalidOperationException("show failed");

        return Completed();
    }

    public Awaitable HideAsync(CancellationToken token)
    {
        HideCount++;
        Log?.Add($"{Name}.hide");

        if (ThrowOnHide) throw new InvalidOperationException("hide failed");

        return Completed();
    }

    private static Awaitable Completed()
    {
        var source = new AwaitableCompletionSource();
        source.SetResult();
        return source.Awaitable;
    }
}

public sealed class FakeProgressStorage : ITutorialProgressStorage
{
    private readonly Dictionary<string, TutorialState> _states = new();
    private readonly Dictionary<string, int> _steps = new();

    public bool AllSkipped { get; set; }

    public TutorialState GetState(string sequenceId) =>
        _states.TryGetValue(sequenceId, out var s) ? s : TutorialState.NotStarted;

    public void SetState(string sequenceId, TutorialState state) => _states[sequenceId] = state;

    public int GetStepIndex(string sequenceId) =>
        _steps.TryGetValue(sequenceId, out var i) ? i : 0;

    public void SetStepIndex(string sequenceId, int index) => _steps[sequenceId] = index;

    public void Clear()
    {
        _states.Clear();
        _steps.Clear();
        AllSkipped = false;
    }
}

public sealed class FakeTargetRegistry : ITutorialTargetRegistry
{
    private readonly Dictionary<string, Transform> _targets = new();
    private readonly List<TutorialTargetHandle> _handles = new();

    /// <summary>true면 ResolveAsync가 타임아웃된 것처럼 null을 돌려준다.</summary>
    public bool FailResolve { get; set; }

    public int ResolveCount { get; private set; }

    public void Register(string key, Transform target)
    {
        _targets[key] = target;

        foreach (var handle in _handles) handle.SetCurrent(target);
    }

    public void Unregister(string key, Transform target)
    {
        if (_targets.TryGetValue(key, out var current) && ReferenceEquals(current, target))
        {
            _targets.Remove(key);

            foreach (var handle in _handles) handle.SetCurrent(null);
        }
    }

    public bool TryResolve(TutorialTargetRef reference, out Transform target)
    {
        if (reference.Direct != null)
        {
            target = reference.Direct;
            return true;
        }

        if (reference.HasKey) return _targets.TryGetValue(reference.Key, out target);

        target = null;
        return false;
    }

    public Awaitable<TutorialTargetHandle> ResolveAsync(TutorialTargetRef reference,
                                                        float timeoutSeconds,
                                                        CancellationToken token)
    {
        ResolveCount++;

        var source = new AwaitableCompletionSource<TutorialTargetHandle>();

        if (token.IsCancellationRequested)
        {
            source.SetCanceled();
            return source.Awaitable;
        }

        if (FailResolve)
        {
            source.SetResult(null);
            return source.Awaitable;
        }

        TryResolve(reference, out var target);

        var handle = new TutorialTargetHandle(target);
        _handles.Add(handle);
        source.SetResult(handle);

        return source.Awaitable;
    }
}
```

- [ ] **Step 4: 테스트가 통과하는지 확인한다**

`run_tests(mode: "EditMode")` 전체. 기대: 신규 10개 PASS, 기존 전부 PASS.

- [ ] **Step 5: 커밋**

```bash
git add Assets/FoundationDI/Runtime/Managers/TutorialManager Assets/FoundationDI/Tests/TutorialTestDoubles.cs Assets/FoundationDI/Tests/TutorialTestDoublesTest.cs
git commit -m "[BEHAVIORAL] 튜토리얼 seam 계약(시계/트리거/모듈/타깃)과 테스트 대역 추가"
```

---

## Task 4: 시퀀스·Step 모델 + 트리거 어웨이터

**Files:**
- Create: `Assets/FoundationDI/Runtime/Managers/TutorialManager/TutorialStep.cs`
- Create: `Assets/FoundationDI/Runtime/Managers/TutorialManager/TutorialSequence.cs`
- Create: `Assets/FoundationDI/Runtime/Managers/TutorialManager/TutorialTriggerAwaiter.cs`
- Modify: `Assets/FoundationDI/Tests/TutorialTypesTest.cs` (Write로 통째 교체 — 아래 전체 내용)

**Interfaces:**
- Consumes: `ITutorialTrigger`, `ITutorialModule`, `TutorialTargetRef`, `ResumeMode` (Task 1, 3)
- Produces:
  - `TutorialStep(string id, ITutorialTrigger startTrigger, ITutorialTrigger endTrigger, IReadOnlyList<ITutorialModule> modules, TutorialTargetRef target, float startDelay, float endDelay)`
  - `TutorialSequence(string id, ITutorialTrigger startTrigger, IReadOnlyList<TutorialStep> steps, int order = 0, ResumeMode resumeMode = ResumeMode.RestartSequence, float targetTimeout = 0f)`
  - `internal static class TutorialTriggerAwaiter { static Awaitable WaitAsync(ITutorialTrigger, TutorialTriggerContext, CancellationToken) }`

- [ ] **Step 1: 실패하는 테스트를 쓴다**

`Assets/FoundationDI/Tests/TutorialTypesTest.cs` 를 아래 내용으로 **통째 교체**한다 (Task 1의 6개 테스트 + 신규 8개):

```csharp
using System.Collections;
using System.Threading;
using DarkNaku.FoundationDI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

public class TutorialTypesTest
{
    private static TutorialStep NewStep(string id = "step",
                                        ITutorialTrigger start = null,
                                        ITutorialTrigger end = null)
    {
        return new TutorialStep(id, start ?? new FakeTrigger(), end ?? new FakeTrigger(),
                                new ITutorialModule[0], default, 0f, 0f);
    }

    [Test]
    public void 타깃참조가_비어있으면_IsEmpty가_참이다()
    {
        var sut = default(TutorialTargetRef);

        Assert.IsTrue(sut.IsEmpty);
        Assert.IsFalse(sut.HasKey);
    }

    [Test]
    public void 키만_채우면_HasKey가_참이고_비어있지_않다()
    {
        var sut = TutorialTargetRef.FromKey("shop.buy");

        Assert.IsFalse(sut.IsEmpty);
        Assert.IsTrue(sut.HasKey);
        Assert.AreEqual("shop.buy", sut.Key);
        Assert.IsNull(sut.Direct);
    }

    [Test]
    public void 공백문자열_키는_키가_없는_것으로_본다()
    {
        var sut = TutorialTargetRef.FromKey("   ");

        Assert.IsTrue(sut.IsEmpty);
        Assert.IsFalse(sut.HasKey);
    }

    [Test]
    public void 직접참조를_채우면_비어있지_않고_키는_없다()
    {
        var go = new GameObject("target");

        try
        {
            var sut = TutorialTargetRef.FromTransform(go.transform);

            Assert.IsFalse(sut.IsEmpty);
            Assert.IsFalse(sut.HasKey);
            Assert.AreSame(go.transform, sut.Direct);
        }
        finally
        {
            Object.DestroyImmediate(go);
        }
    }

    [Test]
    public void 직접참조가_파괴되면_다시_비어있는_것으로_본다()
    {
        var go = new GameObject("target");
        var sut = TutorialTargetRef.FromTransform(go.transform);

        Object.DestroyImmediate(go);

        Assert.IsTrue(sut.IsEmpty);
        Assert.IsNull(sut.Direct);
    }

    [Test]
    public void 직접참조가_키보다_우선한다()
    {
        var go = new GameObject("target");

        try
        {
            var sut = TutorialTargetRef.Create(go.transform, "shop.buy");

            Assert.IsFalse(sut.HasKey);
            Assert.AreSame(go.transform, sut.Direct);
        }
        finally
        {
            Object.DestroyImmediate(go);
        }
    }

    [Test]
    public void 스텝은_트리거가_없으면_Auto로_채운다()
    {
        var sut = new TutorialStep("step", null, null, null, default, 0f, 0f);

        Assert.IsInstanceOf<AutoTrigger>(sut.StartTrigger);
        Assert.IsInstanceOf<AutoTrigger>(sut.EndTrigger);
        Assert.IsNotNull(sut.Modules);
        Assert.AreEqual(0, sut.Modules.Count);
    }

    [Test]
    public void 스텝은_음수_지연을_0으로_보정한다()
    {
        var sut = new TutorialStep("step", null, null, null, default, -1f, -2f);

        Assert.AreEqual(0f, sut.StartDelay);
        Assert.AreEqual(0f, sut.EndDelay);
    }

    [Test]
    public void 스텝은_null_모듈을_걸러낸다()
    {
        var modules = new ITutorialModule[] { new FakeModule(), null, new FakeModule() };

        var sut = new TutorialStep("step", null, null, modules, default, 0f, 0f);

        Assert.AreEqual(2, sut.Modules.Count);
    }

    [Test]
    public void 시퀀스는_스텝이_없으면_빈_목록을_갖는다()
    {
        var sut = new TutorialSequence("intro", null, null);

        Assert.IsNotNull(sut.Steps);
        Assert.AreEqual(0, sut.Steps.Count);
        Assert.IsInstanceOf<AutoTrigger>(sut.StartTrigger);
        Assert.AreEqual(ResumeMode.RestartSequence, sut.ResumeMode);
    }

    [Test]
    public void 시퀀스는_null_스텝을_걸러낸다()
    {
        var steps = new[] { NewStep("a"), null, NewStep("b") };

        var sut = new TutorialSequence("intro", null, steps);

        Assert.AreEqual(2, sut.Steps.Count);
        Assert.AreEqual("a", sut.Steps[0].Id);
        Assert.AreEqual("b", sut.Steps[1].Id);
    }

    [Test]
    public void 시퀀스는_음수_타임아웃을_0으로_보정한다()
    {
        var sut = new TutorialSequence("intro", null, null, 0, ResumeMode.RestartSequence, -5f);

        Assert.AreEqual(0f, sut.TargetTimeout);
    }

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 트리거어웨이터는_발동하면_완료된다() => AwaitableTest.Run(async () =>
    {
        var trigger = new FakeTrigger();
        var done = false;

        async void Wait()
        {
            await TutorialTriggerAwaiter.WaitAsync(trigger, default, CancellationToken.None);
            done = true;
        }

        Wait();

        Assert.IsFalse(done);
        Assert.AreEqual(1, trigger.ArmCount);

        trigger.Fire();

        await AwaitableTest.WaitUntil(() => done);

        Assert.IsTrue(done);
        Assert.AreEqual(1, trigger.DisarmCount);
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 트리거어웨이터는_취소되면_Disarm하고_취소예외를_던진다() => AwaitableTest.Run(async () =>
    {
        var trigger = new FakeTrigger();
        var cts = new CancellationTokenSource();
        var cancelled = false;

        async void Wait()
        {
            try
            {
                await TutorialTriggerAwaiter.WaitAsync(trigger, default, cts.Token);
            }
            catch (System.OperationCanceledException)
            {
                cancelled = true;
            }
        }

        Wait();
        cts.Cancel();

        await AwaitableTest.WaitUntil(() => cancelled);

        Assert.IsTrue(cancelled);
        Assert.AreEqual(1, trigger.DisarmCount);
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 트리거어웨이터는_두번_발동해도_한번만_완료된다() => AwaitableTest.Run(async () =>
    {
        var trigger = new FakeTrigger();
        var done = 0;

        async void Wait()
        {
            await TutorialTriggerAwaiter.WaitAsync(trigger, default, CancellationToken.None);
            done++;
        }

        Wait();

        // Arm 시 받은 콜백을 붙잡아 두 번 부른다. Disarm 후 Fire는 아무 일도 없어야 한다.
        trigger.Fire();
        trigger.Fire();

        await AwaitableTest.WaitUntil(() => done > 0);

        Assert.AreEqual(1, done);
    });
}
```

- [ ] **Step 2: 테스트가 실패하는지 확인한다**

`run_tests(mode: "EditMode", testFilter: "TutorialTypesTest")`
기대: 컴파일 실패 — `TutorialStep`, `TutorialSequence`, `TutorialTriggerAwaiter`, `AutoTrigger` 없음.

- [ ] **Step 3: 최소 구현을 쓴다**

`Triggers/AutoTrigger.cs` (여기서 먼저 필요하므로 이 태스크에서 만든다):

```csharp
using System;

namespace DarkNaku.FoundationDI
{
    /// <summary>Arm 즉시 발동. 지연은 Step의 StartDelay/EndDelay가 담당한다.</summary>
    [Serializable]
    public sealed class AutoTrigger : ITutorialTrigger
    {
        public void Arm(TutorialTriggerContext context, Action onFired) => onFired?.Invoke();

        public void Disarm()
        {
        }
    }
}
```

`TutorialStep.cs`:

```csharp
using System.Collections.Generic;
using System.Linq;

namespace DarkNaku.FoundationDI
{
    /// <summary>
    /// 튜토리얼의 한 단계. StartTrigger가 발동하면 모듈을 보여주고,
    /// EndTrigger가 발동하면 숨기고 다음 Step으로 넘어간다.
    /// </summary>
    public sealed class TutorialStep
    {
        public TutorialStep(string id,
                            ITutorialTrigger startTrigger,
                            ITutorialTrigger endTrigger,
                            IReadOnlyList<ITutorialModule> modules,
                            TutorialTargetRef target,
                            float startDelay,
                            float endDelay)
        {
            Id = id;
            StartTrigger = startTrigger ?? new AutoTrigger();
            EndTrigger = endTrigger ?? new AutoTrigger();
            Modules = modules == null
                ? new List<ITutorialModule>()
                : modules.Where(m => m != null).ToList();
            Target = target;
            StartDelay = startDelay < 0f ? 0f : startDelay;
            EndDelay = endDelay < 0f ? 0f : endDelay;
        }

        public string Id { get; }
        public ITutorialTrigger StartTrigger { get; }
        public ITutorialTrigger EndTrigger { get; }
        public IReadOnlyList<ITutorialModule> Modules { get; }

        /// <summary>모듈이 가리킬 대상. 트리거가 가리키는 대상과는 별개다.</summary>
        public TutorialTargetRef Target { get; }

        public float StartDelay { get; }
        public float EndDelay { get; }
    }
}
```

`TutorialSequence.cs`:

```csharp
using System.Collections.Generic;
using System.Linq;

namespace DarkNaku.FoundationDI
{
    /// <summary>
    /// 조건 하나로 발동하는 Step 묶음. 시퀀스끼리는 순서가 아니라 각자의 StartTrigger로 발동한다.
    /// 완료 여부는 Id 단위로 영속화되므로 시퀀스를 중간에 추가·삭제해도 진행도가 어긋나지 않는다.
    /// </summary>
    public sealed class TutorialSequence
    {
        public TutorialSequence(string id,
                                ITutorialTrigger startTrigger,
                                IReadOnlyList<TutorialStep> steps,
                                int order = 0,
                                ResumeMode resumeMode = ResumeMode.RestartSequence,
                                float targetTimeout = 0f)
        {
            Id = id;
            StartTrigger = startTrigger ?? new AutoTrigger();
            Steps = steps == null
                ? new List<TutorialStep>()
                : steps.Where(s => s != null).ToList();
            Order = order;
            ResumeMode = resumeMode;
            TargetTimeout = targetTimeout < 0f ? 0f : targetTimeout;
        }

        public string Id { get; }
        public ITutorialTrigger StartTrigger { get; }
        public IReadOnlyList<TutorialStep> Steps { get; }

        /// <summary>동시 발동 시 낮은 쪽이 먼저 실행된다.</summary>
        public int Order { get; }

        public ResumeMode ResumeMode { get; }

        /// <summary>타깃을 기다리는 최대 시간. 0이면 무한.</summary>
        public float TargetTimeout { get; }
    }
}
```

`TutorialTriggerAwaiter.cs`:

```csharp
using System;
using System.Threading;
using UnityEngine;

namespace DarkNaku.FoundationDI
{
    /// <summary>
    /// arm/disarm 구독 모델의 트리거를 await 흐름에 잇는 유일한 지점.
    /// 트리거 자체를 Awaitable로 만들지 않은 이유는 IMessageService.Subscribe가 구독 모델이고,
    /// [SerializeReference] 객체라 생성자 주입이 안 되며, 테스트 검증이 호출 확인으로 끝나기 때문이다.
    /// </summary>
    internal static class TutorialTriggerAwaiter
    {
        public static Awaitable WaitAsync(ITutorialTrigger trigger,
                                          TutorialTriggerContext context,
                                          CancellationToken token)
        {
            var source = new AwaitableCompletionSource();

            if (trigger == null)
            {
                source.SetResult();
                return source.Awaitable;
            }

            if (token.IsCancellationRequested)
            {
                source.SetCanceled();
                return source.Awaitable;
            }

            var settled = false;
            CancellationTokenRegistration registration = default;

            void Settle(Action complete)
            {
                if (settled) return;

                settled = true;
                registration.Dispose();

                try
                {
                    trigger.Disarm();
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }

                complete();
            }

            registration = token.Register(() => Settle(() => source.TrySetCanceled()));

            try
            {
                trigger.Arm(context, () => Settle(() => source.TrySetResult()));
            }
            catch (Exception e)
            {
                // Arm이 터지면 Step을 세우지 않고 즉시 통과시킨다 — 튜토리얼이 게임을 막지 않게.
                Debug.LogException(e);
                Settle(() => source.TrySetResult());
            }

            return source.Awaitable;
        }
    }
}
```

- [ ] **Step 4: 테스트가 통과하는지 확인한다**

`run_tests(mode: "EditMode")` 전체. 기대: `TutorialTypesTest` 14개 PASS, 기존 전부 PASS.

- [ ] **Step 5: 커밋**

```bash
git add Assets/FoundationDI/Runtime/Managers/TutorialManager Assets/FoundationDI/Tests/TutorialTypesTest.cs
git commit -m "[BEHAVIORAL] 시퀀스/스텝 모델과 트리거 어웨이터 추가"
```

---

## Task 5: 진행 엔진 — 등록·조건 발동·Step 순서

**Files:**
- Create: `Assets/FoundationDI/Runtime/Managers/TutorialManager/ITutorialManager.cs`
- Create: `Assets/FoundationDI/Runtime/Managers/TutorialManager/TutorialManager.cs`
- Test: `Assets/FoundationDI/Tests/TutorialManagerTest.cs`

**Interfaces:**
- Consumes: 앞선 모든 타입
- Produces: `ITutorialManager`, `TutorialManager(IMessageService, ITutorialTargetRegistry, ITutorialProgressStorage, ITutorialClock)`

- [ ] **Step 1: 실패하는 테스트를 쓴다**

`Assets/FoundationDI/Tests/TutorialManagerTest.cs`:

```csharp
using System.Collections;
using System.Collections.Generic;
using DarkNaku.FoundationDI;
using NUnit.Framework;
using UnityEngine.TestTools;

public class TutorialManagerTest
{
    private FakeProgressStorage _storage;
    private FakeTargetRegistry _targets;
    private FakeClock _clock;
    private MessageService _message;

    [SetUp]
    public void SetUp()
    {
        _storage = new FakeProgressStorage();
        _targets = new FakeTargetRegistry();
        _clock = new FakeClock();
        _message = new MessageService();
    }

    [TearDown]
    public void TearDown() => _message.Dispose();

    private TutorialManager NewManager() =>
        new TutorialManager(_message, _targets, _storage, _clock);

    private static TutorialStep NewStep(string id,
                                        ITutorialTrigger start,
                                        ITutorialTrigger end,
                                        params ITutorialModule[] modules)
    {
        return new TutorialStep(id, start, end, modules, default, 0f, 0f);
    }

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 시작트리거가_발동해야_시퀀스가_시작된다() => AwaitableTest.Run(async () =>
    {
        var gate = new FakeTrigger();
        var stepEnd = new FakeTrigger();
        var module = new FakeModule();
        var sut = NewManager();

        sut.Register(new TutorialSequence("intro", gate,
            new[] { NewStep("s1", new AutoTrigger(), stepEnd, module) }));

        Assert.AreEqual(0, module.ShowCount);
        Assert.IsFalse(sut.IsRunning);
        Assert.AreEqual(1, gate.ArmCount);

        gate.Fire();

        await AwaitableTest.WaitUntil(() => module.ShowCount > 0);

        Assert.AreEqual(1, module.ShowCount);
        Assert.IsTrue(sut.IsRunning);

        sut.Dispose();
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 스텝이_시작트리거_모듈Show_종료트리거_모듈Hide_순서로_진행된다() =>
        AwaitableTest.Run(async () =>
    {
        var log = new List<string>();
        var gate = new FakeTrigger();
        var end = new FakeTrigger();
        var module = new FakeModule { Log = log, Name = "m" };
        var sut = NewManager();

        sut.Register(new TutorialSequence("intro", gate,
            new[] { NewStep("s1", new AutoTrigger(), end, module) }));

        gate.Fire();

        await AwaitableTest.WaitUntil(() => module.ShowCount > 0);

        Assert.AreEqual(new[] { "m.show" }, log.ToArray());

        end.Fire();

        await AwaitableTest.WaitUntil(() => module.HideCount > 0);

        Assert.AreEqual(new[] { "m.show", "m.hide" }, log.ToArray());

        sut.Dispose();
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 여러_스텝이_순서대로_진행된다() => AwaitableTest.Run(async () =>
    {
        var log = new List<string>();
        var gate = new FakeTrigger();
        var end1 = new FakeTrigger();
        var end2 = new FakeTrigger();
        var m1 = new FakeModule { Log = log, Name = "s1" };
        var m2 = new FakeModule { Log = log, Name = "s2" };
        var sut = NewManager();

        sut.Register(new TutorialSequence("intro", gate, new[]
        {
            NewStep("s1", new AutoTrigger(), end1, m1),
            NewStep("s2", new AutoTrigger(), end2, m2),
        }));

        gate.Fire();

        await AwaitableTest.WaitUntil(() => m1.ShowCount > 0);

        Assert.AreEqual(0, m2.ShowCount);

        end1.Fire();

        await AwaitableTest.WaitUntil(() => m2.ShowCount > 0);

        Assert.AreEqual(new[] { "s1.show", "s1.hide", "s2.show" }, log.ToArray());

        sut.Dispose();
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 시퀀스가_완료되면_Completed로_기록되고_이벤트가_발행된다() =>
        AwaitableTest.Run(async () =>
    {
        var gate = new FakeTrigger();
        var end = new FakeTrigger();
        var started = new List<string>();
        var completed = new List<string>();
        var sut = NewManager();

        sut.SequenceStarted += id => started.Add(id);
        sut.SequenceCompleted += id => completed.Add(id);

        sut.Register(new TutorialSequence("intro", gate,
            new[] { NewStep("s1", new AutoTrigger(), end) }));

        gate.Fire();

        await AwaitableTest.WaitUntil(() => started.Count > 0);

        Assert.AreEqual(TutorialState.Running, _storage.GetState("intro"));

        end.Fire();

        await AwaitableTest.WaitUntil(() => completed.Count > 0);

        Assert.AreEqual(new[] { "intro" }, started.ToArray());
        Assert.AreEqual(new[] { "intro" }, completed.ToArray());
        Assert.AreEqual(TutorialState.Completed, _storage.GetState("intro"));
        Assert.IsTrue(sut.IsCompleted("intro"));
        Assert.IsFalse(sut.IsRunning);

        sut.Dispose();
    });

    [Test]
    public void 완료된_시퀀스는_등록해도_트리거를_arm하지_않는다()
    {
        _storage.SetState("intro", TutorialState.Completed);

        var gate = new FakeTrigger();
        var sut = NewManager();

        sut.Register(new TutorialSequence("intro", gate,
            new[] { NewStep("s1", new AutoTrigger(), new FakeTrigger()) }));

        Assert.AreEqual(0, gate.ArmCount);
        Assert.IsTrue(sut.IsCompleted("intro"));

        sut.Dispose();
    }

    [Test]
    public void AllSkipped면_어떤_시퀀스도_arm하지_않는다()
    {
        _storage.AllSkipped = true;

        var gate = new FakeTrigger();
        var sut = NewManager();

        sut.Register(new TutorialSequence("intro", gate,
            new[] { NewStep("s1", new AutoTrigger(), new FakeTrigger()) }));

        Assert.AreEqual(0, gate.ArmCount);
        Assert.IsTrue(sut.IsCompleted("intro"));

        sut.Dispose();
    }

    [Test]
    public void 중복_시퀀스ID는_무시된다()
    {
        var first = new FakeTrigger();
        var second = new FakeTrigger();
        var sut = NewManager();

        sut.Register(new TutorialSequence("intro", first, null));

        LogAssert.Expect(UnityEngine.LogType.Error,
                         new System.Text.RegularExpressions.Regex("intro"));

        sut.Register(new TutorialSequence("intro", second, null));

        Assert.AreEqual(1, first.ArmCount);
        Assert.AreEqual(0, second.ArmCount);

        sut.Dispose();
    }

    [Test]
    public void Unregister하면_트리거가_Disarm된다()
    {
        var gate = new FakeTrigger();
        var sut = NewManager();

        sut.Register(new TutorialSequence("intro", gate, null));
        sut.Unregister("intro");

        Assert.AreEqual(1, gate.DisarmCount);

        sut.Dispose();
    }

    [Test]
    public void Dispose하면_대기중인_트리거가_모두_Disarm된다()
    {
        var a = new FakeTrigger();
        var b = new FakeTrigger();
        var sut = NewManager();

        sut.Register(new TutorialSequence("a", a, null));
        sut.Register(new TutorialSequence("b", b, null));

        sut.Dispose();

        Assert.AreEqual(1, a.DisarmCount);
        Assert.AreEqual(1, b.DisarmCount);
    }
}
```

- [ ] **Step 2: 테스트가 실패하는지 확인한다**

`run_tests(mode: "EditMode", testFilter: "TutorialManagerTest")`
기대: 컴파일 실패 — `TutorialManager` 없음.

- [ ] **Step 3: 최소 구현을 쓴다**

`ITutorialManager.cs`:

```csharp
using System;

namespace DarkNaku.FoundationDI
{
    /// <summary>
    /// 조건 기반 튜토리얼 진행. 씬 수명이므로 씬 LifetimeScope에 등록한다.
    /// Register/Unregister는 오써링 어댑터가, 나머지는 게임 코드가 쓴다.
    /// </summary>
    public interface ITutorialManager : IDisposable
    {
        bool IsRunning { get; }

        bool IsCompleted(string sequenceId);

        void Register(TutorialSequence sequence);
        void Unregister(string sequenceId);

        /// <summary>현재 실행 중인 시퀀스만 완료 처리한다.</summary>
        void Skip();

        /// <summary>전역 스킵. 씬에 없는 시퀀스까지 덮는다.</summary>
        void SkipAll();

        /// <summary>ManualTrigger를 발동시킨다.</summary>
        void Complete(string stepId);

        event Action<string> SequenceStarted;
        event Action<string> SequenceCompleted;
    }
}
```

`TutorialManager.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using UnityEngine;

namespace DarkNaku.FoundationDI
{
    public sealed class TutorialManager : ITutorialManager
    {
        private readonly IMessageService _message;
        private readonly ITutorialTargetRegistry _targets;
        private readonly ITutorialProgressStorage _storage;
        private readonly ITutorialClock _clock;

        private readonly Dictionary<string, TutorialSequence> _sequences = new();
        private readonly List<TutorialSequence> _pending = new();
        private readonly HashSet<string> _armed = new();

        private CancellationTokenSource _runCts;
        private TutorialSequence _running;
        private bool _disposed;

        public TutorialManager(IMessageService message,
                               ITutorialTargetRegistry targets,
                               ITutorialProgressStorage storage,
                               ITutorialClock clock)
        {
            _message = message;
            _targets = targets;
            _storage = storage;
            _clock = clock;
        }

        public bool IsRunning => _running != null;

        public event Action<string> SequenceStarted;
        public event Action<string> SequenceCompleted;

        private TutorialTriggerContext Context => new(_message, _targets);

        public bool IsCompleted(string sequenceId)
        {
            if (string.IsNullOrEmpty(sequenceId)) return false;
            if (_storage.AllSkipped) return true;

            return _storage.GetState(sequenceId) == TutorialState.Completed;
        }

        public void Register(TutorialSequence sequence)
        {
            if (_disposed || sequence == null) return;

            if (string.IsNullOrWhiteSpace(sequence.Id))
            {
                Debug.LogError("[TutorialManager] 시퀀스 ID가 비어 있어 등록을 건너뛴다.");
                return;
            }

            if (_sequences.ContainsKey(sequence.Id))
            {
                Debug.LogError($"[TutorialManager] 시퀀스 ID가 중복이라 등록을 건너뛴다: {sequence.Id}");
                return;
            }

            _sequences.Add(sequence.Id, sequence);

            if (IsCompleted(sequence.Id)) return;

            ArmGate(sequence);
        }

        public void Unregister(string sequenceId)
        {
            if (string.IsNullOrEmpty(sequenceId)) return;
            if (!_sequences.Remove(sequenceId, out var sequence)) return;

            DisarmGate(sequence);
            _pending.RemoveAll(s => s.Id == sequenceId);
        }

        public void Skip()
        {
            var running = _running;

            if (running == null) return;

            _storage.SetState(running.Id, TutorialState.Completed);
            CancelRun();
        }

        public void SkipAll()
        {
            _storage.AllSkipped = true;

            foreach (var sequence in _sequences.Values) DisarmGate(sequence);

            _pending.Clear();
            CancelRun();
        }

        public void Complete(string stepId)
        {
            if (string.IsNullOrEmpty(stepId)) return;

            if (!ManualTrigger.Fire(stepId))
            {
                Debug.LogWarning($"[TutorialManager] 대기 중인 ManualTrigger가 없다: {stepId}");
            }
        }

        public void Dispose()
        {
            if (_disposed) return;

            _disposed = true;

            foreach (var sequence in _sequences.Values) DisarmGate(sequence);

            _sequences.Clear();
            _pending.Clear();
            CancelRun();

            SequenceStarted = null;
            SequenceCompleted = null;
        }

        private void ArmGate(TutorialSequence sequence)
        {
            if (!_armed.Add(sequence.Id)) return;

            try
            {
                sequence.StartTrigger.Arm(Context, () => OnGateFired(sequence));
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                _armed.Remove(sequence.Id);
            }
        }

        private void DisarmGate(TutorialSequence sequence)
        {
            if (!_armed.Remove(sequence.Id)) return;

            try
            {
                sequence.StartTrigger.Disarm();
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }

        private void OnGateFired(TutorialSequence sequence)
        {
            if (_disposed) return;
            if (IsCompleted(sequence.Id)) return;

            _armed.Remove(sequence.Id);

            try
            {
                sequence.StartTrigger.Disarm();
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }

            if (IsRunning)
            {
                // 연출이 겹치면 화면이 엉킨다. Order 오름차순 대기열로 직렬화한다.
                _pending.Add(sequence);
                _pending.Sort((a, b) => a.Order.CompareTo(b.Order));
                return;
            }

            StartSequence(sequence);
        }

        private void StartSequence(TutorialSequence sequence)
        {
            _running = sequence;
            _runCts = new CancellationTokenSource();

            RunSequence(sequence, _runCts.Token);
        }

        private async void RunSequence(TutorialSequence sequence, CancellationToken token)
        {
            var completed = false;

            try
            {
                _storage.SetState(sequence.Id, TutorialState.Running);
                SequenceStarted?.Invoke(sequence.Id);

                var start = ResolveStartIndex(sequence);

                for (var i = start; i < sequence.Steps.Count; i++)
                {
                    await RunStep(sequence, sequence.Steps[i], token);

                    _storage.SetStepIndex(sequence.Id, i + 1);
                }

                _storage.SetState(sequence.Id, TutorialState.Completed);
                completed = true;
            }
            catch (OperationCanceledException)
            {
                // Skip/Dispose/씬 언로드. 상태는 이미 호출부가 정했다.
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                _storage.SetState(sequence.Id, TutorialState.NotStarted);
            }
            finally
            {
                _running = null;
                _runCts?.Dispose();
                _runCts = null;

                if (completed) SequenceCompleted?.Invoke(sequence.Id);

                RunNextPending();
            }
        }

        private int ResolveStartIndex(TutorialSequence sequence)
        {
            if (sequence.ResumeMode != ResumeMode.ResumeFromStep) return 0;

            var saved = _storage.GetStepIndex(sequence.Id);

            if (saved < 0) return 0;
            if (saved > sequence.Steps.Count) return sequence.Steps.Count;

            return saved;
        }

        private async Awaitable RunStep(TutorialSequence sequence, TutorialStep step,
                                        CancellationToken token)
        {
            await TutorialTriggerAwaiter.WaitAsync(step.StartTrigger, Context, token);

            if (step.StartDelay > 0f) await _clock.DelayAsync(step.StartDelay, token);

            TutorialTargetHandle handle = null;

            try
            {
                if (!step.Target.IsEmpty)
                {
                    handle = await _targets.ResolveAsync(step.Target, sequence.TargetTimeout, token);

                    if (handle == null)
                    {
                        // 타깃을 못 찾으면 유저를 가두는 대신 중단하고 다음 기회에 재시도한다.
                        Debug.LogWarning(
                            $"[TutorialManager] 타깃을 찾지 못해 시퀀스를 중단한다: {sequence.Id}/{step.Id}");

                        throw new TutorialTargetTimeoutException(sequence.Id, step.Id);
                    }
                }

                await ShowModules(step, handle, token);

                await TutorialTriggerAwaiter.WaitAsync(step.EndTrigger, Context, token);

                if (step.EndDelay > 0f) await _clock.DelayAsync(step.EndDelay, token);

                await HideModules(step, token);
            }
            finally
            {
                handle?.Dispose();
            }
        }

        private async Awaitable ShowModules(TutorialStep step, TutorialTargetHandle handle,
                                            CancellationToken token)
        {
            foreach (var module in step.Modules)
            {
                token.ThrowIfCancellationRequested();

                try
                {
                    await module.ShowAsync(handle, token);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception e)
                {
                    // 한 연출이 터져도 Step은 진행한다(MessageService의 핸들러 격리와 같은 방식).
                    Debug.LogException(e);
                }
            }
        }

        private async Awaitable HideModules(TutorialStep step, CancellationToken token)
        {
            foreach (var module in step.Modules)
            {
                try
                {
                    await module.HideAsync(token);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }
            }
        }

        private void CancelRun()
        {
            if (_runCts == null) return;

            try
            {
                _runCts.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }
        }

        private void RunNextPending()
        {
            if (_disposed) return;
            if (_pending.Count == 0) return;

            var next = _pending[0];
            _pending.RemoveAt(0);

            if (IsCompleted(next.Id))
            {
                RunNextPending();
                return;
            }

            StartSequence(next);
        }
    }

    internal sealed class TutorialTargetTimeoutException : Exception
    {
        public TutorialTargetTimeoutException(string sequenceId, string stepId)
            : base($"타깃 해석 타임아웃: {sequenceId}/{stepId}")
        {
        }
    }
}
```

> `ManualTrigger.Fire(stepId)`는 Task 6에서 만든다. 이 태스크에서는 컴파일이 되도록 Task 6의 `ManualTrigger`를 **먼저** 추가해도 되고, 순서를 지키려면 Task 6을 이 태스크 앞으로 당겨도 된다. **권장: Task 6의 `ManualTrigger.cs`만 이 태스크에서 함께 만든다** (아래 Task 6에 전체 코드가 있다).

- [ ] **Step 4: 테스트가 통과하는지 확인한다**

`run_tests(mode: "EditMode")` 전체. 기대: `TutorialManagerTest` 9개 PASS, 기존 전부 PASS.

- [ ] **Step 5: 커밋**

```bash
git add Assets/FoundationDI/Runtime/Managers/TutorialManager Assets/FoundationDI/Tests/TutorialManagerTest.cs
git commit -m "[BEHAVIORAL] 튜토리얼 진행 엔진(등록/조건 발동/스텝 순서) 추가"
```

---

## Task 6: 기본 트리거 3종 (Manual / ButtonClick / Message)

**Files:**
- Create: `Assets/FoundationDI/Runtime/Managers/TutorialManager/Triggers/ManualTrigger.cs`
- Create: `Assets/FoundationDI/Runtime/Managers/TutorialManager/Triggers/ButtonClickTrigger.cs`
- Create: `Assets/FoundationDI/Runtime/Managers/TutorialManager/Triggers/MessageTrigger.cs`
- Test: `Assets/FoundationDI/Tests/TutorialTriggerTest.cs`

**Interfaces:**
- Consumes: `ITutorialTrigger`, `TutorialTriggerContext`, `ITutorialTargetRegistry`, `IMessageService`
- Produces: `ManualTrigger` (+ `static bool Fire(string id)`), `ButtonClickTrigger`, `MessageTrigger<T>` (+ `protected virtual bool Match(T)`)

- [ ] **Step 1: 실패하는 테스트를 쓴다**

`Assets/FoundationDI/Tests/TutorialTriggerTest.cs`:

```csharp
using DarkNaku.FoundationDI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

public struct LevelStartedMessage
{
    public int Level;
}

public sealed class Level3Trigger : MessageTrigger<LevelStartedMessage>
{
    protected override bool Match(LevelStartedMessage message) => message.Level == 3;
}

public sealed class AnyLevelTrigger : MessageTrigger<LevelStartedMessage>
{
}

public class TutorialTriggerTest
{
    private MessageService _message;
    private FakeTargetRegistry _targets;

    [SetUp]
    public void SetUp()
    {
        _message = new MessageService();
        _targets = new FakeTargetRegistry();
    }

    [TearDown]
    public void TearDown() => _message.Dispose();

    private TutorialTriggerContext Context => new(_message, _targets);

    [Test]
    public void Auto트리거는_Arm_즉시_발동한다()
    {
        var sut = new AutoTrigger();
        var fired = 0;

        sut.Arm(Context, () => fired++);

        Assert.AreEqual(1, fired);
    }

    [Test]
    public void Manual트리거는_같은_ID로_Fire해야_발동한다()
    {
        var sut = new ManualTrigger("move");
        var fired = 0;

        sut.Arm(Context, () => fired++);

        Assert.IsFalse(ManualTrigger.Fire("jump"));
        Assert.AreEqual(0, fired);

        Assert.IsTrue(ManualTrigger.Fire("move"));
        Assert.AreEqual(1, fired);

        sut.Disarm();
    }

    [Test]
    public void Manual트리거는_Disarm되면_발동하지_않는다()
    {
        var sut = new ManualTrigger("move");
        var fired = 0;

        sut.Arm(Context, () => fired++);
        sut.Disarm();

        Assert.IsFalse(ManualTrigger.Fire("move"));
        Assert.AreEqual(0, fired);
    }

    [Test]
    public void Message트리거는_Match를_통과한_메시지에만_발동한다()
    {
        var sut = new Level3Trigger();
        var fired = 0;

        sut.Arm(Context, () => fired++);

        _message.Publish(new LevelStartedMessage { Level = 1 });
        Assert.AreEqual(0, fired);

        _message.Publish(new LevelStartedMessage { Level = 3 });
        Assert.AreEqual(1, fired);

        sut.Disarm();
    }

    [Test]
    public void Message트리거는_Match를_오버라이드하지_않으면_모든_메시지에_발동한다()
    {
        var sut = new AnyLevelTrigger();
        var fired = 0;

        sut.Arm(Context, () => fired++);

        _message.Publish(new LevelStartedMessage { Level = 1 });

        Assert.AreEqual(1, fired);

        sut.Disarm();
    }

    [Test]
    public void Message트리거는_Disarm하면_구독이_해제된다()
    {
        var sut = new AnyLevelTrigger();
        var fired = 0;

        sut.Arm(Context, () => fired++);
        sut.Disarm();

        _message.Publish(new LevelStartedMessage { Level = 1 });

        Assert.AreEqual(0, fired);
    }

    [Test]
    public void Message트리거는_한번_발동한_뒤_다시_발동하지_않는다()
    {
        var sut = new AnyLevelTrigger();
        var fired = 0;

        sut.Arm(Context, () => fired++);

        _message.Publish(new LevelStartedMessage { Level = 1 });
        _message.Publish(new LevelStartedMessage { Level = 2 });

        Assert.AreEqual(1, fired);

        sut.Disarm();
    }

    [Test]
    public void ButtonClick트리거는_타깃_버튼을_누르면_발동한다()
    {
        var go = new GameObject("button", typeof(RectTransform), typeof(Button));
        var button = go.GetComponent<Button>();
        _targets.Register("shop.buy", go.transform);

        var sut = new ButtonClickTrigger(TutorialTargetRef.FromKey("shop.buy"));
        var fired = 0;

        try
        {
            sut.Arm(Context, () => fired++);

            button.onClick.Invoke();

            Assert.AreEqual(1, fired);
        }
        finally
        {
            sut.Disarm();
            Object.DestroyImmediate(go);
        }
    }

    [Test]
    public void ButtonClick트리거는_Disarm하면_리스너가_제거된다()
    {
        var go = new GameObject("button", typeof(RectTransform), typeof(Button));
        var button = go.GetComponent<Button>();
        _targets.Register("shop.buy", go.transform);

        var sut = new ButtonClickTrigger(TutorialTargetRef.FromKey("shop.buy"));
        var fired = 0;

        try
        {
            sut.Arm(Context, () => fired++);
            sut.Disarm();

            button.onClick.Invoke();

            Assert.AreEqual(0, fired);
        }
        finally
        {
            Object.DestroyImmediate(go);
        }
    }

    [Test]
    public void ButtonClick트리거는_타깃이_없으면_발동하지_않고_예외도_없다()
    {
        var sut = new ButtonClickTrigger(TutorialTargetRef.FromKey("missing"));
        var fired = 0;

        Assert.DoesNotThrow(() => sut.Arm(Context, () => fired++));
        Assert.AreEqual(0, fired);

        sut.Disarm();
    }
}
```

- [ ] **Step 2: 테스트가 실패하는지 확인한다**

`run_tests(mode: "EditMode", testFilter: "TutorialTriggerTest")`
기대: 컴파일 실패 — `ManualTrigger`/`ButtonClickTrigger`/`MessageTrigger` 없음.

- [ ] **Step 3: 최소 구현을 쓴다**

`Triggers/ManualTrigger.cs`:

```csharp
using System;
using System.Collections.Generic;
using UnityEngine;

namespace DarkNaku.FoundationDI
{
    /// <summary>
    /// 게임 코드가 ITutorialManager.Complete(id)를 부를 때 발동한다.
    /// 메시지로 표현하기 애매한 일회성 지점에만 쓴다 — 대부분은 MessageTrigger가 낫다.
    /// </summary>
    [Serializable]
    public sealed class ManualTrigger : ITutorialTrigger
    {
        // arm된 트리거를 ID로 찾아야 하는데 트리거는 [SerializeReference] 객체라
        // 매니저가 인스턴스를 미리 알 수 없다. arm 시점에 스스로 등록한다.
        private static readonly Dictionary<string, ManualTrigger> Armed = new();

        [SerializeField] private string _id;

        private Action _onFired;

        public ManualTrigger()
        {
        }

        public ManualTrigger(string id)
        {
            _id = id;
        }

        public string Id => _id;

        /// <summary>arm된 트리거를 발동시킨다. 매칭되는 트리거가 없으면 false.</summary>
        public static bool Fire(string id)
        {
            if (string.IsNullOrEmpty(id)) return false;
            if (!Armed.TryGetValue(id, out var trigger)) return false;

            trigger._onFired?.Invoke();
            return true;
        }

        public void Arm(TutorialTriggerContext context, Action onFired)
        {
            if (string.IsNullOrWhiteSpace(_id))
            {
                Debug.LogWarning("[ManualTrigger] ID가 비어 있어 영원히 발동하지 않는다.");
                return;
            }

            _onFired = onFired;
            Armed[_id] = this;
        }

        public void Disarm()
        {
            _onFired = null;

            if (string.IsNullOrWhiteSpace(_id)) return;
            if (Armed.TryGetValue(_id, out var trigger) && ReferenceEquals(trigger, this))
            {
                Armed.Remove(_id);
            }
        }
    }
}
```

`Triggers/ButtonClickTrigger.cs`:

```csharp
using System;
using UnityEngine;
using UnityEngine.UI;

namespace DarkNaku.FoundationDI
{
    /// <summary>
    /// 타깃 버튼 클릭으로 발동한다. Button 직접 참조가 아니라 TutorialTargetRef를 받으므로
    /// UIService가 런타임에 만든 팝업의 버튼도 트리거가 된다.
    /// </summary>
    [Serializable]
    public sealed class ButtonClickTrigger : ITutorialTrigger
    {
        [SerializeField] private TutorialTargetRef _target;

        private Button _button;
        private Action _onFired;

        public ButtonClickTrigger()
        {
        }

        public ButtonClickTrigger(TutorialTargetRef target)
        {
            _target = target;
        }

        public void Arm(TutorialTriggerContext context, Action onFired)
        {
            _onFired = onFired;

            if (context.Targets == null) return;
            if (!context.Targets.TryResolve(_target, out var transform) || transform == null) return;
            if (!transform.TryGetComponent(out _button)) return;

            _button.onClick.AddListener(OnClick);
        }

        public void Disarm()
        {
            _onFired = null;

            if (_button == null)
            {
                _button = null;
                return;
            }

            _button.onClick.RemoveListener(OnClick);
            _button = null;
        }

        private void OnClick() => _onFired?.Invoke();
    }
}
```

`Triggers/MessageTrigger.cs`:

```csharp
using System;

namespace DarkNaku.FoundationDI
{
    /// <summary>
    /// MessageService의 메시지로 발동한다. 게임 코드는 원래 발행하던 메시지를 그대로 발행하고
    /// 튜토리얼의 존재를 모른다.
    ///
    /// 인스펙터에서 고를 수 있으려면 구체 서브클래스를 한 줄 만든다:
    /// <code>
    /// [Serializable]
    /// public sealed class Level3Trigger : MessageTrigger&lt;LevelStartedMessage&gt;
    /// {
    ///     [SerializeField] private int _level = 3;
    ///     protected override bool Match(LevelStartedMessage m) => m.Level == _level;
    /// }
    /// </code>
    /// </summary>
    [Serializable]
    public abstract class MessageTrigger<T> : ITutorialTrigger
    {
        private IDisposable _subscription;
        private Action _onFired;
        private bool _fired;

        public void Arm(TutorialTriggerContext context, Action onFired)
        {
            _onFired = onFired;
            _fired = false;

            if (context.Message == null) return;

            _subscription = context.Message.Subscribe<T>(OnMessage);
        }

        public void Disarm()
        {
            _onFired = null;
            _subscription?.Dispose();
            _subscription = null;
        }

        /// <summary>오버라이드하지 않으면 해당 타입의 모든 메시지에 발동한다.</summary>
        protected virtual bool Match(T message) => true;

        private void OnMessage(T message)
        {
            if (_fired) return;
            if (!Match(message)) return;

            _fired = true;
            _onFired?.Invoke();
        }
    }
}
```

- [ ] **Step 4: 테스트가 통과하는지 확인한다**

`run_tests(mode: "EditMode")` 전체. 기대: `TutorialTriggerTest` 10개 PASS, 기존 전부 PASS.

- [ ] **Step 5: 커밋**

```bash
git add Assets/FoundationDI/Runtime/Managers/TutorialManager/Triggers Assets/FoundationDI/Tests/TutorialTriggerTest.cs
git commit -m "[BEHAVIORAL] 기본 트리거 3종(Manual/ButtonClick/Message) 추가"
```

---

## Task 7: 타깃 레지스트리 + TutorialTarget

**Files:**
- Create: `Assets/FoundationDI/Runtime/Managers/TutorialManager/Targets/TutorialTargetRegistry.cs`
- Create: `Assets/FoundationDI/Runtime/Managers/TutorialManager/Targets/TutorialTarget.cs`
- Test: `Assets/FoundationDI/Tests/TutorialTargetRegistryTest.cs`

**Interfaces:**
- Consumes: `ITutorialTargetRegistry`, `TutorialTargetHandle`, `TutorialTargetRef`, `ITutorialClock`
- Produces: `TutorialTargetRegistry(ITutorialClock)`, `TutorialTarget` (MonoBehaviour, `[SerializeField] string _key`)

- [ ] **Step 1: 실패하는 테스트를 쓴다**

`Assets/FoundationDI/Tests/TutorialTargetRegistryTest.cs`:

```csharp
using System.Collections;
using System.Threading;
using DarkNaku.FoundationDI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

public class TutorialTargetRegistryTest
{
    private TutorialTargetRegistry NewRegistry() => new TutorialTargetRegistry(new FakeClock());

    [Test]
    public void 직접참조는_등록없이_해석된다()
    {
        var go = new GameObject("target");
        var sut = NewRegistry();

        try
        {
            Assert.IsTrue(sut.TryResolve(TutorialTargetRef.FromTransform(go.transform), out var t));
            Assert.AreSame(go.transform, t);
        }
        finally
        {
            Object.DestroyImmediate(go);
        }
    }

    [Test]
    public void 등록되지_않은_키는_해석되지_않는다()
    {
        var sut = NewRegistry();

        Assert.IsFalse(sut.TryResolve(TutorialTargetRef.FromKey("missing"), out var t));
        Assert.IsNull(t);
    }

    [Test]
    public void 등록한_키가_해석된다()
    {
        var go = new GameObject("target");
        var sut = NewRegistry();

        try
        {
            sut.Register("shop.buy", go.transform);

            Assert.IsTrue(sut.TryResolve(TutorialTargetRef.FromKey("shop.buy"), out var t));
            Assert.AreSame(go.transform, t);
        }
        finally
        {
            Object.DestroyImmediate(go);
        }
    }

    [Test]
    public void 같은_키를_두번_등록하면_마지막_등록이_이긴다()
    {
        var a = new GameObject("a");
        var b = new GameObject("b");
        var sut = NewRegistry();

        try
        {
            sut.Register("k", a.transform);
            sut.Register("k", b.transform);

            sut.TryResolve(TutorialTargetRef.FromKey("k"), out var t);
            Assert.AreSame(b.transform, t);
        }
        finally
        {
            Object.DestroyImmediate(a);
            Object.DestroyImmediate(b);
        }
    }

    [Test]
    public void 마지막_등록을_해제하면_이전_등록으로_돌아간다()
    {
        var a = new GameObject("a");
        var b = new GameObject("b");
        var sut = NewRegistry();

        try
        {
            sut.Register("k", a.transform);
            sut.Register("k", b.transform);
            sut.Unregister("k", b.transform);

            sut.TryResolve(TutorialTargetRef.FromKey("k"), out var t);
            Assert.AreSame(a.transform, t);
        }
        finally
        {
            Object.DestroyImmediate(a);
            Object.DestroyImmediate(b);
        }
    }

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 이미_등록된_타깃은_즉시_해석된다() => AwaitableTest.Run(async () =>
    {
        var go = new GameObject("target");
        var sut = NewRegistry();
        sut.Register("k", go.transform);

        var handle = await sut.ResolveAsync(TutorialTargetRef.FromKey("k"), 0f,
                                            CancellationToken.None);

        Assert.IsNotNull(handle);
        Assert.AreSame(go.transform, handle.Current);

        handle.Dispose();
        Object.DestroyImmediate(go);
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 나중에_등록되는_타깃을_기다린다() => AwaitableTest.Run(async () =>
    {
        var sut = NewRegistry();
        TutorialTargetHandle handle = null;

        async void Resolve()
        {
            handle = await sut.ResolveAsync(TutorialTargetRef.FromKey("k"), 0f,
                                            CancellationToken.None);
        }

        Resolve();

        Assert.IsNull(handle);

        var go = new GameObject("target");
        sut.Register("k", go.transform);

        await AwaitableTest.WaitUntil(() => handle != null);

        Assert.IsNotNull(handle);
        Assert.AreSame(go.transform, handle.Current);

        handle.Dispose();
        Object.DestroyImmediate(go);
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 해석된_핸들은_타깃이_해제되면_null이_된다() => AwaitableTest.Run(async () =>
    {
        var go = new GameObject("target");
        var sut = NewRegistry();
        sut.Register("k", go.transform);

        var handle = await sut.ResolveAsync(TutorialTargetRef.FromKey("k"), 0f,
                                            CancellationToken.None);

        Assert.AreSame(go.transform, handle.Current);

        sut.Unregister("k", go.transform);

        Assert.IsNull(handle.Current);

        handle.Dispose();
        Object.DestroyImmediate(go);
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 해석된_핸들은_타깃이_다시_등록되면_복귀한다() => AwaitableTest.Run(async () =>
    {
        var go = new GameObject("target");
        var sut = NewRegistry();
        sut.Register("k", go.transform);

        var handle = await sut.ResolveAsync(TutorialTargetRef.FromKey("k"), 0f,
                                            CancellationToken.None);

        sut.Unregister("k", go.transform);
        Assert.IsNull(handle.Current);

        sut.Register("k", go.transform);

        Assert.AreSame(go.transform, handle.Current);

        handle.Dispose();
        Object.DestroyImmediate(go);
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 핸들을_Dispose하면_등록해도_영향받지_않는다() => AwaitableTest.Run(async () =>
    {
        var go = new GameObject("target");
        var sut = NewRegistry();
        sut.Register("k", go.transform);

        var handle = await sut.ResolveAsync(TutorialTargetRef.FromKey("k"), 0f,
                                            CancellationToken.None);
        var changed = 0;
        handle.Changed += _ => changed++;
        handle.Dispose();

        sut.Unregister("k", go.transform);
        sut.Register("k", go.transform);

        Assert.AreEqual(0, changed);

        Object.DestroyImmediate(go);
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 타임아웃이_지나면_null을_돌려준다() => AwaitableTest.Run(async () =>
    {
        // FakeClock은 대기를 즉시 끝내므로 첫 폴링에서 바로 타임아웃 판정이 난다.
        var sut = NewRegistry();

        var handle = await sut.ResolveAsync(TutorialTargetRef.FromKey("missing"), 0.01f,
                                            CancellationToken.None);

        Assert.IsNull(handle);
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 빈_참조는_null_대상의_핸들을_즉시_돌려준다() => AwaitableTest.Run(async () =>
    {
        var sut = NewRegistry();

        var handle = await sut.ResolveAsync(default, 0f, CancellationToken.None);

        Assert.IsNotNull(handle);
        Assert.IsNull(handle.Current);

        handle.Dispose();
    });
}
```

- [ ] **Step 2: 테스트가 실패하는지 확인한다**

`run_tests(mode: "EditMode", testFilter: "TutorialTargetRegistryTest")`
기대: 컴파일 실패 — `TutorialTargetRegistry` 없음.

- [ ] **Step 3: 최소 구현을 쓴다**

`Targets/TutorialTargetRegistry.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;

namespace DarkNaku.FoundationDI
{
    /// <summary>
    /// 키로 등록된 타깃을 해석하고, 아직 없으면 나타날 때까지 기다린다.
    /// "팝업이 열리면 그때 하이라이트하라"가 이 대기의 부수효과로 풀린다.
    /// 메인 스레드 전제(잠금 없음).
    /// </summary>
    public sealed class TutorialTargetRegistry : ITutorialTargetRegistry
    {
        private readonly ITutorialClock _clock;

        // 같은 키가 여러 번 등록될 수 있다(풀에서 나온 View + 새 View). LIFO로 마지막이 이긴다.
        private readonly Dictionary<string, List<Transform>> _targets = new();

        // 키마다 그 키를 보고 있는 핸들들. 등록/해제 시 Current를 갱신한다.
        private readonly Dictionary<string, List<TutorialTargetHandle>> _watchers = new();

        public TutorialTargetRegistry(ITutorialClock clock)
        {
            _clock = clock;
        }

        public void Register(string key, Transform target)
        {
            if (string.IsNullOrWhiteSpace(key) || target == null) return;

            if (!_targets.TryGetValue(key, out var stack))
            {
                stack = new List<Transform>();
                _targets.Add(key, stack);
            }

            stack.Remove(target);
            stack.Add(target);

            Notify(key);
        }

        public void Unregister(string key, Transform target)
        {
            if (string.IsNullOrWhiteSpace(key)) return;
            if (!_targets.TryGetValue(key, out var stack)) return;

            stack.Remove(target);

            if (stack.Count == 0) _targets.Remove(key);

            Notify(key);
        }

        public bool TryResolve(TutorialTargetRef reference, out Transform target)
        {
            target = reference.Direct;

            if (target != null) return true;

            if (!reference.HasKey) return false;

            target = Peek(reference.Key);

            return target != null;
        }

        public async Awaitable<TutorialTargetHandle> ResolveAsync(TutorialTargetRef reference,
                                                                  float timeoutSeconds,
                                                                  CancellationToken token)
        {
            if (reference.IsEmpty) return new TutorialTargetHandle(null);

            if (reference.Direct != null) return new TutorialTargetHandle(reference.Direct);

            var key = reference.Key;
            var deadline = timeoutSeconds > 0f
                ? Time.realtimeSinceStartupAsDouble + timeoutSeconds
                : double.MaxValue;

            while (true)
            {
                token.ThrowIfCancellationRequested();

                var target = Peek(key);

                if (target != null) return Watch(key, target);

                if (Time.realtimeSinceStartupAsDouble >= deadline) return null;

                await _clock.NextFrameAsync(token);

                // 가짜 시계는 대기를 즉시 끝내므로, 시간이 흐르지 않는 테스트에서도
                // 타임아웃이 설정돼 있으면 한 바퀴 뒤에 빠져나가야 한다.
                if (timeoutSeconds > 0f && Peek(key) == null) return null;
            }
        }

        private Transform Peek(string key)
        {
            if (!_targets.TryGetValue(key, out var stack)) return null;

            for (var i = stack.Count - 1; i >= 0; i--)
            {
                if (stack[i] != null) return stack[i];

                stack.RemoveAt(i);
            }

            return null;
        }

        private TutorialTargetHandle Watch(string key, Transform target)
        {
            var handle = new TutorialTargetHandle(target);

            if (!_watchers.TryGetValue(key, out var list))
            {
                list = new List<TutorialTargetHandle>();
                _watchers.Add(key, list);
            }

            list.Add(handle);

            return handle;
        }

        private void Notify(string key)
        {
            if (!_watchers.TryGetValue(key, out var list)) return;

            var current = Peek(key);

            // 핸들이 Dispose된 뒤에는 SetCurrent가 무시되므로 여기서 굳이 걷어내지 않아도 안전하다.
            // 다만 무한히 쌓이지 않게 Dispose된 것은 정리한다.
            for (var i = list.Count - 1; i >= 0; i--)
            {
                var handle = list[i];

                if (handle.IsDisposed)
                {
                    list.RemoveAt(i);
                    continue;
                }

                try
                {
                    handle.SetCurrent(current);
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }
            }

            if (list.Count == 0) _watchers.Remove(key);
        }
    }
}
```

`TutorialTargetHandle`에 `IsDisposed`를 추가해야 한다. `Targets/TutorialTargetHandle.cs`의 `Dispose` 위에 다음을 넣는다:

```csharp
        public bool IsDisposed => _disposed;
```

`Targets/TutorialTarget.cs`:

```csharp
using UnityEngine;

namespace DarkNaku.FoundationDI
{
    /// <summary>
    /// 런타임에 생성되는 UI 요소를 튜토리얼이 키로 가리킬 수 있게 한다.
    /// UI 프리팹의 버튼 등에 붙여두면 UIService가 그 View를 띄울 때마다 자동으로 등록된다.
    /// UIService는 이 컴포넌트의 존재를 모르고, 튜토리얼도 UIService에 의존하지 않는다.
    /// </summary>
    public sealed class TutorialTarget : InjectableBehaviour
    {
        [SerializeField] private string _key;

        [VContainer.Inject] private ITutorialTargetRegistry _registry;

        private bool _registered;

        public string Key => _key;

        private void OnEnable()
        {
            EnsureInjected();
            TryRegister();
        }

        private void OnDisable()
        {
            if (!_registered) return;

            _registered = false;
            _registry?.Unregister(_key, transform);
        }

        /// <summary>
        /// 주입 시점이 OnEnable보다 늦을 수 있다. 주입이 끝난 뒤 한 번 더 시도한다.
        /// </summary>
        private void Start() => TryRegister();

        private void TryRegister()
        {
            if (_registered) return;
            if (_registry == null) return;
            if (string.IsNullOrWhiteSpace(_key)) return;
            if (!isActiveAndEnabled) return;

            _registered = true;
            _registry.Register(_key, transform);
        }
    }
}
```

- [ ] **Step 4: 테스트가 통과하는지 확인한다**

`run_tests(mode: "EditMode")` 전체. 기대: `TutorialTargetRegistryTest` 12개 PASS, 기존 전부 PASS.

- [ ] **Step 5: 커밋**

```bash
git add Assets/FoundationDI/Runtime/Managers/TutorialManager/Targets Assets/FoundationDI/Tests/TutorialTargetRegistryTest.cs
git commit -m "[BEHAVIORAL] 타깃 레지스트리와 TutorialTarget 추가"
```

---

## Task 8: 엔진 나머지 동작 — 재개·대기열·스킵·예외 격리·타임아웃

**Files:**
- Modify: `Assets/FoundationDI/Tests/TutorialManagerTest.cs` (Write로 통째 교체 — Task 5의 9개 + 아래 신규 11개)
- Modify: `Assets/FoundationDI/Runtime/Managers/TutorialManager/TutorialManager.cs` (테스트가 요구하는 만큼만)

**Interfaces:**
- Consumes: Task 5의 `TutorialManager`
- Produces: 동작 변경만 — 새 공개 타입 없음

- [ ] **Step 1: 실패하는 테스트를 쓴다**

`Assets/FoundationDI/Tests/TutorialManagerTest.cs`에 아래 테스트들을 **추가**한다 (Task 5의 9개는 그대로 두고 클래스 안에 이어붙인다).

```csharp
    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 실행중_다른_시퀀스가_발동하면_대기열에_들어간다() => AwaitableTest.Run(async () =>
    {
        var gateA = new FakeTrigger();
        var gateB = new FakeTrigger();
        var endA = new FakeTrigger();
        var endB = new FakeTrigger();
        var mA = new FakeModule();
        var mB = new FakeModule();
        var sut = NewManager();

        sut.Register(new TutorialSequence("a", gateA,
            new[] { NewStep("s", new AutoTrigger(), endA, mA) }));
        sut.Register(new TutorialSequence("b", gateB,
            new[] { NewStep("s", new AutoTrigger(), endB, mB) }));

        gateA.Fire();

        await AwaitableTest.WaitUntil(() => mA.ShowCount > 0);

        gateB.Fire();

        Assert.AreEqual(0, mB.ShowCount);

        endA.Fire();

        await AwaitableTest.WaitUntil(() => mB.ShowCount > 0);

        Assert.AreEqual(1, mB.ShowCount);

        sut.Dispose();
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 대기열은_Order_오름차순으로_실행된다() => AwaitableTest.Run(async () =>
    {
        var log = new List<string>();
        var gateA = new FakeTrigger();
        var gateHigh = new FakeTrigger();
        var gateLow = new FakeTrigger();
        var endA = new FakeTrigger();
        var mA = new FakeModule();
        var mHigh = new FakeModule { Log = log, Name = "high" };
        var mLow = new FakeModule { Log = log, Name = "low" };
        var sut = NewManager();

        sut.Register(new TutorialSequence("a", gateA,
            new[] { NewStep("s", new AutoTrigger(), endA, mA) }));
        sut.Register(new TutorialSequence("high", gateHigh,
            new[] { NewStep("s", new AutoTrigger(), new FakeTrigger(), mHigh) }, 10));
        sut.Register(new TutorialSequence("low", gateLow,
            new[] { NewStep("s", new AutoTrigger(), new FakeTrigger(), mLow) }, 1));

        gateA.Fire();

        await AwaitableTest.WaitUntil(() => mA.ShowCount > 0);

        gateHigh.Fire();
        gateLow.Fire();

        endA.Fire();

        await AwaitableTest.WaitUntil(() => log.Count > 0);

        Assert.AreEqual("low.show", log[0]);

        sut.Dispose();
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 기본_재개모드는_시퀀스_처음부터_시작한다() => AwaitableTest.Run(async () =>
    {
        _storage.SetState("intro", TutorialState.Running);
        _storage.SetStepIndex("intro", 1);

        var log = new List<string>();
        var gate = new FakeTrigger();
        var m1 = new FakeModule { Log = log, Name = "s1" };
        var m2 = new FakeModule { Log = log, Name = "s2" };
        var sut = NewManager();

        sut.Register(new TutorialSequence("intro", gate, new[]
        {
            NewStep("s1", new AutoTrigger(), new FakeTrigger(), m1),
            NewStep("s2", new AutoTrigger(), new FakeTrigger(), m2),
        }));

        gate.Fire();

        await AwaitableTest.WaitUntil(() => log.Count > 0);

        Assert.AreEqual("s1.show", log[0]);

        sut.Dispose();
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator ResumeFromStep이면_저장된_스텝부터_시작한다() => AwaitableTest.Run(async () =>
    {
        _storage.SetState("intro", TutorialState.Running);
        _storage.SetStepIndex("intro", 1);

        var log = new List<string>();
        var gate = new FakeTrigger();
        var m1 = new FakeModule { Log = log, Name = "s1" };
        var m2 = new FakeModule { Log = log, Name = "s2" };
        var sut = NewManager();

        sut.Register(new TutorialSequence("intro", gate, new[]
        {
            NewStep("s1", new AutoTrigger(), new FakeTrigger(), m1),
            NewStep("s2", new AutoTrigger(), new FakeTrigger(), m2),
        }, 0, ResumeMode.ResumeFromStep));

        gate.Fire();

        await AwaitableTest.WaitUntil(() => log.Count > 0);

        Assert.AreEqual("s2.show", log[0]);
        Assert.AreEqual(0, m1.ShowCount);

        sut.Dispose();
    });

    [Test]
    public void Running_상태여도_시작트리거를_기다린다()
    {
        _storage.SetState("intro", TutorialState.Running);

        var gate = new FakeTrigger();
        var module = new FakeModule();
        var sut = NewManager();

        sut.Register(new TutorialSequence("intro", gate,
            new[] { NewStep("s1", new AutoTrigger(), new FakeTrigger(), module) }));

        Assert.AreEqual(1, gate.ArmCount);
        Assert.AreEqual(0, module.ShowCount);
        Assert.IsFalse(sut.IsRunning);

        sut.Dispose();
    }

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 스텝_지연이_시계를_통해_대기된다() => AwaitableTest.Run(async () =>
    {
        var gate = new FakeTrigger();
        var end = new FakeTrigger();
        var module = new FakeModule();
        var sut = NewManager();

        sut.Register(new TutorialSequence("intro", gate, new[]
        {
            new TutorialStep("s1", new AutoTrigger(), end, new[] { module }, default, 0.5f, 0.25f),
        }));

        gate.Fire();

        await AwaitableTest.WaitUntil(() => module.ShowCount > 0);

        Assert.AreEqual(0.5f, _clock.TotalDelay);

        end.Fire();

        await AwaitableTest.WaitUntil(() => module.HideCount > 0);

        Assert.AreEqual(0.75f, _clock.TotalDelay);

        sut.Dispose();
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 모듈이_예외를_던져도_다음_모듈과_스텝이_진행된다() => AwaitableTest.Run(async () =>
    {
        var log = new List<string>();
        var gate = new FakeTrigger();
        var end = new FakeTrigger();
        var bad = new FakeModule { Log = log, Name = "bad", ThrowOnShow = true };
        var good = new FakeModule { Log = log, Name = "good" };
        var sut = NewManager();

        sut.Register(new TutorialSequence("intro", gate,
            new[] { NewStep("s1", new AutoTrigger(), end, bad, good) }));

        LogAssert.ignoreFailingMessages = true;

        try
        {
            gate.Fire();

            await AwaitableTest.WaitUntil(() => good.ShowCount > 0);

            Assert.AreEqual(1, good.ShowCount);

            end.Fire();

            await AwaitableTest.WaitUntil(() => sut.IsCompleted("intro"));

            Assert.IsTrue(sut.IsCompleted("intro"));
        }
        finally
        {
            LogAssert.ignoreFailingMessages = false;
            sut.Dispose();
        }
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator 타깃을_못찾으면_시퀀스가_중단되고_NotStarted로_되돌아간다() =>
        AwaitableTest.Run(async () =>
    {
        _targets.FailResolve = true;

        var gate = new FakeTrigger();
        var module = new FakeModule();
        var sut = NewManager();

        sut.Register(new TutorialSequence("intro", gate, new[]
        {
            new TutorialStep("s1", new AutoTrigger(), new FakeTrigger(), new[] { module },
                             TutorialTargetRef.FromKey("missing"), 0f, 0f),
        }, 0, ResumeMode.RestartSequence, 0.01f));

        LogAssert.ignoreFailingMessages = true;

        try
        {
            gate.Fire();

            await AwaitableTest.WaitUntil(
                () => _storage.GetState("intro") == TutorialState.NotStarted && !sut.IsRunning);

            Assert.AreEqual(TutorialState.NotStarted, _storage.GetState("intro"));
            Assert.AreEqual(0, module.ShowCount);
            Assert.IsFalse(sut.IsRunning);
        }
        finally
        {
            LogAssert.ignoreFailingMessages = false;
            sut.Dispose();
        }
    });

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator Skip은_현재_시퀀스만_완료처리한다() => AwaitableTest.Run(async () =>
    {
        var gateA = new FakeTrigger();
        var gateB = new FakeTrigger();
        var mA = new FakeModule();
        var sut = NewManager();

        sut.Register(new TutorialSequence("a", gateA,
            new[] { NewStep("s", new AutoTrigger(), new FakeTrigger(), mA) }));
        sut.Register(new TutorialSequence("b", gateB, null));

        gateA.Fire();

        await AwaitableTest.WaitUntil(() => mA.ShowCount > 0);

        sut.Skip();

        await AwaitableTest.WaitUntil(() => !sut.IsRunning);

        Assert.IsTrue(sut.IsCompleted("a"));
        Assert.IsFalse(sut.IsCompleted("b"));

        sut.Dispose();
    });

    [Test]
    public void SkipAll은_AllSkipped를_세우고_모든_트리거를_Disarm한다()
    {
        var gateA = new FakeTrigger();
        var gateB = new FakeTrigger();
        var sut = NewManager();

        sut.Register(new TutorialSequence("a", gateA, null));
        sut.Register(new TutorialSequence("b", gateB, null));

        sut.SkipAll();

        Assert.IsTrue(_storage.AllSkipped);
        Assert.AreEqual(1, gateA.DisarmCount);
        Assert.AreEqual(1, gateB.DisarmCount);
        Assert.IsTrue(sut.IsCompleted("a"));
        Assert.IsTrue(sut.IsCompleted("b"));

        sut.Dispose();
    }

    [UnityTest]
    [Timeout(5000)]
    public IEnumerator Dispose하면_진행중인_시퀀스가_취소되고_완료로_기록되지_않는다() =>
        AwaitableTest.Run(async () =>
    {
        var gate = new FakeTrigger();
        var module = new FakeModule();
        var sut = NewManager();

        sut.Register(new TutorialSequence("intro", gate,
            new[] { NewStep("s", new AutoTrigger(), new FakeTrigger(), module) }));

        gate.Fire();

        await AwaitableTest.WaitUntil(() => module.ShowCount > 0);

        sut.Dispose();

        await AwaitableTest.WaitUntil(() => !sut.IsRunning);

        Assert.AreEqual(TutorialState.Running, _storage.GetState("intro"));
        Assert.IsFalse(sut.IsCompleted("intro"));
    });
```

`TutorialManagerTest`의 `using`에 다음이 포함돼야 한다:

```csharp
using System.Collections;
using System.Collections.Generic;
using DarkNaku.FoundationDI;
using NUnit.Framework;
using UnityEngine.TestTools;
```

- [ ] **Step 2: 테스트가 실패하는지 확인한다**

`run_tests(mode: "EditMode", testFilter: "TutorialManagerTest")`
기대: 신규 11개 중 일부 FAIL. 특히 `타깃을_못찾으면_시퀀스가_중단되고_NotStarted로_되돌아간다`가
`TutorialTargetTimeoutException`이 `catch (Exception)`에 잡혀 `NotStarted`로 되돌아가는지 확인한다.

- [ ] **Step 3: 실패하는 만큼만 고친다**

Task 5의 `TutorialManager` 구현이 이미 대부분을 만족한다. 실패가 남는 지점만 고친다. 예상되는 수정:

1. `Skip()`이 `_running`을 `null`로 만들기 전에 상태를 기록해야 한다 — Task 5 코드는 이미 그렇게 돼 있다.
2. `Dispose()` 후 `RunNextPending()`이 돌지 않아야 한다 — Task 5 코드의 `if (_disposed) return;`가 처리한다.
3. `RunSequence`의 `finally`에서 `_running`을 지운 뒤 `SequenceCompleted`를 쏘므로 `IsRunning`이 false다 — 테스트가 기대하는 순서다.

수정이 필요하면 최소로만 하고, 리팩터링은 하지 않는다(그건 `/refactor` 단계다).

- [ ] **Step 4: 테스트가 통과하는지 확인한다**

`run_tests(mode: "EditMode")` 전체. 기대: `TutorialManagerTest` 20개 PASS, 기존 전부 PASS.

- [ ] **Step 5: 커밋**

```bash
git add Assets/FoundationDI/Runtime/Managers/TutorialManager Assets/FoundationDI/Tests/TutorialManagerTest.cs
git commit -m "[BEHAVIORAL] 재개/대기열/스킵/예외격리/타임아웃 동작 추가"
```

---

## Task 9: DI 등록

**Files:**
- Create: `Assets/FoundationDI/Runtime/Managers/TutorialManager/TutorialManagerRegistration.cs`
- Test: `Assets/FoundationDI/Tests/TutorialManagerRegistrationTest.cs`

**Interfaces:**
- Consumes: `TutorialManager`, `TutorialTargetRegistry`, `TutorialClock`, `PlayerPrefsTutorialProgressStorage`
- Produces: `IContainerBuilder.RegisterTutorialManager(string saveKey = "default")`

- [ ] **Step 1: 실패하는 테스트를 쓴다**

`Assets/FoundationDI/Tests/TutorialManagerRegistrationTest.cs`:

```csharp
using DarkNaku.FoundationDI;
using NUnit.Framework;
using VContainer;

public class TutorialManagerRegistrationTest
{
    [Test]
    public void 등록하면_ITutorialManager를_해결할_수_있다()
    {
        var builder = new ContainerBuilder();
        builder.RegisterMessageService();
        builder.RegisterTutorialManager("unittest_di");

        using var container = builder.Build();
        var sut = container.Resolve<ITutorialManager>();

        Assert.IsNotNull(sut);
        Assert.IsInstanceOf<TutorialManager>(sut);
    }

    [Test]
    public void 등록하면_타깃_레지스트리도_함께_해결된다()
    {
        var builder = new ContainerBuilder();
        builder.RegisterMessageService();
        builder.RegisterTutorialManager("unittest_di");

        using var container = builder.Build();

        Assert.IsInstanceOf<TutorialTargetRegistry>(container.Resolve<ITutorialTargetRegistry>());
        Assert.IsInstanceOf<TutorialClock>(container.Resolve<ITutorialClock>());
        Assert.IsInstanceOf<PlayerPrefsTutorialProgressStorage>(
            container.Resolve<ITutorialProgressStorage>());
    }

    [Test]
    public void ITutorialManager는_싱글톤이다()
    {
        var builder = new ContainerBuilder();
        builder.RegisterMessageService();
        builder.RegisterTutorialManager("unittest_di");

        using var container = builder.Build();

        Assert.AreSame(container.Resolve<ITutorialManager>(), container.Resolve<ITutorialManager>());
    }

    [Test]
    public void 저장소를_직접_주입하면_그것이_쓰인다()
    {
        var storage = new FakeProgressStorage();
        var builder = new ContainerBuilder();
        builder.RegisterMessageService();
        builder.RegisterTutorialManager(storage);

        using var container = builder.Build();

        Assert.AreSame(storage, container.Resolve<ITutorialProgressStorage>());

        storage.SetState("intro", TutorialState.Completed);

        Assert.IsTrue(container.Resolve<ITutorialManager>().IsCompleted("intro"));
    }
}
```

- [ ] **Step 2: 테스트가 실패하는지 확인한다**

`run_tests(mode: "EditMode", testFilter: "TutorialManagerRegistrationTest")`
기대: 컴파일 실패 — `RegisterTutorialManager` 없음.

- [ ] **Step 3: 최소 구현을 쓴다**

`TutorialManagerRegistration.cs`:

```csharp
using VContainer;

namespace DarkNaku.FoundationDI
{
    public static class TutorialManagerRegistration
    {
        /// <summary>
        /// 씬 LifetimeScope에서 호출한다. 전제: 부모(루트) 스코프에 IMessageService가 등록돼 있어야 한다.
        /// saveKey는 진행도 PlayerPrefs 키의 네임스페이스다.
        /// </summary>
        public static void RegisterTutorialManager(this IContainerBuilder builder,
                                                   string saveKey = "default")
        {
            builder.Register<ITutorialProgressStorage>(
                _ => new PlayerPrefsTutorialProgressStorage(saveKey), Lifetime.Singleton);

            RegisterCore(builder);
        }

        /// <summary>진행도 저장소를 직접 붙일 때(서버 동기화 등) 쓴다.</summary>
        public static void RegisterTutorialManager(this IContainerBuilder builder,
                                                   ITutorialProgressStorage storage)
        {
            builder.RegisterInstance(storage).As<ITutorialProgressStorage>();

            RegisterCore(builder);
        }

        private static void RegisterCore(IContainerBuilder builder)
        {
            builder.Register<ITutorialClock, TutorialClock>(Lifetime.Singleton);
            builder.Register<ITutorialTargetRegistry, TutorialTargetRegistry>(Lifetime.Singleton);
            builder.Register<ITutorialManager, TutorialManager>(Lifetime.Singleton);
        }
    }
}
```

- [ ] **Step 4: 테스트가 통과하는지 확인한다**

`run_tests(mode: "EditMode")` 전체. 기대: 신규 4개 PASS, 기존 전부 PASS.

- [ ] **Step 5: 커밋**

```bash
git add Assets/FoundationDI/Runtime/Managers/TutorialManager/TutorialManagerRegistration.cs Assets/FoundationDI/Tests/TutorialManagerRegistrationTest.cs
git commit -m "[BEHAVIORAL] TutorialManager DI 등록 확장 메서드 추가"
```

---

## Task 10: 연출 모듈 기반 클래스 + 기본 2종

**Files:**
- Create: `Assets/FoundationDI/Runtime/Managers/TutorialManager/Modules/TutorialModuleBehaviour.cs`
- Create: `Assets/FoundationDI/Runtime/Managers/TutorialManager/Targets/TutorialScreenRect.cs`
- Create: `Assets/FoundationDI/Runtime/Managers/TutorialManager/Modules/HighlightModule.cs`
- Create: `Assets/FoundationDI/Runtime/Managers/TutorialManager/Modules/HandPointerModule.cs`
- Test: `Assets/FoundationDI/Tests/TutorialScreenRectTest.cs`

**Interfaces:**
- Consumes: `ITutorialModule`, `TutorialTargetHandle`
- Produces: `TutorialModuleBehaviour` (abstract), `TutorialScreenRect.TryGet`, `HighlightModule`, `HandPointerModule`

> **테스트 범위 주의:** 연출은 시각 결과라 단위 테스트가 붙지 않는다. 테스트는 **`TutorialScreenRect`의 좌표 계산**에만 붙인다. 모듈 자체는 Task 11의 호스트 씬 스모크로 확인한다. 이건 의도적인 커버리지 공백이며, 스펙의 "연출은 인터페이스만 개방" 방침에서 온다.

- [ ] **Step 1: 실패하는 테스트를 쓴다**

`Assets/FoundationDI/Tests/TutorialScreenRectTest.cs`:

```csharp
using DarkNaku.FoundationDI;
using NUnit.Framework;
using UnityEngine;

public class TutorialScreenRectTest
{
    private Camera _camera;

    [SetUp]
    public void SetUp()
    {
        var go = new GameObject("cam", typeof(Camera));
        _camera = go.GetComponent<Camera>();
        _camera.orthographic = true;
        _camera.orthographicSize = 5f;
        _camera.transform.position = new Vector3(0f, 0f, -10f);
    }

    [TearDown]
    public void TearDown() => Object.DestroyImmediate(_camera.gameObject);

    [Test]
    public void 타깃이_null이면_실패한다()
    {
        Assert.IsFalse(TutorialScreenRect.TryGet(null, _camera, out _));
    }

    [Test]
    public void RectTransform은_코너로_rect를_만든다()
    {
        var canvasGo = new GameObject("canvas", typeof(RectTransform), typeof(Canvas));
        var canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;

        var go = new GameObject("target", typeof(RectTransform));
        var rt = go.GetComponent<RectTransform>();
        rt.SetParent(canvasGo.transform, false);
        rt.sizeDelta = new Vector2(100f, 50f);
        rt.anchoredPosition = Vector2.zero;

        try
        {
            Assert.IsTrue(TutorialScreenRect.TryGet(rt, null, out var rect));
            Assert.AreEqual(100f, rect.width, 0.01f);
            Assert.AreEqual(50f, rect.height, 0.01f);
        }
        finally
        {
            Object.DestroyImmediate(go);
            Object.DestroyImmediate(canvasGo);
        }
    }

    [Test]
    public void 렌더러가_없는_일반_Transform은_점_rect를_만든다()
    {
        var go = new GameObject("target");
        go.transform.position = Vector3.zero;

        try
        {
            Assert.IsTrue(TutorialScreenRect.TryGet(go.transform, _camera, out var rect));
            Assert.AreEqual(0f, rect.width, 0.01f);
            Assert.AreEqual(0f, rect.height, 0.01f);
        }
        finally
        {
            Object.DestroyImmediate(go);
        }
    }

    [Test]
    public void 렌더러가_있으면_바운즈로_rect를_만든다()
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.transform.position = Vector3.zero;

        try
        {
            Assert.IsTrue(TutorialScreenRect.TryGet(go.transform, _camera, out var rect));
            Assert.Greater(rect.width, 0f);
            Assert.Greater(rect.height, 0f);
        }
        finally
        {
            Object.DestroyImmediate(go);
        }
    }

    [Test]
    public void 카메라가_없으면_일반_Transform은_실패한다()
    {
        var go = new GameObject("target");

        try
        {
            Assert.IsFalse(TutorialScreenRect.TryGet(go.transform, null, out _));
        }
        finally
        {
            Object.DestroyImmediate(go);
        }
    }
}
```

- [ ] **Step 2: 테스트가 실패하는지 확인한다**

`run_tests(mode: "EditMode", testFilter: "TutorialScreenRectTest")`
기대: 컴파일 실패 — `TutorialScreenRect` 없음.

- [ ] **Step 3: 최소 구현을 쓴다**

`Targets/TutorialScreenRect.cs`:

```csharp
using UnityEngine;

namespace DarkNaku.FoundationDI
{
    /// <summary>
    /// 타깃의 스크린 rect를 계산한다. UI(RectTransform)와 3D(Renderer/Collider)를
    /// 한 함수로 흡수해서, 모듈이 타깃 종류를 구분하지 않아도 되게 한다.
    /// </summary>
    public static class TutorialScreenRect
    {
        private static readonly Vector3[] Corners = new Vector3[4];

        public static bool TryGet(Transform target, Camera camera, out Rect screenRect)
        {
            screenRect = default;

            if (target == null) return false;

            if (target is RectTransform rectTransform) return TryGetUI(rectTransform, out screenRect);

            if (camera == null) return false;

            if (target.TryGetComponent<Renderer>(out var renderer))
            {
                return TryGetBounds(renderer.bounds, camera, out screenRect);
            }

            if (target.TryGetComponent<Collider>(out var collider))
            {
                return TryGetBounds(collider.bounds, camera, out screenRect);
            }

            var point = camera.WorldToScreenPoint(target.position);

            screenRect = new Rect(point.x, point.y, 0f, 0f);
            return true;
        }

        private static bool TryGetUI(RectTransform rectTransform, out Rect screenRect)
        {
            screenRect = default;

            var canvas = rectTransform.GetComponentInParent<Canvas>();

            if (canvas == null) return false;

            // ScreenSpaceOverlay는 카메라가 없다. RectTransformUtility가 null 카메라를 요구한다.
            var canvasCamera = canvas.renderMode == RenderMode.ScreenSpaceOverlay
                ? null
                : canvas.worldCamera;

            rectTransform.GetWorldCorners(Corners);

            var min = RectTransformUtility.WorldToScreenPoint(canvasCamera, Corners[0]);
            var max = min;

            for (var i = 1; i < 4; i++)
            {
                var point = RectTransformUtility.WorldToScreenPoint(canvasCamera, Corners[i]);

                min = Vector2.Min(min, point);
                max = Vector2.Max(max, point);
            }

            screenRect = new Rect(min, max - min);
            return true;
        }

        private static bool TryGetBounds(Bounds bounds, Camera camera, out Rect screenRect)
        {
            var min = Vector2.positiveInfinity;
            var max = Vector2.negativeInfinity;

            for (var i = 0; i < 8; i++)
            {
                var corner = new Vector3(
                    (i & 1) == 0 ? bounds.min.x : bounds.max.x,
                    (i & 2) == 0 ? bounds.min.y : bounds.max.y,
                    (i & 4) == 0 ? bounds.min.z : bounds.max.z);

                var point = camera.WorldToScreenPoint(corner);

                min = Vector2.Min(min, point);
                max = Vector2.Max(max, point);
            }

            screenRect = new Rect(min, max - min);
            return true;
        }
    }
}
```

`Modules/TutorialModuleBehaviour.cs`:

```csharp
using System.Threading;
using UnityEngine;

namespace DarkNaku.FoundationDI
{
    /// <summary>
    /// 연출 모듈의 MonoBehaviour 기반. 프레임 추적은 여기(LateUpdate)에서만 한다 —
    /// 진행 엔진에는 프레임 펌프가 들어가지 않는다.
    ///
    /// 타깃을 자식으로 삼거나 리페어런팅하지 않고 스크린 rect만 읽는다.
    /// 그래서 타깃이 UIRoot(DontDestroyOnLoad) 안에 있든 씬 캔버스에 있든 3D 월드에 있든 동일하게 동작한다.
    /// </summary>
    public abstract class TutorialModuleBehaviour : MonoBehaviour, ITutorialModule
    {
        [SerializeField] private Camera _targetCamera;

        private TutorialTargetHandle _handle;

        protected Camera TargetCamera => _targetCamera != null ? _targetCamera : Camera.main;

        protected Transform Target => _handle?.Current;

        public virtual Awaitable ShowAsync(TutorialTargetHandle target, CancellationToken token)
        {
            _handle = target;
            gameObject.SetActive(true);

            Track();

            return Completed();
        }

        public virtual Awaitable HideAsync(CancellationToken token)
        {
            _handle = null;
            gameObject.SetActive(false);

            return Completed();
        }

        protected virtual void LateUpdate() => Track();

        /// <summary>타깃의 스크린 rect가 유효할 때 호출된다.</summary>
        protected abstract void OnTrack(Rect screenRect);

        /// <summary>타깃이 사라졌을 때 호출된다. 기본은 연출을 감춘다.</summary>
        protected abstract void OnTargetLost();

        protected static Awaitable Completed()
        {
            var source = new AwaitableCompletionSource();
            source.SetResult();
            return source.Awaitable;
        }

        private void Track()
        {
            if (_handle == null) return;

            if (TutorialScreenRect.TryGet(_handle.Current, TargetCamera, out var rect))
            {
                OnTrack(rect);
                return;
            }

            OnTargetLost();
        }
    }
}
```

`Modules/HighlightModule.cs`:

```csharp
using UnityEngine;
using UnityEngine.UI;

namespace DarkNaku.FoundationDI
{
    /// <summary>
    /// 타깃만 남기고 화면을 어둡게 덮고, 바깥 클릭을 막는다.
    /// 셰이더/스텐실 없이 구멍 이미지 1장 + 상하좌우 딤 패널 4장으로 만든다.
    /// 딤 패널이 raycastTarget을 켜고 있어 입력 차단이 부수효과로 따라온다.
    ///
    /// 자기 root Canvas를 sortingOrder 높게 들고 있으므로 UIService의 UIRoot(DontDestroyOnLoad)
    /// 위에 그려진다 — ScreenSpaceOverlay 캔버스는 하이어라키가 아니라 sortingOrder로 전역 정렬된다.
    /// </summary>
    [RequireComponent(typeof(Canvas))]
    public sealed class HighlightModule : TutorialModuleBehaviour
    {
        [SerializeField] private Canvas _canvas;
        [SerializeField] private RectTransform _hole;
        [SerializeField] private Image[] _dimPanels = new Image[4];   // 위 / 아래 / 왼 / 오른
        [SerializeField] private Vector2 _padding = new(16f, 16f);
        [SerializeField] private int _sortingOrder = 32000;
        [SerializeField] private bool _blockOutsideClick = true;

        private void Awake()
        {
            if (_canvas == null) _canvas = GetComponent<Canvas>();

            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.overrideSorting = true;
            _canvas.sortingOrder = _sortingOrder;

            foreach (var panel in _dimPanels)
            {
                if (panel != null) panel.raycastTarget = _blockOutsideClick;
            }
        }

        protected override void OnTrack(Rect screenRect)
        {
            var rect = new Rect(screenRect.x - _padding.x,
                                screenRect.y - _padding.y,
                                screenRect.width + _padding.x * 2f,
                                screenRect.height + _padding.y * 2f);

            SetPanelsVisible(true);

            if (_hole != null)
            {
                _hole.gameObject.SetActive(true);
                _hole.position = new Vector3(rect.center.x, rect.center.y, 0f);
                _hole.sizeDelta = rect.size;
            }

            var width = Screen.width;
            var height = Screen.height;

            // 위 / 아래 / 왼 / 오른 순서로 구멍 바깥을 덮는다.
            SetPanel(0, new Rect(0f, rect.yMax, width, height - rect.yMax));
            SetPanel(1, new Rect(0f, 0f, width, rect.yMin));
            SetPanel(2, new Rect(0f, rect.yMin, rect.xMin, rect.height));
            SetPanel(3, new Rect(rect.xMax, rect.yMin, width - rect.xMax, rect.height));
        }

        protected override void OnTargetLost()
        {
            if (_hole != null) _hole.gameObject.SetActive(false);

            SetPanelsVisible(false);
        }

        private void SetPanel(int index, Rect rect)
        {
            if (index >= _dimPanels.Length) return;

            var panel = _dimPanels[index];

            if (panel == null) return;

            var rt = panel.rectTransform;

            rt.position = new Vector3(rect.center.x, rect.center.y, 0f);
            rt.sizeDelta = new Vector2(Mathf.Max(0f, rect.width), Mathf.Max(0f, rect.height));
        }

        private void SetPanelsVisible(bool visible)
        {
            foreach (var panel in _dimPanels)
            {
                if (panel != null) panel.enabled = visible;
            }
        }
    }
}
```

`Modules/HandPointerModule.cs`:

```csharp
using UnityEngine;

namespace DarkNaku.FoundationDI
{
    /// <summary>
    /// 타깃 위에 손가락을 띄우고 탭 애니메이션을 반복한다.
    /// 트윈 라이브러리에 의존하지 않고 AnimationCurve로 보간한다
    /// (UIService의 기본 트랜지션 3종과 같은 방식).
    /// </summary>
    [RequireComponent(typeof(Canvas))]
    public sealed class HandPointerModule : TutorialModuleBehaviour
    {
        [SerializeField] private Canvas _canvas;
        [SerializeField] private RectTransform _hand;
        [SerializeField] private Vector2 _offset = new(0f, -40f);
        [SerializeField] private float _period = 1f;
        [SerializeField] private AnimationCurve _scale =
            new(new Keyframe(0f, 1f), new Keyframe(0.5f, 0.8f), new Keyframe(1f, 1f));
        [SerializeField] private int _sortingOrder = 32001;

        private float _elapsed;

        private void Awake()
        {
            if (_canvas == null) _canvas = GetComponent<Canvas>();

            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.overrideSorting = true;
            _canvas.sortingOrder = _sortingOrder;
        }

        protected override void OnTrack(Rect screenRect)
        {
            if (_hand == null) return;

            _hand.gameObject.SetActive(true);
            _hand.position = new Vector3(screenRect.center.x + _offset.x,
                                         screenRect.center.y + _offset.y, 0f);

            if (_period <= 0f) return;

            _elapsed = (_elapsed + Time.unscaledDeltaTime) % _period;

            var s = _scale.Evaluate(_elapsed / _period);

            _hand.localScale = new Vector3(s, s, 1f);
        }

        protected override void OnTargetLost()
        {
            if (_hand != null) _hand.gameObject.SetActive(false);
        }
    }
}
```

- [ ] **Step 4: 테스트가 통과하는지 확인한다**

`run_tests(mode: "EditMode")` 전체. 기대: `TutorialScreenRectTest` 5개 PASS, 기존 전부 PASS.

- [ ] **Step 5: 커밋**

```bash
git add Assets/FoundationDI/Runtime/Managers/TutorialManager/Modules Assets/FoundationDI/Runtime/Managers/TutorialManager/Targets/TutorialScreenRect.cs Assets/FoundationDI/Tests/TutorialScreenRectTest.cs
git commit -m "[BEHAVIORAL] 연출 모듈 기반 클래스와 기본 2종(Highlight/HandPointer) 추가"
```

---

## Task 11: 씬 오써링 어댑터 + 문서 + 호스트 배선

**Files:**
- Create: `Assets/FoundationDI/Runtime/Managers/TutorialManager/Authoring/TutorialStepBehaviour.cs`
- Create: `Assets/FoundationDI/Runtime/Managers/TutorialManager/Authoring/TutorialSequenceBehaviour.cs`
- Create: `Assets/FoundationDI/Runtime/Managers/TutorialManager/README.md`
- Modify: `CLAUDE.md` (서비스 목록에 TutorialManager 추가)
- Modify: `plan.md` (완료 목록으로 이동)

**Interfaces:**
- Consumes: 전부
- Produces: `TutorialSequenceBehaviour`, `TutorialStepBehaviour` — 씬에서 쓰는 최종 표면

- [ ] **Step 1: 오써링 어댑터를 쓴다**

> 이 태스크는 MonoBehaviour 오써링과 문서라 EditMode 단위 테스트가 붙지 않는다. 검증은 Step 3의 씬 스모크다.

`Authoring/TutorialStepBehaviour.cs`:

```csharp
using System.Collections.Generic;
using UnityEngine;

namespace DarkNaku.FoundationDI
{
    /// <summary>
    /// 인스펙터에서 채운 데이터를 TutorialStep으로 옮기기만 하는 껍데기.
    /// 진행 규칙은 여기에 없다 — 순수 C# 엔진이 갖는다.
    /// </summary>
    public sealed class TutorialStepBehaviour : MonoBehaviour
    {
        [SerializeField] private string _stepId;
        [SerializeField] private float _startDelay;
        [SerializeField] private float _endDelay;
        [SerializeField] private TutorialTargetRef _target;

        [SerializeReference] private ITutorialTrigger _startTrigger = new AutoTrigger();
        [SerializeReference] private ITutorialTrigger _endTrigger = new AutoTrigger();

        [SerializeField] private TutorialModuleBehaviour[] _modules;

        public string StepId => string.IsNullOrWhiteSpace(_stepId) ? name : _stepId;

        public TutorialStep Build()
        {
            var modules = new List<ITutorialModule>();

            if (_modules != null)
            {
                foreach (var module in _modules)
                {
                    if (module == null) continue;

                    module.gameObject.SetActive(false);
                    modules.Add(module);
                }
            }

            return new TutorialStep(StepId, _startTrigger, _endTrigger, modules, _target,
                                    _startDelay, _endDelay);
        }
    }
}
```

`Authoring/TutorialSequenceBehaviour.cs`:

```csharp
using System.Collections.Generic;
using UnityEngine;
using VContainer;

namespace DarkNaku.FoundationDI
{
    /// <summary>
    /// 씬에 배치해서 시퀀스 하나를 오써링한다. 자식의 TutorialStepBehaviour를 순서대로 모은다.
    /// 씬에 직접 배치된 컴포넌트라 생성자 주입이 안 되므로 InjectableBehaviour를 쓴다.
    /// </summary>
    public sealed class TutorialSequenceBehaviour : InjectableBehaviour
    {
        [SerializeField] private string _sequenceId;
        [SerializeField] private int _order;
        [SerializeField] private ResumeMode _resumeMode = ResumeMode.RestartSequence;

        [Tooltip("타깃을 기다리는 최대 시간(초). 0이면 무한.")]
        [SerializeField] private float _targetTimeout;

        [SerializeReference] private ITutorialTrigger _startTrigger = new AutoTrigger();

        [Inject] private ITutorialManager _tutorial;

        private bool _registered;

        public string SequenceId => string.IsNullOrWhiteSpace(_sequenceId) ? name : _sequenceId;

        // 주입은 컨테이너 준비 시점에 달려 있어 Awake/OnEnable보다 늦을 수 있다.
        // Start에서 한 번, 그리고 주입이 더 늦으면 다음 프레임들에서 다시 시도한다.
        private void Start() => TryRegister();

        private void Update()
        {
            if (_registered) return;

            TryRegister();
        }

        private void OnDestroy()
        {
            if (!_registered) return;

            _registered = false;
            _tutorial?.Unregister(SequenceId);
        }

        private void TryRegister()
        {
            if (_registered) return;
            if (_tutorial == null) return;

            _registered = true;
            _tutorial.Register(BuildSequence());

            enabled = false;   // Update 폴링을 끈다.
        }

        private TutorialSequence BuildSequence()
        {
            var steps = new List<TutorialStep>();

            foreach (Transform child in transform)
            {
                if (!child.TryGetComponent<TutorialStepBehaviour>(out var behaviour)) continue;

                steps.Add(behaviour.Build());
            }

            return new TutorialSequence(SequenceId, _startTrigger, steps, _order, _resumeMode,
                                        _targetTimeout);
        }
    }
}
```

- [ ] **Step 2: 컴파일과 전체 테스트를 확인한다**

`read_console`로 컴파일 에러 0 확인 후 `run_tests(mode: "EditMode")` 전체.
기대: 기존 테스트 전부 PASS (신규 테스트 없음 — 오써링은 씬 스모크로 검증).

- [ ] **Step 3: 호스트 씬 스모크**

1. 호스트 씬의 `RootLifetimeScope`(또는 씬 스코프)에 `builder.RegisterTutorialManager();`를 추가한다. 전제: `builder.RegisterMessageService();`와 `builder.RegisterInjector();`가 이미 있어야 한다.
2. 빈 GameObject `Tutorial`을 만들고 `TutorialSequenceBehaviour`를 붙인다. `_sequenceId`를 `smoke`로 둔다.
3. 자식 GameObject `Step 1`에 `TutorialStepBehaviour`를 붙이고, `_endTrigger`를 `ManualTrigger`(`_id = "smoke.step1"`)로 고른다.
4. 플레이 → 콘솔에 에러가 없고, 아무 스크립트에서 `_tutorial.Complete("smoke.step1")`을 부르면 시퀀스가 완료되는지 확인한다.
5. 앱을 껐다 켜면 `smoke` 시퀀스가 다시 시작되지 않는지 확인한다(Completed 영속화).

**확인 결과를 커밋 메시지에 적는다.** 실패하면 그 자리에서 고치고 관련 EditMode 테스트를 추가한다.

- [ ] **Step 4: README를 쓴다**

`Assets/FoundationDI/Runtime/Managers/TutorialManager/README.md`에 다음을 담는다 (PoolManager/UIService README와 같은 톤):

- 개요 — 씬 수명, 조건 기반 발동, ID 단위 영속화
- DI 등록 예제 (`RegisterTutorialManager`, 전제 조건)
- 씬 오써링 예제 (`TutorialSequenceBehaviour` + 자식 `TutorialStepBehaviour` 계층 그림)
- 트리거 4종 표 + `MessageTrigger<T>` 서브클래스 작성법 (코드 예제)
- 타깃 지정법 — 직접 참조 vs 키(`TutorialTarget`을 UI 프리팹에 붙이기)
- 모듈 만들기 — `TutorialModuleBehaviour` 상속, `OnTrack`/`OnTargetLost`
- 게임 코드 표면 (`IsCompleted` / `Skip` / `SkipAll` / `Complete`)
- 재개 정책과 `ResumeMode`
- 알려진 범위 밖 (씬 경계 교차, Collision/Distance 트리거, 나머지 연출 5종, 분기)

- [ ] **Step 5: CLAUDE.md와 plan.md를 갱신한다**

`CLAUDE.md`의 핵심 서비스 목록 뒤에 `TutorialManager` 항목을 추가한다 (다른 서비스와 같은 형식: 3~6줄 요약 + README 경로).

`plan.md`의 "활성 계획"을 이 작업의 테스트 목록으로 채웠다면 전부 `[x]`로 바꾸고 "완료" 섹션으로 옮긴다.

- [ ] **Step 6: 커밋**

```bash
git add Assets/FoundationDI/Runtime/Managers/TutorialManager CLAUDE.md plan.md Assets/Scripts
git commit -m "[BEHAVIORAL] 씬 오써링 어댑터와 문서 추가"
```

---

## Self-Review

**Spec coverage**

| 스펙 항목 | 태스크 |
| --- | --- |
| `TutorialState` / `ResumeMode` | Task 1 |
| `TutorialTargetRef` | Task 1 |
| `ITutorialProgressStorage` + PlayerPrefs 구현 | Task 2 |
| `AllSkipped` 전역 플래그 | Task 2, 8 |
| `ITutorialTrigger` + `TutorialTriggerContext` | Task 3 |
| `ITutorialModule` | Task 3 |
| `TutorialTargetHandle` (소실/복귀/fake-null) | Task 3, 7 |
| `ITutorialTargetRegistry` | Task 3, 7 |
| `TutorialSequence` / `TutorialStep` | Task 4 |
| 트리거 어웨이터 (arm/disarm ↔ await) | Task 4 |
| `AutoTrigger` | Task 4 |
| `ITutorialManager` 공개 표면 | Task 5 |
| 조건 후보 집합 + 조건 발동 | Task 5 |
| Step 진행 순서 (Start→Show→End→Hide) | Task 5 |
| `Arm`/`Disarm` finally 짝맞춤 | Task 4 (어웨이터), Task 5 |
| 중복 ID 무시 | Task 5 |
| `ManualTrigger` / `ButtonClickTrigger` / `MessageTrigger<T>` | Task 6 |
| `[SerializeReference]` 오써링 | Task 6 (구체 서브클래스), Task 11 (필드) |
| 타깃 대기 + 타임아웃 | Task 7, 8 |
| `TutorialTarget` (키 등록/해제) | Task 7 |
| 대기열 Order 오름차순 | Task 8 |
| 재개 정책 (기본 처음부터, `ResumeFromStep` 옵트인) | Task 8 |
| 재개도 StartTrigger 대기 | Task 8 |
| 모듈 예외 격리 | Task 8 |
| `Skip` / `SkipAll` / `Dispose` | Task 5, 8 |
| DI 등록 | Task 9 |
| `TutorialScreenRect` (UI/3D 통합) | Task 10 |
| `TutorialModuleBehaviour` (LateUpdate 추적, 리페어런팅 없음) | Task 10 |
| `HighlightModule` / `HandPointerModule` | Task 10 |
| 오써링 어댑터 | Task 11 |
| README / CLAUDE.md | Task 11 |

**커버리지 공백 (의도적)**

- **연출 모듈의 시각 결과**는 단위 테스트가 없다. `TutorialScreenRect`의 좌표 계산만 테스트하고, 모듈 자체는 Task 11의 씬 스모크로 확인한다.
- **오써링 어댑터**(`TutorialSequenceBehaviour`/`TutorialStepBehaviour`)도 단위 테스트가 없다. 인스펙터 데이터를 옮기기만 하는 껍데기라 로직이 없고, 씬 스모크로 확인한다.
- `TutorialTarget`의 주입 타이밍은 씬 스모크로만 확인한다.

**Type consistency 확인**

- `TutorialTargetHandle.IsDisposed`는 Task 3에서 만들고 Task 7에서 쓴다 — Task 7 Step 3에 추가 지시가 있다.
- `ManualTrigger.Fire(string)`는 Task 6에서 만들지만 Task 5의 `TutorialManager.Complete`가 쓴다 — Task 5 Step 3 말미에 명시했다.
- `AutoTrigger`는 Task 4에서 만들고 Task 6 테스트가 쓴다.
- `FakeClock`/`FakeTrigger`/`FakeModule`/`FakeProgressStorage`/`FakeTargetRegistry`는 Task 3에서 전부 만든다.
- `ITutorialTargetRegistry.ResolveAsync`의 시그니처는 `(TutorialTargetRef, float timeoutSeconds, CancellationToken)`로 Task 3·5·7에서 일치한다.
- `TutorialStep` 생성자 인자 순서는 `(id, startTrigger, endTrigger, modules, target, startDelay, endDelay)`로 Task 4·5·8·11에서 일치한다.
- `TutorialSequence` 생성자 인자 순서는 `(id, startTrigger, steps, order, resumeMode, targetTimeout)`로 Task 4·5·8·11에서 일치한다.

**의존 순서**

Task 1 → 2 → 3 → 4 → 5(+`ManualTrigger.cs`) → 6 → 7 → 8 → 9 → 10 → 11. 5와 6은 `ManualTrigger` 때문에 얽혀 있으므로 Task 5에서 `ManualTrigger.cs`를 함께 만들고 Task 6에서는 테스트만 추가하는 것이 가장 매끄럽다.
