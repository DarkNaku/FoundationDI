# HapticService MOST급 재구현 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 기존 HapticService를 폐기하고 프리셋 + AnimationCurve 커브 + 커스텀 패턴 + 단일 활성 재생을 갖춘 MOST급 햅틱을 FoundationDI의 DI/seam 구조 + `Awaitable`로 clean-room 재구현한다.

**Architecture:** 정책 계층(`HapticService`: Enabled 게이트·쿨다운·단일 활성 재생 관리)과 플랫폼 어댑터(`IHapticProvider`: 네이티브 발동·커브/패턴 재생·케이퍼빌리티 폴백)를 분리한다. 플랫폼 비대칭(iOS 패턴=프리셋 시퀀스, Android=웨이브폼)을 provider의 `Awaitable PlayAsync` 안에 가둬 서비스는 플랫폼 무관 정책만 담고, 대역 provider로 EditMode 단위 테스트한다.

**Tech Stack:** Unity 6000.3.17f1, VContainer(DI), `UnityEngine.Awaitable`(async), NSubstitute(mock), Unity Test Framework(EditMode), Objective-C(iOS CoreHaptics/UIKit), `AndroidJavaObject`(Android VibrationEffect).

## Global Constraints

- 네임스페이스: `DarkNaku.FoundationDI` (모든 신규 파일).
- 위치: `Assets/FoundationDI/Runtime/Services/HapticService/`. 테스트: `Assets/FoundationDI/Tests/`.
- 신규 async 표면은 `UnityEngine.Awaitable` 사용. `Task`/`UniTask`를 새 표면에 추가 금지.
- 테스트 함수명은 한국어, 의도 서술형.
- 모킹은 NSubstitute. 테스트 어셈블리 `FoundationDI.Tests`(EditMode).
- 컴파일·테스트는 UnityMCP로: `refresh_unity(compile=request, scope=all)` → `read_console(types=[error])`; `run_tests(mode=EditMode, assembly_names=["FoundationDI.Tests"])` → `get_test_job` 폴링.
- 커밋: 구조/행동 변경을 섞지 않는다. 신규 기능 추가는 `[BEHAVIORAL]`, 커밋 메시지 끝에 `Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>`.
- `#if UNITY_IOS && !UNITY_EDITOR` / `#if UNITY_ANDROID && !UNITY_EDITOR` 로 감싼 네이티브 provider 본문은 에디터 컴파일에 포함되지 않아 `read_console`로 검증 불가 → 실기기/플랫폼 빌드에서 수동 검증(설계 문서와 동일 모델).

---

### Task 1: 데이터 모델 — enum + 커브/패턴 구조체

**Files:**
- Create: `Assets/FoundationDI/Runtime/Services/HapticService/HapticEnums.cs`
- Create: `Assets/FoundationDI/Runtime/Services/HapticService/HapticCurve.cs`
- Create: `Assets/FoundationDI/Runtime/Services/HapticService/HapticPattern.cs`
- Test: `Assets/FoundationDI/Tests/HapticDataTest.cs`

**Interfaces:**
- Consumes: 없음.
- Produces:
  - `enum HapticImpact { Light, Medium, Heavy, Soft, Rigid }`
  - `enum HapticNotification { Success, Warning, Error }`
  - `enum HapticPreset { Selection, Success, Warning, Error, LightImpact, MediumImpact, HeavyImpact, SoftImpact, RigidImpact }`
  - `struct iOSHapticCurve { AnimationCurve Intensity; float DurationMs; float Sharpness; int Samples; float DelayMs; HapticImpact Fallback; }`
  - `struct AndroidHapticCurve { AnimationCurve Intensity; long DurationMs; int MaxAmplitude; int Samples; long DelayMs; HapticImpact Fallback; }`
  - `struct HapticCurve { iOSHapticCurve IOS; AndroidHapticCurve Android; HapticCurve(AnimationCurve, float durationMs=160, float sharpness=0.6f, int samples=16, HapticImpact fallback=Medium, float delayMs=0, int androidMaxAmplitude=255); HapticCurve(iOSHapticCurve, AndroidHapticCurve); }`
  - `struct iOSPulse { HapticPreset Preset; float DelayMs; }`
  - `struct AndroidPulse { long DelayMs; long PulseMs; int Amplitude; }`
  - `struct HapticPattern { iOSPulse[] IOS; AndroidPulse[] Android; HapticPattern(iOSPulse[], AndroidPulse[]); }`

- [ ] **Step 1: 실패 테스트 작성** — `HapticDataTest.cs` 를 Write로 생성

```csharp
using DarkNaku.FoundationDI;
using NUnit.Framework;
using UnityEngine;

public class HapticDataTest
{
    [Test]
    public void HapticCurve_편의생성자는_양_플랫폼_구조체를_기본값으로_채운다()
    {
        var intensity = AnimationCurve.Linear(0f, 0f, 1f, 1f);

        var hc = new HapticCurve(intensity, durationMs: 200f, sharpness: 0.4f, samples: 8,
                                 fallback: HapticImpact.Heavy, delayMs: 10f, androidMaxAmplitude: 200);

        Assert.AreSame(intensity, hc.IOS.Intensity);
        Assert.AreEqual(200f, hc.IOS.DurationMs);
        Assert.AreEqual(0.4f, hc.IOS.Sharpness);
        Assert.AreEqual(8, hc.IOS.Samples);
        Assert.AreEqual(10f, hc.IOS.DelayMs);
        Assert.AreEqual(HapticImpact.Heavy, hc.IOS.Fallback);

        Assert.AreSame(intensity, hc.Android.Intensity);
        Assert.AreEqual(200L, hc.Android.DurationMs);
        Assert.AreEqual(200, hc.Android.MaxAmplitude);
        Assert.AreEqual(8, hc.Android.Samples);
        Assert.AreEqual(10L, hc.Android.DelayMs);
        Assert.AreEqual(HapticImpact.Heavy, hc.Android.Fallback);
    }

    [Test]
    public void HapticPattern_생성자는_양_플랫폼_배열을_보관한다()
    {
        var ios = new[] { new iOSPulse { Preset = HapticPreset.Selection, DelayMs = 0f } };
        var android = new[] { new AndroidPulse { DelayMs = 0L, PulseMs = 50L, Amplitude = 180 } };

        var p = new HapticPattern(ios, android);

        Assert.AreSame(ios, p.IOS);
        Assert.AreSame(android, p.Android);
    }
}
```

- [ ] **Step 2: 컴파일 확인(실패 예상)** — `refresh_unity(compile=request, scope=all)` 후 `read_console(types=[error])`. Expected: `HapticCurve`/`iOSPulse` 등 미정의로 컴파일 에러.

- [ ] **Step 3: enum 파일 작성** — `HapticEnums.cs`

```csharp
namespace DarkNaku.FoundationDI
{
    public enum HapticImpact { Light, Medium, Heavy, Soft, Rigid }

    public enum HapticNotification { Success, Warning, Error }

    // 패턴 펄스 저작 전용 플랫 enum (Impact/Notification/Selection 계열을 하나로 지목)
    public enum HapticPreset
    {
        Selection,
        Success, Warning, Error,
        LightImpact, MediumImpact, HeavyImpact,
        SoftImpact, RigidImpact
    }
}
```

- [ ] **Step 4: 커브 구조체 작성** — `HapticCurve.cs`

