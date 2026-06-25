# ResourceService 설계 문서

- 작성일: 2026-06-25
- 상태: 승인 대기

## 1. 목적

Addressables 패키지 기반의 **범용 에셋 로더 서비스**를 만든다. 임의 타입 `T` 에셋을
키로 로드/해제하는 단일 진입점을 제공하고, 참조 카운팅으로 핸들 생명주기를 안전하게
관리한다. 궁극적으로 프로젝트의 **다른 모든 서비스(PoolService, SoundService 등)가
이 로더에 리소스 로드를 위임**하여 중복된 로딩 로직을 제거한다.

### 배경

현재 `PoolService.Load()` 와 `SoundService.Load()` 는 동일한 로딩 패턴
(`Resources.Load` 시도 → 실패 시 `Addressables.LoadAssetAsync` + `WaitForCompletion()`
→ 핸들 보관/해제)을 각자 중복 구현하고 있다. 이 로직을 ResourceService로 일원화한다.

## 2. 범위

### 포함

- 비동기 로드: `UniTask<T> LoadAsync<T>(string key)`
- 동기 로드: `T Load<T>(string key)` (`WaitForCompletion` 기반)
- 해제: `void Release(string key)` (참조 1 감소)
- `Dispose()` 시 남은 모든 핸들 일괄 해제
- 키 단위 캐싱 + 참조 카운팅

### 제외 (YAGNI)

- `Resources.Load` 폴백 — **Addressables 전용**
- `InstantiateAsync` / `ReleaseInstance`
- 레이블/키 목록 프리로드(Preload/Download)
- 씬 로딩

## 3. 공개 API

```csharp
namespace DarkNaku.FoundationDI
{
    public interface IResourceService : IDisposable
    {
        UniTask<T> LoadAsync<T>(string key) where T : Object;
        T Load<T>(string key) where T : Object;
        void Release(string key);
    }
}
```

동작 규약:

- 같은 키를 반복 로드하면 캐시에서 즉시 반환하고 **참조 카운트를 증가**시킨다.
- `Release(key)` 가 참조 카운트를 **0으로 만들면** 실제 Addressables 핸들을 해제하고
  캐시에서 제거한다. 0이 아니면 카운트만 감소시킨다.
- 보유 참조보다 많이 `Release` 를 호출해도 안전하게 무시한다(음수 방지).
- `Dispose()` 는 남아 있는 모든 키의 핸들을 해제하고 캐시를 비운다.

## 4. 내부 구조

테스트 가능성을 위해 **실제 Addressables 호출을 seam(인터페이스)으로 분리**한다.

```csharp
public interface IResourceProvider
{
    UniTask<T> LoadAsync<T>(string key) where T : Object;
    T Load<T>(string key) where T : Object;
    void Release(string key);   // 키별 핸들 해제
}
```

### 책임 분리

- **`ResourceService`** — 참조 카운팅 + 캐싱만 담당한다.
  - 첫 로드(refCount 0 → 1)일 때만 `provider.LoadAsync` / `provider.Load` 를 호출한다.
  - 마지막 해제(refCount 1 → 0)일 때만 `provider.Release` 를 호출한다.
  - 캐시 항목은 `(에셋 참조, 참조 카운트)` 를 보관한다.
  - 생성자 2개: 기본 생성자는 `AddressableResourceProvider` 를 주입,
    내부/테스트용 생성자는 `IResourceProvider` 를 주입받는다.

- **`AddressableResourceProvider`** (기본 구현) — Addressables 어댑터.
  - `Addressables.LoadAssetAsync<T>`, `WaitForCompletion()`, `Addressables.Release` 를
    감싸고 `Dictionary<string, AsyncOperationHandle>` 로 키→핸들을 보관한다.
  - 얇은 어댑터로 유지하며, 실제 Addressables 연동 검증은 PlayMode 대상으로 남긴다.

### 테스트 전략

단위 테스트는 `IResourceProvider` 를 **NSubstitute로 대체**하여 참조 카운팅/캐싱/해제
로직을 실제 Addressables 빌드 없이 EditMode에서 검증한다.

## 5. 동시성(in-flight) 처리

같은 키에 대해 `LoadAsync` 가 완료되기 전에 다시 `LoadAsync` 가 호출되는 경우,
provider를 중복 호출하지 않도록 진행 중인 `UniTask<T>` 를 캐시하여 공유한다.
초기 구현에서는 순차 호출을 우선 충족시키고, in-flight 중복 제거는 별도 테스트로
다룬다(아래 테스트 9).

## 6. 기존 서비스 위임 (후속 단계)

ResourceService 완성 후, **별도 단계**로 `PoolService.Load()` 와 `SoundService.Load()`
의 중복 로딩 로직을 `IResourceService` 호출로 교체한다. 구조 변경과 동작 변경을
분리(Tidy First)하기 위해 본 스펙의 구현과 커밋을 나눈다.

## 7. 어셈블리 / 테스트 구성

- 새 테스트 asmdef `FoundationDI.Tests` (EditMode) 생성.
  참조: `FoundationDI`, Unity Test Framework, NSubstitute, UniTask.
- 테스트 함수명은 한글로 작성한다.

## 8. 테스트 목록 (TDD 순서)

한 번에 하나씩 RED → GREEN → REFACTOR 로 진행한다.

1. 첫 로드 시 provider를 호출하고 에셋을 반환한다
2. 같은 키 재로드 시 provider를 다시 호출하지 않고 캐시에서 반환한다
3. Release 시 참조 카운트가 0이 아니면 provider.Release를 호출하지 않는다
4. 참조 카운트가 0이 되면 provider.Release를 호출하고 캐시에서 제거한다
5. 0이 되어 제거된 키를 다시 로드하면 provider를 재호출한다
6. 보유 참조보다 많이 Release하면 안전하게 무시한다
7. Dispose 시 남은 모든 키의 핸들을 해제한다
8. 동기 `Load<T>` 도 동일한 참조 카운팅을 따른다
9. 같은 키의 LoadAsync가 진행 중일 때 재호출하면 provider를 중복 호출하지 않는다

## 9. 성공 기준

- 위 테스트가 모두 통과한다.
- 컴파일러/린터 경고가 없다.
- 구조 변경(asmdef 추가 등)과 동작 변경 커밋이 분리되어 있다.
