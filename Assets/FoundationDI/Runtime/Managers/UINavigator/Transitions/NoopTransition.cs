using System.Threading;
using UnityEngine;

namespace DarkNaku.FoundationDI
{
    public sealed class NoopTransition : IUITransition
    {
        public Awaitable ShowAsync(RectTransform target, CancellationToken ct) => UIAwaitable.Completed();
        public Awaitable HideAsync(RectTransform target, CancellationToken ct) => UIAwaitable.Completed();
    }
}