```csharp
using UnityEngine;

namespace DarkNaku.FoundationDI
{
    public struct iOSHapticCurve
    {
        public AnimationCurve Intensity; // X:0..1(정규화 시간), Y:0..1(세기)
        public float DurationMs;
        public float Sharpness;          // 0..1 (CoreHaptics)
        public int Samples;              // 컨트롤 포인트 2..64
        public float DelayMs;
        public HapticImpact Fallback;    // CoreHaptics 미지원 → 프리셋 폴백
    }

    public struct AndroidHapticCurve
    {
        public AnimationCurve Intensity;
        public long DurationMs;
        public int MaxAmplitude;         // 1..255
        public int Samples;              // waveform 세그먼트 2..64
        public long DelayMs;
        public HapticImpact Fallback;    // 진폭제어 미지원 → 프리셋 폴백
    }

    public struct HapticCurve
    {
        public iOSHapticCurve IOS;
        public AndroidHapticCurve Android;

        // 간단: 곡선 하나 + 기본값 → 양 플랫폼 동시 세팅
        public HapticCurve(AnimationCurve intensity, float durationMs = 160f, float sharpness = 0.6f,
                           int samples = 16, HapticImpact fallback = HapticImpact.Medium,
                           float delayMs = 0f, int androidMaxAmplitude = 255)
        {
            IOS = new iOSHapticCurve
            {
                Intensity = intensity,
                DurationMs = durationMs,
                Sharpness = sharpness,
                Samples = samples,
                DelayMs = delayMs,
                Fallback = fallback
            };
            Android = new AndroidHapticCurve
            {
                Intensity = intensity,
                DurationMs = (long)durationMs,
                MaxAmplitude = androidMaxAmplitude,
                Samples = samples,
                DelayMs = (long)delayMs,
                Fallback = fallback
            };
        }

        // 정밀: 각 플랫폼 독립 캘리브레이션
        public HapticCurve(iOSHapticCurve ios, AndroidHapticCurve android)
        {
            IOS = ios;
            Android = android;
        }
    }
}
```

- [ ] **Step 5: 패턴 구조체 작성** — `HapticPattern.cs`

```csharp
namespace DarkNaku.FoundationDI
{
    public struct iOSPulse
    {
        public HapticPreset Preset; // 이 펄스 전 지연 후 발동할 프리셋
        public float DelayMs;
    }

    public struct AndroidPulse
    {
        public long DelayMs;   // 이 펄스 전 무진동 지연
        public long PulseMs;   // 진동 지속
        public int Amplitude;  // 0..255
    }

    public struct HapticPattern
    {
        public iOSPulse[] IOS;
        public AndroidPulse[] Android;

        public HapticPattern(iOSPulse[] ios, AndroidPulse[] android)
        {
            IOS = ios;
            Android = android;
        }
    }
}
```

- [ ] **Step 6: 컴파일 + 테스트 통과 확인** — `refresh_unity` → `read_console(types=[error])`(에러 없음) → `run_tests(mode=EditMode, assembly_names=["FoundationDI.Tests"])` → `get_test_job`. Expected: 신규 2개 PASS(기존 테스트 유지).

- [ ] **Step 7: 커밋**

```bash
git add Assets/FoundationDI/Runtime/Services/HapticService/HapticEnums.cs \
        Assets/FoundationDI/Runtime/Services/HapticService/HapticCurve.cs \
        Assets/FoundationDI/Runtime/Services/HapticService/HapticPattern.cs \
        Assets/FoundationDI/Tests/HapticDataTest.cs
git commit -m "[BEHAVIORAL] 햅틱 데이터 모델(enum/커브/패턴) 추가

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

### Task 2: `IHapticProvider` 인터페이스 + `NoopHapticProvider`

**Files:**
- Modify(전면 재작성): `Assets/FoundationDI/Runtime/Services/HapticService/IHapticProvider.cs`
- Modify(전면 재작성): `Assets/FoundationDI/Runtime/Services/HapticService/Providers/NoopHapticProvider.cs`
- Test: `Assets/FoundationDI/Tests/HapticServiceTest.cs` (전면 재작성 시작 — 이 태스크에선 Noop 테스트만; 이후 태스크가 같은 파일에 추가)

**Interfaces:**
- Consumes: Task 1의 `HapticImpact`, `HapticNotification`, `HapticCurve`, `HapticPattern`.
- Produces:
  - `interface IHapticProvider { void Impact(HapticImpact); void Notification(HapticNotification); void Selection(); Awaitable PlayAsync(HapticCurve, CancellationToken); Awaitable PlayAsync(HapticPattern, CancellationToken); void Stop(); void Prewarm(); }`
  - `class NoopHapticProvider : IHapticProvider` — 모든 메서드 무동작, `PlayAsync`는 즉시완료 `Awaitable` 반환.

- [ ] **Step 1: 실패 테스트 작성** — `HapticServiceTest.cs` 를 Write로 생성(이후 태스크가 확장)

```csharp
using System.Collections;
using System.Threading;
using Cysharp.Threading.Tasks;
using DarkNaku.FoundationDI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

public class HapticServiceTest
{
    [Test]
    public void Noop_provider의_프리셋은_예외없이_무동작한다()
    {
        var provider = new NoopHapticProvider();

        Assert.DoesNotThrow(() =>
        {
            provider.Impact(HapticImpact.Light);
            provider.Notification(HapticNotification.Error);
            provider.Selection();
            provider.Stop();
            provider.Prewarm();
        });
    }

    [UnityTest]
    public IEnumerator Noop_provider의_PlayAsync는_즉시완료된다() => UniTask.ToCoroutine(async () =>
    {
        var provider = new NoopHapticProvider();

        var a = provider.PlayAsync(default(HapticCurve), CancellationToken.None);
        await a;
        Assert.IsTrue(a.IsCompleted);
    });
}
```

- [ ] **Step 2: 컴파일 확인(실패 예상)** — `refresh_unity` → `read_console(types=[error])`. Expected: `NoopHapticProvider`가 새 인터페이스를 아직 구현 안 해 에러.

- [ ] **Step 3: 인터페이스 재작성** — `IHapticProvider.cs`

```csharp
using System.Threading;
using UnityEngine;

namespace DarkNaku.FoundationDI
{
    public interface IHapticProvider
    {
        void Impact(HapticImpact style);
        void Notification(HapticNotification type);
        void Selection();

        // 커브/패턴 재생. 완료(또는 취소) 시 완료되는 Awaitable을 반환한다.
        Awaitable PlayAsync(HapticCurve curve, CancellationToken cancellationToken);
        Awaitable PlayAsync(HapticPattern pattern, CancellationToken cancellationToken);

        void Stop();
        void Prewarm();
    }
}
```

- [ ] **Step 4: Noop 재작성** — `Providers/NoopHapticProvider.cs`

```csharp
using System.Threading;
using UnityEngine;

namespace DarkNaku.FoundationDI
{
    /// <summary>햅틱 미지원 플랫폼(에디터·데스크톱)용. 모든 호출을 무시한다.</summary>
    public class NoopHapticProvider : IHapticProvider
    {
        public void Impact(HapticImpact style) { }
        public void Notification(HapticNotification type) { }
        public void Selection() { }

        public Awaitable PlayAsync(HapticCurve curve, CancellationToken cancellationToken) => Completed();
        public Awaitable PlayAsync(HapticPattern pattern, CancellationToken cancellationToken) => Completed();

        public void Stop() { }
        public void Prewarm() { }

        private static Awaitable Completed()
        {
            var source = new AwaitableCompletionSource();
            source.SetResult();
            return source.Awaitable;
        }
    }
}
```

- [ ] **Step 5: 컴파일 + 테스트 통과 확인** — `refresh_unity` → `read_console`(에러 없음). 단, 기존 `iOSHapticProvider`/`AndroidHapticProvider`/`HapticService`가 아직 옛 인터페이스라 이 시점엔 컴파일 에러가 남는다. **이 태스크는 Task 6~8 완료 전까지 전체 컴파일이 깨진 상태를 허용**하지 않으므로, 순서상 Task 2에서는 옛 `iOSHapticProvider.cs`/`AndroidHapticProvider.cs`/`HapticService.cs`/`HapticServiceTest`(구버전)를 **먼저 임시 제거**해야 한다. → Step 5a 참고.

- [ ] **Step 5a: 구버전 파일 임시 정리** — 아래를 삭제(새 구현은 Task 3~8에서 채운다). 삭제 후 재컴파일하면 `HapticService`/provider 미정의로 에러가 나지만, Task 3 이후 순차 복구된다. 안전하게 하려면 Task 2~5를 하나의 커밋으로 묶지 말고, **Task 3(HapticService)까지 작성한 뒤 함께 컴파일 통과**시키는 것을 권장.

```bash
git rm Assets/FoundationDI/Runtime/Services/HapticService/HapticService.cs \
       Assets/FoundationDI/Runtime/Services/HapticService/Providers/iOSHapticProvider.cs \
       Assets/FoundationDI/Runtime/Services/HapticService/Providers/AndroidHapticProvider.cs
