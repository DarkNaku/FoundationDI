# plan.md

## 활성 계획: HapticService

세부: `docs/superpowers/plans/2026-07-03-haptic-service.md`

테스트 목록 (다음 작업 = 첫 번째 미완료 항목):

- [ ] 활성화 상태에서 Selection 호출 시 provider의 Selection을 호출한다 (타입 골격 생성 포함)
- [ ] 활성화 상태에서 Impact 호출 시 provider에 같은 스타일로 위임한다
- [ ] 활성화 상태에서 Notification 호출 시 provider에 같은 타입으로 위임한다
- [ ] 비활성화 상태에서는 어떤 provider 메서드도 호출하지 않는다
- [ ] Enabled 기본값은 true이다
- [ ] Enabled 설정값은 PlayerPrefs에 영속화된다
- [ ] NoopHapticProvider는 예외 없이 모든 메서드를 수행한다

> 이후 태스크(AndroidHapticProvider / iOS 네이티브 브리지 / 기본 생성자 분기 / VContainer 확장 + README)는
> 디바이스 전용·구조적 변경이라 EditMode 테스트 대상이 아니며, 세부 계획 문서의 Task 7~10을 따른다.
