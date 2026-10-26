# LocalNotificationService

로컬 알림 **예약 엔진**입니다. 무엇을 언제 보낼지는 게임이 `ILocalNotificationPlanner`로 정하고, 서비스는
다시 예약할 때(시작·앱이 포커스를 잃을 때) planner들을 불러 OS에 예약합니다. 권한 1회 요청과 켜기/끄기 영속화도 맡습니다.
(BlockAway·MahjongTile의 `LocalNotifications`에서 게임 고유 규칙을 걷어낸 것입니다.)

- **예약은 늘 "전부 지우고 다시 넣는다"** — 앱을 내리기 직전의 상태(오늘 선물을 받음 등)가 다음 알림에 반영됩니다.
- **지난 시각의 알림은 서비스가 거릅니다.** planner는 `now` 이후만 신경 쓰면 됩니다.
- **planner 하나가 예외를 던져도 나머지는 예약됩니다.** 알림 때문에 게임이 멈추지 않도록 모든 예외는 경고로만 남습니다.
- **알림 창구는 처음 쓸 때 만듭니다.** 모바일 알림 센터 초기화는 저사양 기기에서 0.5~1초 걸립니다.

---

## 1. 설치

1. `com.unity.mobile.notifications`(2.5.0 확인)를 설치합니다. 설치하면 `FOUNDATIONDI_MOBILENOTIFICATIONS` 심볼이
   자동으로 켜지고 옵셔널 어셈블리 `FoundationDI.MobileNotifications`가 컴파일됩니다. **설치하지 않으면 예약하지 않는
   `UnsupportedLocalNotifier`로 떨어집니다**(에디터도 같음 — 예약 개수만 로그로 남습니다).
2. `Create > FoundationDI > Local Notification Service Settings`로 설정 에셋을 만듭니다.
   - **Android Channel Id**: 한 번 출시하면 바꾸지 않습니다(유저의 채널 설정이 이어지지 않습니다).
   - **Android Small Icon**: 흰색 실루엣 + 투명 배경 텍스처. 비우면 상태 표시줄에 흰 사각형이 보입니다.
3. 빌드 직전에 `MobileNotificationSettingsApplier`가 모바일 알림 설정을 맞춥니다(메뉴: `Tools/FoundationDI/Notifications/Apply Mobile Notification Settings`).
   - iOS **Request Authorization on App Launch를 끕니다** — 게임이 정한 때 `TryRequestPermissionOnce`로 묻습니다.
     첫 실행에 맥락 없이 물으면 거절률이 높고, 거절하면 다시 물을 수 없습니다.
   - Android **재부팅 뒤 다시 예약**을 켜고, 정확한 시각 예약(SCHEDULE_EXACT_ALARM)은 쓰지 않습니다.
   - 작은 아이콘을 `fdi_notification_small` 리소스로 등록합니다(읽기 가능·무압축으로 임포트 설정을 바꿉니다).

## 2. 등록과 사용

```csharp
public class RootInstaller : MonoBehaviour, IInstaller
{
    [SerializeField] private LocalNotificationServiceSettings _notificationSettings;

    public void InstallBindings(ContainerBuilder builder)
    {
        builder.RegisterLocalNotificationService(_notificationSettings);

        // 일정은 게임이 정한다. 여럿 등록하면 결과를 합친다.
        builder.RegisterType(typeof(ReminderPlanner), new[] { typeof(ILocalNotificationPlanner) },
                             Lifetime.Singleton, Resolution.Lazy);
    }
}

public sealed class ReminderPlanner : ILocalNotificationPlanner
{
    public IEnumerable<LocalNotification> Plan(DateTime now)
    {
        // 내일부터 7일 동안 매일 20시 (now는 기기 현지 시각)
        for (var day = 1; day <= 7; day++)
        {
            yield return new LocalNotification(now.Date.AddDays(day).AddHours(20), "오늘의 퍼즐", "새 레벨이 기다려요!");
        }
    }
}
```

```csharp
_notifications.Start();                                   // 첫 화면이 뜬 뒤 (지금 예약 + 포커스를 잃을 때마다 다시 예약)
bool asked = _notifications.TryRequestPermissionOnce();   // 첫 승리 뒤 등 — 처음 한 번만 창을 띄운다
if (!asked) _review.Request();                            // 같은 때 두 OS 창을 겹쳐 띄우지 않는다
_notifications.IsEnabled = isOn;                          // 설정 화면 토글 (끄면 예약을 지운다)
```

## 3. API

| 멤버 | 설명 |
| --- | --- |
| `IsEnabled` | 게임 설정의 켜기/끄기. `PlayerPrefs`(기본 켜짐)로 영속화. 끄면 예약을 지우고 켜면 다시 예약 |
| `HasPermission` | OS 알림 권한이 있는지 |
| `Start()` | 지금 예약하고, 앞으로 앱이 포커스를 잃을 때마다 다시 예약 |
| `Reschedule()` | 전부 지우고 다시 예약. 권한이 없거나 꺼져 있으면 지우기만 함. 예약 수를 반환 |
| `TryRequestPermissionOnce()` | 처음 부를 때만 권한 창(기다리지 않음). 창이 떠 있는 동안 앱이 꺼져도 다시 묻지 않게 먼저 기록. 이미 권한이 있으면 창 없이 예약. **이번에 창을 띄웠으면 true** |
| `RequestPermissionAsync()` | 물었는지와 무관하게 권한 요청(설정 화면의 "알림 켜기" 등). 허용되면 예약 |

## 4. 구조

- `ILocalNotifier` — OS 창구 seam. `MobileLocalNotifier`(옵셔널 어셈블리, `NotificationCenter` 공통 API) / `UnsupportedLocalNotifier`.
  권한 확인만 플랫폼 API를 씁니다(공통 API의 `RequestPermission`은 아직 묻지 않았으면 창을 띄워 확인용으로 쓸 수 없습니다).
- `LocalNotifierRegistry` — 코어가 옵셔널 어셈블리를 참조할 수 없어 어댑터가 `BeforeSceneLoad`에 스스로 등록합니다
  (`TrackingAuthorizationRegistry`와 같은 방식). IL2CPP 스트리핑은 `SdkDefineTable` 항목과 `[AlwaysLinkAssembly]`로 막습니다.
- `ILocalNotificationStorage`(기본 `PlayerPrefsLocalNotificationStorage`), `IApplicationFocusSource`(기본 `Application.focusChanged`) seam.

## 5. 알려진 범위 밖

- 알림을 눌러 앱을 연 경우의 처리(`NotificationCenter.LastRespondedNotification`)와 반복 알림, 알림 ID 관리는 다루지 않습니다.
- 실기 검증은 BlockAway의 같은 구현으로만 되어 있습니다(이 패키지 이식 후 실기 미검증).
