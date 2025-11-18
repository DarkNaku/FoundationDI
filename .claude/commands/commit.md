# commit 명령어 - 변경사항 커밋

모든 테스트가 통과할 때 변경사항을 git에 커밋합니다.

## 커밋 조건

✅ **커밋 전 필수 확인**:
- [ ] 모든 테스트가 통과해야 함
- [ ] 컴파일러 경고가 없어야 함
- [ ] 변경사항이 하나의 논리 단위여야 함

## 커밋 유형

### STRUCTURAL (구조적 변경)
- 메서드 추출
- 변수명 변경
- 코드 이동
- 조직화 개선
- **중요**: 행동(동작)은 변경하면 안 됨

```
[STRUCTURAL] 메서드 추출: ValidateGameObject()

- GameObject 유효성 검증 로직을 별도 메서드로 추출
- 중복 코드 제거
- 의도 명확성 향상

테스트: 모두 통과 ✓
```

### BEHAVIORAL (행동적 변경)
- 새 기능 추가
- 버그 수정
- 로직 변경

```
[BEHAVIORAL] GameObject 파괴 후 접근 방지 구현

- Get() 메서드에서 null-safe 처리 추가
- SetParent() 호출 전 GameObject 유효성 확인

테스트:
- shouldReturnNullWhenGameObjectDestroyed ✓
- shouldHandleNullGameObjectInGet ✓
```

## 커밋 규칙 (Tidy First)

✅ **반드시 지켜야 할 규칙**:
- 구조적 변경과 행동적 변경을 절대 섞지 말 것
- 구조적 변경을 먼저 커밋
- 행동적 변경을 나중에 커밋
- 각 커밋은 하나의 논리 단위만 포함
- 작고 자주 커밋 (큰 커밋보다 작은 커밋 선호)

## 커밋 메시지 형식

```
[TYPE] 간단한 설명 (한 줄)

상세 설명:
- 변경 내용
- 변경 이유
- 영향받는 부분

테스트 상태:
- 모든 테스트 통과 ✓
```

## 사용 예

```
/commit
```

다음 단계: `/go` (다음 테스트) 또는 `/status` (진행 상황 확인)
