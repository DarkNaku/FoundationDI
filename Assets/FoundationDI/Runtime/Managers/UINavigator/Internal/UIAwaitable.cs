using UnityEngine;

namespace DarkNaku.FoundationDI
{
    internal static class UIAwaitable
    {
        // 즉시 완료된 Awaitable(단일 사용). 매 호출마다 새 소스를 만든다.
        public static Awaitable Completed()
        {
            var source = new AwaitableCompletionSource();
            source.SetResult();
            return source.Awaitable;
        }
    }
}
