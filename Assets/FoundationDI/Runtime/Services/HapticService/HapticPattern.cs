namespace DarkNaku.FoundationDI
{
    public struct iOSPulse
    {
        public HapticPreset Preset; // 이 펄스 전 지연 후 발동할 프리셋
        public float DelayMs;
    }

    public struct AndroidPulse
    {
        public long DelayMs;   // 이 펄스 전 무진동 지연
        public long PulseMs;   // 진동 지속
        public int Amplitude;  // 0..255
    }

    public struct HapticPattern
    {
        public iOSPulse[] IOS;
        public AndroidPulse[] Android;

        public HapticPattern(iOSPulse[] ios, AndroidPulse[] android)
        {
            IOS = ios;
            Android = android;
        }
    }
}
