# 씬 컴포넌트 DI 인프라 + SoundButton Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 씬 배치 MonoBehaviour에 VContainer 의존성을 주입하는 재사용 인프라(`InjectorService`/`InjectableBehaviour`)와 그 첫 사용처 `SoundButton`(+에디터 키 드롭다운)을 구현한다.

**Architecture:** 컴포넌트가 정적 `InjectorService.Request(this)`로 자신을 등록하면, EntryPoint로 등록된 `InjectorService`가 컨테이너 준비 시 보류분을 flush하거나 준비 후 즉시 주입한다(이벤트 드리븐, 폴링 없음). `InjectableBehaviour` 베이스가 `Awake`에서 멱등 self-request를 캡슐화하고, `SoundButton`이 이를 상속해 `Button.onClick` → `ISoundService.Play(key)`로 잇는다.

**Tech Stack:** Unity 6000.3, VContainer(IObjectResolver/IStartable/EntryPoint), uGUI(Button), UniTask, NSubstitute, Unity Test Framework(EditMode).

## Global Constraints

- 네임스페이스: `DarkNaku.FoundationDI` (런타임), `DarkNaku.FoundationDI.Editor` (에디터).
- 에셋 로딩은 직접 `Resources`/`Addressables` 금지 — `IResourceService` 위임(이 기능엔 직접 로딩 없음).
- 테스트는 EditMode, `FoundationDI.Tests` 어셈블리, NSubstitute로 seam 대체, 테스트 함수명은 한국어.
- 컴파일·테스트는 UnityMCP로만: 스크립트 변경 후 `refresh_unity(compile=request, mode=force)` → `read_console(types=[error])`로 컴파일 확인 → `run_tests(mode=EditMode, assembly_names=[FoundationDI.Tests])` + `get_test_job` 폴링.
- 커밋: STRUCTURAL/BEHAVIORAL 분리, 제목 접두어. 메시지 끝에 `Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>`.
- 모든 테스트 통과 시에만 커밋. `.meta` 파일은 Unity가 생성하며 소스와 함께 커밋한다.

## 파일 구조

- Create: `Assets/FoundationDI/Runtime/DI/InjectorService.cs` — 정적 요청 큐 + EntryPoint 통합.
- Create: `Assets/FoundationDI/Runtime/DI/InjectorVContainerExtensions.cs` — `RegisterInjector` 확장.
- Create: `Assets/FoundationDI/Runtime/DI/InjectableBehaviour.cs` — 씬 컴포넌트 베이스.
- Create: `Assets/FoundationDI/Runtime/Services/SoundService/SoundButton.cs` — 첫 사용처.
- Create: `Assets/FoundationDI/Editor/FoundationDI.Editor.asmdef` — 신규 에디터 어셈블리.
- Create: `Assets/FoundationDI/Editor/SoundButtonEditor.cs` — 키 드롭다운 인스펙터.
- Test: `Assets/FoundationDI/Tests/InjectorServiceTest.cs`, `Assets/FoundationDI/Tests/InjectableBehaviourTest.cs`, `Assets/FoundationDI/Tests/SoundButtonTest.cs`.

참고: `FoundationDI` 런타임 asmdef는 이미 VContainer/uGUI를 참조한다(MessageService가 `IObjectResolver`, UIManager가 `Button` 사용). 기존 `UIManagerFlowTests`가 `Substitute.For<IObjectResolver>()`를 쓰므로 `FoundationDI.Tests`에서 `IObjectResolver` 사용은 추가 참조 없이 가능하다.

---

## Task 1: InjectorService + RegisterInjector

**Files:**
- Create: `Assets/FoundationDI/Runtime/DI/InjectorService.cs`
- Create: `Assets/FoundationDI/Runtime/DI/InjectorVContainerExtensions.cs`
- Test: `Assets/FoundationDI/Tests/InjectorServiceTest.cs`

**Interfaces:**
- Consumes: VContainer `IObjectResolver`, `IStartable`, `IContainerBuilder`.
- Produces:
  - `static void InjectorService.Request(MonoBehaviour target)`
  - `InjectorService(IObjectResolver resolver)` 생성자, `void Start()`(IStartable), `void Dispose()`(IDisposable)
  - `static void IContainerBuilder.RegisterInjector()`

