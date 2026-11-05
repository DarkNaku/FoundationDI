# TODO — 기능 추가 요청

다른 프로젝트에서 FoundationDI를 쓰다가 필요해진 기능을 적는다. 구현하면 항목에 완료 버전을 적고, 요청한 프로젝트에 알린다.

| # | 기능 | 대상 | 요청 프로젝트 | 상태 |
|---|---|---|---|---|
| 1 | `IPoolManager.Prewarm` | PoolManager | MahjongTile (2026-10-11) | 완료 (0.13.0) |
| 2 | `RegisterResourceService` 등록 헬퍼 | ResourceService | MahjongTile (2026-10-11) | 완료 (0.13.0) |
| 3 | 배너 높이를 실제로 그려진 배너 뷰 기준으로(적응형 컨테이너 높이·dp 환산) | AdService (LevelPlay·Dummy) | MahjongTile (2026-10-14) | 완료 (0.13.0, Android 실기 미검증) |
| 4 | 플레이 모드를 끝내도 Dummy 광고 캔버스가 남지 않게 | AdService (Dummy) | CannonPuzzle (2026-10-23) | 완료 (0.13.0) |
| 5 | 영수증 검증기가 Unity IAP 5 의 `GooglePlayTangle`(네임스페이스 안)을 찾게 | IAPService (UnityIAP) | CannonPuzzle (2026-10-26) | 완료 (0.13.0) |
| 6 | 지급 처리기가 준비될 때까지 미확정 구매를 보류 | IAPService | PaperHero (2026-11-05) | 요청 |
| 7 | 실행 중에 미확정 구매를 다시 처리하는 API | IAPService | PaperHero (2026-11-05) | 요청 |
| 8 | 상품 조회 일부 실패 허용, 연결·조회·구매 시간 제한 | IAPService | PaperHero (2026-11-05) | 요청 |
| 9 | 재전달된 구매가 진행 중인 같은 상품 구매를 대신 끝내지 않게 | IAPService | PaperHero (2026-11-05) | 요청 |
| 10 | 비동기 영수증 검증(서버 검증) 훅 | IAPService | PaperHero (2026-11-05) | 요청 (서버를 붙일 때) |
| 11 | 영수증 검증 실패 구매 처리 방식 선택 | IAPService | PaperHero (2026-11-05) | 요청 |
| 12 | 상품 목록을 코드(게임 스펙 표)에서 넘기기 | IAPService | PaperHero (2026-11-05) | 요청 (선택) |

---

## 1. `IPoolManager.Prewarm(string key, int count)`

### 왜 필요한가
- 타일 매칭 게임은 레벨을 열 때 타일 수백 개를 한꺼번에 꺼낸다. 첫 `Get`마다 `Instantiate`와 DI 주입(`InjectGameObject`)이 일어나 첫 프레임이 끊긴다.
- 지금은 프로젝트 쪽에서 `Get` N번 → `Release` N번으로 흉내 낸다. 이 방식은 꺼낼 때마다 `OnGetItem`/`OnReleaseItem` 콜백(활성화·비활성화)이 불필요하게 돌고, 부모 변경도 일어난다.
- 로딩 화면처럼 끊김이 안 보이는 시점에 풀을 데워 두는 공식 경로가 필요하다(Unity 가이드의 "풀 초기화는 로딩 시점에" 권장과 같은 취지).

### API
```csharp
public interface IPoolManager : IDisposable
{
    // 기존 멤버 생략

    /// key 의 풀에 비활성 인스턴스가 최소 count 개 대기하도록 미리 만든다.
    /// 이미 대기 중인 인스턴스가 count 이상이면 아무것도 하지 않는다.
    void Prewarm(string key, int count);
}
```

### 동작
1. `_disposed`이면 아무것도 하지 않는다. `key`가 비었거나 `count <= 0`이면 에러 로그(키 오류) 또는 무시(0 이하).
2. 풀이 없으면 `Get`과 같은 경로(`Load(key)`)로 프리팹을 로드하고 풀을 만든다. 로드 실패 시 에러 로그 후 반환.
3. 현재 대기 수(`ObjectPool.CountInactive`)가 `count`보다 적으면 모자란 만큼 **생성 콜백만** 실행해 인스턴스를 만든다. 생성 콜백은 기존과 같다(`Instantiate` → `IPoolItem` 확보 → `InjectGameObject` → `OnCreateItem`).
4. 만든 인스턴스는 바로 대기 상태로 넣는다. `OnGetItem`은 부르지 않는다. 비활성 상태와 풀 루트 아래 위치는 `OnReleaseItem`과 같게 맞춘다(`SetActive(false)`, 부모 = 풀 루트).
5. `PoolData._items`(꺼낸 목록)에는 넣지 않는다. 대기 인스턴스는 `ObjectPool` 내부 스택에만 있다.

