namespace DarkNaku.FoundationDI
{
    // 두 가지 목적이 있다.
    // 1) 세 광고 SDK 모두 네이티브 스레드에서 콜백이 올라올 수 있어 메인스레드 마샬링이 필요하다.
    // 2) 백오프 지연과 보상 유예 프레임을 가짜 시계로 테스트할 수 있게 한다. 이쪽이 더 큰 이유다.
    //
    // 멤버는 IMainThreadDispatcher에 있다. 이 인터페이스는 광고 어댑터들이 쓰는 이름을 보존하려고 남겨 둔다.
    public interface IAdDispatcher : IMainThreadDispatcher
    {
    }
}
