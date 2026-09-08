using Reflex.Core;
using Reflex.Enums;

namespace DarkNaku.FoundationDI
{
    public static class MessageServiceRegistration
    {
        // 루트 IInstaller의 InstallBindings에서 호출한다.
        //   builder.RegisterMessageService();
        // 컨테이너가 Dispose될 때 Reflex가 MessageService.Dispose를 호출해 구독을 정리한다.
        public static ContainerBuilder RegisterMessageService(this ContainerBuilder builder)
        {
            return builder.RegisterType(
                typeof(MessageService), new[] { typeof(IMessageService) },
                Lifetime.Singleton, Resolution.Lazy);
        }
    }
}
