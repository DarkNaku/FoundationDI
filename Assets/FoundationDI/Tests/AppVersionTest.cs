using DarkNaku.FoundationDI;
using NUnit.Framework;

public class AppVersionTest
{
    [Test]
    public void 버전을_문자열이_아니라_숫자_단위로_비교한다()
    {
        // 문자열 비교면 "1.10.0" < "1.9.3"이 되어 새 버전을 놓친다.
        Assert.AreEqual(1, AppVersion.Compare("1.10.0", "1.9.3"));
        Assert.AreEqual(-1, AppVersion.Compare("1.9.3", "1.10.0"));
        Assert.AreEqual(0, AppVersion.Compare("2.0.1", "2.0.1"));
    }

    [Test]
    public void 자릿수가_다르면_빈_자리를_0으로_본다()
    {
        Assert.AreEqual(0, AppVersion.Compare("1.2", "1.2.0"));
        Assert.AreEqual(0, AppVersion.Compare("1.2.0", "1.2"));
        Assert.AreEqual(1, AppVersion.Compare("1.2.1", "1.2"));
        Assert.AreEqual(-1, AppVersion.Compare("1.2", "1.2.1"));
    }

    [Test]
    public void 숫자가_아닌_마디가_있으면_비교하지_못한다()
    {
        // 비교하지 못한 것을 "같다"로 돌려주면 게임이 업데이트 없음으로 오해한다.
        Assert.IsNull(AppVersion.Compare("1.2.0-beta", "1.2.0"));
        Assert.IsNull(AppVersion.Compare("1.2.0", ""));
        Assert.IsNull(AppVersion.Compare(null, "1.2.0"));
    }
}