### 구현 메모
- `UnityEngine.Pool.ObjectPool<T>`에는 "생성만 하고 대기열에 넣는" API가 없다. 방법은 둘이다.
  - (a) `PoolData`에 `Prewarm(count)`를 두고, `_pool.Get()`으로 count개를 꺼낸 뒤 `_pool.Release()`로 돌려놓는다. 이때 `OnGetItem`/`OnReleaseItem`이 한 번씩 불리므로, 콜백에서 상태를 바꾸는 `PoolItem` 파생 클래스가 영향을 받지 않는지 확인한다.
  - (b) `ObjectPool` 대신 생성 함수를 직접 호출해 `Release`만 하는 경로를 만든다. `ObjectPool.Release`는 `collectionCheck`가 켜져 있으면 "꺼내지 않은 항목" 검사를 하지 않으므로 가능하다. 다만 `actionOnRelease`가 불린다.
  - 어느 쪽이든 결과적으로 `OnReleaseItem`은 불린다. README에 "Prewarm으로 만든 인스턴스는 `OnCreateItem` → `OnReleaseItem` 순서로 콜백을 받는다"고 적는다.
- `maxSize`(현재 ObjectPool 기본값 10000)를 넘는 count는 maxSize까지만 만든다.

### 테스트 (EditMode, 기존 `PoolManagerTest` 방식으로 `IResourceService`를 NSubstitute로 대체)
- 처음 보는 키에 `Prewarm(key, 5)` → 프리팹 로드 1회, 인스턴스 5개 생성, 모두 비활성, 풀 루트 아래.
- 이어서 `Get(key)` 5번 → 새 `Instantiate` 없이 대기 인스턴스를 꺼낸다.
- `Prewarm(key, 3)`을 대기 5개 상태에서 호출 → 아무것도 만들지 않는다.
- `count <= 0` → 아무 일도 없다. 빈 키 → 에러 로그.
- `Dispose` 뒤 `Prewarm` → 아무 일도 없다.
- Prewarm으로 만든 인스턴스에도 DI 주입이 1회 일어난다.

### README 갱신
- API 표에 `Prewarm` 행 추가, "사용법"에 로딩 시점 예시 추가.

---

## 2. `RegisterResourceService(this ContainerBuilder builder, ResourceBackend backend = Resources)`

### 왜 필요한가
- 다른 서비스(`RegisterPoolManager`, `RegisterUINavigator`, `RegisterMessageService` 등)는 등록 확장 메서드가 있는데, ResourceService만 README처럼 `RegisterType`을 직접 써야 한다.
- PoolManager와 UINavigator는 `IResourceService`를 전제로 하므로, 이를 쓰는 모든 프로젝트가 같은 등록 코드를 복사한다.

### API
```csharp
public enum ResourceBackend
{
    Resources,
    Addressables
}

public static ContainerBuilder RegisterResourceService(this ContainerBuilder builder,
                                                       ResourceBackend backend = ResourceBackend.Resources);
```

### 동작
- `backend`에 따라 `ResourcesProvider` 또는 `AddressablesProvider`를 `IResourceProvider`로, `ResourceService`를 `IResourceService`로 등록한다. 둘 다 Singleton, Lazy.
- `AddressablesProvider`는 조건부 컴파일 없이 Addressables를 직접 참조하므로(현재 FoundationDI가 Addressables에 의존), 두 백엔드 모두 별도 심볼 없이 등록할 수 있다.

### 테스트
- `Resources` 등록 후 `IResourceService`·`IResourceProvider` 해석 결과가 각각 `ResourceService`·`ResourcesProvider`인지.
- 같은 컨테이너에서 두 번 해석하면 같은 인스턴스인지(Singleton).

### README 갱신
- ResourceService README "DI 등록" 예시를 확장 메서드로 바꾸고, PoolManager·UINavigator README의 "전제" 문구에 이 메서드를 안내한다.

---

