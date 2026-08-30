# UINavigator 씬 수명 전환 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** `UIService`를 씬 `LifetimeScope`가 소유하는 `UINavigator`로 바꾼다 — 씬이 언로드되면 프리젠터·풀·캔버스가 함께 죽고, 정리 경로가 `Dispose()` 하나로 준다.

**Architecture:** 표시 로직(빌더 API·`OperationQueue`·풀링·트랜지션)은 한 줄도 건드리지 않는다. 바뀌는 것은 세 가지뿐이다 — (1) `DontDestroyOnLoad` 제거로 캔버스가 활성 씬에 귀속, (2) `SceneManager.activeSceneChanged` 리셋 경로 삭제로 정리 경로 단일화, (3) `Root` getter의 dispose 가드. 나머지는 전부 이름·폴더 이동과 문서다.

**Tech Stack:** Unity 6000.3.17f1, VContainer, uGUI, NUnit + NSubstitute 5.3.0, UnityMCP(컴파일 확인 `read_console` / 테스트 `run_tests` → `get_test_job` 폴링)

**Spec:** `docs/superpowers/specs/2026-08-30-ui-navigator-scene-lifetime-design.md`

## Global Constraints

- 브랜치: `feature/ui-navigator-scene-lifetime` (이미 생성됨, 스펙 커밋 `1e5a411`·`031269c`가 올라가 있다)
- 네임스페이스는 `DarkNaku.FoundationDI` 그대로. 런타임은 전부 `FoundationDI` asmdef — **asmdef 수정 없음**(폴더만 옮기므로 asmdef 경계가 바뀌지 않는다)
- async는 `Awaitable`만. UniTask/R3 금지
- 테스트 메서드 이름은 **한국어 + 언더스코어**. 테스트 클래스는 **네임스페이스 없이** 전역에 두고 `using DarkNaku.FoundationDI;`를 쓴다
- **async 테스트는 반드시 `AwaitableTest.Run(async () => {...})`** 로 감싼다. 프레임 대기는 `AwaitableTest.NextFrame()`/`WaitUntil(pred)`. `Awaitable.NextFrameAsync()`를 직접 쓰면 EditMode에서 멈춘다
- **STRUCTURAL과 BEHAVIORAL을 같은 커밋에 섞지 않는다.** 커밋 제목에 접두어를 단다
- **`.meta` 파일은 Unity가 생성한다.** 새 `.cs`를 만든 뒤 `refresh_unity` 또는 `read_console`로 임포트를 한 번 돌린 다음 `git add`해야 `.meta`가 함께 커밋된다
- **파일/폴더 이동은 반드시 `git mv`로 하고 `.meta`를 짝지어 옮긴다.** `.meta`가 빠지면 GUID가 새로 발급되어 프리팹·`.asset`의 스크립트 참조가 끊긴다
- 각 태스크 끝에서 **전체 테스트(EditMode + PlayMode) 그린**을 확인한 뒤 커밋한다
- `Samples~`는 `~` 접미사라 Unity가 임포트하지 않는다 — `.meta`가 없고 컴파일되지 않으므로 텍스트 치환만 하면 된다

---

## File Structure

**이동 (내용 변경은 식별자 치환뿐)**

| 현재 | 이동 후 | 파일 수 |
|---|---|---|
| `Assets/FoundationDI/Runtime/Services/UIService/` | `Assets/FoundationDI/Runtime/Managers/UINavigator/` | 26 (+meta) |
| `Assets/FoundationDI/Editor/UIService/` | `Assets/FoundationDI/Editor/UINavigator/` | 9 (Task 1에서 1개 삭제) |
| `Assets/FoundationDI/Tests/Editor/UIService/` | `Assets/FoundationDI/Tests/Editor/UINavigator/` | 15 (Task 1에서 1개 삭제) |
| `Assets/FoundationDI/Tests/Runtime/UIService/` | `Assets/FoundationDI/Tests/Runtime/UINavigator/` | 12 |

**파일명 변경**

- `UIService.cs` → `UINavigator.cs` (책임: 조립 + 표시 큐 + 수명)
- `IUIService.cs` → `IUINavigator.cs` (책임: 게임 표면 API)
- `Settings/UIServiceSettings.cs` → `Settings/UINavigatorSettings.cs`
- `Assets/Settings/UIServiceSettings.asset` → `UINavigatorSettings.asset`
- 테스트 6개: `UIService*Tests.cs` → `UINavigator*Tests.cs`

**신규**

- `Assets/FoundationDI/Tests/Runtime/UINavigator/UINavigatorSceneLifetimeTests.cs` — 씬 귀속·전환 무반응·Dispose 계약 (Task 3~5)
- `Assets/Scripts/LifetimeScopes/SceneLifetimeScope.cs` — 호스트 프로젝트의 씬 스코프 (Task 6)

**삭제**

- `Assets/FoundationDI/Editor/UIService/UIEditingEnvironment.cs` (Task 1)
- `Assets/FoundationDI/Tests/Editor/UIService/UIEditingEnvironmentTests.cs` (Task 1)
- `Assets/UIEditingEnvironment.unity` (Task 1)
- `Assets/FoundationDI/Tests/Runtime/UINavigator/UINavigatorSceneResetTests.cs` (Task 4)

---

### Task 1: 프리팹 편집 환경 메뉴 제거

수명 변경과 무관한 독립 삭제다. 먼저 해서 이후 태스크의 이동 대상 파일 수를 줄인다.

**Files:**
- Delete: `Assets/FoundationDI/Editor/UIService/UIEditingEnvironment.cs` (+ `.meta`)
- Delete: `Assets/FoundationDI/Tests/Editor/UIService/UIEditingEnvironmentTests.cs` (+ `.meta`)
- Delete: `Assets/UIEditingEnvironment.unity` (+ `.meta`)
- Modify: `Assets/FoundationDI/Runtime/Services/UIService/README.md` (편집 환경 메뉴 설명 삭제)
- Modify: `CLAUDE.md` (에디터 도구 목록에서 해당 항목 삭제)

**Interfaces:**
- Consumes: 없음
- Produces: 없음 (순수 삭제)

- [ ] **Step 1: 삭제 대상이 다른 코드에서 참조되지 않는지 확인**

```bash
grep -rn "UIEditingEnvironment" Assets docs --include='*.cs' --include='*.md' --include='*.asmdef'
```

기대: `UIEditingEnvironment.cs`, `UIEditingEnvironmentTests.cs`, README/CLAUDE.md의 설명 문장만 나온다. 다른 프로덕션 코드가 이 타입을 부르면 여기서 멈추고 보고한다.

- [ ] **Step 2: 파일 삭제**

