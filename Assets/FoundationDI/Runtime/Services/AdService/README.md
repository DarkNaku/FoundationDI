# AdService

**광고 네트워크 중립 서비스**입니다. AdMob 미디에이션 / Unity LevelPlay / AppLovin MAX 중
어느 SDK를 붙이더라도 게임 코드는 동일한 `IAdService` API 하나로 전면·보상·배너 광고를 다룹니다.
재시도·백오프, 보상 확정, 광고제거 게이트 같은 정책은 provider(SDK 어댑터)가 아니라
서비스 자신이 갖고 있어서, 세 SDK 어댑터마다 같은 로직을 복붙하지 않습니다.

현재 구현된 provider는 **Dummy · AppLovin MAX · LevelPlay** 셋입니다. AdMob 어댑터는 아직
없습니다.

Dummy는 SDK 없이 로드 지연·실패·보상·임프레션을 흉내 내어 전체 흐름(재시도 백오프,
`AdServiceRunner` 펌프, `Awaitable` 완료)을 실기에서 검증할 수 있게 하기 위한 것입니다.
AppLovin(`FoundationDI.AppLovin`)과 LevelPlay(`FoundationDI.LevelPlay`)는 각각
`FOUNDATIONDI_APPLOVIN` / `FOUNDATIONDI_LEVELPLAY` 심볼이 걸린 옵셔널 어셈블리라, SDK가 없는
프로젝트에서는 컴파일 대상에서 아예 빠집니다.

---

## 1. 빠른 시작

### 1.1 설정 에셋 만들기

`Assets/Settings/`(또는 원하는 위치)에 우클릭 → `Create > FoundationDI > Ad Service Settings`.

`Dummy Provider` 섹션의 `Load Delay Seconds` / `Failure Rate` / `Ad Duration Seconds` /
`Banner Height`은 Dummy provider가 로드 지연·실패·보상·배너 표시 시간을 흉내 내는 데 쓰는
값입니다. 실기에서 재시도·백오프를 눈으로 확인하려면 `Failure Rate`를 0.3~0.5 정도로 올려 둡니다.

### 1.2 DI 등록

```csharp
using DarkNaku.FoundationDI;
using UnityEngine;
using VContainer;
using VContainer.Unity;

public class RootLifetimeScope : LifetimeScope
{
    [SerializeField] private AdServiceSettings _adServiceSettings;

    protected override void Configure(IContainerBuilder builder)
    {
        builder.RegisterAdService(_adServiceSettings);
    }
}
```

`RegisterAdService`는 `IAdRemovalStorage`(기본 `PlayerPrefsAdRemovalStorage`),
`IAdDispatcher`(기본 `UnityAdDispatcher`), `IAdProviderFactory`(`AdProviderFactory`),
`IAdService`(`AdService`)를 싱글턴으로 등록합니다. `settings`가 `null`이면 에러 로그만 남기고
서비스를 등록하지 않습니다(등록 자체를 건너뛰므로, 주입받는 쪽에서 VContainer 해석 에러로 드러납니다).

### 1.3 사용

```csharp
public class GameOverScreen
{
    private readonly IAdService _ads;

    public GameOverScreen(IAdService ads) => _ads = ads;

    public async Awaitable ShowAsync()
    {
        if (!_ads.IsInitialized) await _ads.InitializeAsync();

        var result = await _ads.Rewarded.ShowAsync("game_over_revive");

        if (result.IsRewarded)
        {
            // result.Reward.Amount / result.Reward.Label
        }
    }
}
```

`InitializeAsync`는 재진입 안전합니다 — 진행 중에 다시 호출하면 새 초기화를 시작하지 않고
같은 결과에 편승합니다. 이미 초기화됐으면 즉시 `true`를 반환합니다.

`Interstitial`/`Rewarded`/`Banner`는 초기화가 성공하기 전에는 `null`입니다. 게임 코드는
반드시 `InitializeAsync`를 먼저 기다려야 합니다.

---

## 2. `AdShowOutcome`

`ShowAsync`는 `Awaitable<AdShowResult>`를 반환합니다. `AdShowResult.Outcome`은 다음 6가지 중 하나입니다.

| 값 | 의미 | 발생 시점 |
| --- | --- | --- |
| `Shown` | 전면 광고가 정상적으로 노출되고 닫혔다 | `FullScreenAdUnit`(전면)이 보상 없이 닫혔을 때 |
| `Rewarded` | 보상형 광고를 끝까지 보고 보상이 확정됐다 | `FullScreenAdUnit`(보상)이 닫혔고 그 전에 Rewarded 콜백이 왔을 때 |
| `Dismissed` | 보상형 광고를 보상 없이 중간에 닫았다 | `FullScreenAdUnit`(보상)이 닫혔지만 Rewarded 콜백이 없었을 때 |
| `NotReady` | 아직 로드되지 않았다 | `ShowAsync` 호출 시 어댑터가 준비되지 않음(내부적으로 `Load()`를 다시 걸어 다음 기회를 준비) |
| `Failed` | 표시 중 실패했거나 중복 호출됐다 | SDK의 표시 실패 콜백, 이미 표시 중인 상태에서 재호출, 서비스가 `Dispose`된 뒤 대기 중이던 호출 |
| `Blocked` | `AdsRemoved` 또는 쿨다운 등 정책에 의해 차단됐다 | 전면 광고에서 `AdsRemoved == true`일 때(로드조차 시도하지 않고 즉시 반환), 또는 전면 광고가 쿨다운 중일 때(아래 2.1절) |

