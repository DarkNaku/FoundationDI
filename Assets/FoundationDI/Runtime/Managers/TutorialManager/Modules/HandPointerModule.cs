using UnityEngine;
using UnityEngine.UI;

namespace DarkNaku.FoundationDI
{
    /// <summary>
    /// 타깃 위에 손가락을 띄우고 탭 애니메이션을 반복한다.
    /// 트윈 라이브러리에 의존하지 않고 AnimationCurve로 보간한다
    /// (UIService의 기본 트랜지션 3종과 같은 방식).
    /// </summary>
    [RequireComponent(typeof(Canvas))]
    public sealed class HandPointerModule : TutorialModuleBehaviour
    {
        [SerializeField] private Canvas _canvas;
        [SerializeField] private RectTransform _hand;
        [SerializeField] private Vector2 _offset = new(0f, -40f);
        [SerializeField] private float _period = 1f;

        [SerializeField] private AnimationCurve _scale =
            new(new Keyframe(0f, 1f), new Keyframe(0.5f, 0.8f), new Keyframe(1f, 1f));

        [SerializeField] private int _sortingOrder = 32001;

        private float _elapsed;

        private void Awake()
        {
            if (_canvas == null) _canvas = GetComponent<Canvas>();

            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.overrideSorting = true;
            _canvas.sortingOrder = _sortingOrder;

            // 손가락은 "이걸 누르세요"라고 가리키는 물건이라 절대 그 버튼의 클릭을 먹으면 안 된다.
            // 이 캔버스에 GraphicRaycaster를 붙이지 않는 게 1차 방어지만,
            // 프리팹에 실수로 붙어도 안전하도록 그래픽 자체를 레이캐스트 대상에서 뺀다.
            if (_hand == null) return;

            foreach (var graphic in _hand.GetComponentsInChildren<Graphic>(true))
            {
                graphic.raycastTarget = false;
            }
        }

        protected override void OnTrack(Rect screenRect)
        {
            if (_hand == null) return;

            _hand.gameObject.SetActive(true);
            _hand.position = new Vector3(screenRect.center.x + _offset.x,
                                         screenRect.center.y + _offset.y, 0f);

            if (_period <= 0f) return;

            // 튜토리얼은 게임이 멈춘 동안에도 떠 있을 수 있으므로 unscaled를 쓴다.
            _elapsed = (_elapsed + Time.unscaledDeltaTime) % _period;

            var scale = _scale.Evaluate(_elapsed / _period);

            _hand.localScale = new Vector3(scale, scale, 1f);
        }

        protected override void OnTargetLost()
        {
            if (_hand != null) _hand.gameObject.SetActive(false);
        }
    }
}
