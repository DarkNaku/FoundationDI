using System;
using System.Collections;
using System.Text.RegularExpressions;
using DarkNaku.FoundationDI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

public class AppUpdateServiceTest
{
    private class FakeAppUpdateStore : IAppUpdateStore, IDisposable
    {
        public bool IsDisposed;

        public AppUpdateCheckResult Result = AppUpdateCheckResult.UpToDate();
        public Exception CheckError;
        public int CheckCount;

        public Awaitable<AppUpdateCheckResult> CheckAsync()
        {
            CheckCount++;

            var source = new AwaitableCompletionSource<AppUpdateCheckResult>();
            if (CheckError != null) source.SetException(CheckError);
            else source.SetResult(Result);
            return source.Awaitable;
        }

        public bool OpenResult = true;
        public Exception OpenError;
        public int OpenCount;

        public bool OpenStore()
        {
            OpenCount++;
            if (OpenError != null) throw OpenError;
            return OpenResult;
        }

        public void Dispose() => IsDisposed = true;
    }

    [UnityTest]
    public IEnumerator CheckAsync는_창구의_확인_결과를_그대로_돌려준다() => AwaitableTest.Run(async () =>
    {
        var store = new FakeAppUpdateStore { Result = AppUpdateCheckResult.Available(daysSinceRelease: 3, priority: 4) };
        var sut = new AppUpdateService(() => store);

        var result = await sut.CheckAsync();

        Assert.AreEqual(1, store.CheckCount);
        Assert.AreEqual(AppUpdateStatus.Available, result.Status);
        Assert.AreEqual(3, result.DaysSinceRelease);
        Assert.AreEqual(4, result.Priority);
    });

    [UnityTest]
    public IEnumerator 창구가_확인_중_예외를_내면_Unknown을_돌려주고_경고만_남긴다() => AwaitableTest.Run(async () =>
    {
        // 업데이트 확인 때문에 게임 시작 흐름이 끊기면 안 된다.
        var store = new FakeAppUpdateStore { CheckError = new InvalidOperationException("boom") };
        var sut = new AppUpdateService(() => store);

        LogAssert.Expect(LogType.Warning, new Regex("boom"));

        var result = await sut.CheckAsync();

        Assert.AreEqual(AppUpdateStatus.Unknown, result.Status);
    });

    [UnityTest]
    public IEnumerator 창구를_만들지_못하면_Unknown을_돌려주고_다시_만들지_않는다() => AwaitableTest.Run(async () =>
    {
        // Android 창구는 생성에서 JNI로 AppUpdateManager를 만든다. 라이브러리가 빌드에 없으면 여기서 던지는데,
        // 확인할 때마다 같은 예외 로그가 쌓일 뿐이다.
        var createCount = 0;
        var sut = new AppUpdateService(() =>
        {
            createCount++;
            throw new InvalidOperationException("no library");
        });

        LogAssert.Expect(LogType.Warning, new Regex("no library"));

        var first = await sut.CheckAsync();
        var second = await sut.CheckAsync();

        Assert.AreEqual(AppUpdateStatus.Unknown, first.Status);
        Assert.AreEqual(AppUpdateStatus.Unknown, second.Status);
        Assert.AreEqual(1, createCount);
    });

    [Test]
    public void OpenStore는_창구에_넘기고_예외가_나면_false를_돌려준다()
    {
        var store = new FakeAppUpdateStore { OpenResult = true };
        var sut = new AppUpdateService(() => store);

        Assert.IsTrue(sut.OpenStore());
        Assert.AreEqual(1, store.OpenCount);

        store.OpenError = new InvalidOperationException("boom");
        LogAssert.Expect(LogType.Warning, new Regex("boom"));

        Assert.IsFalse(sut.OpenStore());
    }

    [Test]
    public void 해제하면_창구를_해제한다()
    {
        var store = new FakeAppUpdateStore();
        var sut = new AppUpdateService(() => store);
        sut.OpenStore();

        sut.Dispose();

        Assert.IsTrue(store.IsDisposed);
    }

    [UnityTest]
    public IEnumerator 에디터와_미지원_플랫폼_창구는_Unknown이고_스토어를_열지_않는다() => AwaitableTest.Run(async () =>
    {
        var store = new UnsupportedAppUpdateStore();

        var result = await store.CheckAsync();

        Assert.AreEqual(AppUpdateStatus.Unknown, result.Status);
        Assert.IsFalse(store.OpenStore());
    });
}
