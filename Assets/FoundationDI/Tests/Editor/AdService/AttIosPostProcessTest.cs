using DarkNaku.FoundationDI.Editor;
using NUnit.Framework;

// 실제 plist 쓰기는 iOS 빌드가 있어야 하고 UNITY_IOS 밖에서는 클래스 멤버조차 컴파일되지
// 않는다. 그래서 문구와 "기존 값을 덮지 않는다"는 규칙만 순수 함수로 잘라 검증한다 —
// 문구가 비면 iOS가 다이얼로그를 아예 띄우지 않으므로 여기서 지키는 값이 크다.
public class AttIosPostProcessTest
{
    [Test]
    public void 문구가_없으면_지정된_ATT_안내를_쓴다()
    {
        Assert.AreEqual("We use your activity data to provide personalized ads and measure ad performance.",
                        AttIosPostProcess.Resolve(null));
    }

    [Test]
    public void 빈_문자열도_비어_있는_것으로_본다()
    {
        Assert.AreEqual(AttIosPostProcess.UsageDescription, AttIosPostProcess.Resolve("   "));
    }

    [Test]
    public void 이미_있는_문구는_덮지_않는다()
    {
        Assert.AreEqual("이미 있는 문구", AttIosPostProcess.Resolve("이미 있는 문구"));
    }

    [Test]
    public void plist_키_이름은_애플이_요구하는_그대로다()
    {
        Assert.AreEqual("NSUserTrackingUsageDescription", AttIosPostProcess.UsageDescriptionKey);
    }
}
