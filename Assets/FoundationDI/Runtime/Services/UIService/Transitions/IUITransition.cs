using System.Threading;
using UnityEngine;

namespace DarkNaku.FoundationDI
{
    public interface IUITransition
    {
        Awaitable ShowAsync(RectTransform target, CancellationToken ct);
        Awaitable HideAsync(RectTransform target, CancellationToken ct);
    }
}
