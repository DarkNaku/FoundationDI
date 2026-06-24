using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace DarkNaku.FoundationDI
{
    public sealed class NoopTransition : IUITransition
    {
        public UniTask PlayShow(RectTransform target, CancellationToken ct) => UniTask.CompletedTask;
        public UniTask PlayHide(RectTransform target, CancellationToken ct) => UniTask.CompletedTask;
    }
}