```

- [ ] **Step 6: 커밋 보류** — Task 2의 산출물(인터페이스+Noop+테스트)은 Task 3(HapticService)와 함께 컴파일이 통과하는 시점에 커밋한다. (전체가 깨진 중간 상태를 커밋하지 않기 위함.)

> **Note:** Task 2·3은 컴파일 의존성이 얽혀 있어 **한 커밋 단위**로 처리한다. Task 3 Step 마지막에서 함께 컴파일·테스트·커밋한다.

---

### Task 3: `HapticService` — Enabled 게이트 · 프리셋 위임 · 쿨다운

**Files:**
- Create: `Assets/FoundationDI/Runtime/Services/HapticService/HapticService.cs`
- Modify: `Assets/FoundationDI/Tests/HapticServiceTest.cs` (프리셋/쿨다운 테스트 추가)

**Interfaces:**
- Consumes: Task 1 enum, Task 2 `IHapticProvider`.
- Produces:
  - `interface IHapticService : IDisposable { void Impact(HapticImpact, float cooldown=0.02f); void Notification(HapticNotification, float cooldown=0.02f); void Selection(float cooldown=0.02f); Awaitable Play(HapticCurve); Awaitable Play(HapticPattern); void Stop(); bool IsPlaying { get; } bool Enabled { get; set; } void Prewarm(); }`
  - `class HapticService : IHapticService` — 생성자 `HapticService(IHapticProvider provider, Func<float> nowSeconds = null)`, `[Inject] HapticService()`.
  - `static class HapticServiceVContainerExtensions { void RegisterHapticService(this IContainerBuilder); }`
  - 쿨다운 클럭 seam: `nowSeconds` 기본 `() => Time.unscaledTime`.

- [ ] **Step 1: 실패 테스트 추가** — `HapticServiceTest.cs` 에 아래 메서드 추가(기존 Noop 테스트 유지)

```csharp
    [Test]
    public void Enabled_기본값은_true이다()
    {
        var sut = new HapticService(Substitute.For<IHapticProvider>());
        Assert.IsTrue(sut.Enabled);
    }

    [Test]
    public void Enabled_설정값은_PlayerPrefs에_영속화된다()
    {
        new HapticService(Substitute.For<IHapticProvider>()).Enabled = false;
        var reloaded = new HapticService(Substitute.For<IHapticProvider>());
        Assert.IsFalse(reloaded.Enabled);
    }

    [Test]
    public void 활성화_상태에서_Impact는_provider에_같은_스타일로_위임한다()
    {
        var provider = Substitute.For<IHapticProvider>();
        var sut = new HapticService(provider) { Enabled = true };
        sut.Impact(HapticImpact.Heavy, cooldown: 0f);
        provider.Received(1).Impact(HapticImpact.Heavy);
    }

    [Test]
    public void 비활성화_상태에서는_어떤_provider_프리셋도_호출하지_않는다()
    {
        var provider = Substitute.For<IHapticProvider>();
        var sut = new HapticService(provider) { Enabled = false };
        sut.Impact(HapticImpact.Medium, cooldown: 0f);
        sut.Notification(HapticNotification.Success, cooldown: 0f);
        sut.Selection(cooldown: 0f);
        provider.DidNotReceive().Impact(Arg.Any<HapticImpact>());
        provider.DidNotReceive().Notification(Arg.Any<HapticNotification>());
        provider.DidNotReceive().Selection();
    }

    [Test]
    public void 쿨다운_간격_내_재호출은_무시된다()
    {
        var provider = Substitute.For<IHapticProvider>();
        float t = 100f;
        var sut = new HapticService(provider, () => t) { Enabled = true };
        sut.Impact(HapticImpact.Medium, cooldown: 0.02f); // t=100 발동
        t = 100.01f;                                       // +10ms < 20ms
        sut.Impact(HapticImpact.Medium, cooldown: 0.02f); // 무시
        provider.Received(1).Impact(HapticImpact.Medium);
    }

    [Test]
    public void cooldown_0이면_항상_발동한다()
    {
        var provider = Substitute.For<IHapticProvider>();
        float t = 100f;
        var sut = new HapticService(provider, () => t) { Enabled = true };
        sut.Impact(HapticImpact.Medium, cooldown: 0f);
        sut.Impact(HapticImpact.Medium, cooldown: 0f);
        provider.Received(2).Impact(HapticImpact.Medium);
    }

    [Test]
    public void 쿨다운은_프리셋_전체가_단일_타임스탬프를_공유한다()
    {
        var provider = Substitute.For<IHapticProvider>();
        float t = 100f;
        var sut = new HapticService(provider, () => t) { Enabled = true };
        sut.Impact(HapticImpact.Light, cooldown: 0.02f); // 발동
        t = 100.005f;                                     // +5ms
        sut.Selection(cooldown: 0.02f);                   // 공유 타임스탬프 → 무시
        provider.Received(1).Impact(HapticImpact.Light);
        provider.DidNotReceive().Selection();
    }
```

그리고 파일 상단 using에 `using NSubstitute;`, `using System;` 추가.

- [ ] **Step 2: 컴파일 확인(실패 예상)** — `refresh_unity` → `read_console(types=[error])`. Expected: `HapticService`/`IHapticService` 미정의 에러.

- [ ] **Step 3: `HapticService.cs` 작성**

```csharp
using System;
using System.Threading;
using UnityEngine;
using VContainer;

namespace DarkNaku.FoundationDI
{
    public interface IHapticService : IDisposable
    {
        void Impact(HapticImpact style, float cooldown = 0.02f);
        void Notification(HapticNotification type, float cooldown = 0.02f);
        void Selection(float cooldown = 0.02f);

        Awaitable Play(HapticCurve curve);
        Awaitable Play(HapticPattern pattern);
        void Stop();
        bool IsPlaying { get; }

        bool Enabled { get; set; }
        void Prewarm();
    }

    public class HapticService : IHapticService
    {
        private const string HAPTIC_ENABLED = "HAPTIC_ENABLED";

        private readonly IHapticProvider _provider;
        private readonly Func<float> _now;

        // 모터는 하나라 프리셋 전체가 쿨다운 타임스탬프를 공유한다.
        private float _lastPresetTime = float.MinValue;

        private CancellationTokenSource _cts;
        private Awaitable _active;

        [Inject]
        public HapticService() : this(CreatePlatformProvider())
        {
        }

        public HapticService(IHapticProvider provider, Func<float> nowSeconds = null)
        {
            _provider = provider;
            _now = nowSeconds ?? (() => Time.unscaledTime);
        }

        private static IHapticProvider CreatePlatformProvider()
        {
#if UNITY_IOS && !UNITY_EDITOR
            return new iOSHapticProvider();
#elif UNITY_ANDROID && !UNITY_EDITOR
            return new AndroidHapticProvider();
#else
            return new NoopHapticProvider();
#endif
        }

        public bool Enabled
        {
            get => PlayerPrefs.GetInt(HAPTIC_ENABLED, 1) != 0;
            set { PlayerPrefs.SetInt(HAPTIC_ENABLED, value ? 1 : 0); PlayerPrefs.Save(); }
        }