```bash
git rm Assets/FoundationDI/Editor/UIService/UIEditingEnvironment.cs \
       Assets/FoundationDI/Editor/UIService/UIEditingEnvironment.cs.meta \
       Assets/FoundationDI/Tests/Editor/UIService/UIEditingEnvironmentTests.cs \
       Assets/FoundationDI/Tests/Editor/UIService/UIEditingEnvironmentTests.cs.meta \
       Assets/UIEditingEnvironment.unity \
       Assets/UIEditingEnvironment.unity.meta
```

- [ ] **Step 3: 문서에서 메뉴 설명 제거**

`Assets/FoundationDI/Runtime/Services/UIService/README.md`에서 `Setup Prefab Editing Environment` / `Clear Prefab Editing Environment`를 설명하는 항목·섹션을 지운다. `Create UI Root Prefab`과 `Create UI Element...` 설명은 **남긴다**.

`CLAUDE.md`의 UIService 항목에서 다음 조각을 지운다 (앞뒤 `·` 구분자도 함께):

```
· `Setup/Clear Prefab Editing Environment`(프리팹을 실제 캔버스 안에서 편집)
```

- [ ] **Step 4: 컴파일 확인**

UnityMCP `read_console` (`types: ["error"]`). 기대: 에러 0건. `editor_state.isCompiling == false`가 될 때까지 기다린 뒤 읽는다.

- [ ] **Step 5: 전체 테스트 실행**

UnityMCP `run_tests` — EditMode, PlayMode 각각. 기대: 전부 PASS. `UIEditingEnvironmentTests`가 목록에서 사라진 것을 확인한다.

- [ ] **Step 6: 커밋**

```bash
git add -A
git commit -m "$(cat <<'MSG'
[STRUCTURAL] 프리팹 편집 환경 메뉴를 제거한다

UI 디자이너용 확인 방법은 추후 다시 설계한다.
Create UI Root Prefab / Create UI Element...는 생성용이므로 유지한다.

Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>
MSG
)"
```

---

### Task 2: UIService를 UINavigator로 개명하고 Managers 아래로 옮긴다

**순수 구조 변경이다. 동작이 한 줄도 바뀌면 안 되고, 끝나면 전체 테스트가 그대로 그린이어야 한다.** 그 그린이 "개명이 구조 변경"이라는 증거다.

**Files:**
- Move: 위 File Structure 표의 4개 폴더 (+ 각 폴더의 `.meta`)
- Rename: `UIService.cs`→`UINavigator.cs`, `IUIService.cs`→`IUINavigator.cs`, `Settings/UIServiceSettings.cs`→`Settings/UINavigatorSettings.cs`, 테스트 6개
- Rename: `Assets/Settings/UIServiceSettings.asset` → `Assets/Settings/UINavigatorSettings.asset`
- Modify(치환): `Assets/**` 전체 — 아래 Step 4의 sed 목록

**Interfaces:**
- Consumes: 없음
- Produces:
  - `public interface IUINavigator { bool IsPopupVisible { get; } T Page<T>() where T : UIPresenter; T Popup<T>() where T : UIPresenter; T Overlay<T>() where T : UIPresenter; }`
  - `public sealed class UINavigator : IUINavigator, IUIElementHost, IDisposable` — 생성자는 `internal UINavigator(UINavigatorSettings settings, UIInstanceFactory factory, IResourceService resource)`
  - `public sealed class UINavigatorSettings : ScriptableObject` — `public UIRoot RootPrefab { get; internal set; }`
  - `public static void RegisterUINavigator(this IContainerBuilder builder, UINavigatorSettings settings)` (in `UINavigatorVContainerExtensions`)
  - **개명하지 않는 타입**(이후 태스크가 그대로 쓴다): `UIPresenter`, `UIPagePresenter<TView>`, `UIPopupPresenter<TView>`, `UIOverlayPresenter<TView>`, `UIView`, `UIRoot`, `UIPrefabAttribute`, `UIPrefabKeyResolver`, `UIInstanceFactory`, `IUIElementHost`, `OperationQueue`, 트랜지션 일체

- [ ] **Step 1: 폴더 이동 (`.meta` 짝 유지)**

```bash
cd /Users/chakyounghoon/Projects/FoundationDI

mkdir -p Assets/FoundationDI/Runtime/Managers
git mv Assets/FoundationDI/Runtime/Services/UIService      Assets/FoundationDI/Runtime/Managers/UINavigator
git mv Assets/FoundationDI/Runtime/Services/UIService.meta Assets/FoundationDI/Runtime/Managers/UINavigator.meta

git mv Assets/FoundationDI/Editor/UIService      Assets/FoundationDI/Editor/UINavigator
git mv Assets/FoundationDI/Editor/UIService.meta Assets/FoundationDI/Editor/UINavigator.meta

git mv Assets/FoundationDI/Tests/Editor/UIService      Assets/FoundationDI/Tests/Editor/UINavigator
git mv Assets/FoundationDI/Tests/Editor/UIService.meta Assets/FoundationDI/Tests/Editor/UINavigator.meta

git mv Assets/FoundationDI/Tests/Runtime/UIService      Assets/FoundationDI/Tests/Runtime/UINavigator
git mv Assets/FoundationDI/Tests/Runtime/UIService.meta Assets/FoundationDI/Tests/Runtime/UINavigator.meta
```

- [ ] **Step 2: 파일명 변경 (`.meta` 짝 유지)**

```bash
R=Assets/FoundationDI/Runtime/Managers/UINavigator
git mv $R/UIService.cs               $R/UINavigator.cs
git mv $R/UIService.cs.meta          $R/UINavigator.cs.meta
git mv $R/IUIService.cs              $R/IUINavigator.cs
git mv $R/IUIService.cs.meta         $R/IUINavigator.cs.meta
git mv $R/Settings/UIServiceSettings.cs      $R/Settings/UINavigatorSettings.cs
git mv $R/Settings/UIServiceSettings.cs.meta $R/Settings/UINavigatorSettings.cs.meta

TE=Assets/FoundationDI/Tests/Editor/UINavigator
git mv $TE/UIServiceSettingsTests.cs      $TE/UINavigatorSettingsTests.cs
git mv $TE/UIServiceSettingsTests.cs.meta $TE/UINavigatorSettingsTests.cs.meta

TR=Assets/FoundationDI/Tests/Runtime/UINavigator
for n in Flow RootPrefab SceneReset ViewInjection WithOverlay; do
  git mv $TR/UIService${n}Tests.cs      $TR/UINavigator${n}Tests.cs
  git mv $TR/UIService${n}Tests.cs.meta $TR/UINavigator${n}Tests.cs.meta
done

git mv Assets/Settings/UIServiceSettings.asset      Assets/Settings/UINavigatorSettings.asset
git mv Assets/Settings/UIServiceSettings.asset.meta Assets/Settings/UINavigatorSettings.asset.meta
```

- [ ] **Step 3: 이동 결과 확인**

```bash
git status --short | head -100
ls Assets/FoundationDI/Runtime/Managers/UINavigator
```

