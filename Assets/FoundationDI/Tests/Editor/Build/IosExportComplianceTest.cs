using DarkNaku.FoundationDI.Editor;
using NUnit.Framework;

// 실제 plist 쓰기는 iOS 빌드 산출물이 있어야 하고 UNITY_IOS 밖에서는 후처리 본체가 아예
// 컴파일되지 않는다. 그래서 키 이름과 "언제 쓰는가"의 판정만 순수 함수로 잘라 검증한다 —
// 여기가 틀리면 실패가 TestFlight 업로드 이후에야 드러난다.
public class IosExportComplianceTest
{
    // 정적 프로퍼티라 값이 테스트 사이로, 그리고 다른 픽스처로 새어 나간다.
    [SetUp]
    public void 스위치를_기본값에서_시작한다() => IosExportCompliance.Enabled = IosExportCompliance.EnabledByDefault;

    [TearDown]
    public void 스위치를_기본값으로_되돌린다() => IosExportCompliance.Enabled = IosExportCompliance.EnabledByDefault;

    [Test]
    public void plist_키_이름은_애플이_요구하는_그대로다()
    {
        // 오타가 나도 plist는 조용히 받아들이고, App Store Connect는 키가 없는 것으로 보아
        // 그대로 "수출 규정 관리 문서 누락"을 띄운다.
        Assert.AreEqual("ITSAppUsesNonExemptEncryption", IosExportCompliance.UsesNonExemptEncryptionKey);
    }

    [Test]
    public void 아무것도_설정하지_않으면_신고한다()
    {
        // 설정 에셋도 심볼도 없는 프로젝트가 그대로 빌드했을 때의 동작이다. 이게 뒤집히면
        // 아무도 모르는 채 "수출 규정 관리 문서 누락"이 다시 돌아온다.
        Assert.IsTrue(IosExportCompliance.EnabledByDefault);
        Assert.IsTrue(IosExportCompliance.ShouldDeclare(IosExportCompliance.EnabledByDefault, false));
    }

    [Test]
    public void 스위치를_끄면_신고하지_않는다()
    {
        IosExportCompliance.Enabled = false;

        Assert.IsFalse(IosExportCompliance.ShouldDeclare(IosExportCompliance.Enabled, false));
    }

    [Test]
    public void 이미_신고된_plist는_덮지_않는다()
    {
        // 프로젝트가 자기 후처리나 Info.plist 템플릿으로 true와 컴플라이언스 코드를 넣었을 수
        // 있다. 그걸 false로 뒤집으면 조용히 허위 신고가 된다.
        Assert.IsFalse(IosExportCompliance.ShouldDeclare(true, true));
    }

    [Test]
    public void 신고할_값은_면제_대상이다()
    {
        // false = "면제 대상 외 암호화를 쓰지 않음". true면 애플 발급 컴플라이언스 코드가
        // 따로 필요해 이 후처리가 감당하는 범위를 벗어난다.
        Assert.IsFalse(IosExportCompliance.UsesNonExemptEncryption);
    }
}
