using VContainer;

namespace DarkNaku.FoundationDI
{
    public static class MessageServiceRegistration
    {
        // 루트 LifetimeScope의 Configure에서 호출한다.
        //   builder.RegisterMessageService();
        // 컨테이너가 Dispose될 때 VContainer가 MessageService.Dispose를 호출해 구독을 정리한다.
        public static IContainerBuilder RegisterMessageService(this IContainerBuilder builder)
        {
            builder.Register<IMessageService, MessageService>(Lifetime.Singleton);

            return builder;
        }
    }
}
