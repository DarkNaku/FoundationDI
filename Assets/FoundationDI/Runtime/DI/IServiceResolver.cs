using UnityEngine;

namespace DarkNaku.FoundationDI
{
    /// <summary>
    /// 런타임 의존성 해석과 주입의 seam. DI 프레임워크 타입을 패키지 공개 표면에서 가린다.
    /// 구현은 <see cref="ReflexServiceResolver"/> 하나뿐이며, 테스트는 이 인터페이스를 대역으로 세운다.
    /// </summary>
    /// <remarks>
    /// 인터페이스가 필요한 이유는 둘이다. Reflex의 Container가 sealed라 모킹할 수 없고,
    /// 컨테이너 타입이 공개 시그니처(InitializeItem.InitializeAsync 등)에 새어 나가면
    /// DI 교체가 곧 공개 API 파괴가 된다 - 이번 마이그레이션이 그 대가를 치른 사례다.
    /// </remarks>
    public interface IServiceResolver
    {
        /// <summary>등록된 계약을 해석한다. 미등록이면 예외를 던진다.</summary>
        T Resolve<T>();

        /// <summary>등록돼 있을 때만 해석한다. 선택적 의존에 쓴다.</summary>
        bool TryResolve<T>(out T resolved);

        /// <summary>대상 객체의 [Inject] 멤버를 채운다. MonoBehaviour가 아니어도 된다.</summary>
        void Inject(object target);

        /// <summary>GameObject와 모든 자손의 MonoBehaviour를 주입한다(비활성 포함).</summary>
        void InjectGameObject(GameObject target);
    }
}
