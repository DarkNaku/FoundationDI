using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// 스프라이트의 종횡비를 유지한 채 부모(예: 화면 전체 캔버스/패널) 영역을 가득 채우도록
/// RectTransform 크기를 자동 조정한다(CSS의 object-fit: cover와 동일).
/// Unity 기본 Image의 Preserve Aspect는 '맞춤(fit)'만 되어 여백이 생기지만, 이 컴포넌트는
/// 넘치게 채우고(잘림) 종횡비는 왜곡 없이 보존한다. Fit 모드는 반대로 안에 맞춘다(레터박스).
///
/// Fit에서 남는 여백은 기본적으로 양쪽에 균등하게 나뉘지만, FitAlignment로 9분할 기준 위치를
/// 지정하면 한쪽으로 몰 수 있다(CSS의 object-position에 해당).
///
/// 넘치는 영역의 잘림은 부모의 RectMask2D/Mask 또는 화면 경계가 처리한다.
/// rect가 스프라이트 종횡비에 정확히 맞춰지므로 Image의 왜곡이 없어 Preserve Aspect가 보존된다.
/// </summary>
[ExecuteAlways]
[RequireComponent(typeof(RectTransform))]
[DisallowMultipleComponent]
[AddComponentMenu("FoundationDI/Aspect Fill Image")]
public sealed class AspectFillImage : UIBehaviour, ILayoutSelfController
{
    public enum FitMode
    {
        /// <summary>부모를 가득 채우고 넘치는 부분은 잘린다(화면 꽉 채움).</summary>
        Cover,

        /// <summary>부모 안에 종횡비를 유지해 맞추고 남는 공간은 여백이 된다(레터박스).</summary>
        Fit,
    }

    [SerializeField] private FitMode _mode = FitMode.Cover;

    [Tooltip("종횡비를 읽어올 Image. 비우면 같은 GameObject의 Image를 사용한다.")]
    [SerializeField] private Image _image;

    [Tooltip("Image/스프라이트가 없을 때 사용할 수동 종횡비(가로÷세로). 0 이하이면 무시.")]
    [SerializeField] private float _manualAspect;

    [Tooltip("Fit에서 남는 여백을 어느 쪽으로 몰지. Cover는 여백이 없어 무시된다.")]
    [SerializeField] private TextAnchor _fitAlignment = TextAnchor.MiddleCenter;

    [System.NonSerialized] private RectTransform _rect;
    private RectTransform Rect => _rect != null ? _rect : (_rect = (RectTransform)transform);

    private DrivenRectTransformTracker _tracker;

    /// <summary>채움 방식(Cover=가득 채워 잘림 / Fit=안에 맞춤).</summary>
    public FitMode Mode
    {
        get => _mode;
        set { if (_mode != value) { _mode = value; Apply(); } }
    }

    /// <summary>Fit에서 여백을 몰 기준 위치(9분할). Cover에서는 아무 효과가 없다.</summary>
    public TextAnchor FitAlignment
    {
        get => _fitAlignment;
        set { if (_fitAlignment != value) { _fitAlignment = value; Apply(); } }
    }

    protected override void OnEnable()
    {
        base.OnEnable();
        Apply();
    }

    protected override void OnDisable()
    {
        _tracker.Clear();
        base.OnDisable();
    }

    // 자신 또는 부모의 크기가 바뀌면(해상도/레이아웃 변화) 다시 계산한다.
    protected override void OnRectTransformDimensionsChange() => Apply();
    protected override void OnTransformParentChanged() => Apply();
    protected override void OnDidApplyAnimationProperties() => Apply();

#if UNITY_EDITOR
    protected override void OnValidate() => Apply();
#endif

    // ILayoutSelfController: 부모 레이아웃 재빌드 시에도 크기를 유지한다.
    public void SetLayoutHorizontal() => Apply();
    public void SetLayoutVertical() { }

    /// <summary>스프라이트를 런타임에 교체하는 등 수동 재계산이 필요할 때 호출한다.</summary>
    public void Refresh() => Apply();

