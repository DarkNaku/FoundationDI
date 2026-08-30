# plan.md

## 활성 계획: 없음

다음 작업이 정해지면 여기에 테스트 목록을 채운다.

---

## 완료: UIButton / UIStateButton

uGUI Button을 상속한 피드백 버튼(사운드+햅틱)과, 상태별로 여러 Image/Text를 스왑하는 버튼.
스왑 세트는 Selectable을 모르는 순수 타입이라 EditMode에서 단독으로 테스트된다.

세부: `docs/superpowers/specs/2026-08-30-ui-button-design.md`
계획: `docs/superpowers/plans/2026-08-30-ui-button.md`

- [x] 상태가 필드를 오버라이드하면 그 상태의 값을 쓴다
- [x] 상태가 오버라이드하지 않으면 Normal 값으로 떨어진다
- [x] Normal도 오버라이드하지 않으면 그 필드를 건드리지 않는다
- [x] Selected를 지정하지 않으면 Normal로 떨어진다
- [x] 색만 오버라이드하면 스프라이트는 원본 그대로다
- [x] 타깃이 null이면 예외 없이 아무 일도 하지 않는다
- [x] Visible 오버라이드는 타깃의 enabled를 바꾼다
- [x] TMP 타깃의 문자열이 바뀐다
- [x] 레거시 Text 타깃의 문자열이 바뀐다
- [x] 색은 타깃 종류와 무관하게 Graphic.color에 들어간다
- [x] TMP 머티리얼은 fontSharedMaterial에 들어간다
- [x] 레거시 Text 머티리얼은 material에 들어간다
- [x] 텍스트 세트도 Selected 미지정이면 Normal로 떨어진다
- [x] 텍스트 타깃이 null이면 예외 없이 아무 일도 하지 않는다
- [x] 서비스가 하나도 등록되지 않아도 클릭이 예외를 내지 않는다
- [x] 햅틱서비스가 등록되지 않아도 주입이 예외를 내지 않는다
- [x] 사운드서비스가 등록되면 클릭시 지정한 SFX로 사운드를 만든다
- [x] SFX를 지정하지 않으면 사운드를 만들지 않는다
- [x] 햅틱을 켜면 클릭시 지정한 강도로 Impact를 부른다
- [x] 햅틱을 끄면 클릭해도 Impact를 부르지 않는다
- [x] SFX가 지정됐는데 사운드서비스가 없으면 한 번만 경고한다
- [x] ApplyState에 각 상태를 넣으면 세트가 그 상태로 적용된다
- [x] interactable을 끄면 Disabled 세트가 적용된다
- [x] interactable을 다시 켜면 Normal 세트가 적용된다
- [x] 세트가 비어도 상태 전이가 예외를 내지 않는다
- [x] 텍스트 세트도 함께 적용된다

EditMode 범위 밖: Pressed/Highlighted/Selected 매핑은 EventSystem 포인터 시뮬레이션이
필요하다. Disabled 경로로 switch 배선은 확인되고, 나머지는 플레이 모드 확인으로 대신한다.

---

## 대기: InjectorService/PoolManager 주입 실패 격리

`UIButton` 설계 중 발견한 기존 결함이다. `[Inject]` 필드를 든 컴포넌트가 미등록 서비스를
요구하면 `PoolManager.cs:154`(`InjectGameObject`)와 `InjectorService.Start()` 둘 다
`try/catch`가 없어 피해가 번진다. 전자는 풀 생성 중 예외로 인스턴스가 씬에 고아로 남고,
후자는 VContainer가 `EntryPointExceptionHandler` 미등록 시 그대로 rethrow하므로
나머지 pending 컴포넌트가 영영 주입을 못 받는다.

세부: `docs/superpowers/specs/2026-08-30-ui-button-design.md` "결정 사항과 근거 > 5"

- [ ] 주입이 실패한 컴포넌트가 있어도 나머지 pending이 모두 주입된다
- [ ] 풀 생성 중 주입이 실패해도 인스턴스가 씬에 고아로 남지 않는다

## 대기: SoundService 기본 Output

`UIButton` 설계 중 확인한 구조적 빈틈이다. SoundService에는 "기본 Output" 개념이 전혀 없다 —
`SoundServiceSettings`에도 `SoundData`에도 없다. 그래서 Output을 비워 두면
`Sound.SetOutput`이 `null`을 넘기고 `SoundSource.cs:308`이 `outputAudioMixerGroup = null`로
세팅해 **믹서를 통째로 우회한다**. 결과적으로 유저가 효과음 볼륨을 0으로 내려도 소리가 그대로 난다.