- **`IsRewarded`** — `Outcome == Rewarded`. "보상을 줘도 되는가"만 볼 때 씁니다.
- **`WasShown`** — `Shown` / `Rewarded` / `Dismissed` 셋 다 포함합니다. "실제로 화면에 광고가
  떴는가"를 볼 때 씁니다(예: 하루 노출 횟수 카운트). `NotReady`/`Failed`/`Blocked`는 포함되지 않습니다.

`Failed`일 때만 `Reward`가 아니라 `Error`(`AdError` — `Code`/`Message`)가 유효합니다.
`Rewarded`일 때만 `Reward`(`AdReward` — `Label`/`Amount`)가 유효합니다.

보상은 **래치됩니다.** SDK마다 "보상 콜백 → 닫힘" 순서가 다르고(AdMob은 보상 먼저, 일부
미디에이션 네트워크는 닫힘이 먼저 오는 사례가 보고됨) 순서를 보장하지 않으므로, `FullScreenAdUnit`은
보상 콜백을 즉시 완료시키지 않고 보관해 두었다가 닫힘에서 유예 프레임(`Reward Grace Frames`,
기본 1프레임)만큼 기다린 뒤 최종 `Outcome`을 확정합니다. 닫힘이 보상보다 먼저 와도 유예
프레임 안에 보상이 도착하면 `Rewarded`로 확정됩니다.

### 2.1 전면 쿨다운 게이트

전면광고가 한 번 표시되면, 설정된 시간(`Interstitial Cooldown Seconds`, 기본 **120초**)이
지나기 전까지는 다시 `ShowAsync`를 불러도 즉시 `Blocked`를 반환합니다. `ShowAsync`의 재진입
가드("이미 표시 중이면 `Failed(-2)`") 다음, `NotReady` 가드 앞에서 검사됩니다 — 이미 표시
중인 재진입은 쿨다운 탓이 아니라 "이미 표시 중"이라는 더 정확한 이유로 진단되고, 반대로
쿨다운으로 막힌 호출은 로드조차 시도하지 않습니다.

레벨 클리어 같은 트리거로 짧은 세션을 연달아 반복하면, 쿨다운이 없을 때 플레이어가 1분
안에 전면광고를 세 번씩 보게 될 수 있습니다. 쿨다운은 그런 몰아치기를 막는 정책입니다.

- **쿨다운은 표시 시점(`Displayed`)에 시작됩니다.** 요청 시점도, 닫힘 시점도 아닙니다 —
  "마지막으로 실제 유저 화면에 뜬 순간" 기준으로 다음 표시까지의 최소 간격을 둔다는
  의도입니다. 표시에 실패한(유저가 보지 못한) 쇼는 쿨다운을 걸지 않습니다.
- **세션 스코프입니다.** 영속화되지 않습니다 — 앱을 재시작하면 쿨다운도 초기화됩니다.
- **전면광고에만 적용됩니다.** 보상형은 유저가 자발적으로 보는 것이라 쿨다운 대상이
  아닙니다(3절의 `AdsRemoved` 면제와 같은 이유입니다). `AdService.BuildAdUnits`가 전면
  유닛에는 설정값을, 보상 유닛에는 항상 `0`(게이트 무력화)을 조립해 넘깁니다.
- **`0`으로 설정하면 게이트가 완전히 꺼집니다.** 표시 직후에도 바로 재표시할 수 있습니다.
- **경과 시간은 `AdServiceRunner.Update()`가 매 프레임 펌프하는 `Time.unscaledDeltaTime`
  기준입니다.** `timeScale = 0`으로 게임을 멈춰도(전면광고 표시 중 흔한 상태) 쿨다운은
  정상적으로 흐릅니다. 반대로 `Update` 자체가 돌지 않는 동안은 흐르지 않습니다 — 앱이
  백그라운드로 내려가 있거나, 네이티브 전면광고 액티비티가 화면을 덮어 Unity 플레이어가
  일시정지된 동안이 그렇습니다. 즉 실제로는 "표시 이후 Unity가 `Update`를 돌린 시간의
  누적"을 잽니다.
- `AdServiceSettings`의 `Interstitial Cooldown Seconds` 필드로 편집합니다. `[Min(0)]`은
  인스펙터 편집만 막으므로, 손으로 고친 `.asset`이나 스크립트로 만든 값이 음수면
  `ToOptions()`가 `0`으로 클램프합니다.

**`CanShow`** — `ShowAsync`를 지금 부르면 실제로 표시가 시작될지 미리 알려주는 프로퍼티입니다
(`IsReady`이고, 해제되지 않았고, `AdsRemoved`에 막히지 않았고, 쿨다운 중이 아니고, 이미 진행
중인 표시 요청이 없을 때 — 요청부터 실제 표시 사이의 구간도 포함해서 — `true`). 게임 UI가
"지금 광고를 보여줄 수 있는가"로 버튼을 켜고 끄려 할 때, `ShowAsync`를 호출해 `Blocked`를
사후에 해석하지 않고 미리 판단할 수 있게 해 줍니다.

---

## 3. `AdsRemoved`의 포맷별 동작

