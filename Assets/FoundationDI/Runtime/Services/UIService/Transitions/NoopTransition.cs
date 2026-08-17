using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace DarkNaku.FoundationDI
{
    public sealed class NoopTransition : IUITransition
    {
        public UniTask ShowAsync(RectTransform target, CancellationToken ct) => UniTask.CompletedTask;
        public UniTask HideAsync(RectTransform target, CancellationToken ct) => UniTask.CompletedTask;
    }
}
