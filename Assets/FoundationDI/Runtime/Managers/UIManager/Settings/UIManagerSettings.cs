using UnityEngine;

namespace DarkNaku.FoundationDI
{
    [CreateAssetMenu(fileName = "UIManagerSettings", menuName = "DarkNaku/UIManagerSettings")]
    public sealed class UIManagerSettings : ScriptableObject
    {
        [SerializeField] private Vector2 _referenceResolution = new Vector2(1920f, 1080f);

        // CanvasScaler(Scale With Screen Size, Expand)의 기준 해상도
        public Vector2 ReferenceResolution => _referenceResolution;
    }
}
