# HapticService Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** iOS/Android 공통 햅틱(촉각 피드백) 서비스 `IHapticService`를 iOS 시맨틱 모델(Impact/Notification/Selection)로 제공한다.

**Architecture:** `HapticService`는 `Enabled` 게이팅과 `PlayerPrefs` 영속화만 담당하고, 실제 촉각 출력은 `IHapticProvider` seam에 위임한다. 플랫폼별 provider(iOS 네이티브 `.mm` 브리지 / Android `AndroidJavaObject` / Noop)가 seam을 구현한다. EditMode 테스트는 seam을 NSubstitute로 대체해 외부 의존 없이 검증한다.

**Tech Stack:** Unity 6000.3.17f1, C#, VContainer, NSubstitute 5.3.0, Objective-C(iOS `UIFeedbackGenerator`), Android `Vibrator`/`VibrationEffect`.

## Global Constraints

- 네임스페이스: `DarkNaku.FoundationDI` (모든 런타임/테스트 코드).
- 런타임 코드는 `Assets/FoundationDI/Runtime/` 단일 asmdef `FoundationDI`에 위치.
- 서비스 규약: `IXxxService : IDisposable` 인터페이스 + 구현 클래스 쌍, VContainer로 `Lifetime.Singleton` 등록.
- seam 분리: 외부 의존은 `IHapticProvider`로 추상화. 기본 생성자는 실제 구현 선택, 별도 생성자는 seam 주입.
- 테스트: `Assets/FoundationDI/Tests/` 평면 배치, asmdef `FoundationDI.Tests.Editor`(EditMode). 클래스 `XxxTest`, 메서드명 한국어, NSubstitute `Substitute.For`. `PlayerPrefs` 사용 테스트는 `[SetUp]`에서 키 삭제.
- PlayerPrefs 키 상수는 `SoundService`의 `SFX_ENABLED` 패턴을 따라 `HAPTIC_ENABLED` 사용.
- 컴파일 확인: 스크립트 수정 후 UnityMCP `read_console`로 컴파일 에러 확인, `editor_state.isCompiling == false`가 된 뒤 새 타입 사용. 테스트는 `run_tests`(EditMode).
- STRUCTURAL/BEHAVIORAL 커밋 분리, 커밋 제목에 접두어. 테스트 전체 통과 시에만 커밋.

---

### Task 1: Selection 위임 — 타입 골격 + seam

**Files:**
- Create: `Assets/FoundationDI/Runtime/Services/HapticService/HapticService.cs`
- Create: `Assets/FoundationDI/Runtime/Services/HapticService/IHapticProvider.cs`
- Test: `Assets/FoundationDI/Tests/HapticServiceTest.cs`

**Interfaces:**
- Consumes: (없음)
- Produces:
  - `enum HapticImpact { Light, Medium, Heavy, Soft, Rigid }`
  - `enum HapticNotification { Success, Warning, Error }`
  - `interface IHapticProvider { void Impact(HapticImpact); void Notification(HapticNotification); void Selection(); }`
  - `interface IHapticService : IDisposable { bool Enabled { get; set; } void Impact(HapticImpact); void Notification(HapticNotification); void Selection(); }`
  - `class HapticService` — 생성자 `HapticService(IHapticProvider provider)`

- [ ] **Step 1: 실패하는 테스트 작성**

`Assets/FoundationDI/Tests/HapticServiceTest.cs`:

```csharp
using DarkNaku.FoundationDI;
using NSubstitute;
using NUnit.Framework;
using UnityEngine;

public class HapticServiceTest
{
    [SetUp]
    public void SetUp()
    {
        PlayerPrefs.DeleteKey("HAPTIC_ENABLED");
    }

    [Test]
    public void 활성화_상태에서_Selection_호출시_provider의_Selection을_호출한다()
    {
        var provider = Substitute.For<IHapticProvider>();
        var sut = new HapticService(provider) { Enabled = true };

        sut.Selection();

        provider.Received(1).Selection();
    }
}
```

- [ ] **Step 2: 테스트 실패(컴파일 에러) 확인**

UnityMCP `read_console`로 확인. Expected: `IHapticProvider` / `HapticService` 미정의로 컴파일 실패.

- [ ] **Step 3: 최소 구현 작성**

`Assets/FoundationDI/Runtime/Services/HapticService/IHapticProvider.cs`:

```csharp
namespace DarkNaku.FoundationDI
{
    public enum HapticImpact { Light, Medium, Heavy, Soft, Rigid }

    public enum HapticNotification { Success, Warning, Error }

    public interface IHapticProvider
    {
        void Impact(HapticImpact style);
        void Notification(HapticNotification type);
        void Selection();
    }
}
```

`Assets/FoundationDI/Runtime/Services/HapticService/HapticService.cs`:

```csharp
using System;
using UnityEngine;

namespace DarkNaku.FoundationDI
{
    public interface IHapticService : IDisposable
    {
        bool Enabled { get; set; }
        void Impact(HapticImpact style);
        void Notification(HapticNotification type);
        void Selection();
    }

    public class HapticService : IHapticService
    {
        private const string HAPTIC_ENABLED = "HAPTIC_ENABLED";

        private readonly IHapticProvider _provider;

        public HapticService(IHapticProvider provider)
        {
            _provider = provider;
        }

        public bool Enabled
        {
            get => PlayerPrefs.GetInt(HAPTIC_ENABLED, 1) != 0;
            set
            {
                PlayerPrefs.SetInt(HAPTIC_ENABLED, value ? 1 : 0);
                PlayerPrefs.Save();
            }
        }

        public void Impact(HapticImpact style)
        {
            if (Enabled) _provider.Impact(style);
        }

        public void Notification(HapticNotification type)
        {
            if (Enabled) _provider.Notification(type);
        }

        public void Selection()
        {
            if (Enabled) _provider.Selection();
        }

        public void Dispose()
        {
        }
    }
}
```

- [ ] **Step 4: 테스트 통과 확인**

UnityMCP `run_tests`(EditMode, 필터 `HapticServiceTest`). Expected: PASS.

- [ ] **Step 5: 커밋**

```bash
git add Assets/FoundationDI/Runtime/Services/HapticService/ Assets/FoundationDI/Tests/HapticServiceTest.cs
git commit -m "[BEHAVIORAL] HapticService Selection 위임 + 타입 골격"
```

---

### Task 2: Impact 위임

**Files:**
- Modify: (없음 — Task 1에서 이미 구현됨)
- Test: `Assets/FoundationDI/Tests/HapticServiceTest.cs`

**Interfaces:**
- Consumes: `HapticService`, `IHapticProvider`, `HapticImpact` (Task 1)
- Produces: (없음)

- [ ] **Step 1: 실패하는 테스트 작성** — `HapticServiceTest`에 메서드 추가

```csharp
    [Test]
    public void 활성화_상태에서_Impact_호출시_provider에_같은_스타일로_위임한다()
    {
        var provider = Substitute.For<IHapticProvider>();
        var sut = new HapticService(provider) { Enabled = true };

        sut.Impact(HapticImpact.Heavy);

        provider.Received(1).Impact(HapticImpact.Heavy);
    }
```

- [ ] **Step 2: 테스트 실행 후 통과 확인**

UnityMCP `run_tests`(EditMode). Expected: PASS (Task 1의 위임 구현이 이미 커버). 만약 FAIL이면 Task 1 구현을 점검.

- [ ] **Step 3: 커밋**

```bash
git add Assets/FoundationDI/Tests/HapticServiceTest.cs
git commit -m "test: HapticService Impact 위임 검증 추가"
```

---

### Task 3: Notification 위임

**Files:**
- Test: `Assets/FoundationDI/Tests/HapticServiceTest.cs`

**Interfaces:**
- Consumes: `HapticService`, `IHapticProvider`, `HapticNotification` (Task 1)
- Produces: (없음)

- [ ] **Step 1: 실패하는 테스트 작성** — `HapticServiceTest`에 메서드 추가

```csharp
    [Test]
    public void 활성화_상태에서_Notification_호출시_provider에_같은_타입으로_위임한다()
    {
        var provider = Substitute.For<IHapticProvider>();
        var sut = new HapticService(provider) { Enabled = true };

        sut.Notification(HapticNotification.Warning);

        provider.Received(1).Notification(HapticNotification.Warning);
    }
```

- [ ] **Step 2: 테스트 실행 후 통과 확인**

UnityMCP `run_tests`(EditMode). Expected: PASS.

- [ ] **Step 3: 커밋**

```bash
git add Assets/FoundationDI/Tests/HapticServiceTest.cs
git commit -m "test: HapticService Notification 위임 검증 추가"
```

---

### Task 4: 비활성화 게이팅

**Files:**
- Test: `Assets/FoundationDI/Tests/HapticServiceTest.cs`

