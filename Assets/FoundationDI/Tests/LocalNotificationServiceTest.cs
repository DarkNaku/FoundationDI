using System;
using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using DarkNaku.FoundationDI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

public class LocalNotificationServiceTest
{
    private class FakeNotifier : ILocalNotifier
    {
        public bool HasPermission { get; set; } = true;
        public int CancelAllCount;
        public int RequestCount;
        public readonly List<LocalNotification> Scheduled = new();
        public AwaitableCompletionSource<bool> Permission = new();

        public Awaitable<bool> RequestPermissionAsync()
        {
            RequestCount++;
            return Permission.Awaitable;
        }

        public void CancelAll()
        {
            CancelAllCount++;
            Scheduled.Clear();
        }

        public void Schedule(LocalNotification notification) => Scheduled.Add(notification);

        public string LastOpenedData;
        public Exception LastOpenedError;
        public int LastOpenedQueryCount;

        public Awaitable<string> GetLastOpenedDataAsync()
        {
            LastOpenedQueryCount++;
            if (LastOpenedError != null) throw LastOpenedError;

            var source = new AwaitableCompletionSource<string>();
            source.SetResult(LastOpenedData);
            return source.Awaitable;
        }
    }

    private class FakeStorage : ILocalNotificationStorage
    {
        public bool IsEnabled { get; set; } = true;
        public bool HasAskedPermission { get; set; }
        public readonly HashSet<string> Opened = new();

        public bool TryMarkOpened(string data) => data != null && Opened.Add(data);
    }

    private class FakeFocus : IApplicationFocusSource
    {
        public event Action<bool> FocusChanged;
        public bool HasSubscribers => FocusChanged != null;
        public void Raise(bool isFocused) => FocusChanged?.Invoke(isFocused);
    }

    private class FakePlanner : ILocalNotificationPlanner
    {
        public readonly List<LocalNotification> Items = new();
        public Exception Error;

        public IEnumerable<LocalNotification> Plan(DateTime now)
        {
            if (Error != null) throw Error;
            return Items;
        }
    }

    private static readonly DateTime Now = new(2026, 10, 26, 9, 0, 0);

    private FakeNotifier _notifier;
    private FakeStorage _storage;
    private FakeFocus _focus;
    private FakePlanner _planner;
    private int _notifierCreated;
    private LocalNotificationService _sut;

    [SetUp]
    public void SetUp()
    {
        _notifier = new FakeNotifier();
        _storage = new FakeStorage();
        _focus = new FakeFocus();
        _planner = new FakePlanner();
        _notifierCreated = 0;
        _sut = Create(_planner);
    }

    [TearDown]
    public void TearDown() => _sut.Dispose();

    private LocalNotificationService Create(params ILocalNotificationPlanner[] planners)
    {
        return new LocalNotificationService(() =>
                                            {
                                                _notifierCreated++;
                                                return _notifier;
                                            },
                                            planners, _storage, _focus, () => Now);
    }

    private static LocalNotification At(int hours, string title = "t") => new(Now.AddHours(hours), title, "b");

    [Test]
    public void Reschedule는_예약을_지운_뒤_모든_planner의_알림을_예약한다()
    {
        var other = new FakePlanner();
        other.Items.Add(At(2, "other"));
        _planner.Items.Add(At(1, "mine"));
        _sut.Dispose();
        _sut = Create(_planner, other);
        _notifier.Scheduled.Add(At(5, "old"));

        var count = _sut.Reschedule();

        Assert.AreEqual(2, count);
        Assert.AreEqual(1, _notifier.CancelAllCount);
        CollectionAssert.AreEquivalent(new[] { "mine", "other" }, _notifier.Scheduled.ConvertAll(n => n.Title));
    }

    [Test]
    public void 권한이_없으면_지우기만_한다()
    {
        _notifier.HasPermission = false;
        _planner.Items.Add(At(1));

        Assert.AreEqual(0, _sut.Reschedule());
        Assert.AreEqual(1, _notifier.CancelAllCount);
        Assert.IsEmpty(_notifier.Scheduled);
    }

    [Test]
    public void 꺼져_있으면_지우기만_한다()
    {
        _storage.IsEnabled = false;
        _planner.Items.Add(At(1));

        Assert.AreEqual(0, _sut.Reschedule());
        Assert.AreEqual(1, _notifier.CancelAllCount);
        Assert.IsEmpty(_notifier.Scheduled);
    }