기대: 모든 이동이 `R`(rename)로 잡히고, `.cs`마다 `.cs.meta`가 짝으로 따라왔다. 짝이 빠진 것이 있으면 여기서 멈추고 보고한다.

- [ ] **Step 4: 식별자 치환 (긴 이름부터)**

`UIServiceSettings`가 `UIService`를 포함하므로 **순서가 중요하다.** 아래 순서를 지킨다.

```bash
FILES=$(grep -rl "UIService" Assets \
  --include='*.cs' --include='*.md' --include='*.asset' --include='*.unity' --include='*.prefab')

for f in $FILES; do
  sed -i '' \
    -e 's/UIServiceVContainerExtensions/UINavigatorVContainerExtensions/g' \
    -e 's/UIServiceSettings/UINavigatorSettings/g' \
    -e 's/RegisterUIService/RegisterUINavigator/g' \
    -e 's/IUIService/IUINavigator/g' \
    -e 's/UIService/UINavigator/g' \
    "$f"
done
```

이것이 커버하는 것: 클래스·인터페이스명, `nameof(UIService)`, 로그 접두어 `[UIService]`, `UIRoot.CreateDefault()`가 짓는 GO 이름 `"[UIService]"`, 테스트 클래스명 `UIService*Tests`, `.asset`의 `m_Name`, 주석, README, 샘플 코드, 호스트 스크립트.

- [ ] **Step 5: 잔여 확인**

```bash
grep -rn "UIService" Assets | grep -v '\.meta:'
```

기대: **0건.** 남아 있으면 그 파일이 위 `--include` 목록에서 빠진 확장자다 — 확인 후 같은 sed를 적용한다.

- [ ] **Step 6: 컴파일 확인**

UnityMCP `refresh_unity` → `read_console` (`types: ["error"]`). 기대: 에러 0건.

폴더 이동으로 `.meta` GUID가 유지되므로 `RootLifetimeScope.prefab`의 `settings` 필드 참조와 `UINavigatorSettings.asset`의 `m_Script` 참조는 살아 있어야 한다. Unity 콘솔에 "The referenced script ... is missing" 이 뜨면 `.meta` 짝이 깨진 것이다 — 되돌리고 Step 1부터 다시 한다.

- [ ] **Step 7: 프리팹 참조 육안 확인**

Unity에서 `Assets/Scripts/LifetimeScopes/RootLifetimeScope.prefab`을 선택해 인스펙터의 `Settings` 슬롯이 `UINavigatorSettings`를 여전히 가리키는지 본다. 비어 있으면 `Assets/Settings/UINavigatorSettings.asset`을 다시 끌어다 놓고 저장한다.

- [ ] **Step 8: 전체 테스트 실행**

UnityMCP `run_tests` — EditMode, PlayMode. **기대: Task 1 직후와 완전히 동일한 결과(전부 PASS).** 하나라도 새로 실패하면 개명이 순수 구조 변경이 아니었다는 뜻이므로 멈추고 원인을 보고한다.

- [ ] **Step 9: 커밋**

```bash
git add -A
git commit -m "$(cat <<'MSG'
[STRUCTURAL] UIService를 UINavigator로 개명하고 Managers 아래로 옮긴다

씬 수명을 가질 것이므로 PoolManager/TutorialManager와 같은 자리로 옮긴다.
동작 변경 없음 — 식별자 치환과 폴더 이동뿐이고 전체 테스트가 그대로 그린이다.
UIPresenter/UIView/UIRoot/UIPrefab 계열은 표시 요소 이름이라 그대로 둔다.

Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>
MSG
)"
```

---

### Task 3: 캔버스를 활성 씬에 귀속시킨다

**Files:**
- Create: `Assets/FoundationDI/Tests/Runtime/UINavigator/UINavigatorSceneLifetimeTests.cs`
- Modify: `Assets/FoundationDI/Runtime/Managers/UINavigator/UINavigator.cs` (`CreateRoot`의 `DontDestroyOnLoad` 삭제, `Root` getter 주석)
- Modify: `Assets/FoundationDI/Tests/Runtime/UINavigator/UINavigatorRootPrefabTests.cs:71,95` (상주 단언 → 활성 씬 단언)

**Interfaces:**
- Consumes: Task 2의 `UINavigator`, `UINavigatorSettings`, `UIInstanceFactory`, `UIPagePresenter<TView>`, `UIView`, `UIRoot`
- Produces: 테스트 픽스처 `UINavigatorSceneLifetimeTests`의 `CreateNavigator()` / 중첩 타입 `V`(UIView), `P`(UIPagePresenter\<V\>, `[UIPrefab("UI/SceneLifetime")]`, `Shown`/`AfterHideCalled` 플래그) — Task 4·5가 같은 파일에 테스트를 덧붙인다

- [ ] **Step 1: 실패하는 테스트를 쓴다 (파일 신규 생성)**

`Assets/FoundationDI/Tests/Runtime/UINavigator/UINavigatorSceneLifetimeTests.cs`:

```csharp
// using System; 을 넣지 않는다 — using UnityEngine; 과 함께 쓰면 Object 가 모호해져 컴파일이 깨진다.
using System.Collections;
using NSubstitute;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using VContainer;
using DarkNaku.FoundationDI;

public class UINavigatorSceneLifetimeTests
{
    public class V : UIView { }

    [UIPrefab("UI/SceneLifetime")]
    public class P : UIPagePresenter<V>
    {
        public bool Shown;
        public bool AfterHideCalled;
        protected internal override void OnAfterShow() => Shown = true;
        protected internal override void OnAfterHide() => AfterHideCalled = true;
    }

    private GameObject _viewPrefab;

    [SetUp]
    public void SetUp()
    {
        // Instantiate 원본은 프리팹 에셋이 아니어도 되므로 에셋 IO 없이 씬 오브젝트로 대체한다.
        _viewPrefab = new GameObject("view", typeof(RectTransform), typeof(CanvasGroup));
        _viewPrefab.AddComponent<V>();
        _viewPrefab.SetActive(false);
    }

    [TearDown]
    public void TearDown()
    {
        if (_viewPrefab != null) Object.DestroyImmediate(_viewPrefab);
    }

    // RootPrefab 미지정 → UIRoot.CreateDefault() 폴백. 경고 로그가 남지만 경고는 테스트를 깨뜨리지 않는다.
    private UINavigator CreateNavigator()
    {
        var resource = Substitute.For<IResourceService>();
        resource.Load<GameObject>("UI/SceneLifetime").Returns(_viewPrefab);
        var settings = ScriptableObject.CreateInstance<UINavigatorSettings>();
        return new UINavigator(settings, new UIInstanceFactory(Substitute.For<IObjectResolver>()), resource);
    }

    [UnityTest]
    public IEnumerator 캔버스는_상주씬이_아니라_활성씬에_속한다() => AwaitableTest.Run(async () =>
    {
        var nav = CreateNavigator();
        var p = nav.Page<P>();
        await AwaitableTest.WaitUntil(() => p.Shown);

        var root = p.ViewBase.transform.root.GetComponent<UIRoot>();

        Assert.IsNotNull(root, "표시된 View는 UIRoot 아래에 있어야 한다");
        Assert.AreNotEqual("DontDestroyOnLoad", root.GO.scene.name,
            "캔버스가 상주하면 씬과 함께 파괴되지 않는다");
        Assert.AreEqual(SceneManager.GetActiveScene().handle, root.GO.scene.handle,
            "캔버스는 자신을 만든 시점의 활성 씬에 속해야 한다");

        nav.Dispose();
    });
}
```