**Interfaces:**
- Consumes: `HapticService`, `IHapticProvider` (Task 1)
- Produces: (없음)

- [ ] **Step 1: 실패하는 테스트 작성** — `HapticServiceTest`에 메서드 추가

```csharp
    [Test]
    public void 비활성화_상태에서는_어떤_provider_메서드도_호출하지_않는다()
    {
        var provider = Substitute.For<IHapticProvider>();
        var sut = new HapticService(provider) { Enabled = false };

        sut.Impact(HapticImpact.Medium);
        sut.Notification(HapticNotification.Success);
        sut.Selection();

        provider.DidNotReceive().Impact(Arg.Any<HapticImpact>());
        provider.DidNotReceive().Notification(Arg.Any<HapticNotification>());
        provider.DidNotReceive().Selection();
    }
```

- [ ] **Step 2: 테스트 실행 후 통과 확인**

UnityMCP `run_tests`(EditMode). Expected: PASS (Task 1의 `if (Enabled)` 게이팅이 커버).

- [ ] **Step 3: 커밋**

```bash
git add Assets/FoundationDI/Tests/HapticServiceTest.cs
git commit -m "test: HapticService 비활성화 게이팅 검증 추가"
```

---

### Task 5: Enabled 영속화 + 기본값

**Files:**
- Test: `Assets/FoundationDI/Tests/HapticServiceTest.cs`

**Interfaces:**
- Consumes: `HapticService` (Task 1)
- Produces: (없음)

- [ ] **Step 1: 실패하는 테스트 작성** — `HapticServiceTest`에 메서드 2개 추가

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
```

- [ ] **Step 2: 테스트 실행 후 통과 확인**

UnityMCP `run_tests`(EditMode). Expected: PASS (Task 1의 PlayerPrefs 구현이 커버).

- [ ] **Step 3: 커밋**

```bash
git add Assets/FoundationDI/Tests/HapticServiceTest.cs
git commit -m "test: HapticService Enabled 영속화/기본값 검증 추가"
```

---

### Task 6: NoopHapticProvider

**Files:**
- Create: `Assets/FoundationDI/Runtime/Services/HapticService/Providers/NoopHapticProvider.cs`
- Test: `Assets/FoundationDI/Tests/HapticServiceTest.cs`

**Interfaces:**
- Consumes: `IHapticProvider` (Task 1)
- Produces: `class NoopHapticProvider : IHapticProvider` — 파라미터 없는 생성자

- [ ] **Step 1: 실패하는 테스트 작성** — `HapticServiceTest`에 메서드 추가

```csharp
    [Test]
    public void Noop provider는_예외없이_모든_메서드를_수행한다()
    {
        var provider = new NoopHapticProvider();

        Assert.DoesNotThrow(() =>
        {
            provider.Impact(HapticImpact.Light);
            provider.Notification(HapticNotification.Error);
            provider.Selection();
        });
    }
```

> 참고: C# 메서드 식별자에 공백이 올 수 없으므로 실제 작성 시 이름을 `Noop_provider는_예외없이_모든_메서드를_수행한다`로 한다.

- [ ] **Step 2: 테스트 실패(컴파일) 확인**

UnityMCP `read_console`. Expected: `NoopHapticProvider` 미정의로 컴파일 실패.

- [ ] **Step 3: 최소 구현 작성**

`Assets/FoundationDI/Runtime/Services/HapticService/Providers/NoopHapticProvider.cs`:

```csharp
namespace DarkNaku.FoundationDI
{
    /// <summary>
    /// 햅틱 미지원 플랫폼(에디터·데스크톱)용 provider. 모든 호출을 무시한다.
    /// </summary>
    public class NoopHapticProvider : IHapticProvider
    {
        public void Impact(HapticImpact style)
        {
        }

        public void Notification(HapticNotification type)
        {
        }