```csharp
_ads.AdsRemoved = true;   // 인앱 구매 완료 시 게임 코드가 직접 설정
```

| 포맷 | `AdsRemoved == true`일 때 |
| --- | --- |
| 전면(Interstitial) | `ShowAsync`가 즉시 `Blocked`를 반환한다. 로드도 시도하지 않는다 |
| 배너(Banner) | `Show()`를 불러도 어댑터를 만들지 않는다. 이미 떠 있었다면 즉시 내려가고 `HeightChanged(0)`이 발화한다 |
| 보상(Rewarded) | **차단하지 않는다.** 계속 정상 로드·표시된다 — 유저가 자발적으로 보는 보상형 광고까지 막을 이유가 없기 때문 |

`AdsRemoved`는 세터입니다. **IAP 자체는 이 서비스의 범위 밖입니다** — 구매 검증·복원·상점 연동은
게임 코드(또는 별도 IAP 서비스)가 담당하고, 구매가 확인되면 `AdService.AdsRemoved = true`를
호출해 값만 밀어 넣습니다. 값은 `IAdRemovalStorage`(기본 `PlayerPrefsAdRemovalStorage`)로
영속화되어 다음 실행에 자동 복원됩니다.

`AdsRemovedChanged` 이벤트로 값이 바뀔 때마다 알림을 받을 수 있습니다(예: 배너 자리를 UI
레이아웃에서 접기/펼치기).

---

## 4. 수익 추적

### 4.1 `Paid` 구독

```csharp
_ads.Paid += impression =>
{
    Debug.Log($"{impression.AdPlatform}/{impression.NetworkName} " +
              $"{impression.Format} {impression.Revenue} {impression.Currency}");
};
```

`AdImpression`의 각 필드는 Firebase Analytics `ad_impression` 이벤트 파라미터에 다음과 같이 매핑됩니다.

| `AdImpression` 필드 | `ad_impression` 파라미터 | 비고 |
| --- | --- | --- |
| `AdPlatform` | `ad_platform` | `"AdMob"` / `"LevelPlay"` / `"AppLovin"` / `"Dummy"` |
| `NetworkName` | `ad_source` | 실제로 지면을 채운 네트워크(미디에이션 낙찰사) |
| `AdUnitId` | `ad_unit_name` | |
| `Revenue` | `value` | |
| `Currency` | `currency` | |
| `Format` | `ad_format` | `Banner`/`Interstitial`/`Rewarded` |
| `Placement` | (게임이 직접 매핑, 보통 커스텀 파라미터) | 아래 4.3 참고 |

> **`Currency`를 무시하고 USD로 가정하면 AdMob에서 틀립니다.** AdMob(GMA)의 임프레션 수익은
> 항상 USD로 온다고 보장되지 않습니다. `Revenue`는 반드시 `Currency`와 함께 저장·집계해야 하며,
> 여러 통화가 섞인 채로 `Revenue`만 합산하면 매출 집계가 틀어집니다.

### 4.2 임프레션이 합류하는 경로

`AdService.Paid`는 두 경로가 합쳐진 하나의 이벤트입니다.

1. **어댑터별 `Paid`** — `IFullScreenAdapter`/`IBannerAdapter` 각각에 달린 임프레션 이벤트.
   AdMob, AppLovin MAX처럼 광고 객체 단위로 수익 콜백이 오는 SDK가 이 경로를 씁니다.
2. **provider 전역 `IAdProvider.ImpressionPaid`** — 임프레션 데이터가 광고 객체가 아니라
   **SDK 전역 이벤트 하나**로만 오는 SDK를 위한 경로입니다. 그런 SDK에서는 어댑터별 `Paid`만
   있으면 특히 **배너 자동 갱신 임프레션**이 어떤 어댑터에도 매칭되지 않아 조용히 누락됩니다.
   **다만 현재 구현된 세 provider 중 이 경로를 쓰는 것은 없습니다** — LevelPlay 9.5.1은 각 광고
   객체가 `OnAdImpressionDataReady`를 갖고 있고 전역 `LevelPlay.OnImpressionDataReady`는
   `[Obsolete]`라, LevelPlay 어댑터도 어댑터별 경로를 씁니다. AdMob 어댑터를 붙일 때 다시
   판단할 seam입니다.

`AdService.BuildAdUnits()`가 두 경로를 모두 `OnPaid`로 구독해 하나의 공개 `Paid`로 합류시킵니다.
**Dummy provider는 `ImpressionPaid`를 절대 발화하지 않습니다** — `DummyAdProvider`의 더미
임프레션은 전부 어댑터별 `Paid`로만 옵니다(no-op 이벤트로 인터페이스 계약만 지킵니다). 만약
Dummy provider가 `ImpressionPaid`도 함께 발화했다면, 어댑터 경로와 provider 경로 양쪽에서
같은 임프레션이 올라와 `Paid`가 두 번 발화되고 수익이 이중 집계됩니다. 실제 SDK 어댑터를
작성할 때는 **한 임프레션이 두 경로 중 정확히 하나로만 나가도록** 해야 합니다(현재는 Dummy·
AppLovin MAX·LevelPlay 셋 모두 어댑터 경로만 씁니다).

### 4.3 `Placement`는 정책 계층이 채운다

```csharp
public string Placement { get; }  // 게임이 ShowAsync에 넘긴 배치명
```

