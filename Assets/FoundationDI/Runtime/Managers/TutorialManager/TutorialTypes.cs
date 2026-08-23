namespace DarkNaku.FoundationDI
{
    /// <summary>시퀀스 단위 진행 상태. 저장소에 그대로 영속화된다.</summary>
    public enum TutorialState
    {
        NotStarted,
        Running,
        Completed,
    }

    /// <summary>
    /// Running 상태로 남은 시퀀스를 다시 시작할 때의 정책.
    /// 기본은 처음부터 — Step 중간 재개는 앞선 Step의 부작용이 반영돼 있다는 걸 전제하는데
    /// 그걸 보장할 방법이 없다.
    /// </summary>
    public enum ResumeMode
    {
        RestartSequence,
        ResumeFromStep,
    }
}