        public void Impact(HapticImpact style, float cooldown = 0.02f)
        {
            if (!Enabled || !TryConsumeCooldown(cooldown)) return;
            _provider.Impact(style);
        }

        public void Notification(HapticNotification type, float cooldown = 0.02f)
        {
            if (!Enabled || !TryConsumeCooldown(cooldown)) return;
            _provider.Notification(type);
        }

        public void Selection(float cooldown = 0.02f)
        {
            if (!Enabled || !TryConsumeCooldown(cooldown)) return;
            _provider.Selection();
        }

        private bool TryConsumeCooldown(float cooldown)
        {
            float now = _now();
            if (now - _lastPresetTime < cooldown) return false;
            _lastPresetTime = now;
            return true;
        }

        // Task 4에서 구현.
        public async Awaitable Play(HapticCurve curve)
        {
            if (!Enabled) return;
            Stop();
            var cts = _cts = new CancellationTokenSource();
            try { _active = _provider.PlayAsync(curve, cts.Token); await _active; }
            catch (OperationCanceledException) { }
            finally { if (_cts == cts) { _cts = null; _active = null; } cts.Dispose(); }
        }

        public async Awaitable Play(HapticPattern pattern)
        {
            if (!Enabled) return;
            Stop();
            var cts = _cts = new CancellationTokenSource();
            try { _active = _provider.PlayAsync(pattern, cts.Token); await _active; }
            catch (OperationCanceledException) { }
            finally { if (_cts == cts) { _cts = null; _active = null; } cts.Dispose(); }
        }

        public bool IsPlaying => _active != null && !_active.IsCompleted;

        public void Stop()
        {
            if (_cts == null) return;
            _cts.Cancel();
            _provider.Stop();
        }

        public void Prewarm() => _provider.Prewarm();

        public void Dispose() => Stop();
    }

    public static class HapticServiceVContainerExtensions
    {
        /// <summary>HapticService를 컨테이너에 등록한다. 외부 리소스 의존이 없어 추가 인자는 불필요하다.</summary>
        public static void RegisterHapticService(this IContainerBuilder builder)
        {
            builder.Register<IHapticService, HapticService>(Lifetime.Singleton);
        }
    }
}
```

- [ ] **Step 4: 컴파일 확인** — `refresh_unity` → `read_console(types=[error])`. Expected: 여전히 옛 `iOSHapticProvider`/`AndroidHapticProvider`가 삭제됐다면 에러 없음(플랫폼 provider는 Task 6~8에서 채우되, 에디터에선 `NoopHapticProvider`만 참조되므로 컴파일 OK). iOS/Android provider 파일이 아직 없으면 `#if UNITY_IOS`/`#if UNITY_ANDROID` 참조는 에디터 컴파일에서 제외되어 문제 없음.

- [ ] **Step 5: 테스트 통과 확인** — `run_tests(mode=EditMode, assembly_names=["FoundationDI.Tests"])` → `get_test_job`. Expected: Task 2 Noop 2개 + Task 3 프리셋/쿨다운 7개 PASS.

- [ ] **Step 6: 커밋** (Task 2 산출물 포함)

```bash
git add Assets/FoundationDI/Runtime/Services/HapticService/IHapticProvider.cs \
        Assets/FoundationDI/Runtime/Services/HapticService/Providers/NoopHapticProvider.cs \
        Assets/FoundationDI/Runtime/Services/HapticService/HapticService.cs \
        Assets/FoundationDI/Tests/HapticServiceTest.cs
git add -u Assets/FoundationDI/Runtime/Services/HapticService/   # 삭제된 구버전 반영
git commit -m "[BEHAVIORAL] HapticService 정책 계층 재작성(프리셋 위임·쿨다운·Enabled)

IHapticProvider를 Awaitable 재생 표면으로 확장, NoopProvider 갱신,
쿨다운 클럭 seam(nowSeconds) 도입으로 결정적 테스트.

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

### Task 4: 단일 활성 재생 — Play 위임 · 취소 · IsPlaying · Stop

**Files:**
- Modify: `Assets/FoundationDI/Tests/HapticServiceTest.cs` (재생 동시성 테스트 추가)

**Interfaces:**
- Consumes: Task 3 `HapticService.Play/Stop/IsPlaying`.
- Produces: 없음(Task 3 구현의 행동 검증).

> Task 3에서 Play/Stop/IsPlaying을 이미 구현했으므로 이 태스크는 그 행동을 고정하는 테스트를 추가한다. 테스트가 통과하지 않으면 Task 3 구현을 수정한다.

- [ ] **Step 1: 실패 테스트 추가** — `HapticServiceTest.cs` 에 추가. 파일 상단 using에 `using System.Collections.Generic;` 추가.

```csharp
    [UnityTest]
    public IEnumerator 활성화시_Play는_provider_PlayAsync에_위임한다() => UniTask.ToCoroutine(async () =>
    {
        var provider = Substitute.For<IHapticProvider>();
        var source = new AwaitableCompletionSource();
        provider.PlayAsync(Arg.Any<HapticCurve>(), Arg.Any<CancellationToken>()).Returns(source.Awaitable);
        var sut = new HapticService(provider) { Enabled = true };

        var p = sut.Play(default(HapticCurve));
        _ = provider.Received(1).PlayAsync(Arg.Any<HapticCurve>(), Arg.Any<CancellationToken>());

        source.SetResult();
        await p;
    });

    [UnityTest]
    public IEnumerator 비활성화시_Play는_provider를_호출하지_않고_즉시완료된다() => UniTask.ToCoroutine(async () =>
    {
        var provider = Substitute.For<IHapticProvider>();
        var sut = new HapticService(provider) { Enabled = false };

        await sut.Play(default(HapticCurve));

        _ = provider.DidNotReceive().PlayAsync(Arg.Any<HapticCurve>(), Arg.Any<CancellationToken>());
    });

    [UnityTest]
    public IEnumerator 새_Play는_이전_재생을_취소하고_Stop을_호출한다() => UniTask.ToCoroutine(async () =>
    {
        var provider = Substitute.For<IHapticProvider>();
        var tokens = new List<CancellationToken>();
        var s1 = new AwaitableCompletionSource();
        var s2 = new AwaitableCompletionSource();
        provider.PlayAsync(Arg.Any<HapticCurve>(), Arg.Do<CancellationToken>(tokens.Add))
                .Returns(s1.Awaitable, s2.Awaitable);
        var sut = new HapticService(provider) { Enabled = true };

        var p1 = sut.Play(default(HapticCurve));   // in-flight
        var p2 = sut.Play(default(HapticCurve));   // 이전 취소

        Assert.IsTrue(tokens[0].IsCancellationRequested, "첫 재생의 토큰이 취소되어야 한다");
        Assert.IsFalse(tokens[1].IsCancellationRequested, "두번째 재생은 진행 중이어야 한다");
        provider.Received(1).Stop();

        s1.SetResult(); s2.SetResult();
        await p2;
    });

    [UnityTest]
    public IEnumerator Play_중에는_IsPlaying이_true고_완료후_false다() => UniTask.ToCoroutine(async () =>
    {
        var provider = Substitute.For<IHapticProvider>();
        var source = new AwaitableCompletionSource();
        provider.PlayAsync(Arg.Any<HapticCurve>(), Arg.Any<CancellationToken>()).Returns(source.Awaitable);
        var sut = new HapticService(provider) { Enabled = true };

        var p = sut.Play(default(HapticCurve));
        Assert.IsTrue(sut.IsPlaying);

        source.SetResult();
        await p;
        Assert.IsFalse(sut.IsPlaying);
    });
```

- [ ] **Step 2: 테스트 실행(통과 예상)** — `refresh_unity` → `read_console(types=[error])`(에러 없음) → `run_tests`. Expected: 신규 4개 PASS. 실패 시 Task 3의 `Play`/`Stop`/`IsPlaying` 구현을 수정.

- [ ] **Step 3: 커밋**

```bash
git add Assets/FoundationDI/Tests/HapticServiceTest.cs
git commit -m "[BEHAVIORAL] 단일 활성 재생 동시성 테스트(위임·취소·IsPlaying)

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

