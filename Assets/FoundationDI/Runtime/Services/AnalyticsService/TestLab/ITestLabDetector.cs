namespace DarkNaku.FoundationDI
{
    // Google Play 사전 출시 보고서는 Firebase Test Lab 실기기에서 앱을 돌린다. 그 기기들이 GA4·MMP에
    // 신규 사용자로 잡혀 리텐션과 인스톨 수를 흐리므로, AnalyticsService가 이 seam으로 판별해 수집하지 않는다.
    public interface ITestLabDetector
    {
        bool IsTestLab { get; }
    }
}
