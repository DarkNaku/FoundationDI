using System;

namespace DarkNaku.FoundationDI
{
    // 네이티브 콜백을 메인 스레드로 넘기고, 지연·프레임 대기를 가짜 시계로 테스트할 수 있게 하는 seam.
    // 처음에는 AdService 전용(IAdDispatcher)이었으나 ConsentService도 ATT 재요청 지연과
    // UMP 콜백 마샬링에 같은 것이 필요해 위로 올렸다.
    //
    // IAdDispatcher의 이름을 바꾸지 않고 상위 인터페이스를 둔 이유: LevelPlay·AppLovin 어댑터는
    // 게이트된 옵셔널 어셈블리라 이 호스트에서 컴파일되지 않는다. 이름을 바꾸면 검증할 수 없는
    // 파일을 손대야 한다.
    public interface IMainThreadDispatcher
    {
        // 메인 스레드에서 실행되도록 큐에 넣는다. 이미 메인 스레드여도 큐를 거친다.
        void Post(Action action);

        // seconds 후 실행. 반환된 IDisposable을 Dispose하면 취소된다.
        IDisposable Delay(float seconds, Action action);

        // count 프레임 후 실행. count가 0이면 즉시 실행한다. 반환값 Dispose로 취소.
        IDisposable NextFrames(int count, Action action);
    }
}
