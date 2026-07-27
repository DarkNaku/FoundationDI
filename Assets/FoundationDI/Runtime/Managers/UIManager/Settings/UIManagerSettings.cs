using UnityEngine;

namespace DarkNaku.FoundationDI
{
    [CreateAssetMenu(fileName = "UIManagerSettings", menuName = "DarkNaku/UIManagerSettings")]
    public sealed class UIManagerSettings : ScriptableObject
    {
        [SerializeField] private Vector2 _referenceResolution = new Vector2(1920f, 1080f);

        [Tooltip("UI Canvas가 얹힐 Sorting Layer 이름. ScreenSpaceCamera일 때 월드 스프라이트와의 정렬에 사용.")]
        [SerializeField] private string _sortingLayerName = "Default";

        [Tooltip("같은 Sorting Layer 내 정렬 순서.")]
        [SerializeField] private int _sortingOrder = 0;

        [Tooltip("ScreenSpaceCamera에서 카메라로부터 UI 평면까지의 거리.")]
        [SerializeField] private float _planeDistance = 100f;

        // CanvasScaler(Scale With Screen Size, Expand)의 기준 해상도
        public Vector2 ReferenceResolution => _referenceResolution;

        public string SortingLayerName => _sortingLayerName;

        public int SortingOrder => _sortingOrder;

        public float PlaneDistance => _planeDistance;
    }
}
