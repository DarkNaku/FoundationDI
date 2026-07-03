namespace DarkNaku.FoundationDI
{
    public enum HapticImpact { Light, Medium, Heavy, Soft, Rigid }

    public enum HapticNotification { Success, Warning, Error }

    public interface IHapticProvider
    {
        void Impact(HapticImpact style);
        void Notification(HapticNotification type);
        void Selection();
    }
}
