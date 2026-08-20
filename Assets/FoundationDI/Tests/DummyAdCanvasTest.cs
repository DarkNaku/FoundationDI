using DarkNaku.FoundationDI;
using NUnit.Framework;

// DummyAdCanvas는 uGUI를 직접 구성하는 클래스라 렌더링 자체는 EditMode에서 검증할 가치가
// 없다(아무도 화면을 기대하지 않는다). 대신 이 클래스가 실제로 버그를 냈던 지점 —
// 콜백 소유권 북키핑(누가 onSkip/onComplete를 쥐고 있는지, 재진입 시 무슨 일이 일어나는지,
// Dispose 후 발화가 멈추는지) — 을 internal 테스트 시드(Tick/OnActionButtonClicked)로 검증한다.
public class DummyAdCanvasTest
{
    private DummyAdCanvas _sut;

    [TearDown]
    public void TearDown()
    {
        _sut?.Dispose();
        _sut = null;
    }

    [Test]
    public void 표시_중에_다시_ShowFullScreen을_호출하면_이전_광고의_onSkip이_호출된다()
    {
        // DummyAdProvider는 전면/보상 두 어댑터가 이 캔버스 하나를 공유한다. 이전 광고가
        // 아직 떠 있는 채로 새 ShowFullScreen이 오면, 이전 소유자는 Closed를 받아야 한다 —
        // 못 받으면 그 어댑터의 _showCompletion이 영원히 안 비고 다음 ShowAsync가 전부 막힌다.
        _sut = new DummyAdCanvas();

        var firstSkipped = 0;
        var firstCompleted = 0;
        _sut.ShowFullScreen(AdFormat.Interstitial, 5f, () => firstSkipped++, () => firstCompleted++);

        var secondSkipped = 0;
        var secondCompleted = 0;
        _sut.ShowFullScreen(AdFormat.Rewarded, 5f, () => secondSkipped++, () => secondCompleted++);

        Assert.AreEqual(1, firstSkipped, "재진입 시 이전 광고의 onSkip이 호출되지 않았다");
        Assert.AreEqual(0, firstCompleted, "재진입은 완료가 아니라 중단이어야 한다");
        Assert.AreEqual(0, secondSkipped, "새 광고의 콜백이 잘못 호출됐다");
        Assert.AreEqual(0, secondCompleted);
    }

    [Test]
    public void 표시_중에_다시_ShowFullScreen을_호출해도_이전_콜백은_다시_발화하지_않는다()
    {
        // 재진입으로 흘려보낸 이전 onSkip이 이후에도 남아 있으면 안 된다 — 새 광고가
        // 끝났을 때 옛 콜백까지 같이 발화하면 이중 완료가 된다.
        _sut = new DummyAdCanvas();

        var firstSkipped = 0;
        _sut.ShowFullScreen(AdFormat.Interstitial, 5f, () => firstSkipped++, () => { });
        Assert.AreEqual(0, firstSkipped);

        var secondCompleted = 0;
        _sut.ShowFullScreen(AdFormat.Interstitial, 0.01f, () => { }, () => secondCompleted++);
        Assert.AreEqual(1, firstSkipped, "재진입 시점에 이전 onSkip이 정확히 한 번 호출돼야 한다");

        // 새 광고를 카운트다운 종료까지 틱하고 닫기 버튼을 눌러 완료시킨다.
        _sut.Tick(1f);   // 실제 프레임 대기 없이 1초치를 손으로 흘려보낸다
        Assert.IsTrue(_sut.IsActionButtonVisible, "카운트다운 종료 후 버튼이 보이지 않는다");
        _sut.OnActionButtonClicked();

        Assert.AreEqual(1, firstSkipped, "이전 광고의 onSkip이 다시 발화했다");
        Assert.AreEqual(1, secondCompleted);
    }

    [Test]
    public void 인터스티셜은_카운트다운이_끝나도_자동으로_닫히지_않고_클릭을_기다린다()
    {
        _sut = new DummyAdCanvas();

        var completed = 0;
        _sut.ShowFullScreen(AdFormat.Interstitial, 0.01f, () => { }, () => completed++);

        _sut.Tick(1f);   // 실제 프레임 대기 없이 1초치를 손으로 흘려보낸다   // unscaledDeltaTime만큼 진행 — 0.01초 듀레이션이므로 카운트다운 종료

        Assert.AreEqual(0, completed, "인터스티셜이 클릭 없이 자동으로 완료됐다");
        Assert.IsTrue(_sut.IsFullScreenActive, "인터스티셜 패널이 클릭 전에 닫혔다");
        Assert.IsTrue(_sut.IsActionButtonVisible, "카운트다운 종료 후 닫기 버튼이 보이지 않는다");

        _sut.OnActionButtonClicked();

        Assert.AreEqual(1, completed, "닫기 버튼 클릭이 onComplete를 호출하지 않았다");
        Assert.IsFalse(_sut.IsFullScreenActive);
    }

    [Test]
    public void 인터스티셜은_카운트다운_중에는_버튼이_보이지_않아_스킵할_수_없다()
    {
        _sut = new DummyAdCanvas();

        var skipped = 0;
        var completed = 0;
        _sut.ShowFullScreen(AdFormat.Interstitial, 100f, () => skipped++, () => completed++);

        Assert.IsFalse(_sut.IsActionButtonVisible, "카운트다운 중인데 버튼이 보인다");

        _sut.Tick(1f);   // 실제 프레임 대기 없이 1초치를 손으로 흘려보낸다   // 듀레이션이 충분히 길어 한 틱으로는 끝나지 않는다

        Assert.IsFalse(_sut.IsActionButtonVisible, "카운트다운 중인데 버튼이 보인다");
        Assert.AreEqual(0, skipped);
        Assert.AreEqual(0, completed);
    }

