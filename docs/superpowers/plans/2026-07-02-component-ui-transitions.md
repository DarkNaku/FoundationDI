# 컴포넌트 기반 UI 트랜지션 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.
>
> **이 리포 특이사항:** 실제 실행은 프로젝트 규약(`plan.md` + `/go`→`/green`→`/refactor`→`/commit`)으로 진행한다. 이 문서는 각 테스트의 상세 코드/구현 참조용이며, `plan.md`의 테스트 목록과 1:1 대응한다.

**Goal:** UI 트랜지션을 ScriptableObject 에셋에서 MonoBehaviour 컴포넌트로 전환해, 한 팝업 안에서 배경(페이드)과 컨텐츠(슬라이드/스케일)를 분리 연출한다.

**Architecture:** `UITransitionBehaviour`(abstract MonoBehaviour, `IUITransition`)를 공통 기반으로 `FadeTransition`/`SlideTransition`/`ScaleTransition` 컴포넌트를 만든다. `UIView`는 `GetComponent<IUITransition>()`로 자신의 트랜지션을 해석한다. settings 공통 기본 트랜지션과 에셋 4종은 제거한다.

**Tech Stack:** Unity 6000.3.17f1, UniTask, VContainer, Unity Test Framework(PlayMode), NUnit.

## Global Constraints

- 네임스페이스: `DarkNaku.FoundationDI` (전 파일 공통).
- `IUITransition` 인터페이스 시그니처는 변경하지 않는다: `UniTask ShowAsync(RectTransform target, CancellationToken ct)`, `UniTask HideAsync(RectTransform target, CancellationToken ct)`.
- `Animate`는 트윈 라이브러리 비의존: `UniTask.Yield(PlayerLoopTiming.Update, ct)` + `Time.unscaledDeltaTime`/`Time.deltaTime` 누적 + `AnimationCurve` 보간.
- 테스트 함수 이름은 한국어 의도의 `should~` 형식.
- 구조적(STRUCTURAL)/행동적(BEHAVIORAL) 변경을 같은 커밋에 섞지 않는다. 커밋 제목에 접두어를 단다.
- 컴파일/테스트는 UnityMCP로 수행하고, 스크립트 수정 후 `read_console`로 컴파일 에러를 먼저 확인한다(`editor_state.isCompiling == false` 확인 후 새 타입 사용).
- 배경 페이드 + 컨텐츠 이동은 **병렬**(`UniTask.WhenAll`). 배경 필드가 null이면 페이드 생략.

## File Structure

**신규(런타임)**
- `Assets/FoundationDI/Runtime/Managers/UIManager/Transitions/UITransitionBehaviour.cs` — 공통 기반. `Animate`, duration/ease/unscaledTime.
- `Assets/FoundationDI/Runtime/Managers/UIManager/Transitions/FadeTransition.cs` — 단일 CanvasGroup 페이드.
- `Assets/FoundationDI/Runtime/Managers/UIManager/Transitions/SlideTransition.cs` — 배경 페이드 + 컨텐츠 슬라이드. `SlideDirection` enum 포함.
- `Assets/FoundationDI/Runtime/Managers/UIManager/Transitions/ScaleTransition.cs` — 배경 페이드 + 컨텐츠 스케일.

**신규(테스트)**
- `Assets/FoundationDI/Tests/PlayMode/FoundationDI.PlayModeTests.asmdef`
- `Assets/FoundationDI/Tests/PlayMode/Transitions/FadeTransitionTests.cs`
- `Assets/FoundationDI/Tests/PlayMode/Transitions/SlideTransitionTests.cs`
- `Assets/FoundationDI/Tests/PlayMode/Transitions/ScaleTransitionTests.cs`
- `Assets/FoundationDI/Tests/PlayMode/Transitions/UIViewTransitionResolveTests.cs`
- `Assets/FoundationDI/Tests/PlayMode/Transitions/TransitionTestHelpers.cs` — 테스트 헬퍼(GO 생성, private [SerializeField] 주입).

**수정**
- `Managers/UIManager/Views/UIView.cs`
- `Managers/UIManager/UIManager.cs`
- `Managers/UIManager/Settings/UIManagerSettings.cs`

**삭제**
- `Managers/UIManager/Transitions/UITransitionAsset.cs` (+.meta)
- `Managers/UIManager/Transitions/FadeTransitionAsset.cs` (+.meta)
- `Managers/UIManager/Transitions/ScaleTransitionAsset.cs` (+.meta)
- `Managers/UIManager/Transitions/SlideTransitionAsset.cs` (+.meta)

