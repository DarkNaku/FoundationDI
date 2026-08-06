# InitializeService 설계

- 날짜: 2026-08-07
- 상태: 승인됨 (구현 계획 대기)
- 관련: 기존 서비스 패턴 `ResourceService`, `HapticService` (구조 관례를 따름)

## 배경 / 목적

게임 시작 시 실행해야 할 초기화 작업(테이블 로드, 저장 데이터 복원, 서비스 워밍업 등)을 **데이터로 선언**하고, 그 목록을 한 번에 순차 실행하는 서비스를 만든다. 초기화 항목 각각은 `ScriptableObject`로 정의해 필요 정보(필드)와 로직을 캡슐화하고, 항목들을 담은 컨테이너 `ScriptableObject`(카탈로그)를 서비스에 매개변수로 넘겨 초기화한다.

세션 동안 **중복 초기화를 방지**한다. 같은 카탈로그를 두 번 넘겨도, 서로 다른 카탈로그에 겹치는 항목이 있어도 각 항목은 세션 내 한 번만 실행된다.

## 결정 사항 (브레인스토밍 확정)

- **async 타입: `UnityEngine.Awaitable`.** 향후 패키지 전체를 UniTask → Unity 내장 Awaitable로 전환할 계획이므로, 신규 코드는 Awaitable로 시작해 마이그레이션 부채를 늘리지 않는다. `async Awaitable` 메서드 내부에서는 기존 UniTask 서비스를 명시적 변환 없이 그대로 `await` 할 수 있으므로 과도기 마찰이 없다.
- **실행 방식: 비동기 순차.** 카탈로그의 항목을 선언 순서대로 하나씩 `await` 한다.
- **DI 유지.** 서비스는 VContainer에 싱글턴 등록되고 `IObjectResolver`를 생성자 주입받아, 각 항목의 초기화 로직에 전달한다. 항목은 이 resolver로 필요한 서비스를 해석한다.
- **트리거: 서비스 메서드 호출.** 부트스트랩/플로우 코드가 `IInitializeService.InitializeAsync(catalog)`를 명시적으로 호출한다. (엔트리포인트 자동 실행은 범위 밖.)
- **중복 방지 기준: 아이템 단위 + 카탈로그 단위 (둘 다).** 아이템 단위 추적이 상위 보장이며, 카탈로그 단위는 재호출을 빠르게 스킵한다.
- **실패 처리: 즉시 중단, 예외 전파.** 항목이 예외를 던지면 전체 초기화를 중단하고 호출측으로 예외를 전파한다. 실패한 항목은 완료로 표시하지 않아 재시도가 가능하다.

## 설계

### 위치 / 네임스페이스

- 위치: `Assets/FoundationDI/Runtime/Services/InitializeService/`
- 네임스페이스: `DarkNaku.FoundationDI`

### 1. `InitializeItem` (추상 ScriptableObject)

초기화 항목의 베이스 클래스. 상속받아 필요한 필드(직렬화 데이터)와 초기화 로직을 구현한다.

```csharp
public abstract class InitializeItem : ScriptableObject
{
    public abstract Awaitable InitializeAsync(IObjectResolver resolver);
}
```

- 중복 방지 키는 **SO 인스턴스 참조 자체**로 사용한다. 세션 내 에셋 인스턴스는 안정적이며, 두 카탈로그가 같은 에셋을 참조하면 동일 인스턴스이므로 참조 기반 dedup이 그대로 성립한다.

### 2. `InitializeCatalog` (컨테이너 ScriptableObject)

초기화 항목들을 목록으로 담는 그릇. 초기화의 매개변수가 되는 파일.

```csharp
[CreateAssetMenu(...)]
public class InitializeCatalog : ScriptableObject
{
    [SerializeField] private List<InitializeItem> _items = new();
    public IReadOnlyList<InitializeItem> Items => _items;
}
```

### 3. `IInitializeService` + `InitializeService`

```csharp
public interface IInitializeService : IDisposable
{
    Awaitable InitializeAsync(InitializeCatalog catalog);
}
```

- 생성자로 `IObjectResolver`를 주입받는다 (seam이자 항목 컨텍스트).
- 세션 상태(싱글턴이라 앱 실행 동안 유지):
  - `HashSet<InitializeItem> _initializedItems` — 아이템 단위 dedup (여러 카탈로그 공유).
  - `HashSet<InitializeCatalog> _initializedCatalogs` — 카탈로그 단위 dedup.

