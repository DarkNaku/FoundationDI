using System;

namespace DarkNaku.FoundationDI
{
    /// <summary>
    /// 조건 기반 튜토리얼 진행. 씬 수명이므로 씬 LifetimeScope에 등록한다.
    /// Register/Unregister는 오써링 어댑터가, 나머지는 게임 코드가 쓴다.
    /// </summary>
    public interface ITutorialManager : IDisposable
    {
        bool IsRunning { get; }

        bool IsCompleted(string sequenceId);

        void Register(TutorialSequence sequence);

        void Unregister(string sequenceId);

        /// <summary>현재 실행 중인 시퀀스만 완료 처리한다.</summary>
        void Skip();

        /// <summary>전역 스킵. 씬에 없는 시퀀스까지 덮는다.</summary>
        void SkipAll();

        /// <summary>ManualTrigger를 발동시킨다.</summary>
        void Complete(string stepId);

        event Action<string> SequenceStarted;

        event Action<string> SequenceCompleted;
    }
}
