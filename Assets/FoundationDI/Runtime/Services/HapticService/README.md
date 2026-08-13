# HapticService

iOS/Android 통합 **햅틱(촉각 피드백) 서비스**입니다. 시맨틱 프리셋(`Impact`/`Notification`/`Selection`)과
커스텀 커브·패턴 재생(`Play(HapticCurve)`/`Play(HapticPattern)`)을 하나의 `IHapticService`로 제공합니다.
실제 재생은 `IHapticProvider` seam 뒤에서 플랫폼별로 처리하며(iOS `CoreHaptics`+`UIFeedbackGenerator`,
Android `Vibrator`/`VibrationEffect`, 그 외 `NoopHapticProvider`), 미지원 기능은 자동으로 프리셋/legacy API로
폴백합니다.

- **프리셋 + 옵트인 쿨다운** — `Impact`/`Notification`/`Selection` 호출마다 쿨다운(기본 20ms)을 지정할 수 있고, 모터가 하나뿐이므로 세 API가 **타임스탬프를 공유**합니다.
- **커브 재생** — `HapticCurve`로 `AnimationCurve` 기반 세기 곡선을 양 플랫폼에 재생(`Play(HapticCurve)`, `Awaitable`).
- **패턴 재생** — `HapticPattern`으로 프리셋 시퀀스(iOS)/웨이브폼(Android)을 재생(`Play(HapticPattern)`).
- **단일 활성 재생** — `Play`는 항상 이전 재생을 `Stop()`한 뒤 시작합니다. 동시에 하나만 재생됩니다.
- **케이퍼빌리티 폴백** — CoreHaptics/진폭 제어/OS 버전을 런타임에 확인해 미지원 시 프리셋이나 legacy API로 자동 폴백합니다.
- **`Enabled` 영속화** — `PlayerPrefs("HAPTIC_ENABLED")`, 기본 `true`. `false`면 모든 호출이 무시됩니다.

코어 `HapticService`는 `IHapticProvider`를 받는 생성자다. 기본 생성자(`[Inject]`)는 빌드 플랫폼에 따라
`iOSHapticProvider`/`AndroidHapticProvider`/`NoopHapticProvider` 중 하나를 자동 선택해 주입합니다.

---

## 사용법

### 1) DI 등록 (VContainer)

```csharp
using VContainer.Unity;
using DarkNaku.FoundationDI;

public class RootLifetimeScope : LifetimeScope
{
    protected override void Configure(IContainerBuilder builder)
    {
        // 외부 리소스 의존이 없어 추가 인자는 불필요하다.
        builder.RegisterHapticService();
    }
}
```

### 2) 프리셋 재생

```csharp
public class MyButton
{
    private readonly IHapticService _haptic;
    public MyButton(IHapticService haptic) => _haptic = haptic;

    public void OnPressed()    => _haptic.Impact(HapticImpact.Light);
    public void OnConfirmed()  => _haptic.Notification(HapticNotification.Success);
    public void OnScrollTick() => _haptic.Selection();

    // 쿨다운을 명시적으로 조정(기본 20ms). 세 API 모두 같은 타임스탬프를 공유한다.
    public void OnRapidTick()  => _haptic.Selection(cooldown: 0.05f);
}
```

### 3) 커브 재생

```csharp
// 간단: 곡선 하나 + 기본값 → iOS/Android 동시 세팅
var curve = new HapticCurve(
    intensity: AnimationCurve.EaseInOut(0f, 0f, 1f, 1f),
    durationMs: 200f,
    sharpness: 0.6f,
    samples: 16,
    fallback: HapticImpact.Medium);

await _haptic.Play(curve);
```

```csharp
// 정밀: 플랫폼별 독립 캘리브레이션
var precise = new HapticCurve(
    ios: new iOSHapticCurve
    {
        Intensity = myIosCurve, DurationMs = 250f, Sharpness = 0.8f,
        Samples = 24, DelayMs = 0f, Fallback = HapticImpact.Heavy
    },
    android: new AndroidHapticCurve
    {
        Intensity = myAndroidCurve, DurationMs = 250L, MaxAmplitude = 200,
        Samples = 24, DelayMs = 0L, Fallback = HapticImpact.Heavy
    });

await _haptic.Play(precise);
```