`InitializeAsync(catalog)` 흐름:

1. `catalog`가 `_initializedCatalogs`에 있으면 → 즉시 반환(스킵).
2. `catalog.Items`를 **선언 순서대로** 순회:
   - 항목이 `_initializedItems`에 있으면 → 스킵.
   - 아니면 `await item.InitializeAsync(_resolver)` → 성공 시 `_initializedItems`에 추가.
   - 예외 발생 시 즉시 전파(실패 항목은 추가하지 않음 → catalog도 완료로 표시 안 됨).
3. 모든 항목 성공 후 → `catalog`를 `_initializedCatalogs`에 추가.

이 설계의 성질: 항목이 중간에 실패해 catalog가 미완료로 남아도, 재호출 시 완료된 항목은 아이템 단위 dedup으로 스킵되어 **실패 지점부터 이어서 재개**된다.

`Dispose()`는 두 HashSet을 clear 한다(세션 상태 초기화).

### 4. DI 등록

- `RootLifetimeScope.Configure`에서 `builder.Register<IInitializeService, InitializeService>(Lifetime.Singleton)`.
- 기존 서비스 관례(`RegisterHapticService`, `RegisterUIManager`)에 맞춰 `builder.RegisterInitializeService()` 확장 메서드를 함께 제공한다.

### 5. 파일 구성

- `InitializeItem.cs` — 추상 SO 베이스
- `InitializeCatalog.cs` — 컨테이너 SO
- `InitializeService.cs` — `IInitializeService` + 구현
- `InitializeServiceRegistration.cs` — DI 확장 메서드
- `README.md` — 다른 서비스와 동일한 사용법/API 문서

## 테스트 (seam)

- 서비스의 유일한 외부 의존은 생성자 주입 `IObjectResolver` → EditMode에서 NSubstitute로 대체.
- 항목은 추상 `InitializeItem`이므로 테스트에서 **fake 서브클래스**를 `ScriptableObject.CreateInstance<T>()`로 만들어 사용. fake는 "즉시 완료 Awaitable"을 반환하고 호출 여부·순서·전달받은 resolver·던진 예외를 기록한다.
- 카탈로그도 `CreateInstance<InitializeCatalog>()`로 만들고 내부 리스트에 fake를 주입한다(테스트 헬퍼/`SerializedObject` 중 최소 방식은 plan 단계에서 확정).

### Awaitable EditMode 펌핑 리스크 (구현 시 조기 검증)

Awaitable의 continuation은 Unity 플레이어 루프/동기화 컨텍스트에 의존한다. 프레임 지연 없이 **동기적으로 완료되는 fake 항목**을 쓰면 continuation이 인라인으로 돌아 EditMode 테스트가 정상 통과하지만, `Awaitable.NextFrameAsync` 류를 섞으면 EditMode에서 안 돌 수 있다. → 테스트용 fake는 즉시 완료 Awaitable을 반환하도록 설계하고, **첫 RED/GREEN 사이클에서 `run_tests`로 조기 검증**한다.

### 테스트 목록 초안

- [ ] 카탈로그 아이템을 선언 순서대로 초기화한다
- [ ] 각 아이템에 resolver를 전달한다
- [ ] 이미 초기화된 아이템은 다시 초기화하지 않는다 (아이템 단위)
- [ ] 이미 초기화된 카탈로그는 다시 초기화하지 않는다 (카탈로그 단위)
- [ ] 두 카탈로그에 겹치는 아이템은 한 번만 초기화된다
- [ ] 아이템이 예외를 던지면 중단하고 예외를 전파한다
- [ ] 실패 후 재호출하면 완료된 아이템은 스킵하고 실패 지점부터 재개한다
- [ ] Dispose 후에는 세션 상태가 초기화된다

## 범위 밖

- 스레드 안전성 없음(메인 스레드 전제, ResourceService와 동일). 같은 catalog에 대한 동시 재진입 호출은 가드하지 않는다.
- 병렬 초기화, 우선순위 정렬, 진행률 리포팅, 취소(CancellationToken)는 이번 범위 밖(추후 필요 시 확장).
- VContainer 엔트리포인트 자동 실행(앱 시작 시 자동 초기화)은 이번 범위 밖.