## 3. 배너 높이를 실제로 그려진 배너 뷰 기준으로

### 왜 필요한가
- **(가장 큰 문제) 적응형 배너는 컨테이너 높이를 알려 준다.** `UseAdaptive` 로 요청하면 `onAdLoaded` 의 `adSize` 가
  `{"description":"CUSTOM","width":384,"height":120}` 처럼 적응형 컨테이너(최대 높이)로 온다. 실제로 붙은 소재는 50dp
  표준 배너(Galaxy A13 에서 900x141px)라 게임 UI 가 약 200px 위에 뜬다. MahjongTile 은 그동안 표준 배너(320x50)로 바꿔 쓴다.
- 게임은 `IBannerAdapter.Height`(화면 픽셀)로 배너 바로 위에 UI 를 붙인다. 값이 실제보다 작으면 UI 가 배너에 겹치거나 간격이 줄어든다.
- `LevelPlayAdMapper.DpToPixels` 는 `Screen.dpi / 160` 을 곱한다. Android 의 `Screen.dpi` 는 물리 dpi(xdpi)라
  레이아웃이 쓰는 화면 밀도(`DisplayMetrics.density`)와 다르다.
  - 실측(Galaxy A13 SM-A135N): density 450(2.8125), xdpi 403.4. 50dp 배너가 실제 141px 인데 126px 로 보고된다(약 15px 작다).
- Dummy 배너(`DummyBannerAdapter`)는 `BannerHeight` 값을 그대로 Height 로 보내지만 Dummy 캔버스가 스케일해 그려
  에디터에서도 그린 높이(예: 135px)와 보고값(100)이 다르다.

### 바꿀 것
- 높이는 요청 크기(adSize)가 아니라 로드된 배너 뷰의 실제 높이(Android `View.getHeight()`, iOS 뷰 frame)로 알린다.
  적응형도 그대로 쓸 수 있게 된다.
- Android: `DisplayMetrics.density`(예: `UnityPlayer.currentActivity.getResources().getDisplayMetrics().density`)를 한 번 읽어 dp × density 로 환산한다. 읽기 실패 시 지금 식으로 떨어진다.
- iOS: 지금처럼 근사(point × nativeScale 이 노출되지 않음)를 유지하거나 `UIScreen.mainScreen.scale` 플러그인을 쓴다.
- Dummy: 그린 배너 RectTransform 의 실제 화면 높이(월드 코너)를 Height 로 보낸다.

---

## 4. 플레이 모드를 끝내도 Dummy 광고 캔버스가 남지 않게

### 왜 필요한가
- `DummyAdCanvas.EnsureRoot` 가 루트를 `HideFlags.HideAndDontSave` 로 만든다. DontSave 계열 플래그가 붙은 오브젝트는
  플레이 모드를 끝내도 Unity 가 지우지 않는다(`DontDestroyOnLoad` 오브젝트만 지워진다).
- 전면광고가 떠 있는 채로 플레이 모드를 멈추면 `[AdService] Dummy Canvas` 가 에디트 모드에 그대로 남아 Game 뷰에
  "Interstitial (Dummy Ad)" 와 멈춘 카운트다운이 계속 보인다(카운트다운은 플레이 모드에서만 돈다).
  계층 창에도 안 보여 지울 방법을 찾기 어렵다. CannonPuzzle 에서 확인(2026-10-23).
- 다음 플레이 때 새 캔버스가 또 만들어져 겹칠 수 있다.

### 바꿀 것
- 플레이 모드에서는 `HideFlags.HideInHierarchy`(또는 플래그 없음)로 만들고 `DontDestroyOnLoad` 로 둔다 — 플레이 모드가
  끝나면 함께 지워진다. 에디트 모드(에디터 스크립트·EditMode 테스트)에서만 `HideAndDontSave` 를 쓴다.
- 안전망으로 에디터에서 `EditorApplication.playModeStateChanged`(ExitingPlayMode)에 `Dispose()` 를 부르는 것도 검토한다.

### 테스트
- PlayMode: 전면광고를 띄운 채 플레이 모드를 끝낸 뒤 `Resources.FindObjectsOfTypeAll<Canvas>()` 에
  `[AdService] Dummy Canvas` 가 없어야 한다(또는 플레이 모드에서 만든 루트의 hideFlags 에 DontSave 가 없음을 확인).
