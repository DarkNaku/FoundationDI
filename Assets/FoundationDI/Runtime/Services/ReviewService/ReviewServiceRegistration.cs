using Reflex.Core;
using Reflex.Enums;
using UnityEngine;
using Resolution = Reflex.Enums.Resolution;

namespace DarkNaku.FoundationDI
{
    public static class ReviewServiceRegistration
    {
        // 루트 IInstaller의 InstallBindings에서 호출한다.
        //   builder.RegisterReviewService(_reviewSettings);
        public static ContainerBuilder RegisterReviewService(this ContainerBuilder builder, ReviewServiceSettings settings)
        {
            if (settings == null)
            {
                Debug.LogError("[ReviewService] ReviewServiceSettings가 null이다. 서비스를 등록하지 않는다.");
                return builder;
            }

            builder.RegisterValue(settings);
            builder.RegisterMainThreadDispatcher();

            // 디스패처는 Android 창구를 만들 때만 해석한다. 해석하면 러너 GameObject가 생기는데,
            // iOS·에디터에서는 쓸 일이 없다.
            return builder.RegisterFactory<IReviewService>(
                container => new ReviewService(() => StoreReviewFactory.Create(
                    settings.UsePlayInAppReview, container.Resolve<IMainThreadDispatcher>)),
                Lifetime.Singleton, Resolution.Lazy);
        }
    }
}
