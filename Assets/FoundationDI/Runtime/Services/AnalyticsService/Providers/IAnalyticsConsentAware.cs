namespace DarkNaku.FoundationDI
{
    // 동의 결과를 자기 SDK의 신호로 번역해야 하는 provider만 구현한다.
    //
    // IAnalyticsProvider 본체에 넣지 않은 이유: Firebase는 UMP가 쓴 TCF·consent mode를 스스로 읽어
    // 할 일이 없고, 번역이 필요한 SDK는 지금 Adjust 하나다(IAnalyticsFlushHook과 같은 이유).
    //
    // 호출 시점: 초기화 직후 첫 판정, 그리고 ConsentChanged마다. 항상 flush 훅보다 먼저다.
    public interface IAnalyticsConsentAware
    {
        void ApplyConsent(ConsentResult result);
    }
}