### Task 5: DI 등록 테스트

**Files:**
- Modify: `Assets/FoundationDI/Tests/HapticServiceTest.cs`

**Interfaces:**
- Consumes: Task 3 `RegisterHapticService`.
- Produces: 없음.

- [ ] **Step 1: 실패 테스트 추가** — `HapticServiceTest.cs`. 상단 using에 `using VContainer;` 추가.

```csharp
    [Test]
    public void RegisterHapticService로_등록하면_IHapticService가_해석된다()
    {
        var builder = new ContainerBuilder();
        builder.RegisterHapticService();
        var container = builder.Build();

        var haptic = container.Resolve<IHapticService>();

        Assert.IsNotNull(haptic);
        Assert.IsInstanceOf<HapticService>(haptic);
    }
```

- [ ] **Step 2: 테스트 실행(통과 예상)** — `run_tests`. Expected: 신규 1개 PASS. (에디터에선 `[Inject] HapticService()` → `NoopHapticProvider` 주입되어 해석 성공.)

- [ ] **Step 3: 커밋**

```bash
git add Assets/FoundationDI/Tests/HapticServiceTest.cs
git commit -m "[BEHAVIORAL] HapticService DI 등록 해석 테스트

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

### Task 6: iOS 네이티브 브리지 `FDI_Haptic.mm` (clean-room)

**Files:**
- Modify(전면 재작성): `Assets/FoundationDI/Runtime/Services/HapticService/Plugins/iOS/FDI_Haptic.mm`
- Modify: `Assets/FoundationDI/Runtime/Services/HapticService/../../../` iOS 빌드 후처리(CoreHaptics.framework) — 기존 프로젝트에 iOS PostProcess가 없으면 Create: `Assets/FoundationDI/Editor/FDI_HapticiOSPostProcess.cs` (아래).

**Interfaces:**
- Consumes: 없음(네이티브).
- Produces (extern "C", `iOSHapticProvider`가 `DllImport("__Internal")`로 소비):
  - `void FDI_HapticImpact(int style)` — style = `UIImpactFeedbackStyle`
  - `void FDI_HapticNotification(int type)` — type = `UINotificationFeedbackType`
  - `void FDI_HapticSelection()`
  - `void FDI_HapticPrewarm()`
  - `bool FDI_HapticSupportsCore()`
  - `void FDI_HapticPlayCurve(float durationSeconds, float sharpness, const float* times, const float* intensities, int count)`
  - `void FDI_HapticStopCurve()`

> **주의:** 공개 UIKit/CoreHaptics API만 사용. MOST `.mm` 미참조. enum 정수값은 UIKit과 1:1(`HapticImpact.Light=0`==`UIImpactFeedbackStyleLight`, `HapticNotification.Success=0`==`UINotificationFeedbackTypeSuccess`).

- [ ] **Step 1: `FDI_Haptic.mm` 재작성** — 캐시된 제너레이터 + prewarm + CoreHaptics 파라미터 커브 플레이어

```objc
// FoundationDI iOS 햅틱 네이티브 브리지 (clean-room, 공개 API만 사용).
// enum 정렬: HapticImpact{Light=0,Medium=1,Heavy=2,Soft=3,Rigid=4}==UIImpactFeedbackStyle
//           HapticNotification{Success=0,Warning=1,Error=2}==UINotificationFeedbackType
#import <UIKit/UIKit.h>
#import <math.h>
#if __has_feature(modules)
@import CoreHaptics;
#else
#import <CoreHaptics/CoreHaptics.h>
#endif

static UISelectionFeedbackGenerator *gSelection = nil;
static UINotificationFeedbackGenerator *gNotif = nil;
static UIImpactFeedbackGenerator *gImpact[5] = { nil, nil, nil, nil, nil };

static CHHapticEngine *gEngine = nil;
static id<CHHapticPatternPlayer> gCurvePlayer = nil;

static float FDI_Clamp(float v, float lo, float hi) {
    if (!isfinite(v)) return lo;
    return v < lo ? lo : (v > hi ? hi : v);
}

static void FDI_EnsureGenerators(void) {
    if (@available(iOS 10.0, *)) {
        if (gSelection) return;
        gSelection = [UISelectionFeedbackGenerator new];
        gNotif = [UINotificationFeedbackGenerator new];
        gImpact[0] = [[UIImpactFeedbackGenerator alloc] initWithStyle:UIImpactFeedbackStyleLight];
        gImpact[1] = [[UIImpactFeedbackGenerator alloc] initWithStyle:UIImpactFeedbackStyleMedium];
        gImpact[2] = [[UIImpactFeedbackGenerator alloc] initWithStyle:UIImpactFeedbackStyleHeavy];
        if (@available(iOS 13.0, *)) {
            gImpact[3] = [[UIImpactFeedbackGenerator alloc] initWithStyle:UIImpactFeedbackStyleSoft];
            gImpact[4] = [[UIImpactFeedbackGenerator alloc] initWithStyle:UIImpactFeedbackStyleRigid];
        } else {
            gImpact[3] = gImpact[0]; // Soft→Light 폴백
            gImpact[4] = gImpact[2]; // Rigid→Heavy 폴백
        }
    }
}

static BOOL FDI_EnsureEngine(void) {
    if (@available(iOS 13.0, *)) {
        id<CHHapticDeviceCapability> caps = [CHHapticEngine capabilitiesForHardware];
        if (![caps supportsHaptics]) return NO;
        NSError *err = nil;
        if (!gEngine) {
            gEngine = [[CHHapticEngine alloc] initAndReturnError:&err];
            if (err || !gEngine) { NSLog(@"[FDI_Haptic] engine init: %@", err); return NO; }
            __weak CHHapticEngine *weakEngine = gEngine;
            gEngine.resetHandler = ^{ NSError *e = nil; [weakEngine startAndReturnError:&e]; };
            gEngine.stoppedHandler = ^(CHHapticEngineStoppedReason r) { };
        }
        if (![gEngine startAndReturnError:&err]) { NSLog(@"[FDI_Haptic] engine start: %@", err); return NO; }
        return YES;
    }
    return NO;
}

