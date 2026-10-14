using DarkNaku.FoundationDI;
using NUnit.Framework;
using UnityEngine;

public class PolicyAgreementStorageTest
{
    [SetUp]
    public void SetUp() => PlayerPrefs.DeleteKey(PlayerPrefsPolicyAgreementStorage.Key);

    [TearDown]
    public void TearDown() => PlayerPrefs.DeleteKey(PlayerPrefsPolicyAgreementStorage.Key);

    [Test]
    public void 저장한_적이_없으면_0을_돌려준다()
    {
        Assert.AreEqual(0, new PlayerPrefsPolicyAgreementStorage().LoadAcceptedVersion());
    }

    [Test]
    public void 저장한_버전을_다시_읽는다()
    {
        var storage = new PlayerPrefsPolicyAgreementStorage();

        storage.SaveAcceptedVersion(3);

        Assert.AreEqual(3, new PlayerPrefsPolicyAgreementStorage().LoadAcceptedVersion());
    }
}
