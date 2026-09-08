using Reflex.Core;
using Reflex.Enums;

namespace DarkNaku.FoundationDI
{
    public static class TutorialManagerRegistration
    {
        /// <summary>
        /// 씬 IInstaller에서 호출한다.
        /// 전제: 부모(루트) 컨테이너에 IMessageService가 등록돼 있어야 한다.
        /// saveKey는 진행도 PlayerPrefs 키의 네임스페이스다.
        /// </summary>
        public static ContainerBuilder RegisterTutorialManager(this ContainerBuilder builder,
                                                               string saveKey = "default")
        {
            builder.RegisterFactory<ITutorialProgressStorage>(
                _ => new PlayerPrefsTutorialProgressStorage(saveKey), Lifetime.Singleton, Resolution.Lazy);

            return RegisterCore(builder);
        }

        /// <summary>진행도 저장소를 직접 붙일 때(서버 동기화 등) 쓴다.</summary>
        public static ContainerBuilder RegisterTutorialManager(this ContainerBuilder builder,
                                                               ITutorialProgressStorage storage)
        {
            builder.RegisterValue(storage, new[] { typeof(ITutorialProgressStorage) });

            return RegisterCore(builder);
        }

        private static ContainerBuilder RegisterCore(ContainerBuilder builder)
        {
            builder.RegisterType(typeof(TutorialClock), new[] { typeof(ITutorialClock) },
                                 Lifetime.Singleton, Resolution.Lazy);
            builder.RegisterType(typeof(TutorialTargetRegistry), new[] { typeof(ITutorialTargetRegistry) },
                                 Lifetime.Singleton, Resolution.Lazy);

            return builder.RegisterType(typeof(TutorialManager), new[] { typeof(ITutorialManager) },
                                        Lifetime.Singleton, Resolution.Lazy);
        }
    }
}