**유지**
- `Transitions/IUITransition.cs`, `Transitions/NoopTransition.cs`

---

### Task 0: PlayMode 테스트 어셈블리 구성 (STRUCTURAL)

**Files:**
- Create: `Assets/FoundationDI/Tests/PlayMode/FoundationDI.PlayModeTests.asmdef`
- Create: `Assets/FoundationDI/Tests/PlayMode/Transitions/TransitionTestHelpers.cs`

**Interfaces:**
- Produces: `FoundationDI.PlayModeTests` 어셈블리(NUnit/UnityEngine.TestTools/FoundationDI/UniTask 참조). 헬퍼 `TransitionTestHelpers.SetPrivate(object, string, object)`, `TransitionTestHelpers.NewUINode(string, params Type[])`.

- [ ] **Step 1: asmdef 작성**

`FoundationDI.PlayModeTests.asmdef`:
```json
{
    "name": "FoundationDI.PlayModeTests",
    "references": ["FoundationDI", "UniTask"],
    "includePlatforms": ["Editor"],
    "overrideReferences": true,
    "precompiledReferences": ["nunit.framework.dll"],
    "defineConstraints": ["UNITY_INCLUDE_TESTS"],
    "optionalUnityReferences": ["TestAssemblies"]
}
```
(`UniTask` 어셈블리명이 다르면 실제 이름으로 교체 — `Cysharp.Threading.Tasks`일 수 있음. UnityMCP `manage_asset`/프로젝트 참조로 확인 후 지정.)

- [ ] **Step 2: 테스트 헬퍼 작성**

`TransitionTestHelpers.cs`:
```csharp
using System;
using System.Reflection;
using UnityEngine;

namespace DarkNaku.FoundationDI.PlayModeTests
{
    public static class TransitionTestHelpers
    {
        // private [SerializeField] 필드 주입
        public static void SetPrivate(object target, string field, object value)
        {
            var f = target.GetType().GetField(field,
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (f == null) throw new ArgumentException($"field not found: {field}");
            f.SetValue(target, value);
        }

        // RectTransform 노드 생성 (+ 추가 컴포넌트)
        public static GameObject NewUINode(string name, params Type[] extra)
        {
            var go = new GameObject(name, typeof(RectTransform));
            foreach (var t in extra) go.AddComponent(t);
            return go;
        }
    }
}
```

- [ ] **Step 3: 컴파일 확인**

UnityMCP `read_console`로 에러 없음 확인. `editor_state.isCompiling == false` 대기.

- [ ] **Step 4: Commit**

```bash
git add Assets/FoundationDI/Tests/PlayMode
git commit -m "[STRUCTURAL] PlayMode 테스트 어셈블리 및 트랜지션 테스트 헬퍼 추가"
```

---

### Task 1: FadeTransition + UITransitionBehaviour 기반 (BEHAVIORAL)

**Files:**
- Create: `Transitions/UITransitionBehaviour.cs`
- Create: `Transitions/FadeTransition.cs`
- Test: `Tests/PlayMode/Transitions/FadeTransitionTests.cs`

**Interfaces:**
- Consumes: `IUITransition`, `TransitionTestHelpers`.
- Produces:
  - `abstract class UITransitionBehaviour : MonoBehaviour, IUITransition` — `protected UniTask Animate(Action<float> apply, CancellationToken ct)`, 필드 `_duration`/`_ease`/`_unscaledTime`.
  - `sealed class FadeTransition : UITransitionBehaviour` — private `[SerializeField] CanvasGroup _target`. 미지정 시 `target.GetComponent<CanvasGroup>()`.

- [ ] **Step 1: 실패 테스트 작성**

`FadeTransitionTests.cs`:
```csharp
using System.Collections;
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace DarkNaku.FoundationDI.PlayModeTests
{
    public class FadeTransitionTests
    {
        [UnityTest]
        public IEnumerator shouldShow완료후알파가1이됨() => UniTask.ToCoroutine(async () =>
        {
            var go = TransitionTestHelpers.NewUINode("fade", typeof(CanvasGroup), typeof(FadeTransition));
            var cg = go.GetComponent<CanvasGroup>();
            cg.alpha = 0f;
            var fade = go.GetComponent<FadeTransition>();
            TransitionTestHelpers.SetPrivate(fade, "_duration", 0.05f);

            await fade.ShowAsync((RectTransform)go.transform, CancellationToken.None);

            Assert.AreEqual(1f, cg.alpha, 0.001f);
            Object.Destroy(go);
        });

        [UnityTest]
        public IEnumerator shouldHide완료후알파가0이됨() => UniTask.ToCoroutine(async () =>
        {
            var go = TransitionTestHelpers.NewUINode("fade", typeof(CanvasGroup), typeof(FadeTransition));
            var cg = go.GetComponent<CanvasGroup>();
            cg.alpha = 1f;
            var fade = go.GetComponent<FadeTransition>();
            TransitionTestHelpers.SetPrivate(fade, "_duration", 0.05f);

            await fade.HideAsync((RectTransform)go.transform, CancellationToken.None);

            Assert.AreEqual(0f, cg.alpha, 0.001f);
            Object.Destroy(go);
        });
    }
}
```

