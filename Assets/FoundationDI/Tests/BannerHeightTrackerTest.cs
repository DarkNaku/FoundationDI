using System.Collections.Generic;
using DarkNaku.FoundationDI;
using NUnit.Framework;

public class BannerHeightTrackerTest
{
    private class FakeMetrics : IBannerViewMetrics
    {
        public bool CanMeasureView { get; set; } = true;
        public float? ViewHeight;
        public float Density = 2.8125f;

        public bool TryGetViewHeight(out float pixels)
        {
            pixels = ViewHeight ?? 0f;
            return ViewHeight.HasValue;
        }

        public float DpToPixels(float dp) => dp * Density;
    }

    private FakeMetrics _metrics;
    private FakeAdDispatcher _dispatcher;
    private BannerHeightTracker _sut;
    private List<float> _changes;

    [SetUp]
    public void SetUp()
    {
        _metrics = new FakeMetrics();
        _dispatcher = new FakeAdDispatcher();
        _sut = new BannerHeightTracker(_metrics, _dispatcher);
        _changes = new List<float>();
        _sut.Changed += h => _changes.Add(h);
    }

    [TearDown]
    public void TearDown() => _sut.Dispose();

    [Test]
    public void 뷰_높이를_잴_수_있으면_요청_크기_대신_잰_높이를_알린다()
    {
        // 적응형 배너는 요청 크기로 컨테이너 최대 높이(120dp)를 알려 주지만 실제 소재는 50dp(141px)다.
        _metrics.ViewHeight = 141f;

        _sut.Refresh(120f, alwaysNotify: false);

        Assert.AreEqual(141f, _sut.Height, 0.001f);
        CollectionAssert.AreEqual(new[] { 141f }, _changes);
    }

    [Test]
    public void 뷰_높이를_잴_수_없는_플랫폼이면_요청_크기를_화면_밀도로_환산해_즉시_알린다()
    {
        _metrics.CanMeasureView = false;

        _sut.Refresh(50f, alwaysNotify: false);

        Assert.AreEqual(50f * 2.8125f, _sut.Height, 0.001f);
        Assert.AreEqual(0, _dispatcher.PendingCount, "잴 수 없으면 다시 잴 이유가 없다");
    }

    [Test]
    public void 레이아웃_전이라_아직_잴_수_없으면_다시_잰다()
    {
        _sut.Refresh(120f, alwaysNotify: false);

        Assert.IsEmpty(_changes, "요청 크기(컨테이너 높이)를 먼저 알리면 UI가 튀었다가 내려온다");

        _metrics.ViewHeight = 141f;
        _dispatcher.Advance(BannerHeightTracker.RemeasureDelays[0]);

        CollectionAssert.AreEqual(new[] { 141f }, _changes);
    }

    [Test]
    public void 끝내_잴_수_없으면_요청_크기로_떨어진다()
    {
        _sut.Refresh(50f, alwaysNotify: false);

        foreach (var delay in BannerHeightTracker.RemeasureDelays)
        {
            _dispatcher.Advance(delay);
        }

        CollectionAssert.AreEqual(new[] { 50f * 2.8125f }, _changes);
        Assert.AreEqual(0, _dispatcher.PendingCount);
    }

    [Test]
    public void 다시_표시할_때는_높이가_같아도_알린다()
    {
        _metrics.ViewHeight = 141f;

        _sut.Refresh(50f, alwaysNotify: false);
        _sut.Refresh(50f, alwaysNotify: false);
        _sut.Refresh(50f, alwaysNotify: true);

        CollectionAssert.AreEqual(new[] { 141f, 141f }, _changes);
    }

    [Test]
    public void 다시_표시할_때_아직_잴_수_없으면_이전_높이를_먼저_알린다()
    {
        // 숨겼다 다시 띄우면 소비자는 0을 받은 상태다. 레이아웃을 기다리는 동안 0으로 두지 않는다.
        _metrics.ViewHeight = 141f;
        _sut.Refresh(50f, alwaysNotify: false);
        _metrics.ViewHeight = null;

        _sut.Refresh(50f, alwaysNotify: true);

        CollectionAssert.AreEqual(new[] { 141f, 141f }, _changes);
    }

    [Test]
    public void 취소하면_대기_중인_재측정을_버린다()
    {
        _sut.Refresh(120f, alwaysNotify: false);

        _sut.Cancel();
        _metrics.ViewHeight = 141f;
        _dispatcher.Advance(10f);

        Assert.IsEmpty(_changes);
        Assert.AreEqual(0, _dispatcher.PendingCount);
    }

    [Test]
    public void 크기를_모르면_이전_높이를_유지한다()
    {
        _metrics.CanMeasureView = false;
        _sut.Refresh(50f, alwaysNotify: false);

        _sut.Refresh(null, alwaysNotify: false);

        Assert.AreEqual(50f * 2.8125f, _sut.Height, 0.001f);
        Assert.AreEqual(1, _changes.Count);
    }
}
