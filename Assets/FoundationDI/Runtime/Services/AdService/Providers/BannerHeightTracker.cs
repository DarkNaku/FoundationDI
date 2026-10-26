using System;
using UnityEngine;

namespace DarkNaku.FoundationDI
{
    // 화면에 붙은 네이티브 배너 뷰를 재는 seam. 플랫폼마다 구현이 다르고(Android는 JNI로 뷰를
    // 찾는다) EditMode에서는 네이티브 뷰가 없으므로 분리한다.
    internal interface IBannerViewMetrics
    {
        // 이 플랫폼에서 배너 뷰 높이를 잴 수 있는지. false면 요청 크기 환산만 쓴다.
        bool CanMeasureView { get; }

        // 화면에 보이는 배너 뷰의 현재 높이(화면 픽셀). 레이아웃 전이거나 찾지 못하면 false.
        bool TryGetViewHeight(out float pixels);

        // 배너 크기 단위(Android dp, iOS point)를 화면 픽셀로.
        float DpToPixels(float dp);
    }

    // 배너 높이를 요청 크기(adSize)가 아니라 실제로 그려진 뷰에서 읽는다.
    //
    // 적응형 배너는 요청 크기로 컨테이너 최대 높이를 알려 준다(예: 120dp). 실제로 붙는 소재는
    // 50dp 표준 배너인 경우가 많아, 그 값을 그대로 쓰면 게임 UI가 배너 위로 붕 뜬다.
    // 뷰는 표시·로드 직후 아직 레이아웃 전이라 높이가 0이므로 몇 번 다시 잰다.
    internal sealed class BannerHeightTracker : IDisposable
    {
        // 누적이 아니라 각 시도 사이의 간격이다. 끝까지 못 재면 요청 크기로 떨어진다.
        internal static readonly float[] RemeasureDelays = { 0.1f, 0.3f, 1f };

        private readonly IBannerViewMetrics _metrics;
        private readonly IMainThreadDispatcher _dispatcher;

        private IDisposable _pending;
        private float? _requestedDp;
        private bool _forceNotify;

        public float Height { get; private set; }

        public event Action<float> Changed;

        public BannerHeightTracker(IBannerViewMetrics metrics, IMainThreadDispatcher dispatcher)
        {
            _metrics = metrics;
            _dispatcher = dispatcher;
        }

        // requestedDp가 null이면(SDK가 크기를 모름) 직전 요청 크기를 쓴다.
        // alwaysNotify는 다시 표시할 때 쓴다 — 숨길 때 소비자는 이미 0을 받았으므로
        // 값이 같아도 "0에서 실제값으로" 바뀐 것이다.
        public void Refresh(float? requestedDp, bool alwaysNotify)
        {
            Cancel();

            if (requestedDp.HasValue) _requestedDp = requestedDp;
            if (alwaysNotify) _forceNotify = true;

            Measure(0);
        }

        public void Cancel()
        {
            _pending?.Dispose();
            _pending = null;
        }

        public void Dispose()
        {
            Cancel();
            Height = 0f;
            Changed = null;
        }

        private void Measure(int attempt)
        {
            _pending = null;

            if (_metrics.CanMeasureView && _metrics.TryGetViewHeight(out var measured) && measured > 0f)
            {
                Apply(measured);
                return;
            }

            if (_metrics.CanMeasureView && attempt < RemeasureDelays.Length)
            {
                // 기다리는 동안 0으로 두지 않는다. 같은 배너를 다시 띄운 것이면 직전 높이가 맞을 확률이 높다.
                if (_forceNotify && Height > 0f) Apply(Height);

                _pending = _dispatcher.Delay(RemeasureDelays[attempt], () => Measure(attempt + 1));
                return;
            }

            // 크기를 알 수 없으면 이전 값을 유지한다. 0으로 덮으면 배너가 떠 있는데도
            // 레이아웃이 배너 없는 상태로 되돌아간다.
            if (_requestedDp.HasValue) Apply(_metrics.DpToPixels(_requestedDp.Value));
        }

        private void Apply(float height)
        {
            if (!_forceNotify && Mathf.Approximately(height, Height)) return;

            _forceNotify = false;
            Height = height;
            Changed?.Invoke(height);
        }
    }
}