- 기존 EditMode 테스트(`DummyAdCanvas` 콜백 북키핑)는 에디트 모드 경로라 그대로 통과해야 한다.

---

## 5. 영수증 검증기가 Unity IAP 5 의 `GooglePlayTangle` 을 찾게

### 왜 필요한가
- `CrossPlatformReceiptValidator.LoadGooglePlayTangle()` 이 `assembly.GetType("GooglePlayTangle", false)` 로 **전역 이름만** 찾는다.
- Unity IAP 5(5.4.4 확인)의 Receipt Validation Obfuscator 는 `UnityEngine.Purchasing.Security.GooglePlayTangle` 을
  `Assets/Scripts/UnityPurchasing/generated/GooglePlayTangle.cs` 에 만든다(`TangleFileConsts.k_OutputPath`). 네임스페이스 안이라
  찾지 못하고, Obfuscator 를 돌렸는데도 "GooglePlayTangle이 없어 영수증을 검증하지 않는다" 경고 뒤 **검증 없이 통과**한다.
  주석·README 의 생성 경로(`Assets/Plugins/UnityPurchasing/generated/`)도 실제와 다르다.
- 생성 클래스는 리플렉션으로만 닿으므로 IL2CPP 스트리핑 대상이다. 지금 link.xml 생성기(`FoundationDILinkXmlGenerator`)는
  Unity.Purchasing 어셈블리만 지키고 Assembly-CSharp 의 Tangle 은 지키지 않는다.
- CannonPuzzle 은 임시로 전역 `GooglePlayTangle`(생성 클래스로 넘겨주는 얇은 클래스)과 두 타입을 지키는 link.xml 을
  `Assets/Scripts/UnityPurchasing/` 에 두었다(2026-10-26, 1.0.0 (3) 빌드 메타데이터에서 둘 다 남은 것 확인). 패키지가 고쳐지면 지운다.

### 바꿀 것
- `LoadGooglePlayTangle` 이 `UnityEngine.Purchasing.Security.GooglePlayTangle` 을 먼저 찾고, 없으면 전역 `GooglePlayTangle` 을 찾는다
  (예전 IAP 버전 호환).
- 링커 보존: link.xml 생성기에 Assembly-CSharp 의 두 타입(`UnityEngine.Purchasing.Security.GooglePlayTangle`, `GooglePlayTangle`)을
  `preserve="all"` 로 넣는다(없는 타입은 링커가 무시하는지 확인, 아니면 생성 파일이 있을 때만 넣는다).
- 주석·README §6 의 생성 경로를 `Assets/Scripts/UnityPurchasing/generated/` 로 고친다.
- 참고: 에디터에서는 Unity IAP 가 `Unity.Purchasing.SecurityStub`(Editor 포함 플랫폼)을 써서 `Obfuscator.DeObfuscate` 가
  `NotImplementedException` 을 던진다 — 에디터에서 `Data()` 를 불러 확인할 수 없다(검증기는 Android 실기에서만 Tangle 을 읽으므로 문제는 아니다).

### 테스트
- EditMode: 네임스페이스 안의 가짜 `UnityEngine.Purchasing.Security.GooglePlayTangle`(테스트 어셈블리)만 있을 때 찾아야 하고,
  전역 이름만 있을 때도 찾아야 한다(찾는 로직을 테스트할 수 있게 타입 해석 부분을 떼어 낸다).
- 빌드 확인(README §9.2 와 같은 방법): `global-metadata.dat` 에 `UnityEngine.Purchasing.Security|GooglePlayTangle` 이 남아 있어야 한다.

---

## 6~12 공통 배경 (PaperHero, 2026-11-05)