`AdImpression.Placement`는 **어댑터가 채우지 않습니다.** 어댑터는 `ShowAsync(placement)`에
넘어온 배치명을 알 방법이 없습니다 — 어댑터가 아는 것은 SDK 콜백이 준 데이터뿐이고, 배치명은
게임 코드가 `ShowAsync` 호출 시점에만 알려주는 값이기 때문입니다.

대신 `FullScreenAdUnit`이 `ShowAsync(placement)` 호출 시 `_activePlacement`에 배치명을
보관해 두었다가, 어댑터의 `Paid`가 올라오면 `AdImpression.WithPlacement(_activePlacement)`로
**스탬프한 사본**을 재발행합니다(`AdImpression`은 `readonly struct`라 원본은 불변입니다).

**배너는 `Placement`가 항상 `null`입니다.** 배너에는 `ShowAsync(placement)` 같은 호출
배치명 자체가 없습니다(`BannerAdUnit.Show()`는 인자를 받지 않습니다) — 배너는 화면에 계속
띄워 두는 것이 전제이므로 "이 배치명으로 보여 달라"는 개념이 없습니다. 배너 임프레션의
`Placement`가 `null`인 것은 결함이 아니라 정상입니다.

**provider 전역 경로(`IAdProvider.ImpressionPaid`)로 온 임프레션도 `Placement`가 항상
`null`입니다 — 전면·보상이어도 마찬가지입니다.** `Placement` 스탬핑은 `FullScreenAdUnit.OnPaid`
(4.2절의 "어댑터별 `Paid`" 경로)에만 있습니다. `AdService.cs`는 `_provider.ImpressionPaid`를
그대로 공개 `Paid`로 흘려보낼 뿐 어떤 배치명도 채우지 않습니다(`AdService.cs:163,174`).
의도적으로 고치지 않았습니다 — "지금 표시 중인 유닛"을 provider 레벨에서 추적하려면 두 포맷이
겹쳐 표시되는 상황(예: 전면이 뜬 채로 보상 로드가 끝나는 경우)에서 어떤 유닛의 배치명을 찍어야
하는지 오귀속 위험이 생기기 때문입니다.

**이 결정은 LevelPlay 어댑터 작업에서 재검토했고, 그대로 두기로 했습니다.** LevelPlay 9.5.1이
광고 객체별 임프레션 콜백을 제공해 어댑터가 전역 경로 대신 어댑터별 `Paid`를 쓰기 때문에,
LevelPlay의 전면·보상 임프레션도 `FullScreenAdUnit`이 정상적으로 배치명을 스탬프합니다.
전역 경로를 쓰는 provider는 현재 하나도 없으므로, 이 문단은 앞으로 그런 SDK가 붙을 때를 위한
서술입니다.

---

## 5. 3사 어댑터를 추가하는 방법

정책 계층(`FullScreenAdUnit`/`BannerAdUnit`/`AdService`)은 건드리지 않습니다. 새 SDK는
`FoundationDI`와 SDK 어셈블리를 함께 참조하는 별도의 옵셔널 asmdef로 추가합니다.
`FoundationDI`는 SDK가 없는 프로젝트에서도 컴파일돼야 하는데, 만약 `Providers/<SDK>/`를
`FoundationDI` 어셈블리 **자체**(즉 `FoundationDI.asmdef`가 이 폴더까지 포함) 안에 두면 그
어셈블리가 없는 SDK 어셈블리를 참조하게 되어 SDK 미설치 프로젝트에서 컴파일 에러가 됩니다 —
그래서 옵셔널 asmdef가 자기 폴더를 `FoundationDI` 어셈블리에서 도려내야 합니다. 그 폴더 자체는
`Assets/FoundationDI/Runtime/Services/AdService/Providers/<SDK>/`처럼 패키지 안에 둬도
됩니다(AppLovin 어댑터가 그렇습니다) — 패키지가 SDK와 함께 배포되는 걸 의도했다면 그게
맞는 위치입니다. 패키지와 별도로 관리하고 싶은 SDK라면 asmdef를 패키지 폴더 바깥, SDK 설치
위치에 가깝게 둘 수도 있습니다. 어느 쪽이든 핵심은 "옵셔널 asmdef가 자기 폴더를 도려낸다"는
것이지 물리적 위치 자체가 아닙니다.

1. SDK를 `Packages/manifest.json` 또는 `.unitypackage`로 설치한다.
2. `SdkDefineTable.Entries`(`Editor/SdkDefines/`)에 한 줄 추가한다 — 심볼, SDK 대표 타입,
   표시 이름. 그러면 SDK를 임포트할 때 `Player Settings > Scripting Define Symbols`에
   심볼이 자동으로 켜지고 SDK를 지우면 꺼진다
   (`AdProviderFactory.IsAvailable`이 이 심볼로 SDK 존재 여부를 판단합니다).
   자동 관리를 쓰지 않는다면 심볼을 직접 정의해도 됩니다.