    [Test]
    public void 지난_시각의_알림은_예약하지_않는다()
    {
        _planner.Items.Add(At(-1, "past"));
        _planner.Items.Add(At(0, "now"));
        _planner.Items.Add(At(1, "future"));

        _sut.Reschedule();

        CollectionAssert.AreEqual(new[] { "future" }, _notifier.Scheduled.ConvertAll(n => n.Title));
    }

    [Test]
    public void planner_하나가_예외를_던져도_나머지는_예약한다()
    {
        var broken = new FakePlanner { Error = new InvalidOperationException("boom") };
        _planner.Items.Add(At(1, "ok"));
        _sut.Dispose();
        _sut = Create(broken, _planner);

        LogAssert.Expect(LogType.Warning, new Regex("boom"));
        var count = _sut.Reschedule();

        Assert.AreEqual(1, count);
        CollectionAssert.AreEqual(new[] { "ok" }, _notifier.Scheduled.ConvertAll(n => n.Title));
    }

    [Test]
    public void 알림_창구는_처음_쓸_때_만든다()
    {
        // 모바일 알림 센터 초기화는 저사양 기기에서 0.5~1초 걸린다. 등록·해석만으로 만들지 않는다.
        Assert.AreEqual(0, _notifierCreated);

        _sut.Reschedule();
        _sut.Reschedule();

        Assert.AreEqual(1, _notifierCreated);
    }

    [Test]
    public void Start_뒤_앱이_포커스를_잃으면_다시_예약하고_Dispose_뒤에는_하지_않는다()
    {
        _sut.Start();
        Assert.AreEqual(1, _notifier.CancelAllCount, "Start는 곧바로 한 번 예약한다");

        _focus.Raise(true);
        Assert.AreEqual(1, _notifier.CancelAllCount, "포커스를 얻을 때는 다시 예약하지 않는다");

        _focus.Raise(false);
        Assert.AreEqual(2, _notifier.CancelAllCount);

        _sut.Dispose();
        Assert.IsFalse(_focus.HasSubscribers);
    }

    [Test]
    public void 끄면_예약을_지우고_상태를_저장한다_켜면_다시_예약한다()
    {
        _planner.Items.Add(At(1));
        _sut.Reschedule();

        _sut.IsEnabled = false;

        Assert.IsFalse(_storage.IsEnabled);
        Assert.IsEmpty(_notifier.Scheduled);

        _sut.IsEnabled = true;

        Assert.IsTrue(_storage.IsEnabled);
        Assert.AreEqual(1, _notifier.Scheduled.Count);
    }

    [UnityTest]
    public IEnumerator 권한을_처음_물을_때만_창을_띄우고_허용되면_예약한다() => AwaitableTest.Run(async () =>
    {
        _notifier.HasPermission = false;
        _planner.Items.Add(At(1));

        Assert.IsTrue(_sut.TryRequestPermissionOnce(), "창을 띄웠으면 true — 같은 때 다른 OS 창(리뷰)을 피하는 데 쓴다");
        Assert.IsTrue(_storage.HasAskedPermission, "창이 떠 있는 동안 앱이 꺼져도 다시 묻지 않게 먼저 적는다");
        Assert.IsFalse(_sut.TryRequestPermissionOnce());
        Assert.AreEqual(1, _notifier.RequestCount);

        _notifier.HasPermission = true;
        _notifier.Permission.SetResult(true);
        await AwaitableTest.NextFrame();

        Assert.AreEqual(1, _notifier.Scheduled.Count);
    });

    [Test]
    public void 이미_권한이_있으면_창_없이_예약만_한다()
    {
        _planner.Items.Add(At(1));

        Assert.IsFalse(_sut.TryRequestPermissionOnce());
        Assert.AreEqual(0, _notifier.RequestCount);
        Assert.AreEqual(1, _notifier.Scheduled.Count);
    }

    [Test]
    public void 알림에_실은_데이터를_그대로_예약한다()
    {
        _planner.Items.Add(new LocalNotification(Now.AddHours(1), "t", "b", "daily|3600"));

        _sut.Reschedule();

        Assert.AreEqual("daily|3600", _notifier.Scheduled[0].Data);
    }

