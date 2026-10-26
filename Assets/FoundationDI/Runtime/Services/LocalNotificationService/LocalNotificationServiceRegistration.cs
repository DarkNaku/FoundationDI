using System.Collections.Generic;
using Reflex.Core;
using Reflex.Enums;
using UnityEngine;
using Resolution = Reflex.Enums.Resolution;

namespace DarkNaku.FoundationDI
{
    public static class LocalNotificationServiceRegistration
    {
        // 루트 IInstaller의 InstallBindings에서 호출한다. 일정은 게임이 ILocalNotificationPlanner로 따로 등록한다.
        //   builder.RegisterLocalNotificationService(_notificationSettings);
        //   builder.RegisterType(typeof(MyPlanner), new[] { typeof(ILocalNotificationPlanner) }, Lifetime.Singleton, Resolution.Lazy);
        public static ContainerBuilder RegisterLocalNotificationService(this ContainerBuilder builder,
                                                                        LocalNotificationServiceSettings settings)
        {
            if (settings == null)
            {
                Debug.LogError("[LocalNotificationService] LocalNotificationServiceSettings가 null이다. 서비스를 등록하지 않는다.");
                return builder;
            }

            builder.RegisterValue(settings);
            builder.RegisterType(typeof(PlayerPrefsLocalNotificationStorage), new[] { typeof(ILocalNotificationStorage) },
                                 Lifetime.Singleton, Resolution.Lazy);

            return builder.RegisterFactory<ILocalNotificationService>(container =>
            {
                // planner는 해석 시점에 모은다. 등록 순서와 무관하게 잡힌다.
                var planners = new List<ILocalNotificationPlanner>(container.All<ILocalNotificationPlanner>());
                var options = settings.ToOptions();

                return new LocalNotificationService(() => LocalNotifierRegistry.Create(options), planners,
                                                    container.Resolve<ILocalNotificationStorage>(),
                                                    new UnityApplicationFocusSource());
            }, Lifetime.Singleton, Resolution.Lazy);
        }
    }
}