- [ ] **Step 2: 테스트 실패 확인**

UnityMCP `run_tests`(PlayMode). 기대: `FadeTransition`/`UITransitionBehaviour` 미정의로 컴파일 실패.

- [ ] **Step 3: 최소 구현**

`UITransitionBehaviour.cs`:
```csharp
using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace DarkNaku.FoundationDI
{
    public abstract class UITransitionBehaviour : MonoBehaviour, IUITransition
    {
        [SerializeField] protected float _duration = 0.2f;
        [SerializeField] protected AnimationCurve _ease = AnimationCurve.EaseInOut(0, 0, 1, 1);
        [SerializeField] protected bool _unscaledTime = true;

        public abstract UniTask ShowAsync(RectTransform target, CancellationToken ct);
        public abstract UniTask HideAsync(RectTransform target, CancellationToken ct);

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
    }
}
```

`FadeTransition.cs`:
```csharp
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace DarkNaku.FoundationDI
{
    [AddComponentMenu("DarkNaku/UI Transition/Fade")]
    public sealed class FadeTransition : UITransitionBehaviour
    {
        [SerializeField] private CanvasGroup _target;

        private CanvasGroup Resolve(RectTransform root)
            => _target != null ? _target : root.GetComponent<CanvasGroup>();

        public override UniTask ShowAsync(RectTransform target, CancellationToken ct)
        {
            var cg = Resolve(target);
            return Animate(t => cg.alpha = t, ct);
        }

        public override UniTask HideAsync(RectTransform target, CancellationToken ct)
        {
            var cg = Resolve(target);
            return Animate(t => cg.alpha = 1f - t, ct);
        }
    }
}
```

- [ ] **Step 4: 테스트 통과 확인**

`run_tests`(PlayMode) → 2개 PASS.

- [ ] **Step 5: Commit**

```bash
git add Assets/FoundationDI/Runtime/Managers/UIManager/Transitions/UITransitionBehaviour.cs \
        Assets/FoundationDI/Runtime/Managers/UIManager/Transitions/FadeTransition.cs \
        Assets/FoundationDI/Tests/PlayMode/Transitions/FadeTransitionTests.cs
git commit -m "[BEHAVIORAL] 컴포넌트 FadeTransition 및 UITransitionBehaviour 기반 추가"
```

---

### Task 1b: SlideDirection enum 별도 파일 분리 (STRUCTURAL)

기존 `SlideDirection` enum은 `SlideTransitionAsset.cs`에 정의돼 있다. Task 2에서 신규 `SlideTransition.cs`가 같은 enum을 필요로 하는데, 에셋 파일은 Task 5까지 살아 있으므로 **enum을 중립 파일로 먼저 분리**해 중복 정의를 원천 차단한다. 순수 구조적 변경(행동 불변).

**Files:**
- Create: `Transitions/SlideDirection.cs`
- Modify: `Transitions/SlideTransitionAsset.cs` (enum 선언 제거)

- [ ] **Step 1: enum 신규 파일 생성**

`SlideDirection.cs`:
```csharp
namespace DarkNaku.FoundationDI
{
    public enum SlideDirection { Left, Right, Top, Bottom }
}
```

- [ ] **Step 2: 기존 에셋에서 enum 선언 제거**

`SlideTransitionAsset.cs` 상단의 `public enum SlideDirection { Left, Right, Top, Bottom }` 줄 삭제(동일 네임스페이스라 참조는 그대로 해소됨).

- [ ] **Step 3: 컴파일 확인** — `read_console` 에러 없음.

- [ ] **Step 4: Commit**

```bash
git add Assets/FoundationDI/Runtime/Managers/UIManager/Transitions/SlideDirection.cs \
        Assets/FoundationDI/Runtime/Managers/UIManager/Transitions/SlideTransitionAsset.cs
git commit -m "[STRUCTURAL] SlideDirection enum을 별도 파일로 분리"
```

