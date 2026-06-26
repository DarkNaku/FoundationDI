# UIManager → ResourceService 전환 설계 문서

- 작성일: 2026-06-26
- 상태: 승인 대기
- 브랜치: `feature/uimanager-renewal`

## 1. 목적

UIManager의 프리팹 로딩을 자체 로더(`IUIAssetLoader`/`ResourcesUILoader`/`AddressablesUILoader`)
대신 공용 `IResourceService`(Addressables 기반)에 위임한다. 에셋 로딩과 핸들 생명주기를
한 곳(ResourceService)에서 참조 카운팅으로 관리하기 위함이다. (CLAUDE.md의 "에셋 로딩은
ResourceService에 위임한다" 규약 적용.)

## 2. 결정 사항

1. **Addressables 전용 전환** — UI 프리팹은 Addressables 항목으로 로드한다. Resources 경로 제거.
2. **직접 의존** — `UIInstanceFactory`가 `IResourceService`에 직접 의존한다. `IUIAssetLoader`
   추상화와 두 구현(`ResourcesUILoader`, `AddressablesUILoader`)을 제거한다.
3. **Release 동작 현행 유지** — `Create` 시 로드만 하고 개별 `Release`는 호출하지 않는다.
   정리는 컨테이너 수명 종료 시 `ResourceService.Dispose`에 위임(현재 동작과 동등).

## 3. 런타임 변경

### `UIInstanceFactory` (`Controllers/UIInstanceFactory.cs`)

- 생성자 의존을 `IUIAssetLoader _loader` → `IResourceService _resource`로 교체한다.
- `Create()` 내부의 `_loader.Load(key)` → `_resource.Load<GameObject>(key)`로 교체한다.
  (둘 다 동기 호출이라 메서드 시그니처·흐름은 그대로 유지된다.)

### 제거 (`Loading/` 폴더)

- `IUIAssetLoader.cs`, `AddressablesUILoader.cs`, `ResourcesUILoader.cs` (각 `.meta` 포함).

### DI 등록

- `UIManagerVContainerExtensions.RegisterUIManager`에서
  `builder.Register<IUIAssetLoader, ResourcesUILoader>(Lifetime.Singleton)` 줄을 제거한다.
- `RootLifetimeScope.Configure`에서 `RegisterUIManager` 호출 **앞에**
  `builder.Register<IResourceService, ResourceService>(Lifetime.Singleton)`를 추가한다.
- `RegisterUIManager`는 컨테이너에 `IResourceService`가 **이미 등록되어 있다고 가정**한다
  (호출자 책임). 이 전제를 확장 메서드 XML 주석으로 명시한다.

## 4. 테스트 변경

| 파일 | 변경 |
| --- | --- |
| `Tests/Editor/UIManager/UIInstanceFactoryTests.cs` | `IUIAssetLoader` 모킹 → `IResourceService` 모킹. `resource.Load<GameObject>("UI/Sample").Returns(prefab)`, `new UIInstanceFactory(resolver, resource)`. |
| `Tests/Editor/UIManager/ResourcesUILoaderTests.cs` | **삭제** (대상 클래스 제거). |
| `Tests/Editor/UIManager/DIRegistrationTests.cs` | `RegisterUIManager` 호출 전에 `builder.Register<IResourceService, ResourceService>(Lifetime.Singleton)` 추가 (전제 반영). |
| `Tests/Runtime/UIManager/UIManagerFlowTests.cs` | 4개 테스트 메서드의 `Substitute.For<IUIAssetLoader>()` + `loader.Load(key).Returns(prefab)` → `Substitute.For<IResourceService>()` + `resource.Load<GameObject>(key).Returns(prefab)`, `new UIInstanceFactory(resolver, resource)`. SetUp/Teardown 불변. |

## 5. 동작 / 범위

- UI 프리팹은 이제 **Addressables 항목**이어야 로드된다(Resources 경로 제거).
- 프리팹 Release는 추가하지 않는다 — `Create` 시 `Load`만, 정리는 `ResourceService.Dispose`에 위임.
- 같은 키를 여러 번 `Create`하면 ResourceService 참조 카운트가 증가만 하지만(해제 없음),
  핸들은 유지되므로 기능상 무해 — 현재의 "키당 1회 로드·해제 없음"과 동등.

## 6. 커밋 분리 (Tidy First)

C# 컴파일 제약상 `IUIAssetLoader`를 사용하는 코드를 먼저 전환한 뒤에야 로더를 삭제할 수 있다.
따라서 순서는 다음과 같다.

1. **[BEHAVIORAL]** `UIInstanceFactory`를 `IResourceService` 의존으로 전환 + 관련 테스트
   (UIInstanceFactoryTests, UIManagerFlowTests) 수정 + DI 등록 전환(RegisterUIManager에서
   loader 등록 제거, RootLifetimeScope/DIRegistrationTests에 IResourceService 등록).
   이 시점에 로더 파일들은 미참조 상태로 남는다(컴파일·테스트 통과).
2. **[STRUCTURAL]** 미참조가 된 `Loading/` 폴더와 `ResourcesUILoaderTests.cs` 삭제.

> 참고: 단위 테스트 레벨에서는 로더/서비스를 모킹하므로 동작이 동일하다. 실제 로딩 백엔드가
> Resources→Addressables로 바뀌는 행동 변화는 컴포지션 루트(실제 구현)에서만 나타난다.

## 7. 검증

- EditMode 테스트(UIInstanceFactoryTests, DIRegistrationTests, 기존 UIManager EditMode 테스트,
  ResourceServiceTest)와 PlayMode 테스트(UIManagerFlowTests)가 모두 통과한다.
- 컴파일 경고 없음.
- `IUIAssetLoader` 참조가 코드베이스에서 완전히 사라진다.

## 8. 범위 외 (후속)

- 프리팹 Release를 UI 요소 생명주기에 맞춰 호출하는 참조 카운팅 활용은 후속 과제.
- PoolService/SoundService의 ResourceService 위임은 별도 계획.
