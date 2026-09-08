using Reflex.Core;
using Reflex.Enums;
using UnityEngine;
using Resolution = Reflex.Enums.Resolution;

namespace DarkNaku.FoundationDI
{
    /// <summary>
    /// IServiceResolver를 루트·씬 컨테이너 양쪽에 자동 등록한다.
    /// 사용자가 등록을 잊으면 [Inject] IServiceResolver가 UnknownContractException으로 터지고,
    /// 씬 주입에는 컴포넌트별 격리가 없어 그 뒤 순번 전체가 주입되지 않는다.
    /// 잊을 수 있는 구조를 아예 만들지 않는다.
    /// </summary>
    internal static class ServiceResolverBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Install()
        {
            ContainerScope.OnRootContainerBuilding += Register;
            ContainerScope.OnSceneContainerBuilding += RegisterForScene;
        }

        private static void RegisterForScene(UnityEngine.SceneManagement.Scene scene, ContainerBuilder builder)
            => Register(builder);

        /// <summary>테스트가 직접 부를 수 있도록 internal로 연다.</summary>
        internal static void Register(ContainerBuilder builder)
        {
            // 팩토리여야 한다 - 어댑터는 '빌드된' 컨테이너를 필요로 하는데
            // 등록 시점에는 아직 없다. RegisterFactory의 콜백이 그것을 넘겨 준다.
            //
            // 씬 컨테이너에도 거는 이유: 부모(루트)의 리졸버를 물려받으면
            // 씬 스코프에만 있는 바인딩(UINavigator 등)을 해석하지 못한다.
            builder.RegisterFactory<IServiceResolver>(
                c => new ReflexServiceResolver(c), Lifetime.Singleton, Resolution.Lazy);
        }
    }
}
