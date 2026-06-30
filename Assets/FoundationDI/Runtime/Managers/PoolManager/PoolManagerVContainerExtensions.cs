using UnityEngine;
using VContainer;

namespace DarkNaku.FoundationDI
{
    public static class PoolManagerVContainerExtensions
    {
        /// <summary>
        /// PoolManager를 컨테이너에 등록한다.
        /// 씬 LifetimeScope에서 호출하면 풀이 씬과 수명을 함께하여, 씬 언로드 시
        /// scope.Dispose()로 풀과 로드한 에셋(IResourceService.Release)이 자동 정리된다.
        /// <paramref name="root"/>(보통 씬 LifetimeScope의 transform)를 넘기면 풀 루트가
        /// 활성 씬이 아니라 그 transform이 속한 씬에 확실히 귀속된다(additive 로드 안전).
        /// 전제: 부모(루트) 스코프에 <see cref="IResourceService"/>가 이미 등록되어 있어야 한다
        /// (PoolManager가 프리팹 로드를 IResourceService에 위임함).
        /// </summary>
        public static void RegisterPoolManager(this IContainerBuilder builder, Transform root = null)
        {
            var registration = builder.Register<IPoolManager, PoolManager>(Lifetime.Singleton);

            if (root != null)
            {
                registration.WithParameter(root);
            }
        }
    }
}
