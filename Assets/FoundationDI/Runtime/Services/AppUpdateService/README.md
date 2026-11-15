# AppUpdateService

스토어(Google Play·App Store)에 새 버전이 올라왔는지 **서버 없이** 확인하고 스토어로 보내는 창구입니다.
원스토어는 대상이 아닙니다(게임 버전을 조회하는 공개 API가 없습니다).

- 서비스는 **확인과 이동만** 합니다. 언제 확인할지, 팝업을 띄울지, 업데이트를 강제할지는 게임이 정합니다.
- 확인에 실패해도 던지지 않고 `Unknown`을 돌려줍니다. 업데이트 확인 때문에 게임 시작 흐름이 끊기면 안 됩니다.

## 1. 설치

1. `Create > FoundationDI > App Update Service Settings`로 설정 에셋을 만듭니다.
2. **Use Play App Update**(기본 켜짐)를 켜 두면 `Assets/FoundationDI.Data/Editor/FoundationDIPlayAppUpdateDependencies.xml`이
   생성되고 EDM4U가 `com.google.android.play:app-update:2.1.0`을 받습니다. 끄면 파일을 지우고 Android에서는 확인하지 않습니다(`Unknown`).
   - 패키지 안에 고정 XML을 두지 않는 이유는 UMP·ReviewService와 같습니다 — EDM4U가 패키지 폴더도 스캔해 안 쓰는 프로젝트에 라이브러리가 강제로 들어갑니다.
   - 인스펙터 토글·`Tools/FoundationDI/App Update/Sync Play In-App Updates Dependencies`·빌드 직전에 동기화합니다.
3. **App Store Country**에 출시 국가 코드(`kr`, `jp` 등)를 넣습니다. 비우면 애플이 미국 스토어를 조회하므로,
   미국에 출시하지 않은 앱은 결과가 비어 늘 `Unknown`이 됩니다(인스펙터가 안내합니다).

## 2. 등록과 사용

```csharp
builder.RegisterAppUpdateService(_appUpdateSettings);   // 루트 IInstaller
```

```csharp
var result = await _appUpdate.CheckAsync();

// 강제 업데이트: 최소 버전은 게임이 정한다(Remote Config 등). 반드시 Available일 때만 막는다.
if (AppVersion.Compare(Application.version, _minVersion) < 0 && result.Status == AppUpdateStatus.Available)
{
    ShowForceUpdatePopup(onConfirm: () => _appUpdate.OpenStore());
}
else if (result.Status == AppUpdateStatus.Available && result.DaysSinceRelease >= 3)
{
    ShowUpdateSuggestion(onConfirm: () => _appUpdate.OpenStore());
}
```

| 멤버 | 설명 |
| --- | --- |
| `CheckAsync()` | `AppUpdateCheckResult`를 돌려준다. 실패해도 던지지 않는다 |
| `OpenStore()` | 스토어의 앱 페이지를 연다. 열었으면 true. iOS는 **확인을 한 번 성공한 뒤에만** 열 수 있다(앱 ID를 조회 응답에서 얻는다) |
| `AppVersion.Compare(a, b)` | 버전을 마디마다 숫자로 비교한다(`1.10.0` > `1.9.3`, `1.2` = `1.2.0`). 숫자가 아닌 마디가 있으면 null |

`AppUpdateCheckResult`

| 필드 | 설명 |
| --- | --- |
| `Status` | `Available` / `UpToDate` / `Unknown`(네트워크 실패·미지원 플랫폼·에디터·스토어 밖 설치) |
| `DaysSinceRelease` | 새 버전이 나온 뒤 지난 날짜. iOS는 `currentVersionReleaseDate` 기준, Android는 **기기의 Play 스토어가 업데이트를 알게 된 뒤** 지난 날짜(`clientVersionStalenessDays`). 모르면 null |
| `Priority` | Play Developer API로 올릴 때 지정한 `inAppUpdatePriority`(0~5). Android 전용이며 Play Console 화면에서는 지정할 수 없다 |

### 왜 Available일 때만 막아야 하나

스토어에 새 버전이 보이지 않는 유저가 있습니다. 최소 버전만 보고 막으면 그 유저는 업데이트할 방법 없이 갇힙니다.

- **Android 단계적 출시**: 출시 대상에 아직 안 든 유저에게는 업데이트가 보이지 않습니다(`UpToDate`).
- **iOS 조회 지연**: iTunes Lookup은 CDN 캐시라 출시 뒤 몇 시간~하루 가까이 예전 버전을 돌려줄 수 있습니다.
- **iOS 최소 OS**: 새 버전이 요구하는 iOS보다 기기 OS가 낮으면 서비스가 `UpToDate`로 답합니다(그 기기는 받을 수 없습니다).

## 3. 구조

- `IAppUpdateStore` seam — `PlayAppUpdateStore`(JNI, `AppUpdateManagerFactory`) / `AppStoreAppUpdateStore`(iTunes Lookup, `UnityWebRequest`) /
  `UnsupportedAppUpdateStore`(에디터, `Unknown`). `AppUpdateStoreFactory`가 `#if` 없이 `Application.platform`으로 고릅니다.
- **Android는 확인만 Play In-App Updates로 하고 스토어 이동은 `market://` 페이지**입니다. 인앱 업데이트 흐름(Immediate/Flexible)은
  쓰지 않습니다 — iOS와 동작을 맞추고, 진행 상태를 서비스가 들고 있지 않기 위해서입니다. Play에는 버전을 조회하는 다른 공개 방법이
  없어 확인에는 라이브러리가 필요합니다. `UpdateAvailability`는 캐스팅하지 않고 `PlayAppUpdateMapper`로 번역합니다.
- Android Task 결과는 안드로이드 UI 스레드로 오므로 `IMainThreadDispatcher`로 유니티 메인 스레드에 넘겨 완료합니다.
  디스패처는 Android 창구를 만들 때만 해석합니다.
- iOS 조회는 `https://itunes.apple.com/lookup?bundleId=<번들ID>&country=<국가>`이고 타임아웃은 10초입니다. 결과가 비면(그 국가에
  미출시) `Unknown`, 스토어 버전이 같거나 낮으면(TestFlight로 새 버전 설치) `UpToDate`입니다.
- 창구는 처음 쓸 때 만듭니다. 라이브러리가 빌드에 없어 생성에서 던지면 그 실패를 DI 해석(앱 시작)으로 번지지 않게 하고, 이후 확인은 `Unknown`입니다.

## 4. 알려진 범위 밖

- **실기 검증이 아직 없습니다.**
- Android는 **Play 스토어에서 설치한 빌드에서만** 동작합니다. 사이드로드 빌드는 Task가 실패해 `Unknown`입니다.
  테스트는 내부 앱 공유(Internal App Sharing)로 설치합니다.
- iOS 기기의 스토어 국가를 자동으로 읽지 않습니다(`SKStorefront`는 네이티브 호출이 필요합니다). 여러 국가에 출시했다면
  주 출시 국가를 넣습니다 — 버전은 국가마다 같으므로 그 국가에 출시돼 있기만 하면 됩니다.
