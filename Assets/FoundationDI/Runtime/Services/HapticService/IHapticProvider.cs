namespace DarkNaku.FoundationDI
{
    public interface IHapticProvider
    {
        void Impact(HapticImpact style);
        void Notification(HapticNotification type);
        void Selection();
    }
}
