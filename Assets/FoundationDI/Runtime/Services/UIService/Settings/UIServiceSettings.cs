using UnityEngine;

namespace DarkNaku.FoundationDI
{
    [CreateAssetMenu(fileName = "UIServiceSettings", menuName = "DarkNaku/UIServiceSettings")]
    public sealed class UIServiceSettings : ScriptableObject
    {
        // CanvasScaler(Scale With Screen Size, Expand)의 기준 해상도
        [SerializeField] private Vector2 _referenceResolution = new Vector2(1920f, 1080f);

        public Vector2 ReferenceResolution => _referenceResolution;
    }
}