- [ ] **Step 1: 실패 테스트 작성** — `Assets/FoundationDI/Tests/InjectorServiceTest.cs` 생성

```csharp
using System;
using DarkNaku.FoundationDI;
using NSubstitute;
using NUnit.Framework;
using UnityEngine;
using VContainer;

public class InjectorServiceTest
{
    private class DummyBehaviour : MonoBehaviour { }

    private GameObject _go;

    [SetUp]
    public void SetUp()
    {
        // 정적 상태 초기화(이전 테스트 잔재 제거)
        new InjectorService(Substitute.For<IObjectResolver>()).Dispose();
        _go = new GameObject("dummy");
    }

    [TearDown]
    public void TearDown()
    {
        new InjectorService(Substitute.For<IObjectResolver>()).Dispose();
        if (_go != null) Object.DestroyImmediate(_go);
    }

    [Test]
    public void 컨테이너_준비전_Request는_보류되어_즉시_주입되지_않는다()
    {
        var resolver = Substitute.For<IObjectResolver>();
        var mb = _go.AddComponent<DummyBehaviour>();

        InjectorService.Request(mb);

        resolver.DidNotReceive().Inject(mb);
    }

    [Test]
    public void Start로_컨테이너가_바인딩되면_보류분이_주입된다()
    {
        var resolver = Substitute.For<IObjectResolver>();
        var mb = _go.AddComponent<DummyBehaviour>();
        InjectorService.Request(mb);          // 보류 (아직 바인딩 전)

        new InjectorService(resolver).Start(); // 바인딩 + flush

        resolver.Received(1).Inject(mb);
    }

    [Test]
    public void 컨테이너_준비후_Request는_즉시_주입한다()
    {
        var resolver = Substitute.For<IObjectResolver>();
        new InjectorService(resolver).Start(); // 먼저 바인딩
        var mb = _go.AddComponent<DummyBehaviour>();

        InjectorService.Request(mb);

        resolver.Received(1).Inject(mb);
    }

    [Test]
    public void Dispose는_정적상태를_초기화한다()
    {
        var resolver = Substitute.For<IObjectResolver>();
        var sut = new InjectorService(resolver);
        sut.Start();
        sut.Dispose();                         // 바인딩 해제
        var mb = _go.AddComponent<DummyBehaviour>();

        InjectorService.Request(mb);           // 다시 보류로 동작해야 함

        resolver.DidNotReceive().Inject(mb);
    }

    [Test]
    public void Request_null은_예외없이_무시한다()
    {
        Assert.DoesNotThrow(() => InjectorService.Request(null));
    }
}
```

- [ ] **Step 2: 컴파일/실패 확인**

UnityMCP: `refresh_unity(compile=request, mode=force)` → `read_console(types=[error])`. `InjectorService` 미정의로 컴파일 에러 = RED.

- [ ] **Step 3: 최소 구현** — 두 파일 생성

`Assets/FoundationDI/Runtime/DI/InjectorService.cs`:
```csharp
using System;
using System.Collections.Generic;
using UnityEngine;
using VContainer;
using VContainer.Unity;

namespace DarkNaku.FoundationDI
{
    /// <summary>
    /// 씬에 배치된(컨테이너가 생성하지 않은) MonoBehaviour에 의존성을 주입하는 진입점.
    /// 컴포넌트는 <see cref="Request"/>로 자신을 등록한다. 컨테이너가 준비되어 있으면
    /// 즉시 주입하고, 아니면 보류했다가 <see cref="Start"/> 시점에 일괄 주입한다.
    /// </summary>
    public sealed class InjectorService : IStartable, IDisposable
    {
        private static IObjectResolver _resolver;
        private static readonly List<MonoBehaviour> _pending = new();

        private readonly IObjectResolver _resolverToBind;

        public InjectorService(IObjectResolver resolver)
        {
            _resolverToBind = resolver;
        }

        public void Start()
        {
            _resolver = _resolverToBind;

            foreach (var target in _pending)
            {
                if (target != null)
                {
                    _resolver.Inject(target);
                }
            }

            _pending.Clear();
        }

        public static void Request(MonoBehaviour target)
        {
            if (target == null) return;

            if (_resolver != null)
            {
                _resolver.Inject(target);
                return;
            }

            _pending.Add(target);
        }

        public void Dispose()
        {
            _resolver = null;
            _pending.Clear();
        }
    }
}
```