3. `FoundationDI.<SDK>`(예: `FoundationDI.AppLovin`)라는 새 asmdef를 만든다. `references`에
   `FoundationDI`와 SDK 어셈블리를 넣고, `defineConstraints`에 1번에서 정의한 심볼(예:
   `FOUNDATIONDI_ADMOB`)을 넣는다. **`defineConstraints`가 충족되지 않으면 Unity는 이
   asmdef를 통째로 건너뛴다** — SDK 어셈블리가 프로젝트에 없어도 참조 자체가 에러가 되지
   않는다(이 동작은 실제로 확인됨). 위치는 패키지 안(`Providers/<SDK>/`, AppLovin 어댑터가
   이 경로)과 패키지 바깥(SDK 설치 위치에 가까운 곳) 둘 다 유효합니다 — `FOUNDATIONDI_<SDK>`
   심볼이 꺼진 프로젝트에서는 위치와 무관하게 asmdef 자체가 스킵되므로 "SDK 없이도 컴파일된다"는
   보장에 영향이 없습니다. 패키지와 함께 SDK까지 배포하고 싶다면 안쪽을, SDK를 프로젝트마다
   따로 설치/관리하게 하고 싶다면 바깥쪽을 고릅니다.
4. 그 안에 `IAdProvider`/`IFullScreenAdapter`/`IBannerAdapter`를 구현하는
   `AdMobProvider`/`AdMobFullScreenAdapter`/`AdMobBannerAdapter`(예시)를 작성한다.
   구현 시 `docs/superpowers/specs/2026-08-20-adservice-design.md`의 **"3사 매핑표"**(6절)를
   대조하며 작성하고, 실제 SDK의 필드명이 표와 어긋나면 표를 갱신한다.
5. **SDK 콜백이 메인 스레드에서 오는지 직접 확인하고, 결과에 맞게 행동한다.**
   `IFullScreenAdapter`/`IBannerAdapter`는 이벤트가 메인 스레드에서 발화된다고 전제하는
   계약입니다(`IFullScreenAdapter.cs`/`IBannerAdapter.cs` 인터페이스 주석 참고) — `AdService`도
   `Ads/` 정책 계층도 이걸 대신 해주지 않습니다. **"SDK가 알아서 마샬링해줄 것"이라고 가정하지
   마세요 — SDK마다 답이 다르고, 한 SDK 안에서도 이벤트마다 다를 수 있습니다.** 벤더 SDK의
   실제 소스(디컴파일이라도)를 읽고 어느 쪽인지 확인한 뒤, 다음 둘 중 하나를 하세요:
   - **SDK가 마샬링하지 않으면**: 콜백 핸들러 안에서 이벤트를 직접 발화시키지 말고
     `_dispatcher.Post(() => Loaded?.Invoke())`처럼 `IAdDispatcher.Post`로 감싸 메인 스레드
     큐에 넣은 뒤 발화시킵니다.
     Dummy provider가 이 단계를 생략하는 이유는 `DummyAdCanvas`/`DummyAdTicker`가 처음부터
     Unity 메인 스레드(`MonoBehaviour.Update`)에서만 콜백을 만들어내기 때문입니다 — 실제 SDK
     어댑터는 이 전제가 없습니다.
   - **SDK가 이미 마샬링한다면**: 다시 감싸지 마세요 — 이미 메인 스레드인 콜백에 프레임
     하나만큼의 지연을 매번 얹는 것뿐입니다. 단, "이 SDK가 마샬링한다"는 결론을 **모든
     이벤트에** 일괄 적용하지 말고, 이벤트마다 개별적으로 확인하세요.

   **AppLovin MAX의 실제 사정** (구현하며 확인함, `Providers/AppLovin/AppLovinAdProvider.cs`
   참고): MAX Unity 플러그인은 대부분의 콜백을 `MaxEventExecutor`
   (`Assets/MaxSdk/Scripts/MaxEventExecutor.cs`)로 메인 스레드 큐잉하지만 전부는 아닙니다.
   `MaxSdkCallbacks`의 `InvokeEvent` 헬퍼들은 네이티브가 실어 보낸 `keepInBackground` 플래그가
   참이면 `MaxEventExecutor`를 건너뛰고 콜백 스레드에서 그 자리에 곧바로 이벤트를 발화시킵니다
   (`MaxSdkCallbacks.cs:965~1053`). iOS 플러그인은 **전면/보상의 수익 콜백**
   (`OnAdRevenuePaidEvent`)에서 이 플래그를 참으로 채웁니다
   (`Assets/MaxSdk/AppLovin/Plugins/iOS/MAUnityAdManager.m:1005`,
   `args[@"keepInBackground"] = @([adFormat isFullscreenAd]);`) — 배너 수익은 영향받지
   않습니다. (같은 패턴이 크리에이티브 ID 생성 이벤트(:1077)와 CMP 에러 경로(:2022, 항상 참)에도
   쓰입니다 — 이 어댑터가 실제로 구독해서 영향받는 건 수익 콜백뿐이라는 뜻이지, 벡터가
   수익 콜백 하나뿐이라는 뜻은 아닙니다.) 즉 **같은 SDK, 같은 이벤트 이름
   (`OnAdRevenuePaidEvent`)인데도 포맷에 따라 스레드가 다릅니다.**
   `MaxSdkBase.InvokeEventsOnUnityMainThread`(공개 `bool?` 세터)를 `true`로 설정하면 이
   우회를 막고 모든 콜백이 `MaxEventExecutor`를 거치게 강제할 수 있습니다 — AppLovin
   어댑터는 `InitializeAsync`의 모든 진입 경로(이미 초기화된 경우 포함) 맨 앞에서 이걸
   세팅해, `_dispatcher.Post` 없이도 안전하게 감쌉니다.

   **이 세팅은 공짜가 아닙니다.** `MAUnityAdManager.m`은 전면 광고가 뜰 때(`didDisplayAd`,
   `isFullscreenAd` 가드) `UnityPause(YES)`를 부르고(:780) 닫힐 때(`didHideAd`)
   `UnityPause(NO)`로 되돌립니다(:853). Unity가 멈춰 있는 동안 `MaxEventExecutor.Update()`도
   돌지 않으므로, `InvokeEventsOnUnityMainThread = true`로 큐에 강제로 밀어 넣은 전면/보상
   수익 이벤트는 **광고가 떠 있는 동안엔 도착하지 않고 광고가 닫혀 Unity가 재개된 뒤에야**
   드레인됩니다 — 그 사이 프로세스가 죽으면 그 임프레션은 재전송 없이 사라집니다. 벤더가
   애초에 `keepInBackground`를 켠 이유가 정확히 이걸 막기 위해서였습니다: "Forward the event
   in background for fullscreen ads so that the user gets the callback even while the ad is
   playing."(`MAUnityAdManager.m:1079`). AppLovin 어댑터는 그래도 이 트레이드를 택했습니다 —
   백그라운드 스레드로 오는 콜백은 이 seam의 계약(메인 스레드 발화)과 그 위 정책
   계층·분석 구독자의 메인 스레드 전제를 둘 다 어기고, 그 실패 모드(경합, 크래시, 조용히
   삼켜지는 예외)가 매 임프레션마다 발생합니다. 광고 표시 도중 프로세스가 죽는 드문
   경우에 임프레션 하나를 잃는 편이 낫다고 판단한 것입니다 — 하지만 이건 무조건 맞는
   답이 아니라 **이 서비스의 우선순위(정확성·안전성 > 임프레션 손실 최소화)에서 나온
   선택**이니, 다른 SDK/다른 우선순위의 어댑터를 붙일 때는 다시 판단하세요.

   **`InvokeEventsOnUnityMainThread`는 프로세스 전역 정적 상태입니다.** 같은 프로세스 안에서
   MAX를 직접(이 어댑터를 거치지 않고) 쓰는 다른 코드가 있다면 그 코드의 콜백 스레드
   동작도 조용히 이 설정을 물려받습니다 — provider 하나만을 위한 설정이라고 생각해도
   실제로는 앱 전체에 적용됩니다.

   **이 문제는 Unity 에디터에서 재현되지 않습니다** — `MaxSdkUnityEditor`는 메인 스레드
   코루틴으로만 콜백을 만들어내므로, 에디터에서 통과하는 코드가 디바이스에서 백그라운드
   스레드 예외로 죽을 수 있습니다. Android는 이 리포지토리에 JVM 툴체인이 없어 `.aar`
   역디컴파일로만 확인했지만 `keepInBackground`/`isMainThread` 리터럴과
   `BackgroundCallbackProxy` 전달 경로가 나와 iOS와 동일한 것으로 간주했습니다.
