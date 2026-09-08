using UnityEngine;

namespace DarkNaku.FoundationDI
{
    // FoundationDI는 이 어셈블리를 참조할 수 없다(참조 방향이 반대라 순환이 된다).
    // 그래서 이쪽이 스스로를 레지스트리에 밀어 넣는다.
    public static class UnityIAPInstaller
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Install()
        {
            IAPProviderRegistry.Register(IAPProviderType.UnityIAP, _ => new UnityIAPProvider());
            IAPReceiptValidatorRegistry.Current = new CrossPlatformReceiptValidator();
        }
    }
}
