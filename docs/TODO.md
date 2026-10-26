# TODO — 기능 추가 요청

다른 프로젝트에서 FoundationDI를 쓰다가 필요해진 기능을 적는다. 구현하면 항목에 완료 버전을 적고, 요청한 프로젝트에 알린다.

| # | 기능 | 대상 | 요청 프로젝트 | 상태 |
|---|---|---|---|---|
| 1 | `IPoolManager.Prewarm` | PoolManager | MahjongTile (2026-10-11) | 완료 (0.13.0) |
| 2 | `RegisterResourceService` 등록 헬퍼 | ResourceService | MahjongTile (2026-10-11) | 완료 (0.13.0) |
| 3 | 배너 높이를 실제로 그려진 배너 뷰 기준으로(적응형 컨테이너 높이·dp 환산) | AdService (LevelPlay·Dummy) | MahjongTile (2026-10-14) | 완료 (0.13.0, Android 실기 미검증) |
| 4 | 플레이 모드를 끝내도 Dummy 광고 캔버스가 남지 않게 | AdService (Dummy) | CannonPuzzle (2026-10-23) | 완료 (0.13.0) |
| 5 | 영수증 검증기가 Unity IAP 5 의 `GooglePlayTangle`(네임스페이스 안)을 찾게 | IAPService (UnityIAP) | CannonPuzzle (2026-10-26) | 완료 (0.13.0) |

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