6. `AdProviderFactory.Build`는 옵셔널 어셈블리를 직접 `new`할 수 없다(참조 방향이 반대라
   순환 참조가 된다). 대신 `Providers/AdProviderRegistry.cs`(`FoundationDI` 어셈블리 소속)에
   creator를 등록한다. 옵셔널 어셈블리 안에 `[RuntimeInitializeOnLoadMethod]` 정적 메서드를
   하나 두고 거기서 호출한다:
   ```csharp
   [RuntimeInitializeOnLoadMethod]
   private static void Register()
   {
       AdProviderRegistry.Register(AdProviderType.AdMob,
           context => new AdMobProvider(context.Dispatcher));
   }
   ```
   `AdProviderCreationContext`는 provider 생성에 필요한 것(현재는 `Dispatcher`뿐)을 담는
   readonly struct다 — 나중에 의존성이 늘어나도 이 델리게이트 시그니처는 그대로다.
   `Register`는 같은 타입으로 다시 호출하면 이전 creator를 예외 없이 교체한다(도메인
   리로드·에디터 재실행이 이 경로를 여러 번 태운다). `AdProviderRegistry`는 심볼 유무와
   무관하게 항상 `FoundationDI`에 존재한다 — "심볼이 정의됐는데 아무도 등록하지 않은" 상태는
   `AdProviderFactory.Build`가 에러 로그와 함께 Dummy로 대체해 조용히 새지 않게 한다.
7. 필요하면 `Consent/`에 provider별 동의 구현(`UmpAdConsent` 등)을 추가해 `IAdProvider.Consent`로 노출한다.
8. **`IFullScreenAdapter.Load()`는 SDK 호출로 바로 이어져도 된다.** `FullScreenAdUnit`이
   이미 로드 진행 중 상태를 추적해 중복 호출을 걸러내므로, 어댑터 스스로 진행 중 로드를
   기억해 뒀다가 걸러내는 자체 로직을 둘 필요가 없다(예: `if (_isLoading) return;` 같은
   가드는 정책 계층의 일이지 어댑터의 일이 아니다).

`FullScreenAdUnit`을 수정해야만 어댑터가 붙는다면 seam 설계가 잘못됐다는 신호입니다 — 멈추고
재검토합니다.

