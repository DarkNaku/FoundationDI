using Reflex.Core;
using Reflex.Enums;
using Resolution = Reflex.Enums.Resolution;

namespace DarkNaku.FoundationDI
{
    public static class MainThreadDispatcherRegistration
    {
        // AdService·ConsentService·ReviewService가 각자 부른다. 먼저 부른 쪽만 등록하고 나머지는 건너뛴다 —
        // 펌프(러너 GameObject)가 하나여야 지연·프레임 계산이 한 시계로 돈다.
        public static ContainerBuilder RegisterMainThreadDispatcher(this ContainerBuilder builder)
        {
            if (builder.HasBinding(typeof(IMainThreadDispatcher))) return builder;

            return builder.RegisterType(typeof(UnityAdDispatcher),
                                        new[] { typeof(IAdDispatcher), typeof(IMainThreadDispatcher) },
                                        Lifetime.Singleton, Resolution.Lazy);
        }
    }
}
