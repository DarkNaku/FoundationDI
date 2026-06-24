using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace DarkNaku.FoundationDI
{
    public interface IUITransition
    {
        UniTask PlayShow(RectTransform target, CancellationToken ct);
        UniTask PlayHide(RectTransform target, CancellationToken ct);
    }
}