### 4) 패턴 재생

```csharp
var pattern = new HapticPattern(
    // iOS: 프리셋 시퀀스(펄스별 지연 후 발동)
    ios: new[]
    {
        new iOSPulse { Preset = HapticPreset.LightImpact, DelayMs = 0f },
        new iOSPulse { Preset = HapticPreset.HeavyImpact, DelayMs = 120f },
    },
    // Android: 웨이브폼(무진동 지연 → 진동 지속 → 진폭)
    android: new[]
    {
        new AndroidPulse { DelayMs = 0,   PulseMs = 40, Amplitude = 100 },
        new AndroidPulse { DelayMs = 120, PulseMs = 60, Amplitude = 220 },
    });

await _haptic.Play(pattern);
```

### 5) Stop / IsPlaying

```csharp
if (_haptic.IsPlaying) _haptic.Stop();
```

### 6) Enabled / Prewarm

```csharp
// 설정 화면 등에서 토글 (PlayerPrefs에 영속화됨)
_haptic.Enabled = false;

// iOS 첫 재생 지연 완화용 사전 워밍(Android/Noop은 no-op)
_haptic.Prewarm();
```

---

## API

### `IHapticService : IDisposable`

| 멤버 | 시그니처 | 설명 |
| --- | --- | --- |
| `Impact` | `void Impact(HapticImpact style, float cooldown = 0.02f)` | 물리 충돌/타격 느낌. `Enabled == false`이거나 쿨다운 미경과 시 무시. |
| `Notification` | `void Notification(HapticNotification type, float cooldown = 0.02f)` | 결과 통지(성공/경고/실패). |
| `Selection` | `void Selection(float cooldown = 0.02f)` | 선택 변경 틱. |
| `Play` | `Awaitable Play(HapticCurve curve)` | `AnimationCurve` 기반 세기 곡선 재생. 재생 완료(또는 `Stop`으로 취소) 시 완료. |
| `Play` | `Awaitable Play(HapticPattern pattern)` | 프리셋 시퀀스(iOS)/웨이브폼(Android) 재생. |
| `Stop` | `void Stop()` | 현재 활성 `Play`를 취소하고 재생 중인 진동을 정지. |
| `IsPlaying` | `bool IsPlaying { get; }` | `Play`가 아직 완료되지 않았으면 `true`. |
| `Enabled` | `bool Enabled { get; set; }` | `PlayerPrefs("HAPTIC_ENABLED")`에 영속화, 기본 `true`. `false`면 프리셋/`Play` 모두 무시. |
| `Prewarm` | `void Prewarm()` | iOS 제너레이터를 미리 준비해 첫 재생 지연을 줄인다. Android/Noop은 no-op. |

**쿨다운(`cooldown`)**

- `Impact`/`Notification`/`Selection`은 **모터가 하나뿐이므로 쿨다운 타임스탬프를 공유**합니다. 세 API 중
  아무거나 연속 호출해도 마지막 발동 시각 기준으로 쿨다운이 계산됩니다.
- 기본값은 `0.02f`(20ms)이며, 호출마다 `cooldown` 인자로 옵트인 조정할 수 있습니다. `0f`을 넘기면 사실상
  쿨다운을 끄는 것과 같습니다(다음 프레임에도 `Time.unscaledTime`이 동일할 정도로 짧은 간격만 걸러짐).
- `Play(HapticCurve)`/`Play(HapticPattern)`은 쿨다운 대상이 아닙니다(대신 단일 활성 재생 규약을 따름, 아래 참고).

**단일 활성 재생**

- `Play`는 호출 시 항상 먼저 `Stop()`을 호출해 이전 재생을 취소한 뒤 새 재생을 시작합니다. 즉 두 번째
  `Play` 호출은 첫 번째 재생을 즉시 중단시킵니다.
