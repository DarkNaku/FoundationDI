# refactor 명령어 - REFACTOR 단계 실행

테스트가 통과하는 상태에서 코드 구조를 개선합니다.

## 실행 내용

1. **코드 분석**: 개선할 점 찾기
   - 중복 코드 제거 (DRY)
   - 변수명/메서드명 개선 (Intent)
   - 메서드 추출 (Extract Method)
   - 구조 단순화

2. **한 번에 하나의 변경만 수행**
3. **각 변경 후 테스트 실행**
4. **완료 후 커밋 준비**

## REFACTOR 단계 규칙

✅ **반드시 지켜야 할 규칙**:
- 테스트는 계속 통과해야 함 (✅)
- 한 번에 하나의 리팩토링만 함
- 각 단계마다 테스트 실행
- 행동을 변경하면 안 됨 (구조만 변경)

✅ **우선순위**:
1. 중복 제거 (DRY - Don't Repeat Yourself)
2. 의도 표현 (Express Intent - 명확한 이름)
3. 메서드 추출 (Extract Method)
4. 변수명 개선 (Rename)

✅ **리팩토링 패턴**:
- Extract Method: 긴 메서드를 작은 메서드들로 분할
- Rename: 불명확한 이름을 명확하게 변경
- Replace Temp with Query: 임시 변수를 메서드로 변환
- Move Method: 메서드를 더 적합한 클래스로 이동

## 사용 예

```
/refactor
```

다음 단계: `/commit` (변경사항 커밋) 또는 `/go` (다음 테스트)
