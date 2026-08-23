using System.Collections.Generic;
using DarkNaku.FoundationDI.Editor;
using NUnit.Framework;

public class SdkDefineSynchronizerTest
{
    private const string Firebase = "FOUNDATIONDI_FIREBASE";
    private const string UnityIap = "FOUNDATIONDI_UNITYIAP";
    private const string AppLovin = "FOUNDATIONDI_APPLOVIN";

    private static Dictionary<string, bool> Present(bool firebase = false, bool unityIap = false,
                                                    bool appLovin = false) =>
        new() { { Firebase, firebase }, { UnityIap, unityIap }, { AppLovin, appLovin } };

    [Test]
    public void SDK가_있으면_없던_심볼을_추가한다()
    {
        var result = SdkDefineSynchronizer.Resolve("", Present(firebase: true));

        Assert.AreEqual(Firebase, result);
    }

    [Test]
    public void SDK가_없으면_있던_심볼을_제거한다()
    {
        var result = SdkDefineSynchronizer.Resolve($"{Firebase};{UnityIap}", Present(unityIap: true));

        Assert.AreEqual(UnityIap, result);
    }

    [Test]
    public void 관리_대상이_아닌_심볼은_건드리지_않는다()
    {
        var current = $"LEVELPLAY_DEPENDENCIES_INSTALLED;{Firebase};MY_GAME_CHEATS";

        var result = SdkDefineSynchronizer.Resolve(current, Present(firebase: true));

        Assert.AreEqual(current, result);
    }

    [Test]
    public void 관리_대상이_아닌_심볼은_제거_후에도_순서를_유지한다()
    {
        var current = $"LEVELPLAY_DEPENDENCIES_INSTALLED;{Firebase};MY_GAME_CHEATS";

        var result = SdkDefineSynchronizer.Resolve(current, Present());

        Assert.AreEqual("LEVELPLAY_DEPENDENCIES_INSTALLED;MY_GAME_CHEATS", result);
    }

    [Test]
    public void 변화가_없으면_입력과_같은_문자열을_돌려준다()
    {
        var current = $"{Firebase};{UnityIap}";

        var result = SdkDefineSynchronizer.Resolve(current, Present(firebase: true, unityIap: true));

        Assert.AreEqual(current, result, "변화가 없는데 문자열이 바뀌면 매번 재컴파일이 걸린다");
    }

    [Test]
    public void 추가되는_심볼은_뒤에_붙고_기존_순서는_유지된다()
    {
        var result = SdkDefineSynchronizer.Resolve($"MY_GAME_CHEATS;{Firebase}",
                                                   Present(firebase: true, unityIap: true));

        Assert.AreEqual($"MY_GAME_CHEATS;{Firebase};{UnityIap}", result);
    }

    [Test]
    public void 여러_SDK가_동시에_추가되면_표_순서대로_붙는다()
    {
        var result = SdkDefineSynchronizer.Resolve("", Present(firebase: true, unityIap: true, appLovin: true));

        Assert.AreEqual($"{Firebase};{UnityIap};{AppLovin}", result);
    }

    [Test]
    public void 공백과_빈_항목과_중복을_정리한다()
    {
        var result = SdkDefineSynchronizer.Resolve($"  MY_GAME_CHEATS ;; {Firebase} ;MY_GAME_CHEATS;",
                                                   Present(firebase: true));

        Assert.AreEqual($"MY_GAME_CHEATS;{Firebase}", result);
    }

    [Test]
    public void null_입력은_빈_심볼로_취급한다()
    {
        Assert.AreEqual(Firebase, SdkDefineSynchronizer.Resolve(null, Present(firebase: true)));
        Assert.AreEqual(string.Empty, SdkDefineSynchronizer.Resolve(null, Present()));
    }

    [Test]
    public void 관리_대상이_비면_아무것도_바꾸지_않는다()
    {
        var current = $"{Firebase};MY_GAME_CHEATS";

        var result = SdkDefineSynchronizer.Resolve(current, new Dictionary<string, bool>());

        Assert.AreEqual(current, result);
    }

    [Test]
    public void 관리_대상_표에_AdMob과_LevelPlay는_없다()
    {
        // 어댑터 어셈블리가 없는 심볼을 켜면 AdProviderFactory가 "creator 없음" 에러를 낸다.
        foreach (var entry in SdkDefineTable.Entries)
        {
            Assert.AreNotEqual("FOUNDATIONDI_ADMOB", entry.Symbol);
            Assert.AreNotEqual("FOUNDATIONDI_LEVELPLAY", entry.Symbol);
        }
    }

    [Test]
    public void 관리_대상_표가_게이트되는_세_어셈블리를_모두_덮는다()
    {
        var symbols = new List<string>();
        foreach (var entry in SdkDefineTable.Entries)
        {
            Assert.IsNotEmpty(entry.MarkerType, $"{entry.Symbol}에 마커 타입이 없다");
            Assert.IsNotEmpty(entry.DisplayName, $"{entry.Symbol}에 표시 이름이 없다");
            symbols.Add(entry.Symbol);
        }

        CollectionAssert.AreEquivalent(new[] { Firebase, UnityIap, AppLovin }, symbols);
    }
}