- [ ] **Step 2: 실패를 확인한다**

UnityMCP `run_tests` — PlayMode, 필터 `UINavigatorSceneLifetimeTests`.
기대: `캔버스는_상주씬이_아니라_활성씬에_속한다` FAIL — `Expected: not equal to "DontDestroyOnLoad"` (현재 `CreateRoot`가 상주화를 적용하므로).

- [ ] **Step 3: 최소 구현 — 상주화를 걷어낸다**

`UINavigator.cs`의 `CreateRoot()`에서 다음 두 줄(주석 포함)을 **삭제**한다:

```csharp
            UnityEngine.Object.DontDestroyOnLoad(root.GO);
```

그리고 바로 위의 메서드 XML/주석 중 상주화를 설명하는 문장(`// 상주화 책임은 서비스가 진다. 루트를 어디서 얻었든(프리팹/폴백) 동일하게 적용한다.`)을 다음으로 교체한다:

```csharp
        // 루트는 부모 없이 인스턴스화되므로 활성 씬에 붙는다 = 씬과 함께 파괴된다.
        // 상주화(DontDestroyOnLoad)는 하지 않는다 — 이 내비게이터의 수명이 씬 수명이기 때문이다.
```

`Root` getter의 주석도 전제가 바뀌었으므로 교체한다:

```csharp
        private UIRoot Root
        {
            get
            {
                // 캔버스는 씬 수명이다. 씬 언로드와 Dispose의 순서는 보장되지 않으므로
                // 파괴된 뒤(fake-null) 접근이 올 수 있다 — 참조를 버리고 재구성한다.
                // UIRoot는 MonoBehaviour다 → ??= 는 fake-null을 못 걸러내므로 쓰지 않는다.
                if (_root == null) DiscardRoot();
                if (_root == null) _root = CreateRoot();
                return _root;
            }
        }
```

- [ ] **Step 4: 기존 상주 단언을 활성 씬 단언으로 바꾼다**

`UINavigatorRootPrefabTests.cs`는 상주화를 두 곳에서 단언한다. 두 곳 모두 교체한다.

파일 상단에 `using UnityEngine.SceneManagement;`를 추가한 뒤:

71행 근처 — `Settings에_루트프리팹이_지정되면_그_프리팹을_인스턴스화한다`:

```csharp
        Assert.AreEqual(SceneManager.GetActiveScene().handle, clone.GO.scene.handle,
            "씬 귀속은 프리팹 경로에서도 동일하게 적용되어야 한다");
```

95행 근처 — `Settings에_루트프리팹이_없으면_코드_기본값으로_폴백한다`:

```csharp
        Assert.AreEqual(SceneManager.GetActiveScene().handle, root.GO.scene.handle);
```

- [ ] **Step 5: 테스트가 통과하는지 확인한다**

UnityMCP `run_tests` — PlayMode 전체.
기대: `UINavigatorSceneLifetimeTests` PASS, `UINavigatorRootPrefabTests` 3건 PASS, 나머지 PlayMode 전부 PASS.

- [ ] **Step 6: EditMode도 돌린다**

UnityMCP `run_tests` — EditMode 전체. 기대: 전부 PASS.

- [ ] **Step 7: 커밋**

```bash
git add -A
git commit -m "$(cat <<'MSG'
[BEHAVIORAL] 캔버스를 활성 씬에 귀속시킨다

DontDestroyOnLoad를 걷어내 루트 캔버스가 씬과 함께 파괴되게 한다.
루트프리팹 테스트의 상주 단언도 활성 씬 단언으로 바꾼다.

Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>
MSG
)"
```

---

### Task 4: 씬 전환 리셋 경로를 제거한다

정리 경로가 둘(씬 이벤트 / `Dispose`)에서 하나로 주는 태스크다. 이번 변경의 핵심 이득이다.

**Files:**
- Modify: `Assets/FoundationDI/Tests/Runtime/UINavigator/UINavigatorSceneLifetimeTests.cs` (테스트 추가)
- Modify: `Assets/FoundationDI/Runtime/Managers/UINavigator/UINavigator.cs` (구독·핸들러 삭제)
- Delete: `Assets/FoundationDI/Tests/Runtime/UINavigator/UINavigatorSceneResetTests.cs` (+ `.meta`)

**Interfaces:**
- Consumes: Task 3의 `UINavigatorSceneLifetimeTests` 픽스처(`CreateNavigator()`, `P.Shown`, `P.AfterHideCalled`)
- Produces: `UINavigator`에 `SceneManager` 의존이 없어진다 — Task 5는 `Dispose`만으로 정리를 검증한다

- [ ] **Step 1: 실패하는 테스트를 쓴다**

`UINavigatorSceneLifetimeTests.cs`의 클래스 안, 기존 테스트 아래에 추가:

```csharp
    [UnityTest]
    public IEnumerator 활성씬이_바뀌어도_표시중인_UI를_스스로_리셋하지_않는다() => AwaitableTest.Run(async () =>
    {
        var nav = CreateNavigator();
        var p = nav.Page<P>();
        await AwaitableTest.WaitUntil(() => p.Shown);

        var previous = SceneManager.GetActiveScene();
        var temp = SceneManager.CreateScene("uinavigator_scene_switch");
        SceneManager.SetActiveScene(temp);
        await AwaitableTest.NextFrame();

        // 정리 경로는 Dispose 하나뿐이다. 씬 이벤트는 더 이상 teardown을 촉발하지 않는다.
        Assert.IsFalse(p.AfterHideCalled, "씬 전환이 presenter를 teardown하면 안 된다");
        Assert.IsTrue(p.ViewBase != null, "표시 중인 View가 파괴되면 안 된다");
        Assert.IsTrue(p.ViewBase.gameObject.activeSelf, "표시 중인 View가 비활성화되면 안 된다");

        SceneManager.SetActiveScene(previous);
        nav.Dispose();
        await Awaitable.FromAsyncOperation(SceneManager.UnloadSceneAsync(temp));
    });
```

- [ ] **Step 2: 실패를 확인한다**

