using UnityEngine;
using UnityEngine.UI;

namespace DarkNaku.FoundationDI
{
    /// <summary>
    /// 타깃만 남기고 화면을 어둡게 덮고, 바깥 클릭을 막는다.
    /// 셰이더/스텐실 없이 구멍 이미지 1장 + 상하좌우 딤 패널 4장으로 만든다.
    /// 딤 패널이 raycastTarget을 켜고 있어 입력 차단이 부수효과로 따라온다.
    ///
    /// 자기 root Canvas를 sortingOrder 높게 들고 있으므로 UIService의 UIRoot(DontDestroyOnLoad)
    /// 위에 그려진다 — ScreenSpaceOverlay 캔버스는 하이어라키가 아니라 sortingOrder로 전역 정렬된다.
    /// </summary>
    [RequireComponent(typeof(Canvas))]
    public sealed class HighlightModule : TutorialModuleBehaviour
    {
        private const int PanelTop = 0;
        private const int PanelBottom = 1;
        private const int PanelLeft = 2;
        private const int PanelRight = 3;

        [SerializeField] private Canvas _canvas;
        [SerializeField] private RectTransform _hole;

        [Tooltip("위 / 아래 / 왼쪽 / 오른쪽 순서로 넣는다.")]
        [SerializeField] private Image[] _dimPanels = new Image[4];

        [SerializeField] private Vector2 _padding = new(16f, 16f);
        [SerializeField] private int _sortingOrder = 32000;
        [SerializeField] private bool _blockOutsideClick = true;

        private void Awake()
        {
            if (_canvas == null) _canvas = GetComponent<Canvas>();

            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.overrideSorting = true;
            _canvas.sortingOrder = _sortingOrder;

            foreach (var panel in _dimPanels)
            {
                if (panel != null) panel.raycastTarget = _blockOutsideClick;
            }
        }

        protected override void OnTrack(Rect screenRect)
        {
            var rect = new Rect(screenRect.x - _padding.x,
                                screenRect.y - _padding.y,
                                screenRect.width + _padding.x * 2f,
                                screenRect.height + _padding.y * 2f);

            SetPanelsVisible(true);

            if (_hole != null)
            {
                _hole.gameObject.SetActive(true);
                _hole.position = new Vector3(rect.center.x, rect.center.y, 0f);
                _hole.sizeDelta = rect.size;
            }

            var width = Screen.width;
            var height = Screen.height;

            SetPanel(PanelTop, new Rect(0f, rect.yMax, width, height - rect.yMax));
            SetPanel(PanelBottom, new Rect(0f, 0f, width, rect.yMin));
            SetPanel(PanelLeft, new Rect(0f, rect.yMin, rect.xMin, rect.height));
            SetPanel(PanelRight, new Rect(rect.xMax, rect.yMin, width - rect.xMax, rect.height));
        }

        protected override void OnTargetLost()
        {
            if (_hole != null) _hole.gameObject.SetActive(false);

            SetPanelsVisible(false);
        }

        private void SetPanel(int index, Rect rect)
        {
            if (index >= _dimPanels.Length) return;

            var panel = _dimPanels[index];

            if (panel == null) return;

            var rectTransform = panel.rectTransform;

            rectTransform.position = new Vector3(rect.center.x, rect.center.y, 0f);
            rectTransform.sizeDelta = new Vector2(Mathf.Max(0f, rect.width),
                                                  Mathf.Max(0f, rect.height));
        }

        private void SetPanelsVisible(bool visible)
        {
            foreach (var panel in _dimPanels)
            {
                if (panel != null) panel.enabled = visible;
            }
        }
    }
}