`SoundServiceSettings`에 `DefaultOutput`을 두고, Output이 비면 SoundService가 그걸로 해석하게 한다.
`Sound`/`Music`/`Playlist`/`DynamicMusic` 전부가 대상이라 별도 스펙·계획이 필요하다.

- [ ] Output을 지정하지 않으면 설정의 기본 Output으로 재생된다
- [ ] 기본 Output도 지정되지 않으면 이전처럼 믹서를 우회한다
- [ ] 명시한 Output이 기본 Output보다 우선한다

## 대기: UIStateButton 복원 기준값

스왑 세트가 복원 기준을 갖고 있지 않아 생기는 문제 두 가지를 함께 푼다. 뿌리가 같다 —
직렬화 필드(`Image.sprite`/`color`/`enabled`)를 되돌릴 기준 없이 직접 쓴다.

1. `Normal`이 오버라이드하지 않는 필드를 다른 상태가 오버라이드하면, 그 상태를 벗어나도
   원래 값으로 돌아오지 않는다. 지금은 인스펙터 경고로만 막고 있다.
2. `Selectable`이 `[ExecuteAlways]`라 에디터에서도 `OnValidate` → `DoStateTransition`이 돌고
   (`Selectable.cs:578-586`), 우리 스왑은 uGUI의 `overrideSprite`/`CanvasRenderer`와 달리
   직렬화 필드를 직접 쓴다. 인스펙터에서 `interactable`을 껐다 켜면 Disabled 값이 프리팹에 구워진다.

- [ ] Normal이 오버라이드하지 않는 필드도 상태를 벗어나면 원래 값으로 돌아온다
- [ ] 풀에서 재사용된 View도 첫 프리팹 값을 기준으로 복원한다
- [ ] 에디터에서 상태를 미리 보아도 프리팹에 값이 구워지지 않는다

---

## 완료: AnalyticsService — Adjust 어댑터

Firebase 어댑터와 같은 모양으로 Adjust(MMP) 어댑터를 붙인다. Adjust는 이벤트 "이름"이 아니라
대시보드 발급 토큰을 요구하므로(README 2.3), 이름→토큰 매핑표는 **어댑터 자기 설정**이 든다.
그래서 코어에 "어댑터 고유 설정"을 실어 나르는 seam 하나가 먼저 필요하다.

- [x] provider 설정 목록에서 요청한 타입을 찾아 준다
- [x] provider 설정 목록에 없는 타입을 요청하면 null을 준다
- [x] 기본 생성한 컨텍스트에 설정을 요청해도 예외가 나지 않는다
- [x] 팩토리가 provider 설정을 creator에게 그대로 넘긴다
- [x] 설정에 담긴 provider 설정 목록이 등록 경로를 타고 creator까지 간다
- [x] 관리 대상 표가 Adjust를 AdjustSdk.Scripts 어셈블리로 판정한다

이후는 `FoundationDI.Adjust` 어셈블리(FOUNDATIONDI_ADJUST 게이트) 안이라 EditMode에서
테스트할 수 없다 — Firebase 어댑터와 같은 이유다. 컴파일과 실기 검증으로 대신한다.

---

## 완료: TutorialManager — 조건 기반 튜토리얼 진행 엔진

게임 조건(레벨 시작, 아이템 등장 등)에 따라 발동하는 튜토리얼 시퀀스를 `ITutorialManager` 하나로
진행·영속화한다. 진행 규칙은 순수 C#(EditMode 테스트 가능), 씬 오써링은 얇은 MonoBehaviour 어댑터.
시퀀스는 순차 리스트가 아니라 조건부 후보 집합이고, 진행도는 인덱스가 아니라 시퀀스 ID로 저장한다.

세부: `docs/superpowers/specs/2026-08-24-tutorial-manager-design.md`
계획: `docs/superpowers/plans/2026-08-24-tutorial-manager.md`

