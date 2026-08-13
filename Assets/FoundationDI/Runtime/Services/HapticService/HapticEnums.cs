namespace DarkNaku.FoundationDI
{
    public enum HapticImpact { Light, Medium, Heavy, Soft, Rigid }

    public enum HapticNotification { Success, Warning, Error }

    // 패턴 펄스 저작 전용 플랫 enum (Impact/Notification/Selection 계열을 하나로 지목)
    public enum HapticPreset
    {
        Selection,
        Success, Warning, Error,
        LightImpact, MediumImpact, HeavyImpact,
        SoftImpact, RigidImpact
    }
}