`Assets/FoundationDI/Runtime/DI/InjectorVContainerExtensions.cs`:
```csharp
using VContainer;

namespace DarkNaku.FoundationDI
{
    public static class InjectorVContainerExtensions
    {
        /// <summary>
        /// 씬 컴포넌트 주입 인프라(InjectorService)를 EntryPoint로 등록한다.
        /// 호스트 LifetimeScope의 Configure에서 호출한다.
        /// </summary>
        public static void RegisterInjector(this IContainerBuilder builder)
        {
            builder.RegisterEntryPoint<InjectorService>();
        }
    }
}
```

- [ ] **Step 4: 컴파일 확인 후 테스트 통과 확인**

UnityMCP: `refresh_unity` → `read_console(types=[error])`(에러 0) → `run_tests(mode=EditMode, assembly_names=[FoundationDI.Tests], test_names=[InjectorServiceTest])` → `get_test_job`. 5개 PASS 기대.

- [ ] **Step 5: 커밋**

```bash
git add Assets/FoundationDI/Runtime/DI/InjectorService.cs Assets/FoundationDI/Runtime/DI/InjectorService.cs.meta Assets/FoundationDI/Runtime/DI/InjectorVContainerExtensions.cs Assets/FoundationDI/Runtime/DI/InjectorVContainerExtensions.cs.meta Assets/FoundationDI/Runtime/DI.meta Assets/FoundationDI/Tests/InjectorServiceTest.cs Assets/FoundationDI/Tests/InjectorServiceTest.cs.meta
git commit -m "$(cat <<'EOF'
[BEHAVIORAL] 씬 컴포넌트 주입 인프라 InjectorService 추가

- 정적 요청 큐 + EntryPoint 통합, 이벤트 드리븐 flush(폴링 없음)
- 컨테이너 준비 전 Request 보류 → Start에서 일괄 주입, 준비 후 즉시 주입
- Dispose로 정적 잔재 정리(도메인 리로드 off 대비)
- RegisterInjector 확장 + InjectorServiceTest 5개

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 2: InjectableBehaviour 베이스

**Files:**
- Create: `Assets/FoundationDI/Runtime/DI/InjectableBehaviour.cs`
- Test: `Assets/FoundationDI/Tests/InjectableBehaviourTest.cs`

**Interfaces:**
- Consumes: `InjectorService.Request`(Task 1).
- Produces: `abstract class InjectableBehaviour : MonoBehaviour`, `protected virtual void Awake()`, `protected void EnsureInjected()`.

- [ ] **Step 1: 실패 테스트 작성** — `Assets/FoundationDI/Tests/InjectableBehaviourTest.cs` 생성

```csharp
using DarkNaku.FoundationDI;
using NSubstitute;
using NUnit.Framework;
using UnityEngine;
using VContainer;

public class InjectableBehaviourTest
{
    private class TestInjectable : InjectableBehaviour
    {
        public void CallEnsure() => EnsureInjected();
    }

    private GameObject _go;

    [SetUp]
    public void SetUp()
    {
        new InjectorService(Substitute.For<IObjectResolver>()).Dispose();
        _go = new GameObject("test");
    }

    [TearDown]
    public void TearDown()
    {
        new InjectorService(Substitute.For<IObjectResolver>()).Dispose();
        if (_go != null) Object.DestroyImmediate(_go);
    }

    [Test]
    public void Awake에서_컴포넌트가_주입_요청된다()
    {
        var resolver = Substitute.For<IObjectResolver>();
        new InjectorService(resolver).Start();   // 컨테이너 준비(즉시 주입 경로)

        var mb = _go.AddComponent<TestInjectable>();  // Awake → EnsureInjected → Request

        resolver.Received(1).Inject(mb);
    }