- [x] 타깃참조가 비어있으면 IsEmpty가 참이다
- [x] 키만 채우면 HasKey가 참이고 비어있지 않다
- [x] 공백문자열 키는 키가 없는 것으로 본다
- [x] 직접참조를 채우면 비어있지 않고 키는 없다
- [x] 직접참조가 파괴되면 다시 비어있는 것으로 본다
- [x] 직접참조가 키보다 우선한다
- [x] 저장한적 없는 시퀀스는 NotStarted다
- [x] 상태를 저장하면 새 인스턴스에서도 읽힌다
- [x] 시퀀스마다 상태가 독립적이다
- [x] 스텝인덱스를 저장하면 새 인스턴스에서도 읽힌다
- [x] AllSkipped는 기본이 거짓이고 저장하면 유지된다
- [x] Clear는 상태와 스텝인덱스와 AllSkipped를 모두 지운다
- [x] 저장키가 다르면 진행도가 섞이지 않는다
- [x] 가짜트리거는 Arm되기 전에 발동해도 아무일이 없다
- [x] 가짜트리거는 Arm 후 Fire하면 콜백을 부른다
- [x] 가짜트리거는 Disarm되면 Fire해도 콜백을 부르지 않는다
- [x] 가짜시계는 대기를 즉시 끝낸다
- [x] 가짜모듈은 Show와 Hide 횟수를 센다
- [x] 가짜레지스트리는 등록된 타깃을 즉시 돌려준다
- [x] 타깃핸들은 대상이 바뀌면 Changed를 쏜다
- [x] 타깃핸들은 같은 대상으로 다시 설정하면 Changed를 쏘지 않는다
- [x] 타깃핸들은 대상이 파괴되면 Current가 null이다
- [x] 타깃핸들은 Dispose 후 Changed를 쏘지 않는다
- [x] 스텝은 트리거가 없으면 Auto로 채운다
- [x] 스텝은 음수 지연을 0으로 보정한다
- [x] 스텝은 null 모듈을 걸러낸다
- [x] 시퀀스는 스텝이 없으면 빈 목록을 갖는다
- [x] 시퀀스는 null 스텝을 걸러낸다
- [x] 시퀀스는 음수 타임아웃을 0으로 보정한다
- [x] 트리거어웨이터는 발동하면 완료된다
- [x] 트리거어웨이터는 취소되면 Disarm하고 취소예외를 던진다
- [x] 트리거어웨이터는 두번 발동해도 한번만 완료된다
- [x] 시작트리거가 발동해야 시퀀스가 시작된다
- [x] 스텝이 시작트리거-모듈Show-종료트리거-모듈Hide 순서로 진행된다
- [x] 여러 스텝이 순서대로 진행된다
- [x] 시퀀스가 완료되면 Completed로 기록되고 이벤트가 발행된다
- [x] 완료된 시퀀스는 등록해도 트리거를 arm하지 않는다
- [x] AllSkipped면 어떤 시퀀스도 arm하지 않는다
- [x] 중복 시퀀스ID는 무시된다
- [x] Unregister하면 트리거가 Disarm된다
- [x] Dispose하면 대기중인 트리거가 모두 Disarm된다
- [x] Auto트리거는 Arm 즉시 발동한다
- [x] Manual트리거는 같은 ID로 Fire해야 발동한다
- [x] Manual트리거는 Disarm되면 발동하지 않는다
- [x] Message트리거는 Match를 통과한 메시지에만 발동한다
- [x] Message트리거는 Match를 오버라이드하지 않으면 모든 메시지에 발동한다
- [x] Message트리거는 Disarm하면 구독이 해제된다
- [x] Message트리거는 한번 발동한 뒤 다시 발동하지 않는다
- [x] ButtonClick트리거는 타깃 버튼을 누르면 발동한다
- [x] ButtonClick트리거는 Disarm하면 리스너가 제거된다
- [x] ButtonClick트리거는 타깃이 없으면 발동하지 않고 예외도 없다
- [x] 직접참조는 등록없이 해석된다
- [x] 등록되지 않은 키는 해석되지 않는다
- [x] 등록한 키가 해석된다
- [x] 같은 키를 두번 등록하면 마지막 등록이 이긴다
- [x] 마지막 등록을 해제하면 이전 등록으로 돌아간다
- [x] 이미 등록된 타깃은 즉시 해석된다
- [x] 나중에 등록되는 타깃을 기다린다
- [x] 해석된 핸들은 타깃이 해제되면 null이 된다
- [x] 해석된 핸들은 타깃이 다시 등록되면 복귀한다
- [x] 핸들을 Dispose하면 등록해도 영향받지 않는다
- [x] 타임아웃이 지나면 null을 돌려준다
- [x] 빈 참조는 null 대상의 핸들을 즉시 돌려준다
- [x] 실행중 다른 시퀀스가 발동하면 대기열에 들어간다
- [x] 대기열은 Order 오름차순으로 실행된다
- [x] 기본 재개모드는 시퀀스 처음부터 시작한다
- [x] ResumeFromStep이면 저장된 스텝부터 시작한다
- [x] Running 상태여도 시작트리거를 기다린다
- [x] 스텝 지연이 시계를 통해 대기된다
- [x] 모듈이 예외를 던져도 다음 모듈과 스텝이 진행된다
- [x] 타깃을 못찾으면 시퀀스가 중단되고 NotStarted로 되돌아간다
- [x] Skip은 현재 시퀀스만 완료처리한다
- [x] SkipAll은 AllSkipped를 세우고 모든 트리거를 Disarm한다
- [x] Dispose하면 진행중인 시퀀스가 취소되고 완료로 기록되지 않는다
- [x] 등록하면 ITutorialManager를 해결할 수 있다
- [x] 등록하면 타깃 레지스트리도 함께 해결된다
- [x] ITutorialManager는 싱글톤이다
- [x] 저장소를 직접 주입하면 그것이 쓰인다
- [x] 타깃이 null이면 실패한다
- [x] RectTransform은 코너로 rect를 만든다
- [x] 렌더러가 없는 일반 Transform은 점 rect를 만든다
- [x] 렌더러가 있으면 바운즈로 rect를 만든다
- [x] 카메라가 없으면 일반 Transform은 실패한다
- [x] 시퀀스ID를 비우면 게임오브젝트 이름을 쓴다
- [x] 스텝ID를 비우면 게임오브젝트 이름을 쓴다
- [x] 자식 스텝을 계층 순서대로 모은다
- [x] 손자 스텝은 모으지 않는다
- [x] 스텝을 안 붙이면 빈 시퀀스가 만들어진다
- [x] 기본 트리거는 Auto다
- [x] (PlayMode) 씬에 배치한 시퀀스가 주입받아 스스로 등록되고 완료된다
- [x] (PlayMode) 완료된 시퀀스는 앱을 다시 켜도 시작하지 않는다