---

### Task 2: SlideTransition (BEHAVIORAL)

**Files:**
- Create: `Transitions/SlideTransition.cs`
- Test: `Tests/PlayMode/Transitions/SlideTransitionTests.cs`

**Interfaces:**
- Consumes: `UITransitionBehaviour`, `SlideDirection`(Task 1b), `TransitionTestHelpers`.
- Produces: `sealed class SlideTransition : UITransitionBehaviour` — private `[SerializeField] CanvasGroup _background`(선택적), `[SerializeField] RectTransform _content`(미지정 시 `target`), `[SerializeField] SlideDirection _direction`. (`SlideDirection`은 재정의하지 않고 Task 1b의 것을 사용.)

- [ ] **Step 1: 실패 테스트 작성**

`SlideTransitionTests.cs`:
```csharp
using System.Collections;
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace DarkNaku.FoundationDI.PlayModeTests
{
    public class SlideTransitionTests
    {
        private static SlideTransition NewSlide(out GameObject root, out RectTransform content, out CanvasGroup bg)
        {
            root = TransitionTestHelpers.NewUINode("slide", typeof(SlideTransition));
            var contentGo = TransitionTestHelpers.NewUINode("content");
            content = (RectTransform)contentGo.transform;
            content.SetParent(root.transform, false);
            content.sizeDelta = new Vector2(200, 200);
            content.anchoredPosition = Vector2.zero;

            var bgGo = TransitionTestHelpers.NewUINode("bg", typeof(CanvasGroup));
            bg = bgGo.GetComponent<CanvasGroup>();
            bgGo.transform.SetParent(root.transform, false);

            var slide = root.GetComponent<SlideTransition>();
            TransitionTestHelpers.SetPrivate(slide, "_duration", 0.05f);
            return slide;
        }

        [UnityTest]
        public IEnumerator shouldShow완료후컨텐츠가home으로온다() => UniTask.ToCoroutine(async () =>
        {
            var slide = NewSlide(out var root, out var content, out var bg);
            TransitionTestHelpers.SetPrivate(slide, "_content", content);
            TransitionTestHelpers.SetPrivate(slide, "_background", bg);

            await slide.ShowAsync((RectTransform)root.transform, CancellationToken.None);

            Assert.AreEqual(Vector2.zero, content.anchoredPosition);
            Assert.AreEqual(1f, bg.alpha, 0.001f);
            Object.Destroy(root);
        });

        [UnityTest]
        public IEnumerator shouldHide완료후컨텐츠가home으로복원된다() => UniTask.ToCoroutine(async () =>
        {
            var slide = NewSlide(out var root, out var content, out var bg);
            TransitionTestHelpers.SetPrivate(slide, "_content", content);
            TransitionTestHelpers.SetPrivate(slide, "_background", bg);

            await slide.HideAsync((RectTransform)root.transform, CancellationToken.None);

            Assert.AreEqual(Vector2.zero, content.anchoredPosition);
            Object.Destroy(root);
        });

        [UnityTest]
        public IEnumerator should배경이null이면페이드생략하고컨텐츠만이동() => UniTask.ToCoroutine(async () =>
        {
            var slide = NewSlide(out var root, out var content, out var bg);
            TransitionTestHelpers.SetPrivate(slide, "_content", content);
            // _background 미주입

            await slide.ShowAsync((RectTransform)root.transform, CancellationToken.None);

            Assert.AreEqual(Vector2.zero, content.anchoredPosition);
            Object.Destroy(root);
        });

        [UnityTest]
        public IEnumerator should컨텐츠가null이면루트가이동대상() => UniTask.ToCoroutine(async () =>
        {
            var slide = NewSlide(out var root, out var content, out var bg);
            var rootRt = (RectTransform)root.transform;
            rootRt.sizeDelta = new Vector2(100, 100);
            rootRt.anchoredPosition = Vector2.zero;
            // _content, _background 미주입

            await slide.ShowAsync(rootRt, CancellationToken.None);

            Assert.AreEqual(Vector2.zero, rootRt.anchoredPosition);
            Object.Destroy(root);
        });
    }
}
```

- [ ] **Step 2: 테스트 실패 확인**

`run_tests`(PlayMode). 기대: `SlideTransition` 미정의로 컴파일 실패.

- [ ] **Step 3: 최소 구현**