UnityMCP `run_tests` — PlayMode, 필터 `UINavigatorSceneLifetimeTests`.
기대: `활성씬이_바뀌어도_표시중인_UI를_스스로_리셋하지_않는다` FAIL — `Assert.IsFalse(p.AfterHideCalled)`에서 실패한다(현재 `OnActiveSceneChanged`가 `ClearContent()`를 불러 `OnAfterHide`를 발화시킨다).

- [ ] **Step 3: 최소 구현 — 씬 이벤트 경로를 삭제한다**

`UINavigator.cs`에서:

1. 생성자에서 구독 삭제:

```csharp
        internal UINavigator(UINavigatorSettings settings, UIInstanceFactory factory, IResourceService resource)
        {
            _settings = settings;
            _factory = factory;
            _resource = resource;
        }
```

2. `OnActiveSceneChanged` 메서드 **전체 삭제** (씬 전환 촉발 경로가 사라진다):

```csharp
        private void OnActiveSceneChanged(Scene previous, Scene next) { ... }   // ← 삭제
```

3. `Dispose()`에서 해제 줄 삭제:

```csharp
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            // 정리 경로는 이것 하나다. 씬 수명이므로 씬 이벤트를 따로 듣지 않는다.
            ClearContent();
            if (_root != null && _root.GO != null) UnityEngine.Object.Destroy(_root.GO);
            _root = null;
        }
```

4. `ClearContent()` 위의 주석을 갱신한다(더 이상 "씬 전환 시"가 아니다):

```csharp
        // 활성 UI를 전부 teardown하고 풀을 dispose한다. 캔버스 파괴는 호출자(Dispose)가 한다.
        private void ClearContent()
```

5. `using UnityEngine.SceneManagement;`가 파일에서 더 이상 쓰이지 않으면 삭제한다. (`grep -n "Scene" UINavigator.cs`로 확인)

- [ ] **Step 4: 낡은 테스트 파일을 삭제한다**

`UINavigatorSceneResetTests`의 두 테스트는 "씬 전환 시 인스턴스는 살고 내용만 리셋된다"를 전제하므로 Step 3 이후 반드시 실패한다. 파일째 지운다 — 대체 커버리지는 Step 1의 테스트와 Task 5의 Dispose 계약 테스트다.

```bash
git rm Assets/FoundationDI/Tests/Runtime/UINavigator/UINavigatorSceneResetTests.cs \
       Assets/FoundationDI/Tests/Runtime/UINavigator/UINavigatorSceneResetTests.cs.meta
```

- [ ] **Step 5: 테스트가 통과하는지 확인한다**

UnityMCP `run_tests` — PlayMode 전체. 기대: 전부 PASS, `UINavigatorSceneResetTests`가 목록에서 사라졌다.

- [ ] **Step 6: EditMode도 돌린다**

UnityMCP `run_tests` — EditMode 전체. 기대: 전부 PASS.

- [ ] **Step 7: 커밋**

```bash
git add -A
git commit -m "$(cat <<'MSG'
[BEHAVIORAL] 씬 전환 리셋 경로를 제거한다

activeSceneChanged 구독과 핸들러를 삭제해 정리 경로를 Dispose 하나로 줄인다.
이 동작을 전제하던 SceneReset 테스트는 폐기하고 씬 수명 테스트로 대체한다.

Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>
MSG
)"
```

---

### Task 5: Dispose 계약을 잠근다

Task 3·4로 `Dispose()`가 유일한 정리 경로가 됐다. 그 계약을 테스트로 고정하고, dispose 이후 캔버스가 되살아나지 않도록 가드를 넣는다.

**Files:**
- Modify: `Assets/FoundationDI/Tests/Runtime/UINavigator/UINavigatorSceneLifetimeTests.cs` (테스트 4건 추가)
- Modify: `Assets/FoundationDI/Runtime/Managers/UINavigator/UINavigator.cs` (`Root` getter 가드)

**Interfaces:**
- Consumes: Task 3의 픽스처, Task 4가 정리한 `Dispose()`
- Produces: 없음 (계약 고정)

- [ ] **Step 1: 실패하는 테스트를 쓴다 — dispose 이후 캔버스 재생성 금지**

`UINavigatorSceneLifetimeTests.cs`에 추가:

```csharp
    [UnityTest]
    public IEnumerator Dispose_이후_Hide요청이_캔버스를_되살리지_않는다() => AwaitableTest.Run(async () =>
    {
        var nav = CreateNavigator();
        var p = nav.Page<P>();
        await AwaitableTest.WaitUntil(() => p.Shown);

        nav.Dispose();
        await AwaitableTest.NextFrame();   // Object.Destroy 반영

        // 게임 코드가 들고 있던 presenter로 뒤늦게 Hide를 부르는 경로.
        // 큐 → 내부 Pool/Root 접근으로 이어지면 다음 씬에 고아 캔버스가 생긴다.
        Assert.DoesNotThrow(() => p.Hide());
        await AwaitableTest.NextFrame();

        Assert.IsNull(GameObject.Find("[UINavigator]"),
            "dispose 이후에는 캔버스가 다시 만들어지면 안 된다");
    });
```

- [ ] **Step 2: 실패(또는 통과)를 확인한다**

UnityMCP `run_tests` — PlayMode, 필터 `UINavigatorSceneLifetimeTests`.

이 테스트는 **현재 구현에서도 통과할 수 있다**(`HandleHideAsync`가 `_active`가 비어 early-return 하므로). 통과하면 그것대로 기록하고 Step 3의 가드를 "심층 방어"로 넣는다 — 회귀 시 이 테스트가 잡는다. FAIL이면 Step 3이 곧바로 GREEN을 만든다.

- [ ] **Step 3: `Root` getter에 dispose 가드를 넣는다**

`UINavigator.cs`의 `Root` getter를 다음으로 교체한다:

```csharp
        private UIRoot Root
        {
            get
            {
                // dispose 이후에는 절대 재구성하지 않는다. 여기서 만들면 파괴되는 씬이 아니라
                // "다음 씬"에 캔버스가 생겨 고아로 남는다. 진입점(Page/Popup/Overlay)의
                // _disposed 검사만으로는 큐에 남은 내부 경로(Pool → Root)를 막지 못한다.
                if (_disposed) throw new ObjectDisposedException(nameof(UINavigator));

                // 캔버스는 씬 수명이다. 씬 언로드와 Dispose의 순서는 보장되지 않으므로
                // 파괴된 뒤(fake-null) 접근이 올 수 있다 — 참조를 버리고 재구성한다.
                // UIRoot는 MonoBehaviour다 → ??= 는 fake-null을 못 걸러내므로 쓰지 않는다.
                if (_root == null) DiscardRoot();
                if (_root == null) _root = CreateRoot();
                return _root;
            }
        }
```

`System`은 이미 `using` 되어 있다(`ObjectDisposedException`이 `Page<T>()`에서 이미 쓰인다).

- [ ] **Step 4: 테스트가 통과하는지 확인한다**