- PaperHero 는 TailedDemonSlayer(TDS) 기획을 따르는 방치형 RPG 다. 상품은 TDS 상점 표(shop.csv, 결제 상품 49개, **전부 소모성**: 보석·패키지·시즌패스)를 그대로 쓴다.
- 진행 데이터(`PlayerProgress`, Easy Save 3)는 루트 컨테이너가 아니라 **Play 씬의 핸들러가 불러온다**. IAP 서비스는 루트 싱글턴이라, 루트에서 초기화하면 진행 데이터보다 먼저 미확정 구매가 재전달될 수 있다.
- TDS 흐름(참고): 구매 → 로컬 영수증 검증 → 서버 `payment` 검증 → 응답 200(또는 10005 이미 처리됨)이면 클라이언트 지급·저장 → `ConfirmPendingPurchase`. 서버 실패면 확정하지 않아 다음 실행에 재전달된다.
- 검토 기준 코드: 0.16.0(`50d2dfd`)의 `Runtime/Services/IAPService/IAPService.cs`. PaperHero 는 아직 예전 버전(`1db581f`, `RegisterIAPService(IAPServiceSettings)`)을 쓰고 있어 올릴 때 등록 코드가 바뀐다.
- 이미 갖춰진 것(요청 아님): 검증 → 지급 → 확정 순서, 지급 실패·예외 시 미확정 유지, 앱 재시작 때 `PendingOrders` 재전달(`isRestored`), 같은 트랜잭션 동시 처리 방지(`_inFlight`), 이번 세션 확정 목록(`_confirmedInSession`), 현지화 가격·거래 ID·영수증 노출, 취소·보류 구분. 세션을 넘는 중복 지급 방지(처리한 TransactionId 저장)는 지급 처리기(게임) 몫이라는 현재 설계를 유지한다.

---

## 6. 지급 처리기가 준비될 때까지 미확정 구매를 보류

### 왜 필요한가
- `RegisterIAPService` 는 서비스를 만들 때 `IIAPFulfillment` 를 **한 번** 해석하고, 등록돼 있지 않으면 `AutoConfirmFulfillment`(지급 없이 확정)로 떨어진다(`IAPServiceRegistration.cs`).
- 지급 처리기는 게임 진행 데이터가 있어야 지급할 수 있다. 진행 데이터가 씬에서 늦게 준비되는 게임은
  (a) 지급 처리기를 루트에 등록하지 않으면 재전달된 구매가 **지급 없이 확정**되고(결제만 되고 보석 없음),
  (b) 등록해도 준비 전이면 `false` 를 돌려줄 수밖에 없는데, 그러면 그 구매는 **다음 실행까지** 다시 오지 않는다(7번).
- `InitializeAsync` 를 진행 데이터 준비 뒤로 미루는 것만으로는 부족하다. 초기화 이후 씬 전환(진행 데이터 해제·재로드) 중에도 `OnPurchasePending` 이 올 수 있다.

### 제안 API (택1, a 권장)
```csharp
// (a) 지급 처리기가 스스로 준비 여부를 알린다. 선택 인터페이스라 기존 구현은 그대로 동작한다.
public interface IIAPFulfillmentGate
{
    bool IsReady { get; }
}

public interface IIAPService
{
    // 기존 멤버 생략
    // 보류해 둔 미확정 구매를 지금 처리한다(7번과 같은 진입점).
    Awaitable ProcessUnconfirmedAsync();
}

// (b) 지급 처리기를 나중에 꽂는다. null 이면 보류.
void SetFulfillment(IIAPFulfillment fulfillment);
```

### 동작
1. 주문(`HandlePurchasePending`, `HandlePurchasesFetched` 의 `PendingOrders`)이 오면 지급 처리기가 `IIAPFulfillmentGate` 이고 `IsReady == false` 이면 검증·지급·확정을 하지 않고 **보류 목록**(트랜잭션 ID 기준, 중복 없이)에 넣는다.
2. 사용자가 시작한 구매(`PurchaseAsync`)가 보류되면 결과는 `Deferred` 와 구분되는 값으로 끝낸다(예: `IAPPurchaseResultType.Pending` 추가, 또는 `Failed` + 전용 코드 `FulfillmentNotReadyCode`).
3. `ProcessUnconfirmedAsync()` 가 보류 목록을 순서대로 기존 `ProcessPending` 경로로 흘려보낸다. 아직 준비되지 않았으면 그대로 둔다.
4. `AutoConfirmFulfillment` 폴백은 유지하되, 소모성 상품이 카탈로그에 있는데 지급 처리기가 등록되지 않았으면 초기화 때 **경고 로그**를 남긴다(지급 없이 확정되는 것을 놓치기 쉽다).

### 테스트
- 준비 전 재전달 → 지급·확정 0회, 보류 1건. `IsReady = true` 후 `ProcessUnconfirmedAsync()` → 지급 1회·확정 1회·`Purchased` 1회.
- 같은 트랜잭션이 보류 중에 다시 와도 보류 1건.
- 게이트가 없는 기존 지급 처리기 → 지금과 같은 동작.
- 소모성 상품 + 지급 처리기 미등록 → 경고 로그 1회.

