using System;
using UnityEngine;
using UnityEngine.UI;

namespace DarkNaku.FoundationDI
{
    // 자립형 가짜 광고 화면. UIService에 의존하지 않는다 —
    // ADService가 UI 시스템에 묶이면 "어떤 네트워크든 동일"이라는 목표와 무관한 결합이 생긴다.
    public class DummyAdCanvas : IDummyAdScreen
    {
        private const int SORTING_ORDER = 32767;   // 항상 최상단

        private GameObject _root;
        private GameObject _fullScreenPanel;
        private GameObject _bannerPanel;
        private Text _label;
        private Text _countdown;
        private Button _closeButton;

        private float _remaining;
        private Action _onSkip;
        private Action _onComplete;
        private DummyAdTicker _ticker;

        public void ShowFullScreen(AdFormat format, float duration, Action onSkip, Action onComplete)
        {
            EnsureRoot();

            _onSkip = onSkip;
            _onComplete = onComplete;
            _remaining = duration;

            _label.text = $"{format}\n(Dummy Ad)";
            _fullScreenPanel.SetActive(true);
            UpdateCountdown();
        }

        public void ShowBanner(BannerPosition position, float height)
        {
            EnsureRoot();

            var rect = _bannerPanel.GetComponent<RectTransform>();
            var top = position == BannerPosition.Top;
            rect.anchorMin = new Vector2(0f, top ? 1f : 0f);
            rect.anchorMax = new Vector2(1f, top ? 1f : 0f);
            rect.pivot = new Vector2(0.5f, top ? 1f : 0f);
            rect.sizeDelta = new Vector2(0f, height);
            rect.anchoredPosition = Vector2.zero;

            _bannerPanel.SetActive(true);
        }

        public void HideBanner()
        {
            if (_bannerPanel != null) _bannerPanel.SetActive(false);
        }

        // 매 프레임 카운트다운을 갱신한다. 전면광고 중에는 timeScale이 0인 경우가 많아
        // unscaledDeltaTime을 쓴다.
        private void Tick()
        {
            if (_fullScreenPanel == null || !_fullScreenPanel.activeSelf) return;

            _remaining -= Time.unscaledDeltaTime;
            UpdateCountdown();

            if (_remaining > 0f) return;

            _fullScreenPanel.SetActive(false);
            var complete = _onComplete;
            _onSkip = null;
            _onComplete = null;
            complete?.Invoke();
        }

        private void UpdateCountdown()
        {
            var canClose = _remaining <= 0f;
            _countdown.text = canClose ? "" : $"{Mathf.CeilToInt(_remaining)}";
            _closeButton.gameObject.SetActive(canClose);
        }

        private void OnCloseClicked()
        {
            _fullScreenPanel.SetActive(false);
            var skip = _onSkip;
            _onSkip = null;
            _onComplete = null;
            skip?.Invoke();
        }

        private void EnsureRoot()
        {
            if (_root != null) return;

            _root = new GameObject("[AdService] Dummy Canvas") { hideFlags = HideFlags.HideAndDontSave };
            UnityEngine.Object.DontDestroyOnLoad(_root);

            var canvas = _root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = SORTING_ORDER;
            _root.AddComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            _root.AddComponent<GraphicRaycaster>();

            _ticker = _root.AddComponent<DummyAdTicker>();
            _ticker.OnTick = Tick;

            _fullScreenPanel = CreatePanel("FullScreen", new Color(0f, 0f, 0f, 0.85f), stretch: true);
            _label = CreateText(_fullScreenPanel.transform, "Label", 48, new Vector2(0f, 60f));
            _countdown = CreateText(_fullScreenPanel.transform, "Countdown", 36, new Vector2(0f, -20f));

            _closeButton = CreateCloseButton(_fullScreenPanel.transform);
            _closeButton.onClick.AddListener(OnCloseClicked);
            _fullScreenPanel.SetActive(false);

            _bannerPanel = CreatePanel("Banner", new Color(0.1f, 0.4f, 0.8f, 0.9f), stretch: false);
            CreateText(_bannerPanel.transform, "BannerLabel", 24, Vector2.zero).text = "Dummy Banner";
            _bannerPanel.SetActive(false);
        }

        private GameObject CreatePanel(string name, Color color, bool stretch)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(_root.transform, false);
            go.GetComponent<Image>().color = color;

            var rect = go.GetComponent<RectTransform>();
            if (stretch)
            {
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.one;
                rect.sizeDelta = Vector2.zero;
                rect.anchoredPosition = Vector2.zero;
            }

            return go;
        }

        private static Text CreateText(Transform parent, string name, int size, Vector2 offset)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);

            var text = go.GetComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = size;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = Color.white;

            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(800f, 120f);
            rect.anchoredPosition = offset;

            return text;
        }

        private static Button CreateCloseButton(Transform parent)
        {
            var go = new GameObject("Close", typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            go.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0.25f);

            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(1f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(1f, 1f);
            rect.sizeDelta = new Vector2(120f, 80f);
            rect.anchoredPosition = new Vector2(-24f, -24f);

            CreateText(go.transform, "X", 36, Vector2.zero).text = "X";

            return go.GetComponent<Button>();
        }

        public void Dispose()
        {
            if (_root == null) return;

            if (_ticker != null) _ticker.OnTick = null;

            if (Application.isPlaying) UnityEngine.Object.Destroy(_root);
            else UnityEngine.Object.DestroyImmediate(_root);

            _root = null;
        }

        // Canvas에 붙어 매 프레임 콜백만 흘려주는 최소 MonoBehaviour.
        private class DummyAdTicker : MonoBehaviour
        {
            public Action OnTick;
            private void Update() => OnTick?.Invoke();
        }
    }
}
