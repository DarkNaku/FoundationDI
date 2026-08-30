using UnityEngine;

namespace FoundationDI.Host
{
    /// 여러 패널을 가로로 나란히 두고 인덱스 사이를 coordinated 슬라이드로 이동하는 pager.
    /// 두 패널이 붙어서 함께 미끄러지며, 방향은 현재 인덱스 대비 목표 인덱스로 자동 결정된다.
    /// (탭 전환은 이 컴포넌트가 처리하므로 UINavigator 페이지 전환과 무관하다.)
    public sealed class TabPager : MonoBehaviour
    {
        [SerializeField] private RectTransform _content; // 패널들을 자식으로 갖는 컨테이너
        [SerializeField] private float _duration = 0.3f;
        [SerializeField] private AnimationCurve _ease = AnimationCurve.EaseInOut(0, 0, 1, 1);

        private int _index;
        private float _width;
        private bool _animating;

        public int Index => _index;

        private RectTransform Viewport => (RectTransform)transform;

        // 뷰포트 실제 폭 기준으로 패널을 가로 배치하고 컨텐츠 폭/위치를 갱신한다(표시 직전 호출).
        public void Relayout()
        {
            Canvas.ForceUpdateCanvases();
            _width = Viewport.rect.width;

            int n = _content.childCount;
            for (int i = 0; i < n; i++)
            {
                var p = (RectTransform)_content.GetChild(i);
                p.anchorMin = new Vector2(0f, 0f);
                p.anchorMax = new Vector2(0f, 1f);
                p.pivot = new Vector2(0f, 0.5f);
                p.sizeDelta = new Vector2(_width, 0f);
                p.anchoredPosition = new Vector2(i * _width, 0f);
            }

            _content.anchorMin = new Vector2(0f, 0f);
            _content.anchorMax = new Vector2(0f, 1f);
            _content.pivot = new Vector2(0f, 0.5f);
            _content.sizeDelta = new Vector2(_width * n, 0f);
            _content.anchoredPosition = new Vector2(-_index * _width, 0f);
        }

        public void SetImmediate(int index)
        {
            _index = Mathf.Clamp(index, 0, Mathf.Max(0, _content.childCount - 1));
        }

        public async void GoTo(int index)
        {
            index = Mathf.Clamp(index, 0, Mathf.Max(0, _content.childCount - 1));
            if (_animating || index == _index) return;

            _animating = true;
            try
            {
                float from = _content.anchoredPosition.x;
                float to = -index * _width;
                float t = 0f;

                while (t < _duration)
                {
                    t += Time.unscaledDeltaTime;
                    float k = _ease.Evaluate(Mathf.Clamp01(t / _duration));
                    var pos = _content.anchoredPosition;
                    pos.x = Mathf.LerpUnclamped(from, to, k);
                    _content.anchoredPosition = pos;
                    await Awaitable.NextFrameAsync();
                }

                var fin = _content.anchoredPosition;
                fin.x = to;
                _content.anchoredPosition = fin;
                _index = index;
            }
            finally
            {
                _animating = false;
            }
        }
    }
}
