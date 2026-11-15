using System;
using System.Collections;
using DarkNaku.FoundationDI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

public class AppStoreLookupTest
{
    private class FakeHttp
    {
        public string RequestedUrl;
        public string Body = "{\"resultCount\":0,\"results\":[]}";

        public Awaitable<string> Get(string url)
        {
            RequestedUrl = url;

            var source = new AwaitableCompletionSource<string>();
            source.SetResult(Body);
            return source.Awaitable;
        }
    }

    private static string _openedUrl;

    private static readonly DateTime Now = new(2026, 11, 16, 9, 0, 0, DateTimeKind.Utc);

    private static AppStoreAppUpdateStore CreateStore(
        FakeHttp http,
        string bundleID = "com.darknaku.game",
        string country = "kr",
        string installedVersion = "1.2.0",
        string osVersion = "18.1")
    {
        return new AppStoreAppUpdateStore(
            bundleID, country, installedVersion, osVersion, http.Get, () => Now, url => _openedUrl = url);
    }

    private static string LookupBody(
        string version,
        string releaseDate = "2026-11-10T08:00:00Z",
        string minimumOsVersion = "13.0",
        long trackID = 1234567890)
    {
        return "{\"resultCount\":1,\"results\":[{" +
               $"\"version\":\"{version}\"," +
               $"\"currentVersionReleaseDate\":\"{releaseDate}\"," +
               $"\"minimumOsVersion\":\"{minimumOsVersion}\"," +
               $"\"trackId\":{trackID}," +
               "\"trackName\":\"Game\"}]}";
    }

    [UnityTest]
    public IEnumerator 번들_ID와_설정한_국가로_조회_URL을_만든다() => AwaitableTest.Run(async () =>
    {
        var http = new FakeHttp();

        await CreateStore(http, country: "kr").CheckAsync();
        Assert.AreEqual("https://itunes.apple.com/lookup?bundleId=com.darknaku.game&country=kr", http.RequestedUrl);

        // 국가를 비우면 애플이 미국 스토어를 본다.
        await CreateStore(http, country: "").CheckAsync();
        Assert.AreEqual("https://itunes.apple.com/lookup?bundleId=com.darknaku.game", http.RequestedUrl);
    });

    [UnityTest]
    public IEnumerator 스토어_버전이_설치_버전보다_높으면_Available이고_출시_후_지난_날짜를_센다() => AwaitableTest.Run(async () =>
    {
        var http = new FakeHttp { Body = LookupBody("1.10.0", releaseDate: "2026-11-10T08:00:00Z") };

        var result = await CreateStore(http, installedVersion: "1.9.3").CheckAsync();

        Assert.AreEqual(AppUpdateStatus.Available, result.Status);
        Assert.AreEqual(6, result.DaysSinceRelease);
        Assert.IsNull(result.Priority);
    });

    [UnityTest]
    public IEnumerator 스토어_버전이_같거나_낮으면_UpToDate다() => AwaitableTest.Run(async () =>
    {
        // 낮은 경우: 심사 중인 새 버전을 TestFlight로 설치했을 때.
        var same = await CreateStore(new FakeHttp { Body = LookupBody("1.2.0") }, installedVersion: "1.2.0").CheckAsync();
        var older = await CreateStore(new FakeHttp { Body = LookupBody("1.2.0") }, installedVersion: "1.3.0").CheckAsync();

        Assert.AreEqual(AppUpdateStatus.UpToDate, same.Status);
        Assert.AreEqual(AppUpdateStatus.UpToDate, older.Status);
    });

    [UnityTest]
    public IEnumerator 조회_결과가_없으면_Unknown이다() => AwaitableTest.Run(async () =>
    {
        // 그 국가 스토어에 출시하지 않았거나 번들 ID가 다르면 결과가 비어 온다. 최신이라고 단정할 수 없다.
        var result = await CreateStore(new FakeHttp { Body = "{\"resultCount\":0,\"results\":[]}" }).CheckAsync();

        Assert.AreEqual(AppUpdateStatus.Unknown, result.Status);
    });

    [UnityTest]
    public IEnumerator 응답이_비었거나_스토어_버전을_읽지_못하면_Unknown이다() => AwaitableTest.Run(async () =>
    {
        var empty = await CreateStore(new FakeHttp { Body = "" }).CheckAsync();
        var unreadable = await CreateStore(new FakeHttp { Body = LookupBody("2.0 beta") }).CheckAsync();

        Assert.AreEqual(AppUpdateStatus.Unknown, empty.Status);
        Assert.AreEqual(AppUpdateStatus.Unknown, unreadable.Status);
    });

    [UnityTest]
    public IEnumerator 새_버전이_요구하는_OS보다_기기_OS가_낮으면_UpToDate다() => AwaitableTest.Run(async () =>
    {
        // 이 기기는 스토어에서 새 버전을 받을 수 없다. Available이라 하면 게임이 막아 버려 유저가 갇힌다.
        var body = LookupBody("1.3.0", minimumOsVersion: "17.0");

        var oldDevice = await CreateStore(new FakeHttp { Body = body }, osVersion: "16.7.2").CheckAsync();
        var newDevice = await CreateStore(new FakeHttp { Body = body }, osVersion: "17.0").CheckAsync();
        var unknownDevice = await CreateStore(new FakeHttp { Body = body }, osVersion: null).CheckAsync();

        Assert.AreEqual(AppUpdateStatus.UpToDate, oldDevice.Status);
        Assert.AreEqual(AppUpdateStatus.Available, newDevice.Status);
        Assert.AreEqual(AppUpdateStatus.Available, unknownDevice.Status);
    });

    [UnityTest]
    public IEnumerator OpenStore는_마지막_조회의_trackId로_스토어를_열고_조회_전이면_false다() => AwaitableTest.Run(async () =>
    {
        _openedUrl = null;
        var store = CreateStore(new FakeHttp { Body = LookupBody("1.2.0", trackID: 1234567890) });

        Assert.IsFalse(store.OpenStore());
        Assert.IsNull(_openedUrl);

        await store.CheckAsync();

        Assert.IsTrue(store.OpenStore());
        Assert.AreEqual("itms-apps://apps.apple.com/app/id1234567890", _openedUrl);
    });
}
