# HapticService 설계

- 작성일: 2026-07-03
- 상태: 승인됨 (구현 착수 대기)

## 목적

iOS와 Android에서 공통으로 사용할 햅틱(진동/촉각 피드백) 서비스를 제공한다. iOS Taptic
Engine의 시맨틱 피드백 모델을 API 표면으로 채택하고, Android는 동일 시맨틱을
`VibrationEffect`로 매핑한다. 기존 FoundationDI 서비스 규약(인터페이스+구현 쌍, seam 분리,
VContainer 등록)을 그대로 따른다.

## 설계 결정 (확정)

1. **API 모델**: iOS 시맨틱 모델 — Impact(Light/Medium/Heavy/Soft/Rigid) +
   Notification(Success/Warning/Error) + Selection.
2. **iOS 구현**: Objective-C 네이티브 플러그인(`.mm`)으로 `UIFeedbackGenerator` 계열 직접 호출.
3. **설정/폴백**: `Enabled` 토글을 `PlayerPrefs`에 영속화. iOS/Android 외 플랫폼(에디터·데스크톱)은
   `NoopHapticProvider`로 안전하게 무시.

## 파일 구조

```
Assets/FoundationDI/Runtime/Services/HapticService/
  HapticService.cs                       # IHapticService + HapticService, enum 정의
  IHapticProvider.cs                     # 플랫폼 seam 인터페이스
  Providers/
    iOSHapticProvider.cs                 # [DllImport("__Internal")] → 네이티브 호출
    AndroidHapticProvider.cs             # AndroidJavaObject Vibrator/VibrationEffect
    NoopHapticProvider.cs                # 에디터/미지원: 무시
  HapticServiceVContainerExtensions.cs   # RegisterHapticService
  README.md
Assets/Plugins/iOS/FoundationHaptic.mm    # Taptic Engine 네이티브 브리지
```

네임스페이스: `DarkNaku.FoundationDI`.

## API 표면

```csharp
public enum HapticImpact { Light, Medium, Heavy, Soft, Rigid }
public enum HapticNotification { Success, Warning, Error }

public interface IHapticService : IDisposable
{
    bool Enabled { get; set; }                    // PlayerPrefs 영속화
    void Impact(HapticImpact style);
    void Notification(HapticNotification type);
    void Selection();
}
```

## seam 분리 (테스트 가능성)

`HapticService`는 활성화 게이팅과 영속화만 담당하고, 실제 촉각 출력은 `IHapticProvider`에 위임한다.

```csharp
public interface IHapticProvider
{
    void Impact(HapticImpact style);
    void Notification(HapticNotification type);
    void Selection();
}
```

- **기본 생성자**: 컴파일 심볼로 플랫폼별 provider를 선택한다.
  `#if UNITY_IOS && !UNITY_EDITOR` → `iOSHapticProvider`,
  `#elif UNITY_ANDROID && !UNITY_EDITOR` → `AndroidHapticProvider`,
  `#else` → `NoopHapticProvider`.
- **테스트 생성자**: `HapticService(IHapticProvider provider)`로 seam을 주입받아 EditMode에서
  NSubstitute로 검증한다.
- `Enabled` 기본값: `PlayerPrefs.GetInt("HAPTIC_ENABLED", 1) != 0` (기존 SoundService의
  `SFXEnabled` 패턴과 동일).

## 동작 규약

- `Enabled == false`이면 `Impact`/`Notification`/`Selection` 호출 시 provider를 호출하지 않고 즉시 반환한다.
- `Enabled == true`이면 각 시맨틱 메서드는 provider의 대응 메서드를 인자 그대로 위임 호출한다.
- `Enabled` setter는 `PlayerPrefs.SetInt` 후 `Save()` (SoundService 규약과 일치).
- `Dispose()`는 특별한 네이티브 자원이 없으므로 no-op에 가깝다(향후 provider가 자원을 가지면 위임).

## 플랫폼 구현 세부

### iOS (`FoundationHaptic.mm` + `iOSHapticProvider`)
- 네이티브가 노출할 C 함수(예): `void FDI_HapticImpact(int style)`,
  `void FDI_HapticNotification(int type)`, `void FDI_HapticSelection()`.
- 내부적으로 `UIImpactFeedbackGenerator`(스타일별)/`UINotificationFeedbackGenerator`/
  `UISelectionFeedbackGenerator`를 `prepare()` 후 발생시킨다. 제너레이터는 재사용을 위해 정적 보관 가능.
- `iOSHapticProvider`는 `[DllImport("__Internal")]`로 위 함수를 바인딩하고 enum → int 매핑을 담당.

### Android (`AndroidHapticProvider`)
- `AndroidJavaObject`로 현재 `Activity`의 `Vibrator` 서비스를 획득.
- API 29+(`Build.VERSION.SDK_INT >= 29`): `VibrationEffect.createPredefined`
  (Selection→EFFECT_TICK, Light/Soft→EFFECT_TICK, Medium→EFFECT_CLICK,
  Heavy/Rigid→EFFECT_HEAVY_CLICK). Notification도 predefined effect로 근사
  (Success→EFFECT_CLICK, Warning/Error→EFFECT_HEAVY_CLICK).
- 구버전 폴백: `vibrate(long milliseconds)`로 스타일별 지속시간 근사.
- **권한**: `AndroidManifest.xml`에 `<uses-permission android:name="android.permission.VIBRATE"/>`
  필요. README에 설정 방법 명시.

### 그 외 플랫폼 (`NoopHapticProvider`)
- 모든 메서드가 아무 것도 하지 않는다. 에디터·데스크톱에서 예외 없이 동작.

## DI 등록

```csharp
public static void RegisterHapticService(this IContainerBuilder builder)
{
    builder.Register<IHapticService, HapticService>(Lifetime.Singleton);
}
```

외부 리소스 의존이 없어 카탈로그 등 추가 인자는 불필요하다.

## 테스트 계획 (EditMode, NSubstitute)

`FoundationDI.Tests` 어셈블리에 추가. seam(`IHapticProvider`)을 substitute로 대체해 외부 의존
없이 검증한다. 예상 테스트(한국어 의도, `should~` 형식):

- 활성화 상태에서 Impact 호출 시 provider의 Impact가 같은 스타일로 호출되어야 한다
- 활성화 상태에서 Notification 호출 시 provider의 Notification이 같은 타입으로 호출되어야 한다
- 활성화 상태에서 Selection 호출 시 provider의 Selection이 호출되어야 한다
- 비활성화 상태에서는 어떤 provider 메서드도 호출되지 않아야 한다
- Enabled setter가 PlayerPrefs에 값을 영속화해야 한다

세부 테스트 목록은 구현 계획(plan.md / `docs/superpowers/plans/`)에서 TDD 사이클 단위로 확정한다.

## 범위 외 (YAGNI)

- 커스텀 진폭/파형 커브 편집 UI.
- 햅틱 세기(intensity) 슬라이더 — 현재는 on/off 토글만.
- iOS `CoreHaptics`(CHHapticEngine) 기반 커스텀 패턴 — 시맨틱 제너레이터로 충분.