---

## 완료: IAPService — 모바일 인앱 구매 서비스

게임 코드가 `IIapService` 하나로 Google Play / App Store의 소모성·비소모성 상품을 구매·복원한다.
Unity IAP 5.4.2는 `FOUNDATIONDI_UNITYIAP` 심볼이 걸린 옵셔널 어셈블리에 격리하고, 코어는 Dummy provider로 완전히 동작한다.

세부: `docs/superpowers/specs/2026-08-23-iapservice-design.md`
계획: `docs/superpowers/plans/2026-08-23-iap-service.md`

- [x] IapProductId가 플랫폼 오버라이드를 고르고 비면 공용 ID로 폴백한다
- [x] 구매결과의 IsSuccess가 성공 결과에서만 참이다
- [x] 초기화하면 provider 상품이 노출된다
- [x] 초기화 전 구매는 NotReady다
- [x] InitializeAsync는 재진입해도 provider를 한 번만 초기화한다
- [x] 구매가 검증·지급·확정 순서로 진행된다
- [x] 지급이 실패하면 확정하지 않는다
- [x] 지급이 예외를 던져도 확정하지 않고 서비스가 살아있다
- [x] 영수증 검증에 실패하면 지급도 확정도 하지 않는다
- [x] 비소모성은 확정 후 소유로 기록되고 소모성은 아니다
- [x] 사용자 취소와 그 외 실패를 구분한다
- [x] 이미 소유한 비소모성은 스토어를 거치지 않는다
- [x] 구매가 진행 중이면 두 번째 호출은 즉시 NotReady다
- [x] provider가 구매 시작을 거부하면 Failed다
- [x] 카탈로그에 없는 상품 구매는 NotReady다
- [x] 미확정 구매는 초기화 때 지급되고 확정된다
- [x] 복원은 비소모성 소유를 되살리고 개수를 보고한다
- [x] 복원이 실패하면 Success가 거짓이다
- [x] 보류된 구매는 지급하지 않고 Deferred를 반환한다
- [x] Dispose하면 provider가 해제되고 이후 구매는 NotReady다
- [x] 저장한 소유 상태가 다시 읽힌다
- [x] IapProviderFactory가 강제 더미·미가용 심볼·미등록 creator를 처리한다
- [x] RegisterIapService로 IIapService가 싱글턴 등록된다

구현만 있고 단위 테스트가 없는 항목(스모크로 검증): Unity IAP 어댑터. 상수 생성기는 순수 함수만 단위 테스트.

---

## 완료: AnalyticsService — 다중 분석/MMP 팬아웃 서비스

Firebase Analytics를 기본으로 하되 MMP(AppsFlyer/Adjust/Singular/Airbridge)를 추가해도
게임 코드는 `IAnalyticsService` API를 한 번만 호출하면 등록된 모든 provider로 브로드캐스트된다.