    [Test]
    public void EnsureInjected는_멱등하여_중복_요청하지_않는다()
    {
        var resolver = Substitute.For<IObjectResolver>();
        new InjectorService(resolver).Start();
        var mb = _go.AddComponent<TestInjectable>();  // Awake에서 1회

        mb.CallEnsure();                               // 추가 호출은 무시되어야 함

        resolver.Received(1).Inject(mb);
    }
}
```

- [ ] **Step 2: 컴파일/실패 확인**

UnityMCP: `refresh_unity` → `read_console(types=[error])`. `InjectableBehaviour` 미정의로 컴파일 에러 = RED.

- [ ] **Step 3: 최소 구현** — `Assets/FoundationDI/Runtime/DI/InjectableBehaviour.cs`

```csharp
using UnityEngine;

namespace DarkNaku.FoundationDI
{
    /// <summary>
    /// 씬에 배치되어 의존성 주입이 필요한 MonoBehaviour의 베이스.
    /// Awake에서 자신을 InjectorService에 1회 등록한다(멱등).
    /// </summary>
    public abstract class InjectableBehaviour : MonoBehaviour
    {
        private bool _requested;

        protected virtual void Awake()
        {
            EnsureInjected();
        }

        protected void EnsureInjected()
        {
            if (_requested) return;
            _requested = true;
            InjectorService.Request(this);
        }
    }
}
```

- [ ] **Step 4: 컴파일 확인 후 전체 테스트 통과 확인**

UnityMCP: `refresh_unity` → `read_console(types=[error])`(에러 0) → `run_tests(mode=EditMode, assembly_names=[FoundationDI.Tests])` → `get_test_job`. 기존 30 + InjectorService 5 + InjectableBehaviour 2 = **37개 PASS** 기대.

- [ ] **Step 5: 커밋**

```bash
git add Assets/FoundationDI/Runtime/DI/InjectableBehaviour.cs Assets/FoundationDI/Runtime/DI/InjectableBehaviour.cs.meta Assets/FoundationDI/Tests/InjectableBehaviourTest.cs Assets/FoundationDI/Tests/InjectableBehaviourTest.cs.meta
git commit -m "$(cat <<'EOF'
[BEHAVIORAL] InjectableBehaviour 베이스 클래스 추가

- 씬 배치 컴포넌트가 Awake에서 InjectorService에 멱등 self-request
- protected EnsureInjected로 lazy 안전망 제공
- InjectableBehaviourTest 2개

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 3: SoundButton

**Files:**
- Create: `Assets/FoundationDI/Runtime/Services/SoundService/SoundButton.cs`
- Test: `Assets/FoundationDI/Tests/SoundButtonTest.cs`

**Interfaces:**
- Consumes: `InjectableBehaviour`(Task 2), `ISoundService`(기존), `SoundCatalog`(기존), uGUI `Button`.
- Produces: `sealed class SoundButton : InjectableBehaviour`, `public void Play()`.

- [ ] **Step 1: 실패 테스트 작성** — `Assets/FoundationDI/Tests/SoundButtonTest.cs` 생성

`_sound`/`_key`는 private이므로 리플렉션으로 설정한다. (실제 DI 대신 직접 주입해 Play 동작만 검증.)

```csharp
using System.Reflection;
using DarkNaku.FoundationDI;
using NSubstitute;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
using VContainer;

public class SoundButtonTest
{
    private GameObject _go;

    [SetUp]
    public void SetUp()
    {
        new InjectorService(Substitute.For<IObjectResolver>()).Dispose();
        _go = new GameObject("sound-button");
    }

    [TearDown]
    public void TearDown()
    {
        new InjectorService(Substitute.For<IObjectResolver>()).Dispose();
        if (_go != null) Object.DestroyImmediate(_go);
    }

    private static void SetPrivate(object obj, string field, object value)
    {
        obj.GetType()
           .GetField(field, BindingFlags.NonPublic | BindingFlags.Instance)
           .SetValue(obj, value);
    }

    [Test]
    public void 클릭하면_주입된_사운드서비스로_지정키를_재생한다()
    {
        var sound = Substitute.For<ISoundService>();
        var button = _go.AddComponent<SoundButton>();   // RequireComponent로 Button 자동 부착, Awake 실행
        SetPrivate(button, "_sound", sound);
        SetPrivate(button, "_key", "Jump");

        button.Play();

        sound.Received(1).Play("Jump");
    }

    [Test]
    public void 사운드서비스가_없으면_에러를_남기고_재생하지_않는다()
    {
        var button = _go.AddComponent<SoundButton>();
        SetPrivate(button, "_key", "Jump");             // _sound는 null 유지

        UnityEngine.TestTools.LogAssert.Expect(LogType.Error,
            new System.Text.RegularExpressions.Regex("ISoundService"));

        Assert.DoesNotThrow(() => button.Play());
    }

    [Test]
    public void RequireComponent로_Button이_자동_부착된다()
    {
        var button = _go.AddComponent<SoundButton>();

        Assert.IsNotNull(button.GetComponent<Button>());
    }
}
```