    private float SourceAspect()
    {
        var img = _image != null ? _image : GetComponent<Image>();
        var sprite = img != null ? img.sprite : null;
        if (sprite != null)
        {
            var r = sprite.rect;
            if (r.height > 0f) return r.width / r.height;
        }
        return _manualAspect > 0f ? _manualAspect : 0f;
    }

    private void Apply()
    {
        if (!IsActive()) return;

        var parent = Rect.parent as RectTransform;
        if (parent == null) return;

        float srcAspect = SourceAspect();
        Vector2 area = parent.rect.size;
        if (srcAspect <= 0f || area.x <= 0f || area.y <= 0f) return;

        _tracker.Clear();
        _tracker.Add(this, Rect,
            DrivenTransformProperties.Anchors |
            DrivenTransformProperties.AnchoredPosition |
            DrivenTransformProperties.Pivot |
            DrivenTransformProperties.SizeDelta);

        // 부모에 꽉 채우는 스트레치 앵커 + 중앙 정렬. 넘침/여백은 sizeDelta로 표현되어
        // 부모 크기 변화(해상도 대응)에 자동으로 반응한다.
        Rect.anchorMin = Vector2.zero;
        Rect.anchorMax = Vector2.one;
        Rect.pivot = new Vector2(0.5f, 0.5f);
        Rect.anchoredPosition = Vector2.zero;

        float areaAspect = area.x / area.y;

        // Cover: 부모를 덮으려면 더 큰 배율(넘침) — 부모가 더 넓으면 가로에 맞추고 세로가 넘친다.
        // Fit: 반대로 더 작은 배율(여백).
        bool widthDriven = _mode == FitMode.Cover ? areaAspect > srcAspect : areaAspect < srcAspect;

        // sizeDelta는 부모 크기 대비 추가 크기. 한 축은 부모에 맞추고(0) 다른 축이 넘치거나 줄어든다.
        Rect.sizeDelta = widthDriven
            ? new Vector2(0f, area.x / srcAspect - area.y)
            : new Vector2(area.y * srcAspect - area.x, 0f);

        // Cover는 넘치는 쪽을 잘라내므로 몰아 줄 여백 자체가 없다. 중앙(=0)을 유지해야
        // 잘림이 양쪽으로 균등하게 일어난다.
        if (_mode == FitMode.Fit) Rect.anchoredPosition = FitOffset(Rect.sizeDelta);
    }

    /// <summary>
    /// Fit에서 sizeDelta는 "부모보다 얼마나 작은가"라 음수이고, 그 절댓값이 곧 남는 여백이다.
    /// 중앙(0.5)에서 기준 위치만큼 벗어난 비율을 여백에 곱하면 이동량이 나온다.
    /// 꽉 찬 축은 sizeDelta가 0이라 기준 위치와 무관하게 0이 된다.
    /// </summary>
    private Vector2 FitOffset(Vector2 sizeDelta)
    {
        Vector2 anchor = Normalize(_fitAlignment);
        return new Vector2((0.5f - anchor.x) * sizeDelta.x, (0.5f - anchor.y) * sizeDelta.y);
    }

    // (0,0)=좌하 ~ (1,1)=우상. 값 순서에 기대지 않고 명시적으로 옮긴다.
    private static Vector2 Normalize(TextAnchor anchor)
    {
        switch (anchor)
        {
            case TextAnchor.UpperLeft: return new Vector2(0f, 1f);
            case TextAnchor.UpperCenter: return new Vector2(0.5f, 1f);
            case TextAnchor.UpperRight: return new Vector2(1f, 1f);
            case TextAnchor.MiddleLeft: return new Vector2(0f, 0.5f);
            case TextAnchor.MiddleRight: return new Vector2(1f, 0.5f);
            case TextAnchor.LowerLeft: return new Vector2(0f, 0f);
            case TextAnchor.LowerCenter: return new Vector2(0.5f, 0f);
            case TextAnchor.LowerRight: return new Vector2(1f, 0f);
            default: return new Vector2(0.5f, 0.5f);
        }
    }
}
