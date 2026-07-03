#if UNITY_IOS && !UNITY_EDITOR
using System.Runtime.InteropServices;

namespace DarkNaku.FoundationDI
{
    /// <summary>
    /// iOS Taptic Engine(UIFeedbackGenerator) 기반 provider.
    /// 네이티브 브리지: Assets/Plugins/iOS/FoundationHaptic.mm
    /// </summary>
    public class iOSHapticProvider : IHapticProvider
    {
        [DllImport("__Internal")]
        private static extern void FDI_HapticImpact(int style);

        [DllImport("__Internal")]
        private static extern void FDI_HapticNotification(int type);

        [DllImport("__Internal")]
        private static extern void FDI_HapticSelection();

        public void Impact(HapticImpact style) => FDI_HapticImpact((int)style);

        public void Notification(HapticNotification type) => FDI_HapticNotification((int)type);

        public void Selection() => FDI_HapticSelection();
    }
}
#endif
