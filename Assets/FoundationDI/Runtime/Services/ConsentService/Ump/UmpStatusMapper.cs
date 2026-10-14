namespace DarkNaku.FoundationDI
{
    // 네이티브 enum 값을 캐스팅하지 않고 명시적으로 번역한다. Android와 iOS의 ConsentStatus
    // 순서가 다르고(REQUIRED/NOT_REQUIRED), SDK가 값을 바꾸면 여기서 테스트가 깨져야 한다.
    internal static class UmpStatusMapper
    {
        public static UmpConsentStatus FromAndroid(int value)
        {
            switch (value)
            {
                case 1: return UmpConsentStatus.NotRequired;
                case 2: return UmpConsentStatus.Required;
                case 3: return UmpConsentStatus.Obtained;
                default: return UmpConsentStatus.Unknown;
            }
        }

        public static UmpConsentStatus FromIos(int value)
        {
            switch (value)
            {
                case 1: return UmpConsentStatus.Required;
                case 2: return UmpConsentStatus.NotRequired;
                case 3: return UmpConsentStatus.Obtained;
                default: return UmpConsentStatus.Unknown;
            }
        }

        public static int ToAndroidGeography(UmpDebugGeography geography) => ToNativeGeography(geography);

        public static int ToIosGeography(UmpDebugGeography geography) => ToNativeGeography(geography);

        private static int ToNativeGeography(UmpDebugGeography geography)
        {
            switch (geography)
            {
                case UmpDebugGeography.Eea: return 1;
                case UmpDebugGeography.NotEea: return 2;
                case UmpDebugGeography.RegulatedUsState: return 3;
                case UmpDebugGeography.Other: return 4;
                default: return 0;
            }
        }
    }
}
