using UnityEngine;

namespace DarkNaku.FoundationDI
{
    /// <summary>
    /// 프레임 번호 공급 seam. 같은 프레임 중복 재생 판정에 쓴다.
    /// <see cref="Time.frameCount"/>는 테스트가 제어할 수 없어(EditMode에서는 에디터 틱에 따라
    /// 임의로 증가한다) 인터페이스로 감싼다.
    /// </summary>
    internal interface ISoundFrameClock
    {
        int FrameCount { get; }
    }

    /// <summary>런타임 기본 구현.</summary>
    internal sealed class UnityFrameClock : ISoundFrameClock
    {
        public int FrameCount => Time.frameCount;
    }
}