extern "C" {

void FDI_HapticImpact(int style) {
    if (@available(iOS 10.0, *)) {
        dispatch_async(dispatch_get_main_queue(), ^{
            FDI_EnsureGenerators();
            int s = (style < 0 || style > 4) ? 1 : style;
            [gImpact[s] prepare];
            [gImpact[s] impactOccurred];
        });
    }
}

void FDI_HapticNotification(int type) {
    if (@available(iOS 10.0, *)) {
        dispatch_async(dispatch_get_main_queue(), ^{
            FDI_EnsureGenerators();
            [gNotif prepare];
            [gNotif notificationOccurred:(UINotificationFeedbackType)type];
        });
    }
}

void FDI_HapticSelection(void) {
    if (@available(iOS 10.0, *)) {
        dispatch_async(dispatch_get_main_queue(), ^{
            FDI_EnsureGenerators();
            [gSelection prepare];
            [gSelection selectionChanged];
        });
    }
}

void FDI_HapticPrewarm(void) {
    if (@available(iOS 10.0, *)) {
        dispatch_async(dispatch_get_main_queue(), ^{
            FDI_EnsureGenerators();
            [gSelection prepare];
            [gNotif prepare];
            for (int i = 0; i < 5; i++) [gImpact[i] prepare];
            if (@available(iOS 13.0, *)) FDI_EnsureEngine();
        });
    }
}

bool FDI_HapticSupportsCore(void) {
    if (@available(iOS 13.0, *)) {
        return [[CHHapticEngine capabilitiesForHardware] supportsHaptics] ? true : false;
    }
    return false;
}

void FDI_HapticStopCurve(void) {
    if (@available(iOS 13.0, *)) {
        dispatch_async(dispatch_get_main_queue(), ^{
            if (!gCurvePlayer) return;
            NSError *e = nil;
            [gCurvePlayer stopAtTime:CHHapticTimeImmediate error:&e];
            gCurvePlayer = nil;
        });
    }
}

void FDI_HapticPlayCurve(float durationSeconds, float sharpness,
                         const float *times, const float *intensities, int count) {
    if (count < 2 || times == NULL || intensities == NULL) return;
    float dur = FDI_Clamp(durationSeconds, 0.01f, 30.0f);
    float shp = FDI_Clamp(sharpness, 0.0f, 1.0f);

    NSMutableArray<NSNumber *> *t = [NSMutableArray arrayWithCapacity:count];
    NSMutableArray<NSNumber *> *v = [NSMutableArray arrayWithCapacity:count];
    for (int i = 0; i < count; i++) {
        [t addObject:@(FDI_Clamp(times[i], 0.0f, dur))];
        [v addObject:@(FDI_Clamp(intensities[i], 0.0f, 1.0f))];
    }

    dispatch_async(dispatch_get_main_queue(), ^{
        if (@available(iOS 13.0, *)) {
            if (!FDI_EnsureEngine()) return;
            if (gCurvePlayer) { NSError *se = nil; [gCurvePlayer stopAtTime:CHHapticTimeImmediate error:&se]; gCurvePlayer = nil; }

            NSMutableArray<CHHapticParameterCurveControlPoint *> *points = [NSMutableArray arrayWithCapacity:count];
            for (int i = 0; i < count; i++) {
                [points addObject:[[CHHapticParameterCurveControlPoint alloc]
                    initWithRelativeTime:[t[i] floatValue] value:[v[i] floatValue]]];
            }

            CHHapticEventParameter *baseIntensity = [[CHHapticEventParameter alloc]
                initWithParameterID:CHHapticEventParameterIDHapticIntensity value:1.0f];
            CHHapticEventParameter *sharpnessParam = [[CHHapticEventParameter alloc]
                initWithParameterID:CHHapticEventParameterIDHapticSharpness value:shp];
            CHHapticEvent *event = [[CHHapticEvent alloc]
                initWithEventType:CHHapticEventTypeHapticContinuous
                parameters:@[baseIntensity, sharpnessParam] relativeTime:0.0 duration:dur];
            CHHapticParameterCurve *curve = [[CHHapticParameterCurve alloc]
                initWithParameterID:CHHapticDynamicParameterIDHapticIntensityControl
                controlPoints:points relativeTime:0.0];

            NSError *err = nil;
            CHHapticPattern *pattern = [[CHHapticPattern alloc]
                initWithEvents:@[event] parameterCurves:@[curve] error:&err];
            if (err || !pattern) { NSLog(@"[FDI_Haptic] pattern: %@", err); return; }

            gCurvePlayer = [gEngine createPlayerWithPattern:pattern error:&err];
            if (err || !gCurvePlayer) { NSLog(@"[FDI_Haptic] player: %@", err); return; }
            [gCurvePlayer startAtTime:CHHapticTimeImmediate error:&err];
        }
    });
}

}
```

- [ ] **Step 2: iOS 빌드 후처리 확인/작성** — 기존 리포에 CoreHaptics.framework 후처리가 없으면 `Assets/FoundationDI/Editor/FDI_HapticiOSPostProcess.cs` 생성

```csharp
#if UNITY_IOS
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.iOS.Xcode;

namespace DarkNaku.FoundationDI.Editor
{
    public static class FDI_HapticiOSPostProcess
    {
        [PostProcessBuild(999)]
        public static void OnPostprocessBuild(BuildTarget target, string path)
        {
            if (target != BuildTarget.iOS) return;

            string projectPath = PBXProject.GetPBXProjectPath(path);
            var project = new PBXProject();
            project.ReadFromFile(projectPath);

            string fw = project.GetUnityFrameworkTargetGuid();
            // CoreHaptics는 iOS13+라 weak-link
            project.AddFrameworkToProject(fw, "CoreHaptics.framework", true);
            project.AddFrameworkToProject(fw, "UIKit.framework", false);

            project.WriteToFile(projectPath);
        }
    }
}
#endif
```

- [ ] **Step 3: 컴파일 확인(에디터)** — `refresh_unity` → `read_console(types=[error])`. Expected: 에러 없음(에디터 스크립트만 컴파일; `.mm`은 iOS 빌드 시 컴파일). Editor asmdef가 `UnityEditor.iOS.Xcode` 참조 가능한지 확인 — FoundationDI 런타임 asmdef가 아닌 Editor 폴더(자동 Editor 어셈블리)여야 함.

- [ ] **Step 4: 커밋**

```bash
git add Assets/FoundationDI/Runtime/Services/HapticService/Plugins/iOS/FDI_Haptic.mm \
        Assets/FoundationDI/Editor/FDI_HapticiOSPostProcess.cs
git commit -m "[BEHAVIORAL] iOS 햅틱 네이티브 브리지 재작성(제너레이터 캐시·prewarm·CoreHaptics 커브)

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

> **수동 검증(실기기)**: iOS 기기 빌드 후 Impact/Notification/Selection 발동, `Play(HapticCurve)`로 램프 곡선 체감, CoreHaptics 미지원 기기(구형)에서 Fallback 프리셋 확인.

---

### Task 7: `iOSHapticProvider` (커브 + 패턴 시퀀싱 + 케이퍼빌리티)

**Files:**
- Modify(전면 재작성): `Assets/FoundationDI/Runtime/Services/HapticService/Providers/iOSHapticProvider.cs`

**Interfaces:**
- Consumes: Task 6 extern "C" 함수, Task 1 데이터 모델, Task 2 `IHapticProvider`.
- Produces: `class iOSHapticProvider : IHapticProvider` (전체 본문 `#if UNITY_IOS && !UNITY_EDITOR`).

- [ ] **Step 1: 재작성**

