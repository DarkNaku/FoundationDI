# plan.md

## 활성 계획: MessageService — MessagePipe 의존성 제거

MessagePipe 래퍼를 폐기하고 `Dictionary<Type, Delegate>` 기반 자체 구현으로 교체한다.
`IMessageService : IDisposable` (동기 `Publish` / `IDisposable` 반환 `Subscribe`)만 남기고,
비동기 API와 `where T : struct` 제약은 제거한다. 메인 스레드 전제.

- [ ] 구독한 핸들러가 발행된 메시지를 받고 다른 타입은 받지 않는다
- [ ] 같은 타입에 여러 핸들러를 구독하면 모두 호출된다
- [ ] 구독을 Dispose하면 더 이상 수신하지 않고 중복 Dispose도 안전하다
- [ ] 발행 중 구독/해제가 일어나도 현재 발행은 스냅샷으로 완주한다
- [ ] 핸들러가 예외를 던져도 나머지 핸들러가 호출된다
- [ ] 서비스를 Dispose하면 모든 구독이 해제되고 이후 사용은 거부된다
- [ ] null 핸들러 구독은 거부된다
- [ ] RegisterMessageService로 IMessageService가 싱글턴 등록된다

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