    [UnityTest]
    public IEnumerator Start하면_알림을_눌러_연_것을_한_번만_알린다() => AwaitableTest.Run(async () =>
    {
        var opened = new List<string>();
        _sut.Opened += opened.Add;
        _notifier.LastOpenedData = "daily|3600";

        _sut.Start();
        await AwaitableTest.NextFrame();

        CollectionAssert.AreEqual(new[] { "daily|3600" }, opened);
        Assert.IsTrue(_storage.Opened.Contains("daily|3600"), "다음 실행에서 다시 알리지 않게 기록한다");

        // OS는 마지막으로 누른 알림을 계속 돌려준다.
        _focus.Raise(true);
        await AwaitableTest.NextFrame();

        Assert.AreEqual(1, opened.Count);
    });

    [UnityTest]
    public IEnumerator 앱이_포커스를_되찾으면_새로_누른_알림을_알린다() => AwaitableTest.Run(async () =>
    {
        var opened = new List<string>();
        _sut.Opened += opened.Add;
        _sut.Start();
        await AwaitableTest.NextFrame();

        _notifier.LastOpenedData = "daily|7200";
        _focus.Raise(true);
        await AwaitableTest.NextFrame();

        CollectionAssert.AreEqual(new[] { "daily|7200" }, opened);
    });

    [UnityTest]
    public IEnumerator 알림으로_열지_않았으면_알리지_않는다() => AwaitableTest.Run(async () =>
    {
        var opened = new List<string>();
        _sut.Opened += opened.Add;

        _sut.Start();
        await AwaitableTest.NextFrame();

        Assert.IsEmpty(opened);
        Assert.AreEqual(1, _notifier.LastOpenedQueryCount);
    });

    [Test]
    public void 연_알림을_읽다_예외가_나도_예약은_한다()
    {
        _notifier.LastOpenedError = new InvalidOperationException("boom");
        _planner.Items.Add(At(1));

        LogAssert.Expect(LogType.Warning, new Regex("연 알림을 읽지 못했다"));
        _sut.Start();

        Assert.AreEqual(1, _notifier.Scheduled.Count);
    }

    [Test]
    public void 처음_물을_때_이미_권한이_있으면_AlreadyGranted를_알린다()
    {
        var answers = new List<LocalNotificationPermissionResult>();
        _sut.PermissionAnswered += answers.Add;

        _sut.TryRequestPermissionOnce();
        _sut.TryRequestPermissionOnce();

        CollectionAssert.AreEqual(new[] { LocalNotificationPermissionResult.AlreadyGranted }, answers);
    }

    [UnityTest]
    public IEnumerator 권한_창에서_허용하면_Granted를_알린다() => AwaitableTest.Run(async () =>
    {
        var answers = new List<LocalNotificationPermissionResult>();
        _sut.PermissionAnswered += answers.Add;
        _notifier.HasPermission = false;

        _sut.TryRequestPermissionOnce();
        _notifier.Permission.SetResult(true);
        await AwaitableTest.NextFrame();

        CollectionAssert.AreEqual(new[] { LocalNotificationPermissionResult.Granted }, answers);
    });

    [UnityTest]
    public IEnumerator 권한_창에서_거절하면_Denied를_알린다() => AwaitableTest.Run(async () =>
    {
        var answers = new List<LocalNotificationPermissionResult>();
        _sut.PermissionAnswered += answers.Add;
        _notifier.HasPermission = false;

        _sut.TryRequestPermissionOnce();
        _notifier.Permission.SetResult(false);
        await AwaitableTest.NextFrame();

        CollectionAssert.AreEqual(new[] { LocalNotificationPermissionResult.Denied }, answers);
    });

    [UnityTest]
    public IEnumerator 설정_화면에서_다시_물은_결과는_알리지_않는다() => AwaitableTest.Run(async () =>
    {
        var answers = new List<LocalNotificationPermissionResult>();
        _sut.PermissionAnswered += answers.Add;
        _notifier.HasPermission = false;
        _notifier.Permission.SetResult(true);

        await _sut.RequestPermissionAsync();

        Assert.IsEmpty(answers, "설치마다 한 번(TryRequestPermissionOnce)만 알린다");
    });
}