**Unity 에디터 한계(AppLovin 어댑터로 확인됨)**: `MaxSdkUnityEditor.GetBannerLayout`은
디바이스 레이아웃 없이 항상 `Rect.zero`를 돌려줍니다. `AppLovinBannerAdapter`는 배너 높이를
이 API로 읽으므로, **에디터에서는 `IBannerAdapter.Height`/`HeightChanged`가 절대 0이 아닌
값을 보고하지 않습니다.** 실기(스모크 테스트 포함)에서만 실제 높이를 검증할 수 있습니다 —
에디터에서 배너가 뜨는데 높이가 계속 0이어도 그 자체는 결함이 아닙니다. SDK별 어댑터를
새로 붙일 때도 "에디터 스텁이 실제 값을 주는가"를 먼저 확인하세요.

---

## 6. 구조

```
AdService/
├── IAdService.cs                 공개 API
├── AdService.cs                  조립 + 초기화 재진입 가드 + 임프레션 합류
├── AdTypes.cs                    AdFormat/AdShowOutcome/AdShowResult/AdImpression/AdRetryPolicy 등 값 타입
├── AdUnitId.cs                   Android/iOS 광고 단위 ID 쌍(인스펙터 직렬화)
├── AdServiceRegistration.cs      builder.RegisterAdService(settings) 확장 메서드
├── Ads/
│   ├── FullScreenAdUnit.cs       전면·보상 정책 계층 — 재시도, 보상 래치, 자동 재로드
│   ├── BannerAdUnit.cs           배너 정책 계층 — 재시도 없음(SDK가 자체 갱신)
│   ├── IFullScreenAd.cs          IInterstitialAd / IRewardedAd
│   └── IBannerAd.cs
├── Providers/
│   ├── IAdProvider.cs            SDK seam. BannerOptions/AdProviderContext도 여기 있다
│   ├── IFullScreenAdapter.cs / IBannerAdapter.cs   광고 단위 하나를 나타내는 어댑터 seam
│   ├── IAdProviderFactory.cs / AdProviderFactory.cs  심볼 기반 provider 선택 + Dummy 폴백
│   ├── AdProviderRegistry.cs     3사 어댑터(옵셔널 어셈블리)가 자신을 등록하는 진입점
│   └── Dummy/                    SDK 없이 흐름을 검증하는 provider 구현
├── Dispatch/
│   ├── IAdDispatcher.cs          메인스레드 마샬링 + 지연 + 프레임 대기 seam
│   ├── UnityAdDispatcher.cs      실제 구현. `HideAndDontSave` GameObject를 코드로 만들어 펌프한다(프리팹 아님)
│   └── AdServiceRunner.cs        Update에서 큐를 퍼내는 MonoBehaviour(직접 배치하지 않는다)
├── Consent/                      IAdConsent seam + NoopAdConsent(Dummy용)
├── Storage/                      IAdRemovalStorage seam + PlayerPrefsAdRemovalStorage
└── Settings/
    ├── AdServiceSettings.cs      ScriptableObject. 인스펙터에서 편집하고 ToOptions()로 값 타입 변환
    └── AdProviderType.cs
```

- **`BannerOptions`는 `Settings/`가 아니라 `Providers/IAdProvider.cs`에 있습니다.** provider가
  배너를 만들 때(`CreateBanner(adUnitId, options)`) 필요로 하는 값이라 provider seam과 같은
  파일에 둔 것이고, `AdServiceSettings.ToOptions()`가 인스펙터 값으로 조립해 넘깁니다.
- `AdService`는 `AdServiceSettings`(ScriptableObject)를 직접 참조하지 않고 `AdServiceOptions`
  (`readonly struct`)를 받습니다. EditMode 테스트가 SO 없이도 서비스를 조립할 수 있게 하기 위함입니다.
- `UnityAdDispatcher`는 `[AdService] Runner`라는 이름의 `GameObject`를 `HideFlags.HideAndDontSave`로
  만들어 그 위에 `AdServiceRunner`를 붙입니다. 하이어라키에 보이지 않는 것이 정상 동작입니다.

---

## 7. IL2CPP 빌드에서의 어댑터 보존

**어댑터 어셈블리(`FoundationDI.AppLovin`, `FoundationDI.LevelPlay`)는 IL2CPP 빌드에서 보존되어야 한다.**

코어(`FoundationDI`)는 어댑터를 참조하지 않는다 — 참조하면 순환이 된다. 대신 어댑터가
`[RuntimeInitializeOnLoadMethod]`로 스스로를 `AdProviderRegistry`에 등록하고 코어는 조회만 한다.
그 결과 어댑터는 참조 그래프상 어디에서도 닿지 않는 섬이 되고, IL2CPP 링커는 닿지 않는
어셈블리를 통째로 걷어낸다. 등록이 일어나지 않으면 조회가 비어 서비스가 조용히 Dummy provider로 떨어진다. `AdProviderFactory.IsAvailable()`은
스크립팅 심볼만 보므로 참을 돌려주고, 그래서 "쓸 수 있다고 판단했는데 등록이 없다"는 모양이 된다.

**에디터에서는 링커가 돌지 않아 재현되지 않는다 — 빌드해 봐야만 드러난다.** 문서가 없으면
매번 같은 시간을 쓰게 되는 종류의 실패다.

### 7.1 패키지가 스스로 막는다 (소비 프로젝트가 할 일 없음)

