using Reflex.Core;
using Reflex.Enums;
using UnityEngine;
using Resolution = Reflex.Enums.Resolution;

namespace DarkNaku.FoundationDI
{
    public static class AppUpdateServiceRegistration
    {
        // 루트 IInstaller의 InstallBindings에서 호출한다.
        //   builder.RegisterAppUpdateService(_appUpdateSettings);
        public static ContainerBuilder RegisterAppUpdateService(this ContainerBuilder builder, AppUpdateServiceSettings settings)
        {
            if (settings == null)
            {
                Debug.LogError("[AppUpdateService] AppUpdateServiceSettings가 null이다. 서비스를 등록하지 않는다.");
                return builder;
            }

            builder.RegisterValue(settings);
            builder.RegisterMainThreadDispatcher();

            // 디스패처는 Android 창구를 만들 때만 해석한다. 해석하면 러너 GameObject가 생기는데,
            // iOS·에디터에서는 쓸 일이 없다.
            return builder.RegisterFactory<IAppUpdateService>(
                container => new AppUpdateService(() => AppUpdateStoreFactory.Create(
                    settings, container.Resolve<IMainThreadDispatcher>)),
                Lifetime.Singleton, Resolution.Lazy);
        }
    }
}
