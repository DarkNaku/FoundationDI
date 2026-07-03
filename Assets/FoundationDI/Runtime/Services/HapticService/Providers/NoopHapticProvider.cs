namespace DarkNaku.FoundationDI
{
    /// <summary>
    /// 햅틱 미지원 플랫폼(에디터·데스크톱)용 provider. 모든 호출을 무시한다.
    /// </summary>
    public class NoopHapticProvider : IHapticProvider
    {
        public void Impact(HapticImpact style)
        {
        }

        public void Notification(HapticNotification type)
        {
        }

        public void Selection()
        {
        }
    }
}
