using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace DarkNaku.FoundationDI
{
    public interface IUITransition
    {
        UniTask ShowAsync(RectTransform target, CancellationToken ct);
        UniTask HideAsync(RectTransform target, CancellationToken ct);
    }
}