`SlideTransition.cs`:
```csharp
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace DarkNaku.FoundationDI
{
    // SlideDirection은 Task 1b의 SlideDirection.cs에 정의됨 — 여기서 재정의하지 않는다.
    [AddComponentMenu("DarkNaku/UI Transition/Slide")]
    public sealed class SlideTransition : UITransitionBehaviour
    {
        [SerializeField] private CanvasGroup _background;
        [SerializeField] private RectTransform _content;
        [SerializeField] private SlideDirection _direction = SlideDirection.Bottom;

        private RectTransform Content(RectTransform root) => _content != null ? _content : root;

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

        public override UniTask ShowAsync(RectTransform target, CancellationToken ct)
        {
            var content = Content(target);
            var home = content.anchoredPosition;
            var off = home + OffsetFor(content);
            var slide = Animate(t => content.anchoredPosition = Vector2.Lerp(off, home, t), ct);
            if (_background == null) return slide;
            var fade = Animate(t => _background.alpha = t, ct);
            return UniTask.WhenAll(slide, fade);
        }

        public override async UniTask HideAsync(RectTransform target, CancellationToken ct)
        {
            var content = Content(target);
            var home = content.anchoredPosition;
            var off = home + OffsetFor(content);
            var slide = Animate(t => content.anchoredPosition = Vector2.Lerp(home, off, t), ct);
            if (_background == null)
            {
                await slide;
            }
            else
            {
                var fade = Animate(t => _background.alpha = 1f - t, ct);
                await UniTask.WhenAll(slide, fade);
            }
            // 휴지 위치를 home으로 복원(캐시 재사용 시 다음 Show가 화면 밖 좌표를 home으로 캡처하는 것 방지).
            content.anchoredPosition = home;
        }
    }
}
```

- [ ] **Step 4: 테스트 통과 확인**

`run_tests`(PlayMode) → 4개 PASS.

- [ ] **Step 5: Commit**

```bash
git add Assets/FoundationDI/Runtime/Managers/UIManager/Transitions/SlideTransition.cs \
        Assets/FoundationDI/Tests/PlayMode/Transitions/SlideTransitionTests.cs
git commit -m "[BEHAVIORAL] 배경 페이드+컨텐츠 슬라이드 SlideTransition 컴포넌트 추가"
```

---

### Task 3: ScaleTransition (BEHAVIORAL)

**Files:**
- Create: `Transitions/ScaleTransition.cs`
- Test: `Tests/PlayMode/Transitions/ScaleTransitionTests.cs`

**Interfaces:**
- Consumes: `UITransitionBehaviour`, `TransitionTestHelpers`.
- Produces: `sealed class ScaleTransition : UITransitionBehaviour` — private `[SerializeField] CanvasGroup _background`(선택적), `[SerializeField] RectTransform _content`(미지정 시 `target`), `[SerializeField] float _fromScale = 0.8f`.

- [ ] **Step 1: 실패 테스트 작성**

`ScaleTransitionTests.cs`:
```csharp
using System.Collections;
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace DarkNaku.FoundationDI.PlayModeTests
{
    public class ScaleTransitionTests
    {
        [UnityTest]
        public IEnumerator shouldShow완료후컨텐츠스케일이1이고배경알파가1() => UniTask.ToCoroutine(async () =>
        {
            var root = TransitionTestHelpers.NewUINode("scale", typeof(ScaleTransition));
            var contentGo = TransitionTestHelpers.NewUINode("content");
            var content = (RectTransform)contentGo.transform;
            content.SetParent(root.transform, false);
            var bgGo = TransitionTestHelpers.NewUINode("bg", typeof(CanvasGroup));
            var bg = bgGo.GetComponent<CanvasGroup>();
            bgGo.transform.SetParent(root.transform, false);

            var scale = root.GetComponent<ScaleTransition>();
            TransitionTestHelpers.SetPrivate(scale, "_duration", 0.05f);
            TransitionTestHelpers.SetPrivate(scale, "_content", content);
            TransitionTestHelpers.SetPrivate(scale, "_background", bg);

            await scale.ShowAsync((RectTransform)root.transform, CancellationToken.None);

            Assert.AreEqual(Vector3.one, content.localScale);
            Assert.AreEqual(1f, bg.alpha, 0.001f);
            Object.Destroy(root);
        });

        [UnityTest]
        public IEnumerator should배경이null이면스케일만수행() => UniTask.ToCoroutine(async () =>
        {
            var root = TransitionTestHelpers.NewUINode("scale", typeof(ScaleTransition));
            var contentGo = TransitionTestHelpers.NewUINode("content");
            var content = (RectTransform)contentGo.transform;
            content.SetParent(root.transform, false);

            var scale = root.GetComponent<ScaleTransition>();
            TransitionTestHelpers.SetPrivate(scale, "_duration", 0.05f);
            TransitionTestHelpers.SetPrivate(scale, "_content", content);
            // _background 미주입

            await scale.ShowAsync((RectTransform)root.transform, CancellationToken.None);

            Assert.AreEqual(Vector3.one, content.localScale);
            Object.Destroy(root);
        });
    }
}
```