- `IsPlaying`은 가장 최근 `Play`가 아직 완료되지 않았을 때만 `true`입니다.
- `Stop()`은 진행 중인 재생이 없으면 아무 것도 하지 않습니다(안전하게 무시).

### `HapticCurve` / `HapticPattern`

```csharp
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
    public HapticImpact Fallback;    // 진폭 제어 미지원 → 프리셋 폴백
}

public struct HapticCurve
{
    // 간단: 곡선 하나 + 기본값 → 양 플랫폼 동시 세팅
    public HapticCurve(AnimationCurve intensity, float durationMs = 160f, float sharpness = 0.6f,
                        int samples = 16, HapticImpact fallback = HapticImpact.Medium,
                        float delayMs = 0f, int androidMaxAmplitude = 255);

    // 정밀: 각 플랫폼 독립 캘리브레이션
    public HapticCurve(iOSHapticCurve ios, AndroidHapticCurve android);
}
```

```csharp
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

    public HapticPattern(iOSPulse[] ios, AndroidPulse[] android);
}
```

- 값 미지정 시 각 provider가 재생 직전에 sanitize한다: `DurationMs`≤0 → 160ms, `Samples`는 2~64로 클램프,
  `Intensity`가 `null`/빈 곡선이면 `AnimationCurve.EaseInOut(0,0,1,1)`로 대체.
- `HapticPreset`은 패턴 저작 전용 플랫 enum(`Selection`/`Success`/`Warning`/`Error`/`LightImpact`/
  `MediumImpact`/`HeavyImpact`/`SoftImpact`/`RigidImpact`)으로, `Impact`/`Notification`/`Selection`
  계열을 하나로 지목할 때 `HapticPattern.IOS`에서 사용한다.

### `IHapticProvider`

실제 재생 백엔드를 추상화한 seam이다. `HapticService`는 이 인터페이스에만 의존하므로, 테스트에서
NSubstitute로 대체해 네이티브 코드 없이 검증할 수 있다.

```csharp
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
```

### 생성자

```csharp
public HapticService(); // [Inject] — 빌드 플랫폼별 provider 자동 선택
public HapticService(IHapticProvider provider, Func<float> nowSeconds = null); // 테스트/커스텀 주입
```

`nowSeconds`는 쿨다운 타임스탬프 계산에 쓰이는 시간 소스로, 기본값은 `Time.unscaledTime`이다. 테스트에서
가짜 시계를 주입해 쿨다운 경계를 결정적으로 검증할 수 있다.

---

## 케이퍼빌리티 폴백 매트릭스

플랫폼별로 지원하지 않는 기능은 런타임에 감지해 **자동으로 다음 단계로 폴백**한다. 예외를 던지지 않는다.

| 상황 | 폴백 |
| --- | --- |
| iOS, CoreHaptics 미지원(`FDI_HapticSupportsCore() == false`) | `Play(HapticCurve)` → `curve.IOS.Fallback` 프리셋(`Impact`)으로 대체 재생 |
| iOS, CoreHaptics 네이티브 호출 예외 | 동일하게 `curve.IOS.Fallback` 프리셋으로 대체 |
| iOS < 13.0, `Impact(HapticImpact.Soft)` | `UIImpactFeedbackStyleLight`로 폴백(제너레이터 자체가 Light 인스턴스로 대체됨) |
| iOS < 13.0, `Impact(HapticImpact.Rigid)` | `UIImpactFeedbackStyleHeavy`로 폴백 |
| Android, `Vibrator` 없음(`hasVibrator() == false`) | 모든 프리셋/커브/패턴 호출이 무시(no-op) |
| Android, 진폭 제어 미지원(`hasAmplitudeControl() == false`, API 26 미만 포함) | `Play(HapticCurve)` → `curve.Android.Fallback` 프리셋(`Impact`)으로 대체 재생 |
| Android API < 26 | `VibrationEffect` 대신 legacy `Vibrator.vibrate(ms)` / `vibrate(timings, -1)` 사용 |
| 에디터 · 데스크톱 · 기타 미지원 플랫폼 | `NoopHapticProvider` — 모든 호출이 즉시 완료되는 no-op |