UnityMCP `run_tests` — PlayMode 전체. 기대: 전부 PASS.

`OperationQueue.ProcessLoop`가 예외를 `Debug.LogException`으로 흘리므로, 가드가 큐 안에서 터지면 **PlayMode 테스트가 예기치 않은 로그로 실패**할 수 있다. 그런 실패가 나오면 그 경로는 가드가 아니라 early-return으로 막아야 한다는 신호다 — 멈추고 보고한다.

- [ ] **Step 5: Dispose 계약 테스트 3건을 추가한다**

같은 파일에 추가:

```csharp
    [UnityTest]
    public IEnumerator Dispose하면_캔버스GO가_파괴된다() => AwaitableTest.Run(async () =>
    {
        var nav = CreateNavigator();
        var p = nav.Page<P>();
        await AwaitableTest.WaitUntil(() => p.Shown);

        var rootGO = p.ViewBase.transform.root.gameObject;
        Assert.IsTrue(rootGO != null, "사전 조건: 캔버스가 살아 있다");

        nav.Dispose();
        await AwaitableTest.NextFrame();   // Object.Destroy 반영

        Assert.IsTrue(rootGO == null, "Dispose는 캔버스를 파괴해야 한다");
    });

    [UnityTest]
    public IEnumerator Dispose하면_활성presenter가_OnAfterHide까지_teardown된다() => AwaitableTest.Run(async () =>
    {
        var nav = CreateNavigator();
        var p = nav.Page<P>();
        await AwaitableTest.WaitUntil(() => p.Shown);

        nav.Dispose();

        Assert.IsTrue(p.AfterHideCalled,
            "Dispose는 활성 presenter의 수명 콜백을 끝까지 흘려야 한다");
    });

    [UnityTest]
    public IEnumerator 캔버스가_먼저_파괴된_뒤_Dispose해도_예외가_없다() => AwaitableTest.Run(async () =>
    {
        var nav = CreateNavigator();
        var p = nav.Page<P>();
        await AwaitableTest.WaitUntil(() => p.Shown);

        // 씬 언로드 시 GameObject 파괴와 컨테이너 Dispose의 순서는 보장되지 않는다.
        // 캔버스가 먼저 가는 쪽을 재현한다.
        Object.DestroyImmediate(p.ViewBase.transform.root.gameObject);

        Assert.DoesNotThrow(() => nav.Dispose(),
            "이미 파괴된 캔버스에 대한 Dispose는 예외 없이 통과해야 한다");
    });
```

- [ ] **Step 6: 테스트를 돌린다**

UnityMCP `run_tests` — PlayMode 전체. 기대: 추가한 3건 PASS, 나머지 전부 PASS.

3건 중 하나라도 FAIL이면 그것은 **진짜 결함**이다(설계는 이 셋을 현재 구현이 이미 만족한다고 본다). 고친 뒤 진행하고, 무엇이 왜 틀렸는지 커밋 메시지에 적는다.

- [ ] **Step 7: EditMode도 돌린다**

UnityMCP `run_tests` — EditMode 전체. 기대: 전부 PASS.

- [ ] **Step 8: 커밋**

```bash
git add -A
git commit -m "$(cat <<'MSG'
[BEHAVIORAL] Dispose 계약을 잠그고 dispose 이후 캔버스 재생성을 막는다

Root getter에 _disposed 가드를 넣어 dispose 이후 접근이 다음 씬에
고아 캔버스를 만들지 못하게 한다. 캔버스 파괴·teardown·선파괴 안전을
테스트로 고정한다.

Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>
MSG
)"
```

---

### Task 6: 호스트 프로젝트를 씬 스코프로 재배선한다

씬 수명이 실제로 도는지 에디터에서 확인할 수 있게 만드는 태스크다.

**Files:**
- Create: `Assets/Scripts/LifetimeScopes/SceneLifetimeScope.cs`
- Modify: `Assets/Scripts/LifetimeScopes/RootLifetimeScope.cs` (UI 등록·엔트리포인트 제거)
- Modify: `Assets/Scenes/Test.unity` (씬 스코프 GameObject 추가 — UnityMCP `manage_gameobject`)

**Interfaces:**
- Consumes: `RegisterUINavigator(UINavigatorSettings)`, `TestHubBootstrap(IUINavigator)`
- Produces: 없음 (호스트 전용)

- [ ] **Step 1: 씬 스코프를 만든다**

`Assets/Scripts/LifetimeScopes/SceneLifetimeScope.cs`:

```csharp
using DarkNaku.FoundationDI;
using UnityEngine;
using VContainer;
using VContainer.Unity;

/// <summary>
/// 씬 수명 컴포넌트를 등록하는 스코프. UINavigator는 이 스코프가 소유하므로
/// 씬이 언로드되면 캔버스·풀·프리젠터가 함께 파괴된다.
/// IResourceService 등 앱 수명 서비스는 부모(RootLifetimeScope)에서 해결된다.
/// </summary>
public class SceneLifetimeScope : LifetimeScope
{
    // 인스펙터에서 Assets/Settings/UINavigatorSettings.asset 을 연결한다.
    [SerializeField] private UINavigatorSettings _uiNavigatorSettings;

    protected override void Configure(IContainerBuilder builder)
    {
        builder.RegisterUINavigator(_uiNavigatorSettings);
        builder.RegisterEntryPoint<TestHubBootstrap>();
    }
}
```

`TestHubBootstrap`을 여기로 옮기는 이유: 이 엔트리포인트가 `IUINavigator`를 생성자 주입으로 받는다. 루트에 남기면 루트 컨테이너가 `IUINavigator`를 해결하지 못해 컨테이너 빌드가 실패한다.

- [ ] **Step 2: 루트 스코프에서 UI 등록을 뺀다**

`RootLifetimeScope.cs`에서:

- `public UIServiceSettings settings;` → **삭제** (Task 2의 sed로 `UINavigatorSettings settings`가 되어 있다)
- `builder.RegisterUINavigator(settings);` → **삭제**
- `builder.RegisterEntryPoint<TestHubBootstrap>();` → **삭제**

나머지 등록(`IResourceProvider`/`IResourceService`/Message/Injector/Haptic/Initialize/Ad/Analytics/IAP/Tutorial)은 그대로 둔다.

`RegisterInjector`와 `RegisterTutorialManager`를 루트에 남기는 것은 의도적이다 — 스펙 "결정 9"에 따라, 씬 배치 컴포넌트는 `IUINavigator`를 주입받지 않는 것을 권장 경로로 삼는다.

- [ ] **Step 3: 컴파일 확인**

UnityMCP `refresh_unity` → `read_console` (`types: ["error"]`). 기대: 에러 0건.

- [ ] **Step 4: Test.unity에 씬 스코프를 배치한다**

Unity에서 `Assets/Scenes/Test.unity`를 연 뒤:

