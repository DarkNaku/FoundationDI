using System;
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
        //
        // 켜기/끄기·권한 물음·연 알림 기록을 게임 세이브에 두려면 ILocalNotificationStorage를 이 호출보다 먼저
        // 등록한다(없으면 PlayerPrefs). configureOptions는 알림 창구를 처음 만들 때 한 번 불린다 — 채널 이름처럼
        // 게임이 현지화한 문구를 넣는 자리다.
        public static ContainerBuilder RegisterLocalNotificationService(this ContainerBuilder builder,
                                                                        LocalNotificationServiceSettings settings,
                                                                        Func<LocalNotificationOptions, LocalNotificationOptions> configureOptions = null)
        {
            if (settings == null)
            {
                Debug.LogError("[LocalNotificationService] LocalNotificationServiceSettings가 null이다. 서비스를 등록하지 않는다.");
                return builder;
            }

            builder.RegisterValue(settings);
            if (!builder.HasBinding(typeof(ILocalNotificationStorage)))
            {
                builder.RegisterType(typeof(PlayerPrefsLocalNotificationStorage), new[] { typeof(ILocalNotificationStorage) },
                                     Lifetime.Singleton, Resolution.Lazy);
            }

            return builder.RegisterFactory<ILocalNotificationService>(container =>
            {
                // planner는 해석 시점에 모은다. 등록 순서와 무관하게 잡힌다.
                var planners = new List<ILocalNotificationPlanner>(container.All<ILocalNotificationPlanner>());
                return new LocalNotificationService(() => LocalNotifierRegistry.Create(CreateOptions(settings, configureOptions)),
                                                    planners,
                                                    container.Resolve<ILocalNotificationStorage>(),
                                                    new UnityApplicationFocusSource());
            }, Lifetime.Singleton, Resolution.Lazy);
        }

        private static LocalNotificationOptions CreateOptions(LocalNotificationServiceSettings settings,
                                                              Func<LocalNotificationOptions, LocalNotificationOptions> configure)
        {
            var options = settings.ToOptions();
            return configure != null ? configure(options) : options;
        }
    }
}
