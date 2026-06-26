using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace DarkNaku.FoundationDI
{
    [CreateAssetMenu(fileName = "ScaleTransition", menuName = "DarkNaku/UI Transition/Scale")]
    public sealed class ScaleTransitionAsset : UITransitionAsset
    {
        [SerializeField] private float _fromScale = 0.8f;

        public override UniTask ShowAsync(RectTransform target, CancellationToken ct)
            => Animate(t => target.localScale = Vector3.one * Mathf.Lerp(_fromScale, 1f, t), ct);

        public override UniTask HideAsync(RectTransform target, CancellationToken ct)
            => Animate(t => target.localScale = Vector3.one * Mathf.Lerp(1f, _fromScale, t), ct);
    }
}