---

## 7. 실행 중에 미확정 구매를 다시 처리하는 API

### 왜 필요한가
- 지급 실패(`FulfillAsync` 가 false·예외)면 확정하지 않고 끝나며, 그 구매는 **다음 실행의 `FetchPurchases`** 에서만 다시 온다. 이번 실행 안에 재시도할 공개 경로가 없다.
- 일시적 실패(서버 검증 지연, 저장 실패, 6번의 준비 전 상태) 뒤 사용자가 앱을 계속 쓰면 결제한 보석을 받을 때까지 재시작해야 한다. Google Play 는 3일 안에 확정하지 않으면 자동 환불한다.

### 제안 API
```csharp
// 스토어에 미확정 구매를 다시 묻고(FetchPurchases), 받은 PendingOrders 를 지급 파이프라인으로 보낸다.
// 6번의 보류 목록도 함께 처리한다. 초기화 전·해제 후에는 아무것도 하지 않는다.
Awaitable ProcessUnconfirmedAsync();
```

### 동작
- 내부적으로 `FetchPurchasesAsync()` 를 다시 부른다(이미 `_inFlight`·`_confirmedInSession` 이 이중 지급을 막는다). 복원(`RestoreAsync`) 진행 중이면 그 조회에 편승하거나 끝날 때까지 기다린다.
- 동시에 여러 번 불려도 조회는 하나만 진행한다(초기화 편승과 같은 방식).
- 게임은 포그라운드 복귀, 상점 열기, 6번의 준비 완료 시점에 부른다. README 에 권장 호출 시점을 적는다.

### 테스트
- 지급 실패 1회 뒤 `ProcessUnconfirmedAsync()` → 같은 주문 재처리, 지급 성공 시 확정 1회.
- 연속 2번 호출 → `FetchPurchases` 1회.
- 초기화 전 호출 → 아무 일도 없음.

---

## 8. 상품 조회 일부 실패 허용, 연결·조회·구매 시간 제한

### 왜 필요한가
- `HandleProductsFetchFailed` 가 오면 `_fetchingProducts` 를 false 로 끝내 **초기화 전체가 실패**한다. 스토어 콘솔에 아직 없는(심사 전·삭제된) 상품 ID 가 하나만 있어도 상점 전체가 열리지 않을 수 있다. TDS 처럼 결제 상품이 49개면 일부 미등록이 흔하다.
  - 확인할 것: Unity IAP 5.4 에서 일부 상품이 없을 때 `OnProductsFetched`(받은 것만)와 `OnProductsFetchFailed` 가 둘 다 오는지, 순서는 어떤지. 실기(Android·iOS)에서 확인한다.
- `ConnectAsync`·`FetchProductsAsync`·`FetchPurchasesAsync`·`PurchaseAsync` 는 이벤트가 오지 않으면 영원히 기다린다. 스토어 앱 문제·네트워크 끊김에서 로딩 화면이 멈춘다(TDS 는 구매 로딩을 10초로 끊었다).

### 바꿀 것
- 조회에 성공한 상품이 하나라도 있으면 초기화는 성공으로 둔다. 실패한 ID 는 `IAPProduct.IsAvailable == false`(또는 `Products` 에서 제외)로 두고 경고 로그에 ID 목록을 남긴다. 받은 것이 없을 때만 실패.
- 시간 제한: `InitializeAsync(float timeoutSeconds = 30f)`(연결·조회 각각), `PurchaseAsync` 는 스토어 UI 가 떠 있는 동안이라 길게 두거나 설정값으로. 시간 초과면 `IAPPurchaseResult.Failed(TimeoutCode)`, 늦게 도착한 주문은 그대로 파이프라인을 탄다(지급·`Purchased` 는 일어난다 — README 에 명시).
- 연결 실패 뒤 다시 `InitializeAsync` 를 부르면 처음부터 재시도되는지 확인하고, 안 되면 상태를 초기화한다.

### 테스트
- 조회 결과 2개 + 실패 1개 → 초기화 성공, `Products` 2개, 실패 ID 경고.
- 연결 이벤트가 오지 않음 → 시간 제한 뒤 false, 다시 호출하면 재시도.
- 구매 이벤트가 오지 않음 → 시간 제한 뒤 `Failed(TimeoutCode)`, 이후 도착한 주문은 정상 지급.

