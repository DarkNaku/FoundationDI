using UnityEngine;

namespace DarkNaku.FoundationDI
{
    public sealed class PlayerPrefsPolicyAgreementStorage : IPolicyAgreementStorage
    {
        internal const string Key = "FoundationDI.Consent.PolicyVersion";

        public int LoadAcceptedVersion() => PlayerPrefs.GetInt(Key, 0);

        public void SaveAcceptedVersion(int version)
        {
            PlayerPrefs.SetInt(Key, version);

            // 수락 직후 앱이 강제 종료되면 다음 실행에 다시 묻게 된다. 저장을 미루지 않는다.
            PlayerPrefs.Save();
        }
    }
}