- [ ] **Step 2: 컴파일/실패 확인**

UnityMCP: `refresh_unity` → `read_console(types=[error])`. `SoundButton` 미정의로 컴파일 에러 = RED.

- [ ] **Step 3: 최소 구현** — `Assets/FoundationDI/Runtime/Services/SoundService/SoundButton.cs`

```csharp
using UnityEngine;
using UnityEngine.UI;
using VContainer;

namespace DarkNaku.FoundationDI
{
    /// <summary>
    /// 버튼 클릭 시 카탈로그에서 고른 사운드를 재생하는 컴포넌트.
    /// _catalog는 에디터 키 드롭다운 소스 전용이며, 런타임 재생은 주입된
    /// ISoundService가 자체 카탈로그로 처리한다(_key가 DI 카탈로그에 존재해야 함).
    /// </summary>
    [RequireComponent(typeof(Button))]
    public sealed class SoundButton : InjectableBehaviour
    {
        [Inject] private ISoundService _sound;

        [SerializeField] private SoundCatalog _catalog;
        [SerializeField] private string _key;

        protected override void Awake()
        {
            base.Awake();
            GetComponent<Button>().onClick.AddListener(Play);
        }

        public void Play()
        {
            EnsureInjected();

            if (_sound == null)
            {
                Debug.LogError("[SoundButton] ISoundService가 주입되지 않았습니다.");
                return;
            }

            _sound.Play(_key);
        }
    }
}
```

- [ ] **Step 4: 컴파일 확인 후 전체 테스트 통과 확인**

UnityMCP: `refresh_unity` → `read_console(types=[error])`(에러 0) → `run_tests(mode=EditMode, assembly_names=[FoundationDI.Tests])` → `get_test_job`. 이전 37 + SoundButton 3 = **40개 PASS** 기대.

주의: `클릭하면...` 테스트에서 `Play()` 내부 `EnsureInjected()`는 `Awake`에서 이미 `_requested=true`이므로 재요청하지 않아 리플렉션으로 설정한 `_sound`를 덮어쓰지 않는다.

- [ ] **Step 5: 커밋**

