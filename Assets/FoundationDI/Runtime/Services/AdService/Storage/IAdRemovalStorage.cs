namespace DarkNaku.FoundationDI
{
    // 광고제거(인앱 구매) 상태의 영속화 seam. SoundService의 ISoundVolumeStorage와 같은 패턴이다.
    // 서버 권위 저장소를 쓰는 프로젝트는 이 인터페이스를 갈아끼우면 된다.
    public interface IAdRemovalStorage
    {
        bool Load();
        void Save(bool removed);
    }
}
