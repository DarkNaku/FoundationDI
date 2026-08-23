using VContainer;

namespace DarkNaku.FoundationDI
{
    public static class TutorialManagerRegistration
    {
        /// <summary>
        /// 씬 LifetimeScope에서 호출한다.
        /// 전제: 부모(루트) 스코프에 IMessageService가 등록돼 있어야 한다.
        /// saveKey는 진행도 PlayerPrefs 키의 네임스페이스다.
        /// </summary>
        public static void RegisterTutorialManager(this IContainerBuilder builder,
                                                   string saveKey = "default")
        {
            builder.Register<ITutorialProgressStorage>(
                _ => new PlayerPrefsTutorialProgressStorage(saveKey), Lifetime.Singleton);

            RegisterCore(builder);
        }

        /// <summary>진행도 저장소를 직접 붙일 때(서버 동기화 등) 쓴다.</summary>
        public static void RegisterTutorialManager(this IContainerBuilder builder,
                                                   ITutorialProgressStorage storage)
        {
            builder.RegisterInstance(storage).As<ITutorialProgressStorage>();

            RegisterCore(builder);
        }

        private static void RegisterCore(IContainerBuilder builder)
        {
            builder.Register<ITutorialClock, TutorialClock>(Lifetime.Singleton);
            builder.Register<ITutorialTargetRegistry, TutorialTargetRegistry>(Lifetime.Singleton);
            builder.Register<ITutorialManager, TutorialManager>(Lifetime.Singleton);
        }
    }
}