        public void Selection()
        {
        }
    }
}
```

- [ ] **Step 4: 테스트 통과 확인**

UnityMCP `run_tests`(EditMode). Expected: PASS.

- [ ] **Step 5: 커밋**

```bash
git add Assets/FoundationDI/Runtime/Services/HapticService/Providers/NoopHapticProvider.cs Assets/FoundationDI/Tests/HapticServiceTest.cs
git commit -m "[BEHAVIORAL] NoopHapticProvider 추가 (미지원 플랫폼 폴백)"
```

---

### Task 7: AndroidHapticProvider

> 디바이스 전용 코드 — EditMode 단위 테스트 대상 아님. `AndroidJavaObject`는 실기기에서만 유효하므로 컴파일 성공만 검증한다. 시맨틱 매핑 로직은 리뷰로 확인한다.

**Files:**
- Create: `Assets/FoundationDI/Runtime/Services/HapticService/Providers/AndroidHapticProvider.cs`

**Interfaces:**
- Consumes: `IHapticProvider`, `HapticImpact`, `HapticNotification` (Task 1)
- Produces: `class AndroidHapticProvider : IHapticProvider` — 파라미터 없는 생성자

- [ ] **Step 1: 구현 작성**

`Assets/FoundationDI/Runtime/Services/HapticService/Providers/AndroidHapticProvider.cs`:

```csharp
#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine;

namespace DarkNaku.FoundationDI
{
    /// <summary>
    /// Android Vibrator/VibrationEffect 기반 provider.
    /// AndroidManifest.xml에 android.permission.VIBRATE 권한이 필요하다.
    /// </summary>
    public class AndroidHapticProvider : IHapticProvider
    {
        // VibrationEffect predefined effect id (API 29+)
        private const int EFFECT_TICK = 2;
        private const int EFFECT_CLICK = 0;
        private const int EFFECT_HEAVY_CLICK = 5;

        private readonly AndroidJavaObject _vibrator;
        private readonly bool _supportsEffect;

        public AndroidHapticProvider()
        {
            using var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
            using var activity = player.GetStatic<AndroidJavaObject>("currentActivity");
            _vibrator = activity.Call<AndroidJavaObject>("getSystemService", "vibrator");

            using var version = new AndroidJavaClass("android.os.Build$VERSION");
            _supportsEffect = version.GetStatic<int>("SDK_INT") >= 29;
        }

        public void Impact(HapticImpact style)
        {
            switch (style)
            {
                case HapticImpact.Light:
                case HapticImpact.Soft:
                    Play(EFFECT_TICK, 20);
                    break;
                case HapticImpact.Heavy:
                case HapticImpact.Rigid:
                    Play(EFFECT_HEAVY_CLICK, 60);
                    break;
                default: // Medium
                    Play(EFFECT_CLICK, 40);
                    break;
            }
        }

        public void Notification(HapticNotification type)
        {
            // 짧은 패턴으로 근사 (성공: 틱, 경고/실패: 더 강한 클릭)
            switch (type)
            {
                case HapticNotification.Success:
                    Play(EFFECT_CLICK, 40);
                    break;
                default: // Warning / Error
                    Play(EFFECT_HEAVY_CLICK, 60);
                    break;
            }
        }

        public void Selection()
        {
            Play(EFFECT_TICK, 15);
        }

        private void Play(int effectId, long fallbackMs)
        {
            if (_vibrator == null) return;

            if (_supportsEffect)
            {
                using var effectClass = new AndroidJavaClass("android.os.VibrationEffect");
                using var effect = effectClass.CallStatic<AndroidJavaObject>("createPredefined", effectId);
                _vibrator.Call("vibrate", effect);
            }
            else
            {
                _vibrator.Call("vibrate", fallbackMs);
            }
        }
    }
}
#endif
```

- [ ] **Step 2: 컴파일 확인**

UnityMCP `read_console`. 현재 에디터 플랫폼에서는 `#if UNITY_ANDROID` 밖이라 컴파일에서 제외됨 — 에러 없음 확인. (Android 빌드 타깃 검증은 실기기/CI에서.)

- [ ] **Step 3: 커밋**

```bash
git add Assets/FoundationDI/Runtime/Services/HapticService/Providers/AndroidHapticProvider.cs
git commit -m "[BEHAVIORAL] AndroidHapticProvider 추가 (Vibrator/VibrationEffect)"
```

---

### Task 8: iOS 네이티브 브리지 + iOSHapticProvider

> 디바이스 전용 — EditMode 테스트 대상 아님. 네이티브 `.mm`은 iOS 빌드 시 Xcode에서 컴파일된다.

**Files:**
- Create: `Assets/Plugins/iOS/FoundationHaptic.mm`
- Create: `Assets/FoundationDI/Runtime/Services/HapticService/Providers/iOSHapticProvider.cs`

**Interfaces:**
- Consumes: `IHapticProvider`, `HapticImpact`, `HapticNotification` (Task 1)
- Produces: `class iOSHapticProvider : IHapticProvider` — 파라미터 없는 생성자
- 네이티브 C 심볼: `void FDI_HapticImpact(int style)`, `void FDI_HapticNotification(int type)`, `void FDI_HapticSelection()`