- [ ] **Step 2: 테스트 실패 확인** — `run_tests`(PlayMode). 기대: `ScaleTransition` 미정의로 컴파일 실패.

- [ ] **Step 3: 최소 구현**

`ScaleTransition.cs`:
```csharp
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace DarkNaku.FoundationDI
{
    [AddComponentMenu("DarkNaku/UI Transition/Scale")]
    public sealed class ScaleTransition : UITransitionBehaviour
    {
        [SerializeField] private CanvasGroup _background;
        [SerializeField] private RectTransform _content;
        [SerializeField] private float _fromScale = 0.8f;

        private RectTransform Content(RectTransform root) => _content != null ? _content : root;

        public override UniTask ShowAsync(RectTransform target, CancellationToken ct)
        {
            var content = Content(target);
            var scale = Animate(t => content.localScale = Vector3.one * Mathf.Lerp(_fromScale, 1f, t), ct);
            if (_background == null) return scale;
            var fade = Animate(t => _background.alpha = t, ct);
            return UniTask.WhenAll(scale, fade);
        }

        public override async UniTask HideAsync(RectTransform target, CancellationToken ct)
        {
            var content = Content(target);
            var scale = Animate(t => content.localScale = Vector3.one * Mathf.Lerp(1f, _fromScale, t), ct);
            if (_background == null)
            {
                await scale;
                return;
            }
            var fade = Animate(t => _background.alpha = 1f - t, ct);
            await UniTask.WhenAll(scale, fade);
        }
    }
}
```

- [ ] **Step 4: 테스트 통과 확인** — `run_tests`(PlayMode) → 2개 PASS.

- [ ] **Step 5: Commit**

```bash
git add Assets/FoundationDI/Runtime/Managers/UIManager/Transitions/ScaleTransition.cs \
        Assets/FoundationDI/Tests/PlayMode/Transitions/ScaleTransitionTests.cs
git commit -m "[BEHAVIORAL] 배경 페이드+컨텐츠 스케일 ScaleTransition 컴포넌트 추가"
```

---

### Task 4: UIView가 컴포넌트를 GetComponent로 해석 (BEHAVIORAL)

**Files:**
- Modify: `Views/UIView.cs`
- Test: `Tests/PlayMode/Transitions/UIViewTransitionResolveTests.cs`

**Interfaces:**
- Consumes: `UIView`, `FadeTransition`.
- Produces: `UIView.Resolve()`가 `Transition ?? GetComponent<IUITransition>() ?? Noop` 순으로 해석. per-show `Transition` 프로퍼티는 유지.

- [ ] **Step 1: 실패 테스트 작성**

`UIViewTransitionResolveTests.cs`:
```csharp
using System.Collections;
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace DarkNaku.FoundationDI.PlayModeTests
{
    public class UIViewTransitionResolveTests
    {
        private sealed class TestView : UIView { }

        [UnityTest]
        public IEnumerator should부착된트랜지션컴포넌트를해석한다() => UniTask.ToCoroutine(async () =>
        {
            var go = TransitionTestHelpers.NewUINode("view", typeof(CanvasGroup), typeof(FadeTransition), typeof(TestView));
            var cg = go.GetComponent<CanvasGroup>();
            cg.alpha = 0f;
            var fade = go.GetComponent<FadeTransition>();
            TransitionTestHelpers.SetPrivate(fade, "_duration", 0.05f);
            var view = go.GetComponent<TestView>();

            await view.ShowAsync(CancellationToken.None);

            Assert.AreEqual(1f, cg.alpha, 0.001f); // 컴포넌트 트랜지션이 적용됨
            Object.Destroy(go);
        });

        [UnityTest]
        public IEnumerator should트랜지션컴포넌트없으면Noop으로즉시완료() => UniTask.ToCoroutine(async () =>
        {
            var go = TransitionTestHelpers.NewUINode("view", typeof(CanvasGroup), typeof(TestView));
            var view = go.GetComponent<TestView>();

            await view.ShowAsync(CancellationToken.None); // 예외 없이 즉시 완료

            Assert.Pass();
            Object.Destroy(go);
        });
    }
}
```

