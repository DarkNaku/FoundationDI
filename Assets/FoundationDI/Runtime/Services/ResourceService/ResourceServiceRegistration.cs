using System;
using Reflex.Core;
using Reflex.Enums;

namespace DarkNaku.FoundationDI
{
    public enum ResourceBackend
    {
        Resources,
        Addressables
    }

    public static class ResourceServiceRegistration
    {
        // 루트 IInstaller의 InstallBindings에서 호출한다.
        //   builder.RegisterResourceService();                                // Resources
        //   builder.RegisterResourceService(ResourceBackend.Addressables);    // Addressables
        // PoolManager·UINavigator가 IResourceService를 전제로 하므로 그 등록보다 먼저(부모 컨테이너에) 둔다.
        public static ContainerBuilder RegisterResourceService(this ContainerBuilder builder,
                                                               ResourceBackend backend = ResourceBackend.Resources)
        {
            var providerType = backend switch
            {
                ResourceBackend.Resources => typeof(ResourcesProvider),
                ResourceBackend.Addressables => typeof(AddressablesProvider),
                _ => throw new ArgumentOutOfRangeException(nameof(backend), backend, null)
            };

            builder.RegisterType(providerType, new[] { typeof(IResourceProvider) },
                                 Lifetime.Singleton, Resolution.Lazy);

            return builder.RegisterType(typeof(ResourceService), new[] { typeof(IResourceService) },
                                        Lifetime.Singleton, Resolution.Lazy);
        }
    }
}
