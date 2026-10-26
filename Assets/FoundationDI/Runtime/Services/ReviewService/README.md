# ReviewService

OS 앱 내 리뷰 창(Android **Play In-App Review**, iOS **SKStoreReviewController**)을 띄우는 창구입니다.
**언제 부를지는 게임이 정합니다**(예: 10·20·40레벨 승리 뒤). BlockAway의 `IStoreReview` 구현을 옮겼습니다.

- 우리 팝업으로 의견을 먼저 묻지 않습니다 — Google은 리뷰 창 앞에서 의견을 묻는 것을, Apple은 자체 리뷰 요청 팝업을 막습니다.
- 리뷰 창은 OS가 횟수를 제한해 **요청해도 뜨지 않을 수 있고, 떴는지·몇 점을 줬는지는 알 수 없습니다.**
  iOS는 1년에 최대 3번이며 TestFlight에서는 뜨지 않습니다. Android는 내부 테스트 트랙에서 동작을 확인할 수 있습니다.
- 예외는 삼키고 경고만 남깁니다. 리뷰 창 때문에 게임 흐름이 끊기면 안 됩니다.

## 1. 설치

1. `Create > FoundationDI > Review Service Settings`로 설정 에셋을 만듭니다.
2. **Use Play In-App Review**(기본 켜짐)를 켜 두면 `Assets/FoundationDI.Data/Editor/FoundationDIPlayReviewDependencies.xml`이
   생성되고 EDM4U가 `com.google.android.play:review:2.0.2`를 받습니다. 끄면 파일을 지우고 Android에서는 창을 띄우지 않습니다.
   - 패키지 안에 고정 XML을 두지 않는 이유는 UMP와 같습니다 — EDM4U가 패키지 폴더도 스캔해 안 쓰는 프로젝트에 라이브러리가 강제로 들어갑니다.
   - 인스펙터 토글·`Tools/FoundationDI/Review/Sync Play In-App Review Dependencies`·빌드 직전에 동기화합니다.
   - EDM4U가 없으면 인스펙터가 경고합니다(LevelPlay를 설치했다면 함께 들어 있습니다).
3. Google의 Unity 플러그인(`com.google.play.review`)은 설치하지 않습니다. EDM4U를 UPM으로 함께 들여 Assets의 EDM4U와 겹칩니다.

## 2. 등록과 사용

```csharp
builder.RegisterReviewService(_reviewSettings);   // 루트 IInstaller
```

```csharp
// 승리 창이 뜰 때 — 리뷰를 띄울 차례면 미리 준비(Android는 ReviewInfo를 받아 둔다)
if (IsReviewLevel(level)) _review.Prepare();

// 메뉴로 돌아갈 때
if (IsReviewLevel(level) && !_notifications.TryRequestPermissionOnce()) _review.Request();
```

| 멤버 | 설명 |
| --- | --- |
| `Prepare()` | 곧 띄울 것 같으면 미리 준비. Android만 의미가 있다(ReviewInfo는 잠시만 유효하므로 띄우기 직전에) |
| `Request()` | 리뷰 창 요청. **요청을 보냈으면 true — 창이 뜬다는 뜻은 아니다.** 준비된 것이 없으면 받은 뒤 바로 띄운다 |

## 3. 구조

- `IStoreReview` seam — `AndroidStoreReview`(JNI, `ReviewManagerFactory`) / `IosStoreReview`(`Device.RequestStoreReview`) /
  `UnsupportedStoreReview`(에디터, 로그만). `StoreReviewFactory`가 `#if` 없이 `Application.platform`으로 고릅니다 —
  어느 빌드 타깃에서도 두 구현의 컴파일이 확인됩니다.
- Android Task 결과는 안드로이드 UI 스레드로 오므로 `IMainThreadDispatcher`로 유니티 메인 스레드에 넘겨 상태를 한 스레드에서만 만집니다.
  디스패처는 Android 창구를 만들 때만 해석합니다(러너 GameObject가 생기므로).
- 창구는 처음 쓸 때 만듭니다. Play Review 라이브러리가 빌드에 없으면 생성에서 던지는데, 그 실패를 DI 해석(앱 시작)으로 번지지 않게 하고 이후 요청은 `false`로 끝냅니다.

## 4. 알려진 범위 밖

- 이 패키지로 옮긴 뒤 실기 검증은 아직 없습니다(BlockAway에서 같은 구현을 출시했습니다).