```csharp
#if UNITY_IOS && !UNITY_EDITOR
using System.Threading;
using System.Runtime.InteropServices;
using UnityEngine;

namespace DarkNaku.FoundationDI
{
    /// <summary>iOS UIFeedbackGenerator(프리셋) + CoreHaptics(커브) 기반 provider.
    /// 네이티브 브리지: Plugins/iOS/FDI_Haptic.mm</summary>
    public class iOSHapticProvider : IHapticProvider
    {
        [DllImport("__Internal")] private static extern void FDI_HapticImpact(int style);
        [DllImport("__Internal")] private static extern void FDI_HapticNotification(int type);
        [DllImport("__Internal")] private static extern void FDI_HapticSelection();
        [DllImport("__Internal")] private static extern void FDI_HapticPrewarm();
        [DllImport("__Internal")] [return: MarshalAs(UnmanagedType.I1)] private static extern bool FDI_HapticSupportsCore();
        [DllImport("__Internal")] private static extern void FDI_HapticPlayCurve(
            float durationSeconds, float sharpness, float[] times, float[] intensities, int count);
        [DllImport("__Internal")] private static extern void FDI_HapticStopCurve();

        public void Impact(HapticImpact style) { try { FDI_HapticImpact((int)style); } catch (System.Exception e) { Debug.LogError(e); } }
        public void Notification(HapticNotification type) { try { FDI_HapticNotification((int)type); } catch (System.Exception e) { Debug.LogError(e); } }
        public void Selection() { try { FDI_HapticSelection(); } catch (System.Exception e) { Debug.LogError(e); } }
        public void Prewarm() { try { FDI_HapticPrewarm(); } catch (System.Exception e) { Debug.LogError(e); } }
        public void Stop() { try { FDI_HapticStopCurve(); } catch { } }

        public async Awaitable PlayAsync(HapticCurve curve, CancellationToken ct)
        {
            var k = Sanitize(curve.IOS);
            if (k.DelayMs > 0f) await Awaitable.WaitForSecondsAsync(k.DelayMs / 1000f, ct);

            if (!FDI_HapticSupportsCore()) { Impact(k.Fallback); return; }

            Sample(k, out float[] times, out float[] intensities);
            try { FDI_HapticPlayCurve(k.DurationMs / 1000f, k.Sharpness, times, intensities, k.Samples); }
            catch (System.Exception e) { Debug.LogError(e); Impact(k.Fallback); return; }

            await Awaitable.WaitForSecondsAsync(k.DurationMs / 1000f, ct);
        }

        public async Awaitable PlayAsync(HapticPattern pattern, CancellationToken ct)
        {
            var seq = pattern.IOS;
            if (seq == null) return;
            for (int i = 0; i < seq.Length; i++)
            {
                float delay = Mathf.Max(0f, seq[i].DelayMs);
                if (delay > 0f) await Awaitable.WaitForSecondsAsync(delay / 1000f, ct);
                ct.ThrowIfCancellationRequested();
                FirePreset(seq[i].Preset);
            }
        }

        private void FirePreset(HapticPreset p)
        {
            switch (p)
            {
                case HapticPreset.Selection: Selection(); break;
                case HapticPreset.Success: Notification(HapticNotification.Success); break;
                case HapticPreset.Warning: Notification(HapticNotification.Warning); break;
                case HapticPreset.Error: Notification(HapticNotification.Error); break;
                case HapticPreset.LightImpact: Impact(HapticImpact.Light); break;
                case HapticPreset.MediumImpact: Impact(HapticImpact.Medium); break;
                case HapticPreset.HeavyImpact: Impact(HapticImpact.Heavy); break;
                case HapticPreset.SoftImpact: Impact(HapticImpact.Soft); break;
                case HapticPreset.RigidImpact: Impact(HapticImpact.Rigid); break;
            }
        }

        private static iOSHapticCurve Sanitize(iOSHapticCurve c)
        {
            c.DurationMs = c.DurationMs <= 0f ? 160f : Mathf.Max(10f, c.DurationMs);
            c.Sharpness = Mathf.Clamp01(c.Sharpness);
            c.Samples = Mathf.Clamp(c.Samples <= 0 ? 16 : c.Samples, 2, 64);
            c.DelayMs = Mathf.Max(0f, c.DelayMs);
            if (c.Intensity == null || c.Intensity.length == 0)
                c.Intensity = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
            return c;
        }

        private static void Sample(iOSHapticCurve c, out float[] times, out float[] intensities)
        {
            float durSec = c.DurationMs / 1000f;
            times = new float[c.Samples];
            intensities = new float[c.Samples];
            for (int i = 0; i < c.Samples; i++)
            {
                float n = i / (float)(c.Samples - 1);
                times[i] = n * durSec;
                intensities[i] = Mathf.Clamp01(c.Intensity.Evaluate(n));
            }
            times[0] = 0f;
            times[c.Samples - 1] = durSec;
        }
    }
}
#endif
```

- [ ] **Step 2: 컴파일 확인(에디터 한계)** — `refresh_unity` → `read_console(types=[error])`. Expected: 에디터에선 본문이 `#if UNITY_IOS && !UNITY_EDITOR`로 제외되어 에러 없음. **실제 컴파일 검증은 iOS 플랫폼 스위치 필요**(선택: `manage_editor`로 iOS 타깃 전환 후 재컴파일).

- [ ] **Step 3: 커밋**

```bash
git add Assets/FoundationDI/Runtime/Services/HapticService/Providers/iOSHapticProvider.cs
git commit -m "[BEHAVIORAL] iOSHapticProvider 재작성(커브·패턴 시퀀싱·케이퍼빌리티 폴백)

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

> **수동 검증(실기기)**: iOS 타깃 빌드 성공, 커브/패턴 체감.

---

### Task 8: `AndroidHapticProvider` (케이퍼빌리티 프로빙 + 웨이브폼 + 커브 샘플링)

**Files:**
- Modify(전면 재작성): `Assets/FoundationDI/Runtime/Services/HapticService/Providers/AndroidHapticProvider.cs`
- Modify: `Assets/FoundationDI/Runtime/Services/HapticService/Plugins/Android/FoundationDIHaptic.androidlib/src/main/AndroidManifest.xml` (WAKE_LOCK 추가 판단)

**Interfaces:**
- Consumes: Task 1 데이터 모델, Task 2 `IHapticProvider`.
- Produces: `class AndroidHapticProvider : IHapticProvider` (전체 본문 `#if UNITY_ANDROID && !UNITY_EDITOR`).

- [ ] **Step 1: 재작성**

