# HapticService

iOS/Android 공통 햅틱(촉각 피드백) 서비스. iOS Taptic Engine의 시맨틱 피드백 모델을 API로 채택하고,
Android는 동일 시맨틱을 `VibrationEffect`로 매핑한다. 미지원 플랫폼(에디터·데스크톱)은 무시(Noop)한다.

## API

```csharp
public enum HapticImpact { Light, Medium, Heavy, Soft, Rigid }
public enum HapticNotification { Success, Warning, Error }

public interface IHapticService : IDisposable
{
    bool Enabled { get; set; }              // PlayerPrefs("HAPTIC_ENABLED")에 영속화, 기본 true
    void Impact(HapticImpact style);        // 물리 충돌/타격 느낌
    void Notification(HapticNotification type); // 결과 통지(성공/경고/실패)
    void Selection();                       // 선택 변경 틱
}
```

`Enabled == false`이면 모든 호출이 무시된다.

## 시맨틱 매핑

| API | iOS | Android (지속시간/패턴, ms) |
|---|---|---|
| Impact.Light | UIImpactFeedbackStyle.Light | 40 |
| Impact.Soft | UIImpactFeedbackStyle.Soft | 45 |
| Impact.Medium | UIImpactFeedbackStyle.Medium | 60 |
| Impact.Rigid | UIImpactFeedbackStyle.Rigid | 75 |
| Impact.Heavy | UIImpactFeedbackStyle.Heavy | 90 |
| Notification.Success | UINotificationFeedbackType.Success | 50 |
| Notification.Warning | UINotificationFeedbackType.Warning | 50·50 |
| Notification.Error | UINotificationFeedbackType.Error | 70·70·70 |
| Selection | UISelectionFeedbackGenerator | 30 |

Android은 `VibrationEffect.createOneShot`/`createWaveform`(+`DEFAULT_AMPLITUDE`)로 실제 진동을 보장한다.
`createPredefined`(EFFECT_*)는 벤더 미구현 기기에서 조용히 무시되므로 사용하지 않는다.
진폭 제어가 없는 ERM 모터에서는 **지속시간만이 세기 차별 요소**이며, ~30ms 미만은 지각되지 않으므로
가장 가벼운 Selection/Light 도 30~40ms 하한을 둔다. API 26 미만은 legacy `Vibrator.vibrate`로 폴백한다.
iOS 네이티브 브리지: `Assets/Plugins/iOS/FoundationHaptic.mm`.

## DI 등록

```csharp
// RootLifetimeScope.Configure(IContainerBuilder builder)
builder.RegisterHapticService();
```

## 사용

```csharp
public class MyButton
{
    private readonly IHapticService _haptic;
    public MyButton(IHapticService haptic) => _haptic = haptic;

    public void OnPressed()   => _haptic.Impact(HapticImpact.Light);
    public void OnConfirmed() => _haptic.Notification(HapticNotification.Success);
    public void OnScrollTick()=> _haptic.Selection();
}
```

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

## 플랫폼 참고

- iOS 시뮬레이터는 햅틱을 지원하지 않는다 — 실기기에서 확인.
- 에디터/데스크톱은 `NoopHapticProvider`로 아무 동작도 하지 않는다.