---

## 9. 재전달된 구매가 진행 중인 같은 상품 구매를 대신 끝내지 않게

### 왜 필요한가
- `CompletePendingFor(productId, …)` 는 **상품 ID 만** 비교한다. 사용자가 상품 A 를 구매하는 중에 같은 상품 A 의 예전 미확정 구매가 재전달(`isRestored: true`)되거나, 그 처리 결과(검증 실패·지급 실패)가 나오면 지금 진행 중인 `PurchaseAsync` 가 그 결과로 끝난다. 실제 새 구매는 결과를 기다리는 호출자 없이 나중에 처리된다.

### 바꿀 것
- `isRestored == true` 인 주문의 결과로는 대기 중인 구매를 끝내지 않는다(`FulfillAndConfirmAsync` 에서 `CompletePendingFor` 를 `!isRestored` 일 때만).
- 단, Google 은 같은 소모성 상품이 미확정이면 새 구매를 `ItemAlreadyOwned`(또는 v5 의 대응 실패)로 거절한다. 그 실패가 오면 재전달 처리(7번)를 바로 시도하고 결과를 그 구매에 돌려줄지 정한다 — 최소한 실패 사유를 호출자가 구분할 수 있게 코드로 남긴다.

### 테스트
- 상품 A 구매 대기 중 A 의 재전달 주문 처리 완료 → 대기 중 구매는 계속 대기.
- 상품 A 구매 대기 중 A 의 새 주문 → 그 결과로 완료.

---

## 10. 비동기 영수증 검증(서버 검증) 훅

### 왜 필요한가
- `IReceiptValidator.Validate(purchase, out error)` 가 동기라 서버 검증(TDS `payment` 요청 같은)을 넣을 수 없다. 지급 처리기 안에서 서버를 부르면 검증 실패와 지급 실패가 섞이고, "나중에 다시"(서버 장애)를 표현할 수 없다.

### 제안 API
```csharp
public enum IAPValidationResult { Valid, Invalid, RetryLater }

public interface IAsyncReceiptValidator
{
    Awaitable<(IAPValidationResult Result, string Error)> ValidateAsync(IAPPurchase purchase);
}
```

### 동작
- 등록돼 있으면 로컬 검증(`IReceiptValidator`) 뒤에 부른다. `Valid` → 지급, `Invalid` → 11번 정책, `RetryLater` → 확정하지 않고 끝내며 7번으로 재시도할 수 있게 둔다(결과 코드 구분).
- 기다리는 사이 해제되면 확정하지 않는다(지금 지급 대기와 같은 처리).

---

## 11. 영수증 검증 실패 구매 처리 방식 선택

### 왜 필요한가
- 지금은 검증 실패면 지급도 확정도 하지 않는다. 그 구매는 매 실행 재전달되고 매번 에러 로그가 남는다(위조 영수증이면 영원히). TDS 는 검증 실패를 확정해서 버렸다.

### 바꿀 것
- 등록 옵션으로 고른다: `InvalidReceiptPolicy.KeepPending`(지금 동작, 기본) / `ConfirmWithoutGrant`(확정만 하고 지급하지 않음, 로그 1회). 같은 트랜잭션의 검증 실패 로그는 세션당 1회로 줄인다.

---

## 12. 상품 목록을 코드(게임 스펙 표)에서 넘기기 (선택)

### 왜 필요한가
- 상품은 Unity IAP Catalog(`ProductCatalog.LoadDefaultCatalog()`)에서만 읽는다. 게임이 상품을 스펙 표(PaperHero `shop.csv`, pid 49개)로 관리하면 Catalog 와 표가 따로 놀아, 표에 상품을 더하고 Catalog 를 빠뜨리는 실수가 생긴다.

### 제안 API
```csharp
// 지정하면 IAP Catalog 대신 이 목록을 쓴다.
public static ContainerBuilder RegisterIAPService(this ContainerBuilder builder,
                                                  Func<IEnumerable<IAPProductDefinition>> catalogSource,
                                                  bool verboseLogging = false);
// 또는 선택 등록 인터페이스 IIAPCatalogSource
```
- `IAPProductDefinition`(공용 ID, 타입, 스토어별 ID 선택)을 공개하고 내부에서 `ProductDefinition` 으로 바꾼다. 상수 생성기는 Catalog 를 쓸 때만 동작해도 된다.

