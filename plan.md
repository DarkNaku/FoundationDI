# plan.md

## 활성 계획: SoundCatalog 직접 클립 참조 전환

세부: `docs/superpowers/plans/2026-07-29-sound-catalog-direct-clip.md`
스펙: `docs/superpowers/specs/2026-07-29-sound-catalog-direct-clip-design.md`

테스트 목록 (다음 작업 = 첫 번째 미완료 항목):

- [ ] 등록된 키는 클립으로 변환된다 (TryGetClip)
- [ ] 미등록 키는 클립 변환에 실패한다
- [ ] PreloadClips는 Preload가 true인 클립만 노출한다
- [ ] SFX 재생시 카탈로그에서 클립을 가져온다
- [ ] 같은 프레임 SFX는 클립을 한번만 조회한다
- [ ] BGM 재생시 카탈로그에서 클립을 가져온다
- [ ] 카탈로그에 없는 SFX/BGM 키는 재생하지 않고 에러를 남긴다
- [ ] PreloadAsync는 카탈로그의 PreloadClips를 열거한다
- [ ] 문자열키 API 제거 후 Keys 순서/중복 키 경고가 클립 기반으로 유지된다

---

## 완료: UIManager Screen Space - Camera + Sorting Layer 정렬

세부: `docs/superpowers/plans/2026-07-27-uimanager-screenspace-camera-sorting.md`

- [x] UIManagerSettings는 SortingLayerName/SortingOrder/PlaneDistance를 설정값으로 반환한다
- [x] UIRoot는 카메라가 있으면 Canvas를 ScreenSpaceCamera와 지정 정렬/거리로 구성한다
- [x] UIRoot는 카메라가 없으면 Canvas를 ScreenSpaceOverlay로 폴백한다
- [x] UIRoot의 Canvas GO는 생성 시점 active 씬에 소속된다(DontDestroyOnLoad 아님)
- [x] active 씬이 바뀌면 활성 presenter가 teardown되고 풀 View가 파괴된다
- [x] 씬 전환 후 Page 재요청 시 새 씬에서 정상적으로 Show까지 도달한다
