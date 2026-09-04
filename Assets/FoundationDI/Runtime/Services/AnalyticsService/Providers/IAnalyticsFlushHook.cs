namespace DarkNaku.FoundationDI
{
    // 초기화 직후 버퍼된 유저 상태와 이벤트를 모두 내보낸 뒤 정확히 한 번 호출되는 선택적 seam.
    // 필요한 어댑터만 IAnalyticsProvider와 함께 구현한다.
    //
    // IAnalyticsProvider 본체에 넣지 않은 이유는 이걸 필요로 하는 SDK가 Adjust 하나이기
    // 때문이다. Adjust는 첫 세션(=인스톨) 패키지를 InitSdk 시점에 만들어 보내므로, 그 뒤에
    // 붙는 전역 콜백 파라미터(A/B 그룹·설치 버전·유저 ID)가 정작 인스톨 레코드에만 빠진다.
    // 첫 세션을 지연시켜 두고 이 훅에서 풀어 주면 버퍼된 파라미터가 전부 첫 세션에 실린다.
    //
    // 어댑터가 프레임을 세어 짐작하는 방식과 달리 시점이 결정적이고, 그래서 EditMode에서
    // 검증된다. 대신 규칙 하나가 따라온다 — 첫 세션에 실을 파라미터는 InitializeAsync를
    // 부르기 전에 SetUserId/SetUserProperty로 넣어야 한다(코어가 버퍼해 준다). await 이후에
    // 부르면 훅이 이미 지나가 있다.
    public interface IAnalyticsFlushHook
    {
        void OnBufferedStateFlushed();
    }
}
