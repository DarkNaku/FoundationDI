using DarkNaku.FoundationDI.Editor;
using NUnit.Framework;

public class UmpBuildRulesTest
{
    [Test]
    public void UMP를_끄면_앱_ID를_건드리지_않는다()
    {
        Assert.AreEqual(AppIdAction.Skip, UmpBuildRules.DecideAppId(false, "", false));
    }

    [Test]
    public void 이미_있으면_덮지_않는다()
    {
        // LevelPlay AdMob 어댑터가 넣은 값이다. 덮으면 매니페스트 병합 충돌이 나거나 다른 ID로 바뀐다.
        Assert.AreEqual(AppIdAction.Skip, UmpBuildRules.DecideAppId(true, "ca-app-pub-1~2", true));
    }

    [Test]
    public void 없고_설정에_있으면_넣는다()
    {
        Assert.AreEqual(AppIdAction.Inject, UmpBuildRules.DecideAppId(true, "ca-app-pub-1~2", false));
    }

    [Test]
    public void 없고_설정에도_없으면_빌드를_멈춘다()
    {
        // UMP는 앱 ID 없이 실행 시점에 크래시한다. 빌드에서 잡는 편이 낫다.
        Assert.AreEqual(AppIdAction.Fail, UmpBuildRules.DecideAppId(true, "", false));
    }

    [Test]
    public void Firebase_수집_기본값은_동의를_기다리고_Firebase가_있고_아직_없을_때만_넣는다()
    {
        Assert.IsTrue(UmpBuildRules.ShouldDisableFirebaseCollection(true, true, false));
        Assert.IsFalse(UmpBuildRules.ShouldDisableFirebaseCollection(false, true, false));
        Assert.IsFalse(UmpBuildRules.ShouldDisableFirebaseCollection(true, false, false));
        Assert.IsFalse(UmpBuildRules.ShouldDisableFirebaseCollection(true, true, true));
    }
}
