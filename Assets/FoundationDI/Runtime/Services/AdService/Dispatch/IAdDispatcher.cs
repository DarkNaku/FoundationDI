using System;

namespace DarkNaku.FoundationDI
{
    // 두 가지 목적이 있다.
    // 1) 세 광고 SDK 모두 네이티브 스레드에서 콜백이 올라올 수 있어 메인스레드 마샬링이 필요하다.
    // 2) 백오프 지연과 보상 유예 프레임을 가짜 시계로 테스트할 수 있게 한다. 이쪽이 더 큰 이유다.
    public interface IAdDispatcher
    {
        // 메인 스레드에서 실행되도록 큐에 넣는다. 이미 메인 스레드여도 큐를 거친다.
        void Post(Action action);

        // seconds 후 실행. 반환된 IDisposable을 Dispose하면 취소된다.
        IDisposable Delay(float seconds, Action action);

        // count 프레임 후 실행. count가 0이면 즉시 실행한다. 반환값 Dispose로 취소.
        IDisposable NextFrames(int count, Action action);
    }
}
