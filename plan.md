# plan.md

## 활성 계획: 없음

다음 작업이 정해지면 여기에 테스트 목록을 채운다.

---

## 완료: IAPService — 모바일 인앱 구매 서비스

게임 코드가 `IIapService` 하나로 Google Play / App Store의 소모성·비소모성 상품을 구매·복원한다.
Unity IAP 5.4.2는 `FOUNDATIONDI_UNITYIAP` 심볼이 걸린 옵셔널 어셈블리에 격리하고, 코어는 Dummy provider로 완전히 동작한다.

세부: `docs/superpowers/specs/2026-08-23-iapservice-design.md`
계획: `docs/superpowers/plans/2026-08-23-iap-service.md`

- [x] IapProductId가 플랫폼 오버라이드를 고르고 비면 공용 ID로 폴백한다
- [x] 구매결과의 IsSuccess가 성공 결과에서만 참이다
- [x] 초기화하면 provider 상품이 노출된다
- [x] 초기화 전 구매는 NotReady다
- [x] InitializeAsync는 재진입해도 provider를 한 번만 초기화한다
- [x] 구매가 검증·지급·확정 순서로 진행된다
- [x] 지급이 실패하면 확정하지 않는다
- [x] 지급이 예외를 던져도 확정하지 않고 서비스가 살아있다
- [x] 영수증 검증에 실패하면 지급도 확정도 하지 않는다
- [x] 비소모성은 확정 후 소유로 기록되고 소모성은 아니다
- [x] 사용자 취소와 그 외 실패를 구분한다
- [x] 이미 소유한 비소모성은 스토어를 거치지 않는다
- [x] 구매가 진행 중이면 두 번째 호출은 즉시 NotReady다
- [x] provider가 구매 시작을 거부하면 Failed다
- [x] 카탈로그에 없는 상품 구매는 NotReady다
- [x] 미확정 구매는 초기화 때 지급되고 확정된다
- [x] 복원은 비소모성 소유를 되살리고 개수를 보고한다
- [x] 복원이 실패하면 Success가 거짓이다
- [x] 보류된 구매는 지급하지 않고 Deferred를 반환한다
- [x] Dispose하면 provider가 해제되고 이후 구매는 NotReady다
- [x] 저장한 소유 상태가 다시 읽힌다
- [x] IapProviderFactory가 강제 더미·미가용 심볼·미등록 creator를 처리한다
- [x] RegisterIapService로 IIapService가 싱글턴 등록된다

구현만 있고 단위 테스트가 없는 항목(스모크로 검증): Unity IAP 어댑터. 상수 생성기는 순수 함수만 단위 테스트.

---

## 완료: AnalyticsService — 다중 분석/MMP 팬아웃 서비스

Firebase Analytics를 기본으로 하되 MMP(AppsFlyer/Adjust/Singular/Airbridge)를 추가해도
게임 코드는 `IAnalyticsService` API를 한 번만 호출하면 등록된 모든 provider로 브로드캐스트된다.

세부: `docs/superpowers/specs/2026-08-23-analyticsservice-design.md`
계획: `docs/superpowers/plans/2026-08-23-analytics-service.md`

- [x] 컬렉션 초기화가 파라미터의 순서와 타입을 보존한다
- [x] 이벤트를 발행하면 모든 provider가 각각 한 번씩 받는다
- [x] 한 provider가 예외를 던져도 나머지 provider는 호출된다
- [x] 초기화 전 이벤트는 버퍼링됐다가 초기화 후 순서대로 전달된다
- [x] 초기화 전 SetUserProperty는 같은 키의 마지막 값만 전달된다
- [x] 초기화 시 유저 상태가 버퍼된 이벤트보다 먼저 전달된다
- [x] provider 하나가 초기화에 실패해도 초기화는 성공하고 실패한 provider에는 전달되지 않는다
- [x] 모든 provider가 초기화에 실패하면 false를 반환하고 버퍼는 유지된다
- [x] InitializeAsync는 재진입해도 초기화를 두 번 시작하지 않는다
- [x] CollectionEnabled가 false면 어떤 provider에도 전달되지 않는다
- [x] CollectionEnabled를 바꾸면 모든 provider에 전파되고 같은 값 재설정은 전파되지 않는다
- [x] Dispose하면 모든 provider가 Dispose되고 이후 호출은 무시된다
- [x] AnalyticsProviderFactory는 creator가 없는 provider만 건너뛰고 나머지를 생성한다
- [x] RegisterAnalyticsService로 IAnalyticsService가 싱글턴 등록된다

구현만 있고 단위 테스트가 없는 항목(스모크로 검증): Debug provider, Firebase 어댑터.

---

## 완료: MessageService — MessagePipe 의존성 제거

