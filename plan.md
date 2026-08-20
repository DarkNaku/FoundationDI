# plan.md

## 활성 계획: 없음

다음 작업이 정해지면 여기에 테스트 목록을 채운다.

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
