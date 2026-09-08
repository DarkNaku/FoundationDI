using Reflex.Core;
using Reflex.Injectors;
using UnityEngine;

namespace DarkNaku.FoundationDI
{
    /// <summary>
    /// 패키지 안에서 Reflex를 아는 유일한 타입. 컨테이너 교체 비용을 이 파일 하나로 묶어 둔다.
    /// </summary>
    internal sealed class ReflexServiceResolver : IServiceResolver
    {
        private readonly Container _container;

        public ReflexServiceResolver(Container container)
        {
            _container = container;
        }

        public T Resolve<T>() => _container.Resolve<T>();

        public bool TryResolve<T>(out T resolved)
        {
            // Reflex는 미등록 계약에 UnknownContractException을 던진다.
            // 선택적 의존을 예외 없이 다루려면 먼저 물어봐야 한다.
            if (_container.HasBinding<T>())
            {
                resolved = _container.Resolve<T>();
                return true;
            }

            resolved = default;
            return false;
        }

        public void Inject(object target) => AttributeInjector.Inject(target, _container);

        public void InjectGameObject(GameObject target)
            => GameObjectInjector.InjectRecursive(target, _container);
    }
}