MessagePipe 래퍼를 폐기하고 `Dictionary<Type, Delegate>` 기반 자체 구현으로 교체한다.
`IMessageService : IDisposable` (동기 `Publish` / `IDisposable` 반환 `Subscribe`)만 남기고,
비동기 API와 `where T : struct` 제약은 제거한다. 메인 스레드 전제.

- [x] 구독한 핸들러가 발행된 메시지를 받고 다른 타입은 받지 않는다
- [x] 같은 타입에 여러 핸들러를 구독하면 모두 호출된다
- [x] 구독을 Dispose하면 더 이상 수신하지 않고 중복 Dispose도 안전하다
- [x] 발행 중 구독/해제가 일어나도 현재 발행은 스냅샷으로 완주한다
- [x] 핸들러가 예외를 던져도 나머지 핸들러가 호출된다
- [x] 서비스를 Dispose하면 모든 구독이 해제되고 이후 사용은 거부된다
- [x] null 핸들러 구독은 거부된다
- [x] RegisterMessageService로 IMessageService가 싱글턴 등록된다

---

## 완료: ADService — 광고 네트워크 중립 서비스

세부: `docs/superpowers/specs/2026-08-20-adservice-design.md`

- [x] 재시도 정책이 지수 백오프와 상한을 계산한다
- [x] 로드 실패 시 지수 백오프로 재시도하고 한도를 넘으면 중단한다
- [x] ShowAsync가 광고제거·중복호출·미준비를 구분해 즉시 반환한다
- [x] 보상을 래치하고 닫힘에서 유예 프레임 후 확정한다
- [x] 닫힘이 보상보다 먼저 와도 보상을 잃지 않는다
- [x] 광고가 닫히거나 표시에 실패하면 다음 광고를 자동 로드한다
- [x] 배너가 숨김/파괴/재부착과 높이 중계를 처리한다
- [x] 광고제거 상태가 전면·배너를 차단하고 보상은 통과시키며 영속화된다
- [x] AdService가 어댑터와 provider 전역 임프레션을 하나의 Paid로 합류시킨다
- [x] UnityAdDispatcher가 메인스레드 마샬링·지연·프레임 대기를 제공한다
- [x] Dummy provider가 지연·실패·보상·임프레션을 시뮬레이션한다
- [x] 설정과 스크립팅 심볼로 provider를 고르고 없으면 Dummy로 폴백한다

**후속 예정**: AdMob/LevelPlay/AppLovin 실제 어댑터 (spec의 3사 매핑표 참조)

---

## 완료: SoundService 리뉴얼 — 태그 기반 오디오 시스템

`SoundCatalogSO` 기반 SFX/BGM 서비스를 폐기하고, 태그 기반 오디오 시스템으로 전면 재작성했다.

- [x] SoundData/컬렉션이 태그 하나에 여러 클립을 묶고 인덱스/무작위로 클립을 고른다
- [x] SoundServiceSettings가 데이터 컬렉션과 오클루전 파라미터를 DI로 공급한다
- [x] SoundService가 AudioSource를 풀링하고 Dispose 시 정리한다
- [x] Sound/Music/Playlist/DynamicMusic 빌더가 체이닝으로 재생을 구성한다
- [x] 페이드 인/아웃, 루프 사이클·트랙 전환 콜백, 일시정지/재개가 동작한다
- [x] id로 참조 없이 Pause/Stop/Resume과 일괄 제어가 가능하다
- [x] AudioMixer Output 볼륨이 ISoundVolumeStorage로 영속화·복원된다
- [x] 레이캐스트 기반 3D 오클루전이 로우패스와 볼륨에 반영된다
- [x] Audio Creator/Collection/Output Manager/Settings 에디터 창이 데이터를 편집한다
- [x] 태그 목록에서 SFX/Track/Output 유사 enum 코드를 생성하고 asmref로 런타임 어셈블리에 합류시킨다
- [x] MusicZone/SoundButton/OutputVolumeSlider/VolumeSlider 씬 컴포넌트를 제공한다

---

## 완료: UIManager Screen Space - Camera + Sorting Layer 정렬

세부: `docs/superpowers/plans/2026-07-27-uimanager-screenspace-camera-sorting.md`

- [x] UIManagerSettings는 SortingLayerName/SortingOrder/PlaneDistance를 설정값으로 반환한다
- [x] UIRoot는 카메라가 있으면 Canvas를 ScreenSpaceCamera와 지정 정렬/거리로 구성한다
- [x] UIRoot는 카메라가 없으면 Canvas를 ScreenSpaceOverlay로 폴백한다
- [x] UIRoot의 Canvas GO는 생성 시점 active 씬에 소속된다(DontDestroyOnLoad 아님)
- [x] active 씬이 바뀌면 활성 presenter가 teardown되고 풀 View가 파괴된다
- [x] 씬 전환 후 Page 재요청 시 새 씬에서 정상적으로 Show까지 도달한다
