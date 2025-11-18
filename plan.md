# PoolService 개선 계획

## 개요
PoolService의 안정성, 성능, 테스트 용이성을 개선하기 위한 TDD 기반 개발 계획

---

## P0: 크래시 방지 (Critical Bugs)

### [ ] P0-1: GameObject 파괴 후 접근 방지
**파일**: PoolService.cs:33-48
**문제**: Get() 반환 후 GameObject가 파괴되었을 때 Transform 접근 시 크래시
**테스트**:
- [ ] shouldReturnNullWhenGameObjectDestroyed
- [ ] shouldHandleNullGameObjectInGet

**구현 사항**:
1. Get() 메서드에서 item.GO 널 체크 추가
2. SetParent() 호출 전 GameObject 유효성 확인

---

### [ ] P0-2: Release() 중복 호출 방지
**파일**: PoolItem.cs:57-84
**문제**: Release(delay) 중복 호출 시 같은 아이템이 여러 번 Pool에 반환될 수 있음
**테스트**:
- [ ] shouldPreventDoubleReleaseWithDelay
- [ ] shouldIgnoreReleaseIfAlreadyReleasing
- [ ] shouldHandleReleaseAfterDestroy

**구현 사항**:
1. PoolItem에 `_isReleasing` 플래그 추가
2. Release() 호출 시 플래그 확인
3. 비동기 Release 완료 후 플래그 해제

---

## P1: 안정성 및 디버깅 (Important Features)

### [ ] P1-1: Thread-Safety 추가
**파일**: PoolService.cs:19, 35-48
**문제**: 멀티스레드 환경에서 동일 키에 대해 Load()가 중복 호출될 수 있음
**테스트**:
- [ ] shouldHandleConcurrentGetRequests
- [ ] shouldNotDuplicatePoolForSameKey

**구현 사항**:
1. _table에 접근하는 모든 부분에 lock 추가
2. Load() 중복 방지 로직 구현
3. PoolData 레지스트리에 스레드 안전 보장

---

### [ ] P1-2: 명확한 에러 메시지 추가
**파일**: PoolService.cs:40, 55-60
**문제**: Get() 실패 시 조용히 null 반환되어 원인 파악 어려움
**테스트**:
- [ ] shouldLogErrorWhenPoolLoadFails
- [ ] shouldLogErrorWhenReleaseCalledWithoutPoolItem

**구현 사항**:
1. Get() 실패 시 명확한 Debug.LogError 추가
2. Load() 실패 원인별 상세 에러 메시지
3. Release() 호출 시 IPoolItem 미보유 경고

---

### [ ] P1-3: PoolData.Clear() 안전성 개선
**파일**: PoolData.cs:40-62
**문제**: Clear() 중 _items 순회 중 수정 가능성, 핸들 누수
**테스트**:
- [ ] shouldSafelyClearPoolDataDuringIteration
- [ ] shouldProperlyReleaseAddressablesHandle
- [ ] shouldNotThrowWhenClearingEmptyPool

**구현 사항**:
1. _items 복사본으로 순회
2. Addressables 핸들 해제 전 유효성 확인
3. Clear() 후 상태 검증

---

## P2: 기능 및 성능 (Enhancement)

### [ ] P2-1: 풀 상태 모니터링 API
**파일**: PoolService.cs
**목적**: 디버깅 및 성능 모니터링을 위한 상태 조회 기능
**테스트**:
- [ ] shouldReturnPoolDataByKey
- [ ] shouldReturnAllPoolsInfo
- [ ] shouldReturnPoolStatistics

**구현 사항**:
1. TryGetPoolData(string key, out PoolData data) 메서드
2. GetAllPools() → IReadOnlyDictionary<string, PoolData>
3. PoolData에 통계 정보 추가 (활성 아이템 수, 생성된 아이템 수)

---

### [ ] P2-2: Addressables 핸들 관리 개선
**파일**: PoolService.cs:147, PoolData.cs:52-55
**문제**: Resources 로드 시 default 핸들 사용, 핸들 상태 혼란
**테스트**:
- [ ] shouldProperlyHandleResourcesLoadedAssets
- [ ] shouldProperlyHandleAddressablesLoadedAssets
- [ ] shouldNotReleaseInvalidHandles

**구현 사항**:
1. Resources/Addressables 로드 구분 (별도 필드)
2. PoolData에 로드 방식 추적
3. Clear()에서 로드 방식별 정리 로직

---

### [ ] P2-3: 풀 생명주기 이벤트
**파일**: PoolData.cs
**목적**: 풀 생성/정리 시점에 외부 로직 실행 가능
**테스트**:
- [ ] shouldInvokeOnPoolCreatedEvent
- [ ] shouldInvokeOnPoolClearedEvent
- [ ] shouldNotInvokeEventsAfterDispose

**구현 사항**:
1. PoolData.OnPoolCreated 이벤트
2. PoolData.OnPoolCleared 이벤트
3. PoolService에서 이벤트 발행

---

### [ ] P2-4: 비동기 AssetUnload
**파일**: PoolService.cs:78
**문제**: Resources.UnloadUnusedAssets() 동기 호출로 프레임 드롭 가능
**테스트**:
- [ ] shouldUnloadAssetsWithoutBlockingMainThread
- [ ] shouldCompleteUnloadAfterDisposeCall

**구현 사항**:
1. UnloadAssetsAsync() 메서드 추가
2. Dispose()에서 비동기 언로드 옵션 제공
3. 언로드 완료 콜백 지원

---

### [ ] P2-5: HashSet 추적 최적화
**파일**: PoolData.cs:13, 19, 28, 37, 43
**목적**: 불필요한 메모리 오버헤드 제거, 성능 향상
**테스트**:
- [ ] shouldOptionallyTrackPoolItems
- [ ] shouldNotTrackItemsWhenDisabled
- [ ] shouldMaintainAccurateCountWithTracking

**구현 사항**:
1. PoolData 생성 시 추적 옵션 추가
2. 추적 비활성화 시 HashSet 사용 않음
3. Get/Release 성능 비교 벤치마크

---

## 개발 규칙

- **TDD 프로세스**: Red → Green → Refactor
- **테스트 작성**: 한 번에 하나의 테스트만 작성
- **커밋 규칙**:
  - 구조적 변경과 행동적 변경 분리
  - 모든 테스트 통과 후 커밋
- **코드 리뷰**: 특히 thread-safety, null-safety 확인
- **성능**: 각 개선사항별로 벤치마크 수행

---

## 진행 상황 추적

```
P0 (Critical):     0/2
P1 (Important):    0/3
P2 (Enhancement):  0/5
─────────────────────
총 진행률:         0/10 (0%)
```

---

## 참고 자료

- 문제점 분석 문서: `분석 결과.md`
- 관련 파일:
  - Assets/FoundationDI/Runtime/Services/PoolService/PoolService.cs
  - Assets/FoundationDI/Runtime/Services/PoolService/PoolItem.cs
  - Assets/FoundationDI/Runtime/Services/PoolService/PoolData.cs