- [ ] **Step 2: 테스트 실패 확인**

`run_tests`(PlayMode). 기대: 첫 테스트 FAIL — 현재 `UIView`는 `_transition`(에셋) 기반이라 컴포넌트를 해석하지 않아 `cg.alpha`가 1이 되지 않음.

- [ ] **Step 3: 최소 구현 — UIView.Resolve 변경**

`Views/UIView.cs`에서 `Resolve()`를 컴포넌트 해석으로 교체(캐싱). `_transition`/`DefaultTransition`은 이 커밋에서 참조만 끊고, 필드 제거는 Task 5(구조적)에서 수행:

```csharp
        // 해석 우선순위: per-show 오버라이드(Transition) > 부착된 컴포넌트 > Noop
        public IUITransition Transition { get; set; }

        private IUITransition _componentTransition;
        private bool _resolvedComponent;

        private IUITransition ResolveComponent()
        {
            if (!_resolvedComponent)
            {
                _componentTransition = GetComponent<IUITransition>();
                _resolvedComponent = true;
            }
            return _componentTransition;
        }

        private IUITransition Resolve() => Transition ?? ResolveComponent() ?? Noop;
```

> 주의: `GetComponent<IUITransition>()`가 자기 자신(만약 UIView가 IUITransition을 구현했다면)을 잡지 않도록 `UIView`는 `IUITransition`을 구현하지 않는다(현재도 미구현). `NoopTransition`은 컴포넌트가 아니므로 걸리지 않는다.
> 이 시점에 `DefaultTransition` 프로퍼티는 아직 존재하지만 `Resolve()`에서 참조하지 않는다(UIManager가 여전히 값을 set — 무해). 실제 제거는 Task 5.

- [ ] **Step 4: 테스트 통과 확인**

`run_tests`(PlayMode) → 2개 PASS. 기존 전체 스위트도 실행해 회귀 없음 확인.

- [ ] **Step 5: Commit**

```bash
git add Assets/FoundationDI/Runtime/Managers/UIManager/Views/UIView.cs \
        Assets/FoundationDI/Tests/PlayMode/Transitions/UIViewTransitionResolveTests.cs
git commit -m "[BEHAVIORAL] UIView가 부착된 IUITransition 컴포넌트를 GetComponent로 해석"
```

---

### Task 5: 에셋 트랜지션 및 settings 기본값 제거 (STRUCTURAL)

**Files:**
- Modify: `Views/UIView.cs` (`_transition` 필드, `DefaultTransition` 프로퍼티 제거)
- Modify: `UIManager.cs` (`ShowAsync`의 `defaultTransition` 경로 제거, 호출부 3곳 수정)
- Modify: `Settings/UIManagerSettings.cs` (트랜지션 3필드/프로퍼티 제거)
- Delete: `Transitions/UITransitionAsset.cs`, `FadeTransitionAsset.cs`, `ScaleTransitionAsset.cs`, `SlideTransitionAsset.cs` (+.meta)

**Interfaces:**
- Consumes: 없음(제거 전용).
- Produces: `UIManager`의 `ShowAsync(UIPresenter presenter, CancellationToken ct)` (파라미터 축소). `UIManagerSettings`는 `ReferenceResolution`만 노출.

> 구조적 변경 전용(행동 불변). 순수 제거이므로 신규 테스트 없음. 기존 전체 스위트가 계속 통과하는지로 검증한다.

- [ ] **Step 1: UIView 정리**

`Views/UIView.cs`에서 다음 두 줄 제거:
```csharp
[SerializeField] private UITransitionAsset _transition;   // 제거
public IUITransition DefaultTransition { get; set; }      // 제거
```

- [ ] **Step 2: UIManager 정리**

`ShowAsync` 시그니처에서 `IUITransition defaultTransition` 제거, 본문에서 `presenter.ViewBase.DefaultTransition = defaultTransition;` 제거:
```csharp
        private async UniTask ShowAsync(UIPresenter presenter, CancellationToken ct)
        {
            presenter.ViewBase.gameObject.SetActive(true);
            presenter.ViewBase.Transition = presenter.TransitionOverride;
            presenter.OnBeforeShow();
            presenter.Fire(UIPresenter.LifecycleEvent.BeforeShow);
            await presenter.ViewBase.ShowAsync(ct);
            presenter.OnAfterShow();
            presenter.Fire(UIPresenter.LifecycleEvent.AfterShow);
        }
```
호출부 3곳 수정:
```csharp
await ShowAsync(presenter, ct);   // ShowPageAsync
await ShowAsync(presenter, ct);   // ShowOverlayAsync
await ShowAsync(presenter, ct);   // ShowPopupAsync
```