- [ ] **Step 1: 네이티브 브리지 작성**

`Assets/Plugins/iOS/FoundationHaptic.mm`:

```objc
#import <UIKit/UIKit.h>

// style: 0=Light 1=Medium 2=Heavy 3=Soft 4=Rigid (HapticImpact enum 순서와 일치)
extern "C" void FDI_HapticImpact(int style)
{
    if (@available(iOS 10.0, *))
    {
        UIImpactFeedbackStyle mapped = UIImpactFeedbackStyleMedium;
        switch (style)
        {
            case 0: mapped = UIImpactFeedbackStyleLight; break;
            case 1: mapped = UIImpactFeedbackStyleMedium; break;
            case 2: mapped = UIImpactFeedbackStyleHeavy; break;
            case 3: if (@available(iOS 13.0, *)) { mapped = UIImpactFeedbackStyleSoft; } break;
            case 4: if (@available(iOS 13.0, *)) { mapped = UIImpactFeedbackStyleRigid; } break;
        }
        UIImpactFeedbackGenerator *generator = [[UIImpactFeedbackGenerator alloc] initWithStyle:mapped];
        [generator prepare];
        [generator impactOccurred];
    }
}

// type: 0=Success 1=Warning 2=Error (HapticNotification enum 순서와 일치)
extern "C" void FDI_HapticNotification(int type)
{
    if (@available(iOS 10.0, *))
    {
        UINotificationFeedbackType mapped = UINotificationFeedbackTypeSuccess;
        switch (type)
        {
            case 0: mapped = UINotificationFeedbackTypeSuccess; break;
            case 1: mapped = UINotificationFeedbackTypeWarning; break;
            case 2: mapped = UINotificationFeedbackTypeError; break;
        }
        UINotificationFeedbackGenerator *generator = [[UINotificationFeedbackGenerator alloc] init];
        [generator prepare];
        [generator notificationOccurred:mapped];
    }
}

extern "C" void FDI_HapticSelection()
{
    if (@available(iOS 10.0, *))
    {
        UISelectionFeedbackGenerator *generator = [[UISelectionFeedbackGenerator alloc] init];
        [generator prepare];
        [generator selectionChanged];
    }
}
```

- [ ] **Step 2: iOSHapticProvider 작성**

`Assets/FoundationDI/Runtime/Services/HapticService/Providers/iOSHapticProvider.cs`:

```csharp
#if UNITY_IOS && !UNITY_EDITOR
using System.Runtime.InteropServices;

namespace DarkNaku.FoundationDI
{
    /// <summary>
    /// iOS Taptic Engine(UIFeedbackGenerator) 기반 provider.
    /// 네이티브 브리지: Assets/Plugins/iOS/FoundationHaptic.mm
    /// </summary>
    public class iOSHapticProvider : IHapticProvider
    {
        [DllImport("__Internal")]
        private static extern void FDI_HapticImpact(int style);

        [DllImport("__Internal")]
        private static extern void FDI_HapticNotification(int type);

        [DllImport("__Internal")]
        private static extern void FDI_HapticSelection();

        public void Impact(HapticImpact style) => FDI_HapticImpact((int)style);

        public void Notification(HapticNotification type) => FDI_HapticNotification((int)type);

        public void Selection() => FDI_HapticSelection();
    }
}
#endif
```

- [ ] **Step 3: 컴파일 확인**

UnityMCP `read_console`. 에디터 플랫폼에서는 `#if UNITY_IOS` 밖이라 제외 — 에러 없음 확인.

- [ ] **Step 4: 커밋**

```bash
git add Assets/Plugins/iOS/FoundationHaptic.mm Assets/FoundationDI/Runtime/Services/HapticService/Providers/iOSHapticProvider.cs
git commit -m "[BEHAVIORAL] iOS 햅틱 네이티브 브리지 + iOSHapticProvider 추가"
```

---

### Task 9: 기본 생성자 플랫폼 분기

**Files:**
- Modify: `Assets/FoundationDI/Runtime/Services/HapticService/HapticService.cs`

**Interfaces:**
- Consumes: `NoopHapticProvider`(Task 6), `AndroidHapticProvider`(Task 7), `iOSHapticProvider`(Task 8)
- Produces: `HapticService()` 파라미터 없는 생성자 (VContainer가 해석 가능)

