namespace DarkNaku.FoundationDI
{
    internal static class NativePolicyDialogFactory
    {
        // 실기가 아니면 null — NativePolicyAgreementPrompt가 대체 창을 쓴다.
        public static INativePolicyDialog Create(IMainThreadDispatcher dispatcher)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            return AndroidNativePolicyDialog.TryCreate(dispatcher);
#elif UNITY_IOS && !UNITY_EDITOR
            return new IosNativePolicyDialog(dispatcher);
#else
            return null;
#endif
        }
    }
}
