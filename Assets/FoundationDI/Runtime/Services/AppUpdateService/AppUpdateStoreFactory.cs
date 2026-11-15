using System;
using UnityEngine;
using UnityEngine.Networking;

namespace DarkNaku.FoundationDI
{
    internal static class AppUpdateStoreFactory
    {
        private const int LOOKUP_TIMEOUT_SECONDS = 10;

        // #if 대신 실행 플랫폼으로 고른다 — 에디터가 어느 빌드 타깃에 있든 두 구현의 컴파일 에러가 바로 드러난다.
        //
        // UsePlayAppUpdate가 꺼져 있으면 Play App Update 라이브러리가 빌드에 없다(의존성 XML을 만들지 않았다).
        // 그 상태로 Android 창구를 만들면 ClassNotFound라 미리 피한다.
        public static IAppUpdateStore Create(AppUpdateServiceSettings settings, Func<IMainThreadDispatcher> dispatcher = null)
        {
            switch (Application.platform)
            {
                case RuntimePlatform.Android when settings.UsePlayAppUpdate:
                    return new PlayAppUpdateStore(dispatcher?.Invoke());
                case RuntimePlatform.Android:
                    Debug.LogWarning("[AppUpdateService] AppUpdateServiceSettings의 Use Play App Update가 꺼져 있어 " +
                                     "Android에서는 업데이트를 확인하지 않는다.");
                    return new UnsupportedAppUpdateStore();
                case RuntimePlatform.IPhonePlayer:
                    return new AppStoreAppUpdateStore(
                        Application.identifier,
                        settings.AppStoreCountry,
                        Application.version,
                        IosSystemVersion(),
                        GetTextAsync,
                        () => DateTime.UtcNow,
                        Application.OpenURL);
                default:
                    return new UnsupportedAppUpdateStore();
            }
        }

        // UnityEngine.iOS는 Android 플레이어 빌드에 없다(에디터에서는 컴파일된다).
        private static string IosSystemVersion()
        {
#if UNITY_IOS
            return UnityEngine.iOS.Device.systemVersion;
#else
            return null;
#endif
        }

        private static async Awaitable<string> GetTextAsync(string url)
        {
            using var request = UnityWebRequest.Get(url);
            request.timeout = LOOKUP_TIMEOUT_SECONDS;

            await request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                throw new InvalidOperationException($"App Store 조회 실패: {request.error}");
            }

            return request.downloadHandler.text;
        }
    }
}