```bash
git add Assets/FoundationDI/Runtime/Services/SoundService/SoundButton.cs Assets/FoundationDI/Runtime/Services/SoundService/SoundButton.cs.meta Assets/FoundationDI/Tests/SoundButtonTest.cs Assets/FoundationDI/Tests/SoundButtonTest.cs.meta
git commit -m "$(cat <<'EOF'
[BEHAVIORAL] SoundButton 컴포넌트 추가

- InjectableBehaviour 상속, Button.onClick → ISoundService.Play(key)
- _sound 미주입 시 에러 로그 후 무시(안전망 EnsureInjected)
- SoundButtonTest 3개

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 4: 에디터 키 드롭다운

**Files:**
- Create: `Assets/FoundationDI/Editor/FoundationDI.Editor.asmdef`
- Create: `Assets/FoundationDI/Editor/SoundButtonEditor.cs`

**Interfaces:**
- Consumes: `SoundButton`, `SoundCatalog`(직렬화 필드 `_catalog`/`_key`, `SoundCatalog.Keys`).
- Produces: 인스펙터 GUI(런타임 API 없음).

> 이 태스크는 에디터 인스펙터 GUI다. GUI 단위 테스트는 비용 대비 가치가 낮아(plan-mandated YAGNI) **컴파일 성공 + 기존 EditMode 전체 그린 유지**로 검증한다.

- [ ] **Step 1: 에디터 어셈블리 생성** — `Assets/FoundationDI/Editor/FoundationDI.Editor.asmdef`

```json
{
    "name": "FoundationDI.Editor",
    "rootNamespace": "",
    "references": [
        "FoundationDI"
    ],
    "includePlatforms": [
        "Editor"
    ],
    "excludePlatforms": [],
    "allowUnsafeCode": false,
    "overrideReferences": false,
    "precompiledReferences": [],
    "autoReferenced": true,
    "defineConstraints": [],
    "versionDefines": [],
    "noEngineReferences": false
}
```

- [ ] **Step 2: 인스펙터 구현** — `Assets/FoundationDI/Editor/SoundButtonEditor.cs`

```csharp
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace DarkNaku.FoundationDI.Editor
{
    [CustomEditor(typeof(SoundButton))]
    public sealed class SoundButtonEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            var catalogProp = serializedObject.FindProperty("_catalog");
            var keyProp = serializedObject.FindProperty("_key");

            EditorGUILayout.PropertyField(catalogProp);

            var catalog = catalogProp.objectReferenceValue as SoundCatalog;
            var keys = catalog != null ? new List<string>(catalog.Keys) : null;

            if (keys != null && keys.Count > 0)
            {
                int current = Mathf.Max(0, keys.IndexOf(keyProp.stringValue));
                int selected = EditorGUILayout.Popup("Key", current, keys.ToArray());
                keyProp.stringValue = keys[selected];
            }
            else
            {
                EditorGUILayout.PropertyField(keyProp);
            }

            DrawPropertiesExcluding(serializedObject, "m_Script", "_catalog", "_key");

            serializedObject.ApplyModifiedProperties();
        }
    }
}
```

- [ ] **Step 3: 컴파일 확인 후 전체 테스트 그린 유지 확인**

UnityMCP: `refresh_unity(compile=request, mode=force)` → `read_console(types=[error])`(에러 0) → `run_tests(mode=EditMode, assembly_names=[FoundationDI.Tests])` → `get_test_job`. **40개 PASS 유지** 기대.

- [ ] **Step 4: 커밋**

```bash
git add Assets/FoundationDI/Editor/FoundationDI.Editor.asmdef Assets/FoundationDI/Editor/FoundationDI.Editor.asmdef.meta Assets/FoundationDI/Editor/SoundButtonEditor.cs Assets/FoundationDI/Editor/SoundButtonEditor.cs.meta Assets/FoundationDI/Editor.meta
git commit -m "$(cat <<'EOF'
[BEHAVIORAL] SoundButton 카탈로그 키 드롭다운 인스펙터 추가

- 신규 FoundationDI.Editor 어셈블리
- _catalog 할당 시 SoundCatalog.Keys를 팝업으로, 미할당 시 텍스트 폴백

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## 자체 검토 결과

- **스펙 커버리지**: 3.1 InjectorService → Task1; 3.2 RegisterInjector → Task1; 3.3 InjectableBehaviour → Task2; 3.4 SoundButton → Task3; 3.5 키 드롭다운(Editor asmdef) → Task4; 5 에러처리(_sound null/Request null/Dispose 정리) → Task1·Task3; 7 테스트전략 → 각 Task. 6 위치/asmdef → 파일 구조 및 각 Task. 8(범위 밖)은 의도적 제외. 누락 없음.
- **플레이스홀더**: 없음(모든 코드/명령 구체적).
- **타입 일관성**: `InjectorService.Request(MonoBehaviour)`, `InjectorService(IObjectResolver)`, `Start()`/`Dispose()`, `RegisterInjector()`, `InjectableBehaviour.EnsureInjected()`/`Awake()`, `SoundButton.Play()`가 전 태스크에서 동일하게 사용됨. 테스트의 리플렉션 필드명 `_sound`/`_key`/`_catalog`가 SoundButton 구현과 일치.
