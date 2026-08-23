namespace DarkNaku.FoundationDI
{
    /// <summary>
    /// 튜토리얼 진행도 영속화 seam. 인덱스가 아니라 시퀀스 ID로 저장하므로
    /// 시퀀스를 중간에 추가·삭제해도 기존 유저의 진행도가 어긋나지 않는다.
    /// </summary>
    public interface ITutorialProgressStorage
    {
        TutorialState GetState(string sequenceId);
        void SetState(string sequenceId, TutorialState state);

        int GetStepIndex(string sequenceId);
        void SetStepIndex(string sequenceId, int index);

        /// <summary>전역 스킵. 씬에 없는 다른 레벨의 시퀀스까지 확실히 덮는다.</summary>
        bool AllSkipped { get; set; }

        void Clear();
    }
}