    [Test]
    public void 리워드는_카운트다운_중_스킵_버튼_클릭으로_onSkip이_호출되고_onComplete는_호출되지_않는다()
    {
        _sut = new DummyAdCanvas();

        var skipped = 0;
        var completed = 0;
        _sut.ShowFullScreen(AdFormat.Rewarded, 100f, () => skipped++, () => completed++);

        Assert.IsTrue(_sut.IsActionButtonVisible, "리워드는 카운트다운 중 스킵 버튼이 보여야 한다");

        _sut.OnActionButtonClicked();

        Assert.AreEqual(1, skipped, "중간 스킵인데 onSkip이 호출되지 않았다 — 스펙의 Dismissed 경로");
        Assert.AreEqual(0, completed, "중간 스킵인데 onComplete가 호출됐다");
        Assert.IsFalse(_sut.IsFullScreenActive);
    }

    [Test]
    public void 리워드는_카운트다운을_완주하면_스킵없이_onComplete가_자동_호출된다()
    {
        _sut = new DummyAdCanvas();

        var skipped = 0;
        var completed = 0;
        _sut.ShowFullScreen(AdFormat.Rewarded, 0.01f, () => skipped++, () => completed++);

        _sut.Tick(1f);   // 실제 프레임 대기 없이 1초치를 손으로 흘려보낸다

        Assert.AreEqual(0, skipped);
        Assert.AreEqual(1, completed, "카운트다운을 완주했는데 onComplete가 자동 호출되지 않았다");
        Assert.IsFalse(_sut.IsFullScreenActive);
    }

    [Test]
    public void 리워드는_duration이_0이어도_즉시_onComplete가_호출되고_브릭되지_않는다()
    {
        // Tick()의 완료 전환은 카운트다운이 "지금 막" 0을 통과할 때만 발화한다
        // (이미 0 이하면 early-return). duration<=0으로 시작하면 그 전환을 절대
        // 못 만나므로, 리셋 없이 즉시 완료시키지 않으면 패널이 안 닫히고 onComplete도
        // 안 와서 그 유닛의 _showCompletion이 영구히 안 빈다 — 이 웨이브가 잡으려던
        // 바로 그 부류의 브릭이다.
        _sut = new DummyAdCanvas();

        var skipped = 0;
        var completed = 0;
        _sut.ShowFullScreen(AdFormat.Rewarded, 0f, () => skipped++, () => completed++);

        Assert.AreEqual(0, skipped);
        Assert.AreEqual(1, completed, "duration 0인데 onComplete가 즉시 호출되지 않았다(브릭)");
        Assert.IsFalse(_sut.IsFullScreenActive);

        // Tick을 더 돌려도 다시 발화하면 안 된다(이중 완료 방지 확인).
        _sut.Tick(1f);
        Assert.AreEqual(1, completed);
    }

    [Test]
    public void 리워드는_duration이_음수여도_즉시_onComplete가_호출되고_브릭되지_않는다()
    {
        // 손으로 고친 .asset이나 스크립트 생성값은 [Min]을 우회하므로 음수도 들어올 수
        // 있다 — 0과 같은 취급이어야 한다.
        _sut = new DummyAdCanvas();

        var completed = 0;
        _sut.ShowFullScreen(AdFormat.Rewarded, -5f, () => { }, () => completed++);

        Assert.AreEqual(1, completed, "duration이 음수인데 onComplete가 즉시 호출되지 않았다(브릭)");
        Assert.IsFalse(_sut.IsFullScreenActive);
    }

    [Test]
    public void 인터스티셜은_duration이_0이어도_브릭되지_않고_클릭을_기다린다()
    {
        // 인터스티셜은 애초에 자동완료가 없다 — duration<=0이면 버튼이 처음부터
        // 보이고 클릭을 기다릴 뿐, 브릭되지 않는다는 것을 확인한다.
        _sut = new DummyAdCanvas();

        var completed = 0;
        _sut.ShowFullScreen(AdFormat.Interstitial, 0f, () => { }, () => completed++);

        Assert.AreEqual(0, completed, "인터스티셜이 클릭 없이 즉시 완료됐다");
        Assert.IsTrue(_sut.IsFullScreenActive);
        Assert.IsTrue(_sut.IsActionButtonVisible, "duration 0인데 닫기 버튼이 바로 보이지 않는다");

        _sut.OnActionButtonClicked();

        Assert.AreEqual(1, completed);
    }

    [Test]
    public void Dispose_이후에는_Tick이_아무_콜백도_발화시키지_않는다()
    {
        _sut = new DummyAdCanvas();

        var skipped = 0;
        var completed = 0;
        _sut.ShowFullScreen(AdFormat.Rewarded, 0.01f, () => skipped++, () => completed++);

        _sut.Dispose();

        Assert.DoesNotThrow(() => _sut.Tick(), "Dispose 후 Tick이 예외를 던졌다");
        Assert.AreEqual(0, skipped);
        Assert.AreEqual(0, completed, "Dispose 후에도 Tick이 콜백을 발화시켰다");
    }
}