1. 빈 GameObject `SceneLifetimeScope`를 만들고 `SceneLifetimeScope` 컴포넌트를 붙인다
2. 인스펙터의 `Ui Navigator Settings` 슬롯에 `Assets/Settings/UINavigatorSettings.asset`을 연결한다
3. `LifetimeScope`의 `Parent Reference` 는 **비워 둔다** — VContainer가 런타임에 `LifetimeScope.Find`로 루트를 자동 탐색한다. 루트가 `DontDestroyOnLoad`로 먼저 떠 있어야 하므로, 씬에 `RootLifetimeScope` 프리팹 인스턴스가 함께 있는지 확인한다
4. 씬을 저장한다

`Assets/Scenes/Test2.unity`도 같은 방식으로 배치한다(씬 전환 시 캔버스가 실제로 파괴·재생성되는지 확인하는 대상이다).

- [ ] **Step 5: 플레이 모드로 눈으로 확인한다**

`Test.unity`를 플레이한다. 확인할 것:

1. UI가 정상 표시된다 (콘솔 에러 0건)
2. 하이어라키에서 `[UINavigator]` 캔버스가 **`DontDestroyOnLoad` 아래가 아니라 `Test` 씬 아래**에 있다
3. 플레이를 멈추면 콘솔에 `ObjectDisposedException`이나 `MissingReferenceException`이 남지 않는다

3번에서 예외가 보이면 Task 5의 가드가 종료 경로에서 터지는 것이다 — 멈추고 보고한다.

- [ ] **Step 6: 전체 테스트 실행**

UnityMCP `run_tests` — EditMode, PlayMode. 기대: 전부 PASS.

- [ ] **Step 7: 커밋**

```bash
git add -A
git commit -m "$(cat <<'MSG'
[STRUCTURAL] 호스트 프로젝트를 씬 스코프로 재배선한다

SceneLifetimeScope를 추가해 UINavigator와 TestHubBootstrap을 씬 수명으로 옮긴다.
IResourceService 등 앱 수명 서비스는 루트에 그대로 남는다.

Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>
MSG
)"
```

---

### Task 7: 문서를 갱신하고 0.9.0으로 올린다

**Files:**
- Modify: `Assets/FoundationDI/Runtime/Managers/UINavigator/README.md`
- Modify: `Assets/FoundationDI/README.md`
- Modify: `Assets/FoundationDI/Runtime/Managers/TutorialManager/README.md`
- Modify: `Assets/FoundationDI/Runtime/Managers/TutorialManager/Modules/HighlightModule.cs`, `Modules/TutorialModuleBehaviour.cs` (주석)
- Modify: `Assets/FoundationDI/Runtime/Services/InjectorService/README.md`
- Modify: `Assets/FoundationDI/Samples~/Common/SampleLifetimeScope.cs` 및 각 샘플 README
- Modify: `Assets/FoundationDI/package.json`
- Modify: `CLAUDE.md`
- Modify: `plan.md`

**Interfaces:**
- Consumes: Task 2~6의 최종 이름과 동작
- Produces: 없음

- [ ] **Step 1: UINavigator README를 고친다**

Task 2의 sed가 이름은 이미 바꿔 놨다. **내용이 틀린 곳**만 고친다:

- 상단 특징 목록의 "**상주 캔버스** — 단일 루트 Canvas는 `DontDestroyOnLoad`로 앱 전체에 1개만 상주 … 씬 전환 시 자식 UI만 clear하고 캔버스는 유지" →

```markdown
- **씬 수명 캔버스** — 루트 Canvas는 자신을 만든 씬에 속한다. `UINavigatorSettings.RootPrefab`을 인스턴스화하며(렌더 모드/CanvasScaler/레이어는 프리팹이 결정), 미지정 시 코드 기본값(ScreenSpaceOverlay/1920x1080)으로 폴백. 씬이 언로드되면 캔버스·풀·프리젠터가 함께 파괴된다.
```

- 176행 근처의 같은 취지 문단에서 "어느 경로든 `DontDestroyOnLoad`는 서비스가 인스턴스화 직후 적용하므로 **씬을 넘어 앱 전체에 1개만 상주**합니다" →

```markdown
어느 경로든 상주화는 하지 않습니다 — 루트는 부모 없이 인스턴스화되어 **활성 씬에 붙고, 그 씬과 함께 파괴**됩니다.
```

- **DI 등록 절을 씬 스코프로 고친다.** `builder.RegisterUINavigator(settings)`를 **씬 `LifetimeScope`** 에서 부른다고 명시하고, `IResourceService`는 루트에 있어도 된다고 적는다.

- **마이그레이션 절을 새로 추가한다** (0.8.x → 0.9.0):

```markdown
## 0.8.x → 0.9.0 마이그레이션

| 구 (0.8.x) | 신 (0.9.0) |
|---|---|
| `IUIService` | `IUINavigator` |
| `UIServiceSettings` | `UINavigatorSettings` |
| `builder.RegisterUIService(settings)` | `builder.RegisterUINavigator(settings)` |

**등록 위치가 바뀝니다**: 루트 `LifetimeScope` → 씬 `LifetimeScope`. `IResourceService`는 루트에 남겨도 됩니다(자식 스코프가 부모에서 해결).

**동작이 바뀝니다**: 씬이 언로드되면 캔버스·풀·프리젠터가 모두 파괴됩니다. 씬을 가로질러 살아남아야 하는 UI(로딩 화면·페이드)는 이 컴포넌트 밖에서 별도 캔버스로 만드세요.

**`InjectorService`로 주입되는 씬 배치 컴포넌트는 `IUINavigator`를 해결하지 못합니다.** `InjectorService`는 정적 리졸버 하나를 들고 있어, `RegisterInjector`가 루트에 있으면 씬 배치 MonoBehaviour가 루트 컨테이너로 주입됩니다. `IUINavigator`가 필요하면 `RegisterInjector`도 같은 씬 스코프에 두거나(권장하지 않음), UI를 `UIPresenter`/`View` 계층에서만 다루세요 — 이 경로는 `UIInstanceFactory`가 씬 스코프 리졸버를 쓰므로 정상 동작합니다.

`UIPresenter`/`UIView`/`UIRoot`/`[UIPrefab]`은 이름이 그대로이므로 **프리젠터·뷰 선언부는 손댈 필요가 없습니다.**
```

- **additive 씬을 설명하는 항목을 추가한다** (스펙 결정 6):

```markdown
### additive 씬

씬 둘이 각자 `LifetimeScope`를 가지면 `UINavigator`도 둘, 캔버스도 둘입니다. 각 씬이 자기 UI를 갖는다는 뜻이며 막지 않습니다. 겹침 정렬은 각 `RootPrefab`의 `Canvas.sortingOrder`로 정하세요 — 코어는 관여하지 않습니다.
```

- **알려진 한계를 추가한다** (스펙 결정 4c):

