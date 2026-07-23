# PoolManager 의존성 주입 설계

작성일: 2026-07-23

## 배경 / 문제

`PoolManager`는 `Object.Instantiate(prefab)`로 풀 아이템 인스턴스를 만들 뿐(`PoolManager.cs:140`),
`IObjectResolver`를 받지도 않고 어떤 DI 주입도 하지 않는다. 따라서 풀에서 꺼낸 GameObject의
컴포넌트에 `[Inject]` 필드/메서드가 있어도 채워지지 않는다.

대비되는 사례: `UIManager`는 `UIInstanceFactory`에서 `IObjectResolver.Inject(presenter)`로
프리젠터에 의존성을 주입한다(`UIInstanceFactory.cs:20`). PoolManager도 동일하게 인스턴스에
의존성을 주입할 수 있어야 한다.

## 목표

풀 아이템 인스턴스가 **생성될 때 1회** 컨테이너 의존성을 주입받게 한다.

## 설계 결정

### 1. 생성자에 `IObjectResolver` 추가

```csharp
public PoolManager(IResourceService resourceService, IObjectResolver resolver, Transform parent = null)
```

- VContainer는 `IObjectResolver`를 자동 등록하므로, `RegisterPoolManager` 확장 메서드는
  변경 없이 생성자 주입으로 자동 해결된다.
- `resolver`는 **null 가드**로 둔다(이 코드베이스의 defensive 스타일과 일관). null이면 주입을
  조용히 건너뛴다 — 주입이 필요 없는 EditMode 테스트나 컨테이너 없는 사용에서도 깨지지 않는다.

### 2. 주입 시점·대상: 생성 팩토리 안, 1회만, 계층 전체

`Register`의 `ObjectPool` create 람다 순서:

```
Object.Instantiate(prefab)
  → IPoolItem get 또는 PoolItem AddComponent
  → resolver?.InjectGameObject(go)
  → item.OnCreateItem()
```

- PoolItem을 먼저 확보한 뒤 `InjectGameObject`를 호출하므로, 프리팹에 원래 있던 컴포넌트든
  방금 붙인 PoolItem이든 모두 주입 대상이 된다.
- `OnCreateItem()` 이전에 주입하므로 초기화 콜백에서 주입된 의존성을 사용할 수 있다.
- 풀에서 재사용되는 `Get`/`OnGetItem` 경로에는 주입을 넣지 않는다(생성 시 1회로 충분 —
  VContainer 의존성은 대부분 싱글턴/씬 스코프).

### 3. `InjectGameObject`의 동작 (참고)

`ObjectResolverUnityExtensions.InjectGameObject`는 확장 메서드로, GameObject 계층을 재귀
순회하며 각 MonoBehaviour에 대해 `resolver.Inject(monoBehaviour)`를 호출한다
(`ObjectResolverUnityExtensions.cs:50`). 따라서 NSubstitute로 대체한 `IObjectResolver`에서도
`Inject`가 호출되므로 EditMode 단위 테스트로 검증 가능하다.

## 테스트 (TDD, EditMode + NSubstitute)

- 첫 실패 테스트: **"새 인스턴스 생성 시 resolver로 컴포넌트에 주입한다"**
  - `Substitute.For<IObjectResolver>()`를 생성자에 넘기고, `Get` 후
    `resolver.Received().Inject(Arg.Any<object>())`(또는 해당 컴포넌트 인자)로 검증.
- (선택) **"resolver가 null이면 주입 없이 정상 동작한다"** — null 가드 검증.
- (선택) **"같은 키 두 번째 Get(재사용)에서는 다시 주입하지 않는다"** — 생성 시 1회 보장.

## 작업 순서 (Tidy First — 구조/행동 분리 커밋)

1. `[STRUCTURAL]` 생성자에 `IObjectResolver` 파라미터 추가 + 기존 테스트(`new PoolManager(resource)`)와
   호출부를 새 시그니처에 맞게 수정. 동작은 불변(주입 로직은 아직 없음).
2. `[BEHAVIORAL]` 주입 로직 추가. TDD 사이클(RED → GREEN → REFACTOR). `plan.md`에 테스트 항목 추가.

## 범위 밖 (YAGNI)

- Get마다 재주입 옵션, 주입 대상 선택 API 등은 만들지 않는다. 필요해지면 그때 추가한다.