세부: `docs/superpowers/specs/2026-08-23-analyticsservice-design.md`
계획: `docs/superpowers/plans/2026-08-23-analytics-service.md`

- [x] 컬렉션 초기화가 파라미터의 순서와 타입을 보존한다
- [x] 이벤트를 발행하면 모든 provider가 각각 한 번씩 받는다
- [x] 한 provider가 예외를 던져도 나머지 provider는 호출된다
- [x] 초기화 전 이벤트는 버퍼링됐다가 초기화 후 순서대로 전달된다
- [x] 초기화 전 SetUserProperty는 같은 키의 마지막 값만 전달된다
- [x] 초기화 시 유저 상태가 버퍼된 이벤트보다 먼저 전달된다
- [x] provider 하나가 초기화에 실패해도 초기화는 성공하고 실패한 provider에는 전달되지 않는다
- [x] 모든 provider가 초기화에 실패하면 false를 반환하고 버퍼는 유지된다
- [x] InitializeAsync는 재진입해도 초기화를 두 번 시작하지 않는다
- [x] CollectionEnabled가 false면 어떤 provider에도 전달되지 않는다
- [x] CollectionEnabled를 바꾸면 모든 provider에 전파되고 같은 값 재설정은 전파되지 않는다
- [x] Dispose하면 모든 provider가 Dispose되고 이후 호출은 무시된다
- [x] AnalyticsProviderFactory는 creator가 없는 provider만 건너뛰고 나머지를 생성한다
- [x] RegisterAnalyticsService로 IAnalyticsService가 싱글턴 등록된다

구현만 있고 단위 테스트가 없는 항목(스모크로 검증): Debug provider, Firebase 어댑터.

---

## 완료: MessageService — MessagePipe 의존성 제거

MessagePipe 래퍼를 폐기하고 `Dictionary<Type, Delegate>` 기반 자체 구현으로 교체한다.
`IMessageService : IDisposable` (동기 `Publish` / `IDisposable` 반환 `Subscribe`)만 남기고,
비동기 API와 `where T : struct` 제약은 제거한다. 메인 스레드 전제.

- [x] 구독한 핸들러가 발행된 메시지를 받고 다른 타입은 받지 않는다
- [x] 같은 타입에 여러 핸들러를 구독하면 모두 호출된다
- [x] 구독을 Dispose하면 더 이상 수신하지 않고 중복 Dispose도 안전하다
- [x] 발행 중 구독/해제가 일어나도 현재 발행은 스냅샷으로 완주한다
- [x] 핸들러가 예외를 던져도 나머지 핸들러가 호출된다
- [x] 서비스를 Dispose하면 모든 구독이 해제되고 이후 사용은 거부된다
- [x] null 핸들러 구독은 거부된다
- [x] RegisterMessageService로 IMessageService가 싱글턴 등록된다

---

## 완료: ADService — 광고 네트워크 중립 서비스

세부: `docs/superpowers/specs/2026-08-20-adservice-design.md`

- [x] 재시도 정책이 지수 백오프와 상한을 계산한다
- [x] 로드 실패 시 지수 백오프로 재시도하고 한도를 넘으면 중단한다
- [x] ShowAsync가 광고제거·중복호출·미준비를 구분해 즉시 반환한다
- [x] 보상을 래치하고 닫힘에서 유예 프레임 후 확정한다
- [x] 닫힘이 보상보다 먼저 와도 보상을 잃지 않는다
- [x] 광고가 닫히거나 표시에 실패하면 다음 광고를 자동 로드한다
- [x] 배너가 숨김/파괴/재부착과 높이 중계를 처리한다
- [x] 광고제거 상태가 전면·배너를 차단하고 보상은 통과시키며 영속화된다
- [x] AdService가 어댑터와 provider 전역 임프레션을 하나의 Paid로 합류시킨다
- [x] UnityAdDispatcher가 메인스레드 마샬링·지연·프레임 대기를 제공한다
- [x] Dummy provider가 지연·실패·보상·임프레션을 시뮬레이션한다
- [x] 설정과 스크립팅 심볼로 provider를 고르고 없으면 Dummy로 폴백한다

**후속 예정**: AdMob 어댑터 (spec의 3사 매핑표 참조). AppLovin MAX·LevelPlay 어댑터는 구현
완료 — 다만 두 SDK 모두 이 리포지토리에 미설치라 어댑터 어셈블리가 컴파일된 적이 없다.
컴파일·실기 검증과 테스트 어셈블리 추가는 SDK 설치 시점의 과제로 남아 있다.

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