---

## 플랫폼 지원

| 플랫폼 | Provider | 프리셋 | 커브(`Play(HapticCurve)`) | 패턴(`Play(HapticPattern)`) |
| --- | --- | --- | --- | --- |
| iOS (실기기) | `iOSHapticProvider` | `UIFeedbackGenerator` | CoreHaptics(지원 시) / 프리셋 폴백 | `iOSPulse[]` 프리셋 시퀀스 |
| Android | `AndroidHapticProvider` | `VibrationEffect`(API≥26) / legacy `vibrate` | 진폭 제어 지원 시 waveform / 프리셋 폴백 | `AndroidPulse[]` waveform |
| 에디터 · 데스크톱 · 기타(WebGL 등) | `NoopHapticProvider` | no-op | no-op(즉시 완료) | no-op(즉시 완료) |

- iOS 시뮬레이터는 햅틱을 지원하지 않는다 — 실기기에서 확인.
- **WebGL을 포함해 iOS/Android 이외 모든 플랫폼은 `NoopHapticProvider`로 처리된다.** 전용 provider는
  없으며, 별도 대응이 필요 없다.
- iOS 네이티브 브리지: `Plugins/iOS/FDI_Haptic.mm`.

## Android 권한

Android에서 진동하려면 `android.permission.VIBRATE` 권한이 필요하다. **이 권한은 패키지가 직접 제공하므로
소비 프로젝트에서 별도로 매니페스트를 수정할 필요가 없다.**

`Plugins/Android/FoundationDIHaptic.androidlib/` 가 권한만 선언하는 Android 라이브러리 플러그인이며,
Gradle 병합 단계에서 앱의 최종 `AndroidManifest.xml` 에 자동 병합된다.

```
Plugins/Android/FoundationDIHaptic.androidlib/
├── build.gradle                 # com.android.library + namespace (AGP 8/9 필수)
└── src/main/AndroidManifest.xml # <uses-permission ... VIBRATE />
```

- VIBRATE는 normal 레벨 권한이라 설치 시 자동 승인되며 런타임 요청 코드는 불필요하다.
- `.androidlib`는 그냥 `AndroidManifest.xml`을 `Plugins/Android/`에 두는 것과 다르다 — 후자는 **메인
  매니페스트를 교체**해 호스트 것과 충돌하지만, `.androidlib`는 **병합**되어 충돌하지 않는다.
- `build.gradle`의 `compileSdk`는 프로젝트 SDK와 맞춘다(현재 36). Unity가 SDK를 올리면 함께 갱신한다.

## 테스트

- EditMode 단위 테스트(`Assets/FoundationDI/Tests/HapticServiceTest.cs`, `HapticDataTest.cs`)는
  `IHapticProvider`를 NSubstitute로 대체하여, 실제 네이티브 코드 없이 쿨다운·단일 활성 재생·`Stop`/
  `IsPlaying`·`Enabled`·`Prewarm` 위임을 검증한다.
- iOS/Android provider는 각각 `UNITY_IOS`/`UNITY_ANDROID` 빌드에서만 컴파일되므로(에디터 컴파일 제외),
  실제 네이티브 동작(`iOSHapticProvider`/`AndroidHapticProvider`)은 실기기·PlayMode 검증 대상이다.

## 한계 / 후속 과제

- 에러 처리: 네이티브 호출 실패는 대부분 `Debug.LogError` 후 무시하거나 프리셋으로 폴백한다(예외를
  호출자에 전파하지 않음).
- 스레드 안전성 없음 — Unity 메인 스레드 사용을 전제로 한다.
- 샘플 상한(`Samples` 2~64)과 진동 지속시간 하한(10ms) 등은 provider 내부 sanitize 값으로 고정되어
  있으며 별도 설정 API는 없다.