- [ ] **Step 3: UIManagerSettings 정리**

`Settings/UIManagerSettings.cs`에서 트랜지션 3필드 + 3프로퍼티 제거, `ReferenceResolution`만 유지:
```csharp
using UnityEngine;

namespace DarkNaku.FoundationDI
{
    [CreateAssetMenu(fileName = "UIManagerSettings", menuName = "DarkNaku/UIManagerSettings")]
    public sealed class UIManagerSettings : ScriptableObject
    {
        [SerializeField] private Vector2 _referenceResolution = new Vector2(1920f, 1080f);

        // CanvasScaler(Scale With Screen Size, Expand)의 기준 해상도
        public Vector2 ReferenceResolution => _referenceResolution;
    }
}
```

- [ ] **Step 4: 에셋 트랜지션 파일 삭제**

UnityMCP `delete_script`(또는 `manage_asset` 삭제)로 `.cs`+`.meta` 제거:
- `Transitions/UITransitionAsset.cs`
- `Transitions/FadeTransitionAsset.cs`
- `Transitions/ScaleTransitionAsset.cs`
- `Transitions/SlideTransitionAsset.cs`

- [ ] **Step 5: 컴파일 + 전체 테스트**

`read_console`로 컴파일 에러 없음 확인. `run_tests`(EditMode + PlayMode) 전체 GREEN 확인.

- [ ] **Step 6: Commit**

```bash
git add -A Assets/FoundationDI/Runtime/Managers/UIManager
git commit -m "[STRUCTURAL] 에셋 트랜지션 4종·settings 기본 트랜지션·UIView 에셋 필드 제거"
```

---

### Task 6: README 갱신 (STRUCTURAL/문서)

**Files:**
- Modify: `Managers/UIManager/README.md`

- [ ] **Step 1: 트랜지션 절 갱신**

에셋(`CreateAssetMenu`) 기반 설명을 컴포넌트 기반으로 교체. 포함 내용:
- 트랜지션은 View 루트 GameObject에 `FadeTransition`/`SlideTransition`/`ScaleTransition` 컴포넌트를 부착해 사용한다.
- Slide/Scale은 인스펙터에서 `_background`(선택적 CanvasGroup, 미지정 시 페이드 생략)와 `_content`(미지정 시 View 루트) 지정.
- 해석 우선순위: `.WithTransition()` per-show 오버라이드 > 부착 컴포넌트 > Noop.
- settings 공통 기본 트랜지션은 제거됨.

- [ ] **Step 2: Commit**

```bash
git add Assets/FoundationDI/Runtime/Managers/UIManager/README.md
git commit -m "[STRUCTURAL] UIManager README 트랜지션 절 컴포넌트 방식으로 갱신"
```

---

## Self-Review

**Spec coverage:**
- 전체 컴포넌트 전환 → Task 1~3(신규 컴포넌트), Task 5(에셋 제거). ✅
- GetComponent 자동 탐색 → Task 4. ✅
- settings 기본값 제거 → Task 5. ✅
- 병렬(WhenAll) 재생 → Task 2/3 구현. ✅
- 배경 선택적 → Task 2/3 `_background == null` 분기 + 테스트. ✅
- 컨텐츠 미지정 폴백 → Task 2 `should컨텐츠가null이면루트가이동대상`. ✅
- PlayMode 테스트 전략 → Task 0 어셈블리. ✅
- README → Task 6. ✅

**Placeholder scan:** 모든 코드 스텝에 실제 코드 포함. UniTask 어셈블리명만 프로젝트 확인 필요(Task 0 Step 1 주석에 명시). 그 외 TBD 없음.

**Type consistency:** `UITransitionBehaviour.Animate`(Task 1) ↔ Task 2/3에서 동일 시그니처 사용. `SlideDirection` 중복 정의 위험은 **Task 1b**(enum을 `SlideDirection.cs`로 분리)로 해소 — Task 2 신규 파일과 기존 에셋이 동일 정의를 공유하므로 충돌 없음. Task 5에서 에셋 삭제 시에도 enum은 별도 파일에 남아 안전. ✅

**태스크 순서:** Task 0 → 1 → 1b → 2 → 3 → 4 → 5 → 6. (1b는 순수 구조적이므로 1 이후 어디든 삽입 가능하나 2 이전 필수.)
