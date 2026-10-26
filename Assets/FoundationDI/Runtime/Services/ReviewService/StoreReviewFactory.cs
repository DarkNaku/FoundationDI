using System;
using UnityEngine;

namespace DarkNaku.FoundationDI
{
    internal static class StoreReviewFactory
    {
        // AndroidJavaObject·UnityEngine.iOS.Device는 모든 플랫폼에서 컴파일되므로 #if 대신 실행 플랫폼으로
        // 고른다 — 에디터가 어느 빌드 타깃에 있든 두 구현의 컴파일 에러가 바로 드러난다.
        //
        // usePlayInAppReview가 꺼져 있으면 Play Review 라이브러리가 빌드에 없다(의존성 XML을 만들지 않았다).
        // 그 상태로 Android 창구를 만들면 ClassNotFound라 미리 피한다.
        public static IStoreReview Create(bool usePlayInAppReview, Func<IMainThreadDispatcher> dispatcher = null)
        {
            switch (Application.platform)
            {
                case RuntimePlatform.Android when usePlayInAppReview:
                    return new AndroidStoreReview(dispatcher?.Invoke());
                case RuntimePlatform.Android:
                    Debug.LogWarning("[ReviewService] ReviewServiceSettings의 Use Play In-App Review가 꺼져 있어 " +
                                     "Android 리뷰 창을 띄우지 않는다.");
                    return new UnsupportedStoreReview();
                case RuntimePlatform.IPhonePlayer:
                    return new IosStoreReview();
                default:
                    return new UnsupportedStoreReview();
            }
        }
    }
}