- [ ] **Step 1: 기본 생성자 + 팩토리 추가** — `HapticService`에 아래를 추가 (기존 seam 생성자 위에 배치)

```csharp
        public HapticService() : this(CreatePlatformProvider())
        {
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
```

- [ ] **Step 2: 컴파일 + 전체 테스트 확인**

UnityMCP `read_console`로 컴파일 에러 없음 확인 후 `run_tests`(EditMode). Expected: 기존 HapticServiceTest 전체 PASS (기본 생성자 추가가 seam 생성자 동작을 바꾸지 않음).

- [ ] **Step 3: 커밋**

```bash
git add Assets/FoundationDI/Runtime/Services/HapticService/HapticService.cs
git commit -m "[STRUCTURAL] HapticService 기본 생성자에 플랫폼 provider 분기 추가"
```

---

### Task 10: VContainer 확장 + README

**Files:**
- Create: `Assets/FoundationDI/Runtime/Services/HapticService/HapticServiceVContainerExtensions.cs`
- Create: `Assets/FoundationDI/Runtime/Services/HapticService/README.md`

**Interfaces:**
- Consumes: `IHapticService`, `HapticService` (Task 1·9)
- Produces: `IContainerBuilder.RegisterHapticService()`

- [ ] **Step 1: VContainer 확장 작성**

`Assets/FoundationDI/Runtime/Services/HapticService/HapticServiceVContainerExtensions.cs`:

```csharp
using VContainer;

namespace DarkNaku.FoundationDI
{
    public static class HapticServiceVContainerExtensions
    {
        /// <summary>
        /// HapticService를 컨테이너에 등록한다. 외부 리소스 의존이 없어 추가 인자는 불필요하다.
        /// </summary>
        public static void RegisterHapticService(this IContainerBuilder builder)
        {
            builder.Register<IHapticService, HapticService>(Lifetime.Singleton);
        }
    }
}
```

- [ ] **Step 2: 컴파일 확인**

UnityMCP `read_console`. Expected: 에러 없음.

- [ ] **Step 3: README 작성**

`Assets/FoundationDI/Runtime/Services/HapticService/README.md` — API 표면, 시맨틱 매핑 표(iOS↔Android), DI 등록 예시, **Android `VIBRATE` 권한 설정 안내**, iOS 네이티브 브리지 위치, 사용 예:

```csharp
// 등록 (RootLifetimeScope.Configure)
builder.RegisterHapticService();

// 사용 (생성자 주입)
public class MyButton
{
    private readonly IHapticService _haptic;
    public MyButton(IHapticService haptic) => _haptic = haptic;

    public void OnPressed() => _haptic.Impact(HapticImpact.Light);
    public void OnConfirmed() => _haptic.Notification(HapticNotification.Success);
    public void OnScrollTick() => _haptic.Selection();
}
```

Android 권한 안내: `Assets/Plugins/Android/AndroidManifest.xml`(또는 커스텀 매니페스트)에 다음 추가.

```xml
<uses-permission android:name="android.permission.VIBRATE" />
```

- [ ] **Step 4: 커밋**

```bash
git add Assets/FoundationDI/Runtime/Services/HapticService/HapticServiceVContainerExtensions.cs Assets/FoundationDI/Runtime/Services/HapticService/README.md
git commit -m "[BEHAVIORAL] HapticService VContainer 등록 확장 + README 추가"
```

---

## 통합 후속 (호스트 프로젝트, 계획 범위 밖 참고)

- `RootLifetimeScope.Configure`에서 `builder.RegisterHapticService()` 호출로 실제 등록.
- Android 빌드 시 `VIBRATE` 권한, iOS 빌드 시 실기기에서 시맨틱 피드백 수동 검증(시뮬레이터는 햅틱 미지원).

## Self-Review 결과

- **Spec coverage**: 스펙의 API 표면(Task 1), seam 분리(Task 1·9), Enabled 게이팅/영속화(Task 4·5), iOS 네이티브(Task 8), Android(Task 7), Noop(Task 6), DI 등록(Task 10), 테스트 계획(Task 1~6) 모두 태스크로 매핑됨.
- **Placeholder scan**: TBD/TODO 없음. 모든 코드 단계에 실제 코드 포함.
- **Type consistency**: `IHapticProvider`/`IHapticService` 시그니처, `HapticImpact`/`HapticNotification` enum 순서(네이티브 int 매핑과 일치), `HAPTIC_ENABLED` 키, provider 클래스명이 태스크 전반에서 일관됨.