- `FoundationDILinkXmlGenerator`(`Editor/Linker/`)가 `IUnityLinkerProcessor`로 빌드마다
  link.xml을 생성해 링커에 넘긴다. 어댑터 어셈블리와 **그 뒤의 SDK 어셈블리**(`MaxSdk.Scripts`, `Unity.LevelPlay`)를
  함께 보존한다.
- 각 어댑터 폴더의 `AssemblyInfo.cs`에 `[assembly: AlwaysLinkAssembly]`가 붙어 있다.
  생성 link.xml이 닿지 않는 빌드 경로에서도 어댑터 자신은 살아남게 하는 2차 방어선이다.

> 어댑터만 보존해서는 부족하다. 링커는 어댑터가 실제로 건드리는 멤버만 남기는데,
> MAX와 LevelPlay는 네이티브가 `UnitySendMessage`로 이름을 찍어 관리 코드를 되부르기 때문에 링커가 그
> 사용을 볼 수 없다. 실제로 이번 사고에서 `MaxSdk`가 통째로 사라졌다.


> **link.xml 파일을 패키지에 그냥 넣어 두는 방법은 통하지 않는다.** 에디터가 사용자 link.xml을
> 수집하는 곳은 `UnityEditorInternal.AssemblyStripper.GetUserBlacklistFiles` 하나뿐이고, 그
> 구현은 `Directory.GetFiles("Assets", "link.xml", SearchOption.AllDirectories)`다. 즉 `Assets/`
> 아래만 본다. UPM(git URL)으로 설치하면 패키지는 `Library/PackageCache/` 아래에 놓이므로
> 거기 넣어 둔 link.xml은 영원히 읽히지 않는다.

### 7.2 빌드에서 확인하는 방법

빌드 산출물의 global-metadata에 타입 이름이 남아 있는지 본다.

```bash
# Android APK
unzip -p app.apk assets/bin/Data/Managed/Metadata/global-metadata.dat \
  | strings | grep -E 'AppLovinInstaller|AppLovinAdProvider|MaxSdk|LevelPlayInstaller'
```

하나도 안 나오면 어셈블리가 통째로 스트리핑된 것이다. 런타임 증상은 이 에러 로그다.

```
[AdService] AppLovin provider가 요청됐지만 등록된 creator가 없다. FOUNDATIONDI_APPLOVIN 심볼이
없어 어댑터가 컴파일되지 않았거나, IL2CPP 빌드에서 FoundationDI.AppLovin 어셈블리가 통째로
스트리핑된 것이다(에디터에서는 재현되지 않는다). Dummy provider로 대체한다.
```

### 7.3 어댑터를 추가할 때

`SdkDefineTable.Entries`(`Editor/SdkDefines/`)에 한 줄 넣는 것이 전부다 — 심볼, 판정용
어셈블리, 어댑터 어셈블리, 보존할 SDK 어셈블리가 한 곳에 있다. 빠뜨리면
`FoundationDILinkXmlTest`가 asmdef와 대조해 EditMode에서 잡는다(스트리핑 자체는 EditMode에서
재현할 수 없지만, 표 누락은 잡을 수 있다).

## 8. 알려진 범위 밖

- **AdMob 어댑터** — seam과 매핑표만 준비돼 있고, SDK 설치 후 별도 계획으로 진행합니다.
- **AppLovin MAX·LevelPlay 어댑터의 컴파일·실기 검증** — 두 어댑터는 구현돼 있지만 **두 SDK
  모두 이 리포지토리에 설치돼 있지 않습니다.** 어댑터 asmdef가 `defineConstraints`로 컴파일
  대상에서 빠져 있어, SDK를 설치하기 전까지는 이 코드가 한 번도 컴파일되지 않습니다 — SDK
  API 표면(타입명·시그니처)과 실기 동작은 아직 검증되지 않았습니다. 단위 테스트도 같은 이유로
  없으며, SDK 설치 시점에 같은 `defineConstraints`를 건 테스트 어셈블리를 함께 추가합니다.
- **IAP(인앱 구매)** — `AdsRemoved`는 세터만 제공합니다. 구매 검증·복원·상점 연동은 이 서비스의
  책임이 아닙니다.
- **AppOpen / MREC / Native 광고 포맷** — `AdFormat`은 `Banner`/`Interstitial`/`Rewarded` 셋뿐입니다.
- **리모트 컨피그 연동**(광고 단위 ID·재시도 정책을 서버에서 갱신) — `AdServiceSettings`는
  에디터에서 편집하는 정적 값입니다.
- **`AdService`의 스레드 안전성** — `AdService`/`FullScreenAdUnit`/`BannerAdUnit`은 어댑터가
  올려주는 이벤트가 이미 메인 스레드에서 온다고 가정합니다. **서비스 자신은 어떤 콜백도
  메인 스레드로 마샬링하지 않습니다** — `IAdDispatcher.Post`는 어댑터가 쓰라고 있는
  도구일 뿐, `Ads/`나 `AdService.cs` 어디에서도 호출하지 않습니다(실제로 `Post`를 호출하는
  곳은 테스트뿐입니다). 세 SDK 모두 네이티브 스레드에서 콜백을 올릴 수 있으므로, **메인
  스레드로 마샬링하는 책임은 3사 어댑터 구현체에 있습니다** — 자세한 내용은 5절을 참고하세요.
  `AdService` 자신의 상태(`_initializing`, `_adsRemoved` 등) 역시 메인 스레드 단독 접근을
  전제합니다.
