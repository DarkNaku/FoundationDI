# HapticService MOST급 재구현 설계

- **일자**: 2026-08-13
- **대상**: `Assets/FoundationDI/Runtime/Services/HapticService/`
- **성격**: 기존 HapticService를 폐기하고 MOST_HapticFeedback급 기능을 FoundationDI의 DI/seam 구조 + `Awaitable`로 **clean-room 재구현**.

## 배경 / 목표

현 `HapticService`는 프리셋(Impact/Notification/Selection)만 제공하는 얇은 시맨틱 서비스다. 사내 전용 프로젝트에서 MOST_HapticFeedback(상용 에셋)급 표현력·네이티브 견고성을 원하되:

- **DI/seam 구조 유지** — `IHapticService` + `IHapticProvider` 분리, 서비스 로직은 EditMode 단위 테스트.
- **`Task` 대신 `Awaitable`** — 프로젝트의 UniTask→Awaitable 마이그레이션 방향에 맞춤. 메인 스레드 재생이라 MOST의 JNI-스레드풀 리스크 없음.
- **clean-room** — MOST는 "무엇을·어떻게 동작하게 할지" 레퍼런스일 뿐, 코드(특히 `.mm`)는 공개 API(UIKit/CoreHaptics/VibrationEffect)로 직접 작성. MOST 소스 미복사.

기능 범위는 논의에서 택한 **옵션 3(MOST 전 기능 대등)**: 프리셋 + 커스텀 패턴 + AnimationCurve 커브 + 재생 제어.

## 확정된 설계 결정

| 항목 | 결정 |
|---|---|
| 서비스 분해 | **통합** 단일 `IHapticService`(프리셋+커브+패턴+재생제어) |
| 프리셋 API | 현행 **시맨틱 분리**(Impact/Notification/Selection), enum 유지 |
| 쿨다운 | **옵트인**, 기본 `0.02f`(20ms), 프리셋 공유 단일 타임스탬프. 커브/패턴은 미적용 |
| 커브 데이터 모델 | **플랫폼별 분리**(iOSHapticCurve+AndroidHapticCurve) + 편의 생성자 |
| 패턴 | **포함**, 플랫폼별 분리(iOS=프리셋 시퀀스, Android=웨이브폼) |
| 패턴 펄스 프리셋 지목 | 저작 전용 **플랫 `HapticPreset` enum**(9종) 도입 |
| 플랫폼 | iOS + Android + Noop (WebGL 없음) |
| 재생 동시성 | **단일 활성**(새 커브/패턴이 이전 취소), 프리셋은 독립 fire-and-forget |
| async | **`Awaitable`**, 메인 스레드, `CancellationToken` 기반 |
| 네이티브 | **clean-room 신규 작성**(공개 API) |

## 공개 API

```csharp
public interface IHapticService : IDisposable
{
    // 프리셋 (시맨틱 분리, 옵트인 쿨다운 기본 20ms)
    void Impact(HapticImpact style, float cooldown = 0.02f);
    void Notification(HapticNotification type, float cooldown = 0.02f);
    void Selection(float cooldown = 0.02f);

    // 시간 재생 (Awaitable, 단일 활성)
    Awaitable Play(HapticCurve curve);
    Awaitable Play(HapticPattern pattern);
    void Stop();
    bool IsPlaying { get; }

    // 제어
    bool Enabled { get; set; }   // PlayerPrefs "HAPTIC_ENABLED"
    void Prewarm();              // 첫 햅틱 지연 감소(제너레이터/엔진 워밍)
}
```

## 책임 분리 (seam)

