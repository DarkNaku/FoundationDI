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
        private AdFormat _format;
        private Action _onSkip;
        private Action _onComplete;
        private DummyAdTicker _ticker;

        // 테스트 전용 관찰 지점. uGUI 구성 자체는 EditMode에서 검증할 가치가 없지만
        // (아무도 렌더링을 기대하지 않는다), 콜백 소유권 북키핑은 검증돼야 한다.
        internal bool IsFullScreenActive => _fullScreenPanel != null && _fullScreenPanel.activeSelf;
        internal bool IsActionButtonVisible => _closeButton != null && _closeButton.gameObject.activeSelf;

        public void ShowFullScreen(AdFormat format, float duration, Action onSkip, Action onComplete)
        {
            EnsureRoot();

            if (_fullScreenPanel.activeSelf)
            {
                // 이 캔버스는 전면/보상 두 어댑터가 공유한다(DummyAdProvider). 이전 광고가
                // 아직 떠 있는 채로 새 ShowFullScreen이 오면, 콜백을 그냥 덮어써 버려서는
                // 안 된다 — 이전 소유자는 Closed를 영원히 못 받고, 그 어댑터의 다음
                // ShowAsync가 전부 "이미 표시 중" Failed로 막혀버린다(브릭). 새 쇼가
                // 이전 쇼를 중단시킨 것으로 보고 이전 onSkip을 먼저 흘려보낸다.
                var interrupted = _onSkip;
                _onSkip = null;
                _onComplete = null;
                interrupted?.Invoke();
            }

            _format = format;
            _onSkip = onSkip;
            _onComplete = onComplete;
            _remaining = Mathf.Max(0f, duration);

            _label.text = $"{format}\n(Dummy Ad)";
            _fullScreenPanel.SetActive(true);
            UpdateCountdown();

            // duration<=0인 리워드는 카운트다운이 이미 끝난 채로 시작한다. Tick()의 완료
            // 전환은 "카운트다운이 지금 막 0을 통과했을 때"만 발화하도록 짜여 있어서
            // (early-return: 이미 0 이하면 아무것도 안 함), 처음부터 0이면 그 전환을
            // 영원히 못 만난다 — 패널이 안 닫히고 콜백도 안 오는 채로 그 유닛의
            // _showCompletion이 영구히 안 비는, 이 웨이브가 잡으려던 바로 그 부류의
            // 브릭이다. 인터스티셜은 애초에 자동완료가 없고 항상 클릭을 기다리므로
            // duration<=0이어도 브릭되지 않는다(버튼이 바로 보일 뿐이다) — 그래서
            // 여기서는 리워드만 즉시 완료시킨다.
            if (_remaining <= 0f && _format != AdFormat.Interstitial) CompleteFullScreen();
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
        // unscaledDeltaTime을 쓴다. internal인 이유: DummyAdTicker.Update()가 정상 경로로
        // 부르지만, 테스트는 프레임을 실제로 기다릴 수 없다 — 에디터에서는 재생 중이
        // 아니면 Time.unscaledDeltaTime이 사실상 0에 가까워 실제 프레임을 아무리 돌려도
        // 카운트다운이 끝나지 않는다. FakeAdDispatcher.Advance(seconds)와 같은 방식으로
        // deltaTime을 직접 넣을 수 있는 오버로드를 열어 시간을 손으로 돌린다.
        internal void Tick() => Tick(Time.unscaledDeltaTime);

        internal void Tick(float deltaTime)
        {
            if (_fullScreenPanel == null || !_fullScreenPanel.activeSelf) return;
            if (_remaining <= 0f) return;   // 카운트다운은 끝났고 클릭을 기다리는 중이다

            _remaining -= deltaTime;
            if (_remaining > 0f)
            {
                UpdateCountdown();
                return;
            }

            _remaining = 0f;
            UpdateCountdown();

            // 리워드는 카운트다운을 완주하면 자동으로 보상+닫힘까지 이어진다.
            // 인터스티셜은 여기서 자동으로 닫지 않는다 — 닫기 버튼을 보여주고
            // 클릭을 기다린다(OnActionButtonClicked). 실제 네트워크의 "N초 후 닫기 버튼
            // 노출, 클릭 전엔 안 닫힘" 동작을 흉내낸다.
            if (_format != AdFormat.Interstitial) CompleteFullScreen();
        }

        private void UpdateCountdown()
        {
            var countdownDone = _remaining <= 0f;
            _countdown.text = countdownDone ? "" : $"{Mathf.CeilToInt(_remaining)}";

            // 인터스티셜: 카운트다운 중엔 버튼을 숨기고(스킵 불가), 끝나면 노출해 클릭을 기다린다.
            // 리워드: 카운트다운 내내 스킵 버튼을 보여주고, 끝나면(자동 완료되므로) 함께 숨긴다.
            _closeButton.gameObject.SetActive(_format == AdFormat.Interstitial ? countdownDone : !countdownDone);
        }

        // internal인 이유는 Tick()과 같다 — 버튼 클릭을 실제 uGUI 이벤트 없이 재현한다.
        internal void OnActionButtonClicked()
        {
            // 버튼이 보이는 조건 자체가 포맷별로 반대이므로(위 UpdateCountdown), 눌렸을 때
            // 무엇을 뜻하는지도 반대다: 인터스티셜은 카운트다운 종료 후의 "닫기"만 가능하고,
            // 리워드는 카운트다운 중의 "스킵"만 가능하다.
            if (_format == AdFormat.Interstitial) CompleteFullScreen();
            else SkipFullScreen();
        }

        private void CompleteFullScreen()
        {
            _fullScreenPanel.SetActive(false);
            var complete = _onComplete;
            _onSkip = null;
            _onComplete = null;
            complete?.Invoke();
        }

        private void SkipFullScreen()
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

            // DontDestroyOnLoad는 플레이 모드 밖에서 부르면 예외를 던진다(에디터 스크립트/
            // EditMode 테스트에서 이 클래스를 직접 생성할 때가 그렇다). Dispose()가 이미
            // Application.isPlaying으로 Destroy/DestroyImmediate를 갈라 쓰는 것과 같은 이유로,
            // 여기서도 플레이 모드에서만 부른다 — HideAndDontSave 자체가 이미 씬 저장에서는
            // 제외시키므로 에디터에서 건너뛰어도 안전하다.
            if (Application.isPlaying) UnityEngine.Object.DontDestroyOnLoad(_root);

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
            _closeButton.onClick.AddListener(OnActionButtonClicked);
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