```markdown
### 알려진 한계

캔버스가 이미 파괴됐지만 아직 `Dispose()`가 오지 않은 창(씬 언로드 도중 게임 코드가 UI를 새로 여는 경우)에서 `Page<T>()`를 부르면, 캔버스가 **다음 씬에** 만들어져 고아로 남습니다. 캔버스가 예기치 않게 파괴됐을 때 UI가 영구히 죽지 않도록 재구성 동작을 유지한 결과입니다. 씬 전환 중에는 UI를 새로 열지 마세요.
```

- Task 1에서 지운 편집 환경 메뉴가 README에 남아 있지 않은지 재확인한다.

- [ ] **Step 2: 주변 문서의 전제를 고친다**

- `TutorialManager/README.md` 165행: "타깃이 `UIRoot`(DontDestroyOnLoad) 안에 있든" → "타깃이 `UIRoot`(씬 수명 캔버스) 안에 있든". 모듈이 리페어런팅하지 않는다는 결론은 그대로다.
- `TutorialManager/Modules/HighlightModule.cs:11`, `Modules/TutorialModuleBehaviour.cs:11`: 주석의 `(DontDestroyOnLoad)`를 `(씬 수명 캔버스)`로 바꾼다.
- `InjectorService/README.md` 162행: 루트 단일 스코프 가정을 설명하는 항목에 한 문장 덧붙인다 —

```markdown
  씬 스코프에 등록한 서비스(예: `IUINavigator`)는 이 정적 리졸버로 해결되지 않습니다. 씬 배치 컴포넌트가 그런 서비스를 요구하면 `RegisterInjector`를 같은 씬 스코프에 두어야 합니다.
```

- `Assets/FoundationDI/README.md`: UI 항목의 수명 서술과 등록 위치를 씬 스코프로 고친다.

- [ ] **Step 3: 샘플을 확인한다**

`Samples~/Common/SampleLifetimeScope.cs`는 이미 씬에 배치된 `LifetimeScope`라 Task 2의 sed 이후 그대로 동작한다. 코드 변경은 필요 없다. 다만 클래스 XML 주석에 씬 수명임을 한 줄 명시한다:

```csharp
    /// 샘플 공통 부트스트랩. 각 샘플은 이를 상속해 Configure에서 RegisterEntryPoint로 Demo 드라이버를 추가한다.
    /// 씬에 배치된 스코프이므로 여기 등록된 UINavigator는 씬 수명을 갖는다.
```

`Samples~/01-BasicUsage/README.md` 등 샘플 문서에 남은 수명 서술이 있으면 씬 수명으로 고친다.

```bash
grep -rn "DontDestroyOnLoad\|상주" Assets/FoundationDI/Samples~
```

- [ ] **Step 4: CLAUDE.md를 고친다**

`### 핵심 서비스` 목록의 **UIService 항목을 `Managers` 쪽으로 옮기고** 다음을 반영한다:

- 제목: `**UINavigator** (`Managers/UINavigator/`)`
- 수명 문장: "상주 캔버스: 루트는 … `DontDestroyOnLoad`로 앱 전체에 1개만 상주하며 씬 전환 시 자식 UI만 clear" → "**씬 수명**: 씬 `LifetimeScope`가 소유한다. 루트 캔버스는 활성 씬에 붙고 씬 언로드 시 캔버스·풀·프리젠터가 함께 파괴된다. 정리 경로는 `Dispose()` 하나뿐이다."
- DI 등록 문장: "`builder.RegisterUINavigator(settings)` 확장 메서드(**씬** `LifetimeScope`에서, `IResourceService` 등록 이후에 호출)"
- 에디터 도구 문장에서 Task 1이 지운 항목이 남아 있지 않은지 확인
- `TutorialManager` 항목의 "`UIRoot`의 `DontDestroyOnLoad`와 무관하게 동작한다"를 "`UIRoot`가 씬과 함께 파괴돼도 타깃 소실/복귀를 `TutorialTargetHandle`이 흡수한다"로 고친다
- `PoolManager`·`TutorialManager`와 나란히 놓이도록 `Managers` 문단 순서를 정리한다

- [ ] **Step 5: 버전을 올린다**

`Assets/FoundationDI/package.json`:

```json
  "version": "0.9.0",
```

- [ ] **Step 6: plan.md를 정리한다**

`plan.md`의 `## 활성 계획: 없음` 아래에 완료 섹션을 추가한다(기존 완료 섹션들과 같은 형식):

```markdown
## 완료: UINavigator 씬 수명 전환

UIService를 씬 LifetimeScope가 소유하는 UINavigator로 바꿨다. 캔버스가 활성 씬에
귀속되고, 정리 경로가 Dispose 하나로 줄었다.

세부: `docs/superpowers/specs/2026-08-30-ui-navigator-scene-lifetime-design.md`
계획: `docs/superpowers/plans/2026-08-30-ui-navigator-scene-lifetime.md`

- [x] 캔버스는 상주씬이 아니라 활성씬에 속한다
- [x] 활성씬이 바뀌어도 표시중인 UI를 스스로 리셋하지 않는다
- [x] Dispose 이후 Hide요청이 캔버스를 되살리지 않는다
- [x] Dispose하면 캔버스GO가 파괴된다
- [x] Dispose하면 활성presenter가 OnAfterHide까지 teardown된다
- [x] 캔버스가 먼저 파괴된 뒤 Dispose해도 예외가 없다
```

- [ ] **Step 7: 남은 구 이름을 전수 확인한다**

```bash
grep -rn "UIService" Assets CLAUDE.md plan.md README.md 2>/dev/null | grep -v '\.meta:'
```

기대: **0건.** (`docs/superpowers/specs/`와 `plans/`의 과거 문서는 역사 기록이므로 검사 대상에서 제외한다.)

- [ ] **Step 8: 전체 테스트 실행**

UnityMCP `run_tests` — EditMode, PlayMode. 기대: 전부 PASS.

- [ ] **Step 9: 커밋**

```bash
git add -A
git commit -m "$(cat <<'MSG'
[STRUCTURAL] 문서를 씬 수명에 맞추고 0.9.0으로 올린다

README/CLAUDE.md의 상주 캔버스 서술을 씬 수명으로 고치고,
0.8.x → 0.9.0 마이그레이션 표와 InjectorService 제약을 싣는다.

Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>
MSG
)"
```

---

## 완료 조건

- `grep -rn "UIService" Assets` 가 0건 (`.meta` 제외)
- EditMode + PlayMode 전체 테스트 PASS
- `Test.unity` 플레이 시 `[UINavigator]` 캔버스가 `DontDestroyOnLoad`가 아닌 씬 아래에 생기고, 정지 시 콘솔이 깨끗하다
- `package.json` 이 0.9.0
- 커밋 7개가 STRUCTURAL / BEHAVIORAL로 정확히 갈려 있다