| 계층 | 책임 |
|---|---|
| `HapticService` (정책, 플랫폼 무관, **테스트 대상**) | `Enabled` 게이트, 쿨다운(프리셋 공유 단일 타임스탬프), **단일 활성 재생 관리**(`CancellationTokenSource` 교체), `IsPlaying`, `Stop`. 발동·재생은 provider 위임. |
| `IHapticProvider` (플랫폼, 네이티브 어댑터) | 동기 `Impact/Notification/Selection` · `Awaitable PlayAsync(HapticCurve, CancellationToken)` · `Awaitable PlayAsync(HapticPattern, CancellationToken)` · `void Stop()` · `void Prewarm()`. **iOS 패턴 시퀀싱 루프·Android 웨이브폼 빌드·AnimationCurve 샘플링·케이퍼빌리티 폴백은 각 provider 내부**. |
| 네이티브 | iOS `FDI_Haptic.mm` 확장(UIFeedbackGenerator 프리셋 캐시+prewarm + CoreHaptics 파라미터 커브 플레이어 + 케이퍼빌리티) / Android `AndroidHapticProvider`(C#, Vibrator·VibrationEffect, 케이퍼빌리티 프로빙, createWaveform, 예외 가드). |

**근거**: iOS 커브=CoreHaptics 1회로 엔벨로프 네이티브 재생, iOS 패턴=프리셋 탭 시퀀스(C# 지연 필요), Android 커브/패턴=`createWaveform` 1회. 이 플랫폼 비대칭을 provider의 `PlayAsync`(Awaitable) 안에 가둬 서비스는 `#if` 없이 정책만 담당 → 서비스 로직을 대역 provider로 단위 테스트 가능.

**DI 등록**: `builder.RegisterHapticService()` 유지. 기본 생성자가 플랫폼 provider 팩토리 선택(iOS/Android/Noop), 테스트 생성자는 `IHapticProvider` 주입.

## 데이터 모델

```csharp
enum HapticImpact { Light, Medium, Heavy, Soft, Rigid }
enum HapticNotification { Success, Warning, Error }

// 패턴 펄스 저작 전용 플랫 enum (9종)
enum HapticPreset {
    Selection,
    Success, Warning, Error,                 // Notification 계열
    LightImpact, MediumImpact, HeavyImpact,   // Impact 계열
    SoftImpact, RigidImpact
}

struct iOSHapticCurve {
    AnimationCurve Intensity;  // X:0..1(정규화 시간), Y:0..1(세기)
    float DurationMs;
    float Sharpness;           // 0..1 (CoreHaptics)
    int   Samples;             // 컨트롤 포인트 2..64
    float DelayMs;
    HapticImpact Fallback;     // CoreHaptics 미지원 → 프리셋 폴백
}
struct AndroidHapticCurve {
    AnimationCurve Intensity;
    long  DurationMs;
    int   MaxAmplitude;        // 1..255
    int   Samples;             // waveform 세그먼트 2..64
    long  DelayMs;
    HapticImpact Fallback;     // 진폭제어 미지원 → 프리셋 폴백
}
struct HapticCurve {
    iOSHapticCurve     IOS;
    AndroidHapticCurve Android;
    // 간단: 곡선 하나 + 기본값 → 양쪽 동시 세팅
    HapticCurve(AnimationCurve intensity, float durationMs = 160, float sharpness = 0.6f,
                int samples = 16, HapticImpact fallback = HapticImpact.Medium,
                float delayMs = 0, int androidMaxAmplitude = 255);
    // 정밀: 각 플랫폼 독립 캘리브레이션
    HapticCurve(iOSHapticCurve ios, AndroidHapticCurve android);
}

struct iOSPulse     { HapticPreset Preset; float DelayMs; }        // 지연 후 프리셋 발동
struct AndroidPulse { long DelayMs; long PulseMs; int Amplitude; } // 지연 후 PulseMs 동안 Amplitude
struct HapticPattern {
    iOSPulse[]     IOS;
    AndroidPulse[] Android;
    HapticPattern(iOSPulse[] ios, AndroidPulse[] android);
}
```

## 재생 엔진

```csharp
CancellationTokenSource _cts;
Awaitable _active;

public async Awaitable Play(HapticCurve curve)
{
    if (!Enabled) return;
    Stop();                                   // 진행 중 재생 취소(단일 활성)
    var cts = _cts = new CancellationTokenSource();
    try { _active = _provider.PlayAsync(curve, cts.Token); await _active; }
    catch (OperationCanceledException) { }    // 교체/Stop은 정상 흐름 → 삼킴
    finally { if (_cts == cts) { _cts = null; _active = null; } cts.Dispose(); }
}
// Play(HapticPattern) 동일 구조

public bool IsPlaying => _active != null && !_active.IsCompleted;
public void Stop() { _cts?.Cancel(); _provider.Stop(); }
```

- 취소된 `Play`의 await는 예외 없이 정상 완료 → `_ = Play(...)` fire-and-forget 가능.
- 메인 스레드 Awaitable.

**provider 내부 재생 (예: iOS)**
```csharp
async Awaitable PlayAsync(HapticCurve c, CancellationToken ct) {
    var k = Sanitize(c.IOS);
    if (k.DelayMs > 0) await Awaitable.WaitForSecondsAsync(k.DelayMs/1000f, ct);
    if (!SupportsCoreHaptics()) { Impact(k.Fallback); return; }   // 폴백
    Sample(k, out float[] times, out float[] intensities);        // AnimationCurve → 배열
    FDI_HapticPlayCurve(k.DurationMs/1000f, k.Sharpness, times, intensities, k.Samples);
    await Awaitable.WaitForSecondsAsync(k.DurationMs/1000f, ct);
}

async Awaitable PlayAsync(HapticPattern p, CancellationToken ct) {
    foreach (var pulse in p.IOS) {
        if (pulse.DelayMs > 0) await Awaitable.WaitForSecondsAsync(pulse.DelayMs/1000f, ct);
        FirePreset(pulse.Preset);
    }
}
```
- Android 커브: 곡선 → 세그먼트 `timings[]`+`amplitudes[]`(×MaxAmplitude) → `createWaveform` 1회 → 총길이 대기.
- Android 패턴: `AndroidPulse[]` → `timings[]`/`amplitudes[]` → `createWaveform` 1회 → 총길이 대기.
- Noop: 모든 `PlayAsync` 즉시완료 Awaitable, 프리셋 no-op.

**게이팅/쿨다운**
- `Enabled==false` → 프리셋 즉시 return, `Play`는 즉시완료 Awaitable.
- 쿨다운은 프리셋만(모터 1개 공유 단일 타임스탬프, 기본 20ms). 커브/패턴은 단일 활성이 스팸 방지.
- `Prewarm()` → `provider.Prewarm()`.

## 케이퍼빌리티 폴백 매트릭스

| 상황 | 동작 |
|---|---|
| iOS CoreHaptics 미지원(`supportsHaptics==false`) | 커브 → `Fallback` 프리셋 |
| iOS <13 (Rigid/Soft 없음) | 네이티브에서 Heavy/Light 폴백 (프리셋 iOS 10+) |
| Android 진동자 없음(`hasVibrator()==false`) | 전부 no-op |
| Android 진폭제어 없음(`hasAmplitudeControl()==false`) | 커브 → `Fallback` 프리셋(지속시간 코딩) |
| Android <26 | legacy `vibrate`(지속시간만), 진폭 무시 |
| 에디터·데스크톱 | `NoopHapticProvider` |

## 에러 처리

- provider의 네이티브 호출은 try/catch → `Debug.LogError`, 게임플레이로 예외 전파 금지. iOS `.mm`은 `dispatch_async(main)` + 내부 가드.
- 취소(`OperationCanceledException`)는 정상 흐름 → 서비스 경계에서 삼킴.
- provider 경계에서 입력 sanitize/clamp(DurationMs 하한, Samples 2..64, Amplitude 0..255, Sharpness 0..1).

## 테스트 전략

- **서비스 정책 EditMode 단위 테스트** (NSubstitute `IHapticProvider`):
  - `Enabled` 게이트(off면 provider 무호출), 프리셋 위임(같은 style/type), `Selection` 위임.
  - 쿨다운: 옵트인·기본 20ms·프리셋 공유 타임스탬프(간격 내 재호출 무시, `cooldown:0`이면 항상 발동).
  - 단일 활성: 새 `Play`가 이전 재생 취소(`Stop`/토큰 취소 검증), `IsPlaying` 전이, `Stop()`.
  - `Play(curve/pattern)` → provider `PlayAsync` 위임.
- Awaitable은 즉시완료/제어형 대역(`AwaitableCompletionSource`)으로 순서·취소만 검증. 실시간 `WaitForSecondsAsync`는 EditMode 프레임펌핑 이슈로 지양.
- DI 등록 테스트: `RegisterHapticService()` → `IHapticService` 해석.
- 네이티브 provider(iOS/Android)는 단위 테스트 안 함 — 실기기 수동 검증(현행 모델과 동일).

## 폐기 / 유지

- **폐기**: 기존 `HapticService`/`IHapticProvider`/프리셋 provider 3종/`FDI_Haptic.mm`(UIFeedbackGenerator only)/`HapticServiceTest`.
- **유지**: 네임스페이스 `DarkNaku.FoundationDI`, 폴더 `Services/HapticService/`, Android `.androidlib`(VIBRATE). WAKE_LOCK 추가 여부는 커브 장시간 재생 대비로 구현 시 판단.

## 미해결 / 구현 시 판단

- Android WAKE_LOCK 매니페스트 추가 여부.
- iOS 프리셋 제너레이터 캐싱 수명(정적 vs provider 인스턴스) — 네이티브 측 전역이 자연스러움.
- 커브 샘플링 개수 기본값/상한(설계 기본 16/64, MOST와 동일)의 실기기 튜닝.
- `Prewarm` 자동 호출 시점(수동 vs DI 등록 직후).
