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

| API | iOS | Android(API 29+) |
|---|---|---|
| Impact.Light / Soft | UIImpactFeedbackStyle.Light / Soft | EFFECT_TICK |
| Impact.Medium | UIImpactFeedbackStyle.Medium | EFFECT_CLICK |
| Impact.Heavy / Rigid | UIImpactFeedbackStyle.Heavy / Rigid | EFFECT_HEAVY_CLICK |
| Notification.Success | UINotificationFeedbackType.Success | EFFECT_CLICK |
| Notification.Warning / Error | UINotificationFeedbackType.Warning / Error | EFFECT_HEAVY_CLICK |
| Selection | UISelectionFeedbackGenerator | EFFECT_TICK |

Android API 29 미만은 스타일별 지속시간(ms)으로 근사한다. iOS 네이티브 브리지: `Assets/Plugins/iOS/FoundationHaptic.mm`.

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

Android에서 진동하려면 매니페스트에 권한이 필요하다. `Assets/Plugins/Android/AndroidManifest.xml`
(없으면 생성)에 다음을 추가한다.

```xml
<uses-permission android:name="android.permission.VIBRATE" />
```

## 플랫폼 참고

- iOS 시뮬레이터는 햅틱을 지원하지 않는다 — 실기기에서 확인.
- 에디터/데스크톱은 `NoopHapticProvider`로 아무 동작도 하지 않는다.