```csharp
#if UNITY_ANDROID && !UNITY_EDITOR
using System.Threading;
using UnityEngine;

namespace DarkNaku.FoundationDI
{
    /// <summary>Android Vibrator/VibrationEffect 기반 provider.
    /// 진폭 제어(hasAmplitudeControl)를 런타임 확인해 지원 시 커브/웨이브폼, 아니면 프리셋 폴백.</summary>
    public class AndroidHapticProvider : IHapticProvider
    {
        private readonly AndroidJavaObject _vibrator;
        private readonly int _api;
        private readonly bool _hasVibrator;
        private readonly bool _hasAmplitude;

        public AndroidHapticProvider()
        {
            try
            {
                using var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
                using var activity = player.GetStatic<AndroidJavaObject>("currentActivity");
                _vibrator = activity.Call<AndroidJavaObject>("getSystemService", "vibrator");

                using var ver = new AndroidJavaClass("android.os.Build$VERSION");
                _api = ver.GetStatic<int>("SDK_INT");

                _hasVibrator = _vibrator != null && _vibrator.Call<bool>("hasVibrator");
                _hasAmplitude = _api >= 26 && _vibrator != null && _vibrator.Call<bool>("hasAmplitudeControl");
            }
            catch (System.Exception e) { Debug.LogError(e); }
        }

        public void Impact(HapticImpact style)
        {
            switch (style)
            {
                case HapticImpact.Light: OneShot(40); break;
                case HapticImpact.Soft: OneShot(45); break;
                case HapticImpact.Heavy: OneShot(90); break;
                case HapticImpact.Rigid: OneShot(75); break;
                default: OneShot(60); break; // Medium
            }
        }

        public void Notification(HapticNotification type)
        {
            switch (type)
            {
                case HapticNotification.Success: Waveform(new long[] { 0, 50 }, null); break;
                case HapticNotification.Warning: Waveform(new long[] { 0, 50, 90, 50 }, null); break;
                default: Waveform(new long[] { 0, 70, 90, 70, 90, 70 }, null); break; // Error
            }
        }

        public void Selection() => OneShot(30);

        public void Prewarm() { /* Android는 워밍 불필요 */ }

        public void Stop()
        {
            if (_vibrator == null) return;
            try { _vibrator.Call("cancel"); } catch { }
        }

        public async Awaitable PlayAsync(HapticCurve curve, CancellationToken ct)
        {
            var k = Sanitize(curve.Android);
            if (k.DelayMs > 0L) await Awaitable.WaitForSecondsAsync(k.DelayMs / 1000f, ct);

            if (!_hasVibrator) return;
            if (!_hasAmplitude) { Impact(k.Fallback); return; }

            BuildCurveWaveform(k, out long[] timings, out int[] amplitudes);
            Waveform(timings, amplitudes);
            await Awaitable.WaitForSecondsAsync(k.DurationMs / 1000f, ct);
        }

        public async Awaitable PlayAsync(HapticPattern pattern, CancellationToken ct)
        {
            var seq = pattern.Android;
            if (seq == null || seq.Length == 0 || !_hasVibrator) return;

            long[] timings = new long[seq.Length * 2];
            int[] amplitudes = new int[seq.Length * 2];
            long total = 0L;
            for (int i = 0; i < seq.Length; i++)
            {
                long delay = System.Math.Max(0L, seq[i].DelayMs);
                long pulse = System.Math.Max(1L, seq[i].PulseMs);
                timings[i * 2] = delay; amplitudes[i * 2] = 0;
                timings[i * 2 + 1] = pulse; amplitudes[i * 2 + 1] = Mathf.Clamp(seq[i].Amplitude, 0, 255);
                total += delay + pulse;
            }

            Waveform(timings, _hasAmplitude ? amplitudes : null);
            await Awaitable.WaitForSecondsAsync(total / 1000f, ct);
        }

        private void OneShot(long ms)
        {
            if (!_hasVibrator) return;
            try
            {
                if (_api >= 26)
                {
                    using var effectClass = new AndroidJavaClass("android.os.VibrationEffect");
                    int amp = _hasAmplitude ? effectClass.GetStatic<int>("DEFAULT_AMPLITUDE") : -1;
                    using var effect = effectClass.CallStatic<AndroidJavaObject>("createOneShot", ms, amp);
                    _vibrator.Call("vibrate", effect);
                }
                else { _vibrator.Call("vibrate", ms); }
            }
            catch (System.Exception e) { Debug.LogError(e); }
        }

        private void Waveform(long[] timings, int[] amplitudes)
        {
            if (!_hasVibrator || timings == null || timings.Length == 0) return;
            try
            {
                if (_api >= 26)
                {
                    using var effectClass = new AndroidJavaClass("android.os.VibrationEffect");
                    using AndroidJavaObject effect = (amplitudes != null && amplitudes.Length == timings.Length)
                        ? effectClass.CallStatic<AndroidJavaObject>("createWaveform", timings, amplitudes, -1)
                        : effectClass.CallStatic<AndroidJavaObject>("createWaveform", timings, -1);
                    _vibrator.Call("vibrate", effect);
                }
                else { _vibrator.Call("vibrate", timings, -1); }
            }
            catch (System.Exception e) { Debug.LogError(e); }
        }

        private static AndroidHapticCurve Sanitize(AndroidHapticCurve c)
        {
            c.DurationMs = c.DurationMs <= 0L ? 160L : System.Math.Max(10L, c.DurationMs);
            c.MaxAmplitude = Mathf.Clamp(c.MaxAmplitude <= 0 ? 255 : c.MaxAmplitude, 1, 255);
            c.Samples = Mathf.Clamp(c.Samples <= 0 ? 16 : c.Samples, 2, 64);
            c.DelayMs = System.Math.Max(0L, c.DelayMs);
            if (c.Intensity == null || c.Intensity.length == 0)
                c.Intensity = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
            return c;
        }

        private static void BuildCurveWaveform(AndroidHapticCurve c, out long[] timings, out int[] amplitudes)
        {
            int durMs = (int)System.Math.Max(10L, c.DurationMs);
            int count = Mathf.Clamp(c.Samples, 2, Mathf.Min(64, durMs));
            timings = new long[count];
            amplitudes = new int[count];
            long remaining = durMs;
            for (int i = 0; i < count; i++)
            {
                long seg = System.Math.Max(1L, remaining / (count - i));
                remaining -= seg;
                float n = (i + 0.5f) / count;
                float intensity = Mathf.Clamp01(c.Intensity.Evaluate(n));
                timings[i] = seg;
                amplitudes[i] = Mathf.Clamp(Mathf.RoundToInt(intensity * c.MaxAmplitude), 0, 255);
            }
        }
    }
}
#endif
```

- [ ] **Step 2: WAKE_LOCK 판단** — 커브/패턴 최대 지속시간이 짧으면(수백 ms) WAKE_LOCK 불필요. 장시간 재생 계획이 없으면 매니페스트를 VIBRATE만 유지. 추가하려면:

```xml
<manifest xmlns:android="http://schemas.android.com/apk/res/android">
    <uses-permission android:name="android.permission.VIBRATE" />
</manifest>
```
(기본: WAKE_LOCK 미추가. 필요 판명 시 별도 커밋.)

- [ ] **Step 3: 컴파일 확인(에디터 한계)** — `refresh_unity` → `read_console(types=[error])`. Expected: 에디터에선 본문 제외로 에러 없음. **실제 컴파일 검증은 Android 타깃 전환 필요**.

- [ ] **Step 4: 커밋**

```bash
git add Assets/FoundationDI/Runtime/Services/HapticService/Providers/AndroidHapticProvider.cs
git commit -m "[BEHAVIORAL] AndroidHapticProvider 재작성(케이퍼빌리티 프로빙·웨이브폼·커브 샘플링)

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

> **수동 검증(실기기)**: Android 기기 빌드, 진폭 지원/미지원 기기에서 커브/폴백, 패턴 리듬 체감.

---

### Task 9: README 갱신 + 최종 회귀

**Files:**
- Create/Modify: `Assets/FoundationDI/Runtime/Services/HapticService/README.md`

**Interfaces:**
- Consumes: 전체 공개 API.
- Produces: 없음.

- [ ] **Step 1: README 작성** — 공개 API(프리셋+쿨다운·Play(curve/pattern)·Stop/IsPlaying·Enabled·Prewarm), 데이터 모델(HapticCurve/HapticPattern 편의 생성자 예제), 케이퍼빌리티 폴백 매트릭스, DI 등록(`RegisterHapticService`), 플랫폼별 동작을 문서화. (구체 문안은 구현 확정 API 기준으로 작성.)

- [ ] **Step 2: 전체 EditMode 회귀** — `run_tests(mode=EditMode, assembly_names=["FoundationDI.Tests"])`. Expected: 햅틱 신규 테스트 전부 + 기존 프로젝트 테스트 전부 PASS.

- [ ] **Step 3: 커밋**

```bash
git add Assets/FoundationDI/Runtime/Services/HapticService/README.md
git commit -m "[BEHAVIORAL] HapticService README를 신규 API로 갱신

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Self-Review (작성자 체크 결과)

**1. 스펙 커버리지**
- 통합 단일 서비스 → Task 3. 시맨틱 프리셋 + 옵트인 쿨다운(20ms, 공유 타임스탬프) → Task 3. 플랫폼별 커브 모델 + 편의 생성자 → Task 1. 패턴(플랫 HapticPreset) → Task 1/7/8. 단일 활성 재생/취소/IsPlaying/Stop → Task 3+4. Awaitable → 전체. 케이퍼빌리티 폴백 → Task 6/7/8. 에러 가드 → Task 7/8. 테스트 전략 → Task 1~5. clean-room 네이티브 → Task 6. DI 등록 → Task 3/5. 폐기/유지 → Task 2 Step 5a/6. **누락 없음.**
- 미해결 항목(WAKE_LOCK·제너레이터 수명·샘플 상한·Prewarm 시점)은 각 태스크에서 판단으로 처리.

**2. 플레이스홀더 스캔** — Task 9 README 문안만 "구현 확정 기준"으로 열어둠(그 외 코드 스텝은 실제 코드 포함). 데이터/서비스/네이티브 전부 실코드.

**3. 타입 일관성** — `IHapticService`/`IHapticProvider` 시그니처가 Task 3 정의와 Task 6~8 소비에서 일치(`PlayAsync(HapticCurve, CancellationToken)`, `FDI_HapticPlayCurve(float,float,float[],float[],int)`). enum 정수 정렬(iOS) 명시.

## 주의: 컴파일 의존성 순서

Task 2(인터페이스 확장)는 구 `HapticService`/구 provider를 깨뜨린다. 따라서 **Task 2~3을 한 커밋 단위**로 처리하고(구버전 삭제 → 신 인터페이스/Noop/HapticService 동시 작성 → 컴파일 통과), 이후 Task 4~9를 진행한다. iOS/Android provider는 에디터 컴파일에서 제외되므로 Task 6~8 이전에도 에디터 테스트는 통과한다.
