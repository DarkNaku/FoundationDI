using System.Collections.Generic;
using UnityEngine;

namespace DarkNaku.FoundationDI
{
    [CreateAssetMenu(fileName = "ConsentServiceSettings", menuName = "FoundationDI/Consent Service Settings")]
    public class ConsentServiceSettings : ScriptableObject
    {
        [Header("Policy (약관·개인정보처리방침)")]
        // 기본 문구가 영어인 이유: 기본 프리팹은 TMP 기본 폰트(LiberationSans)를 쓰는데 한글·일본어 등
        // CJK 글리프가 없어 네모로 깨진다. 다른 언어는 스킨 프리팹에 해당 폰트를 넣고 문구를 바꾸거나
        // IConsentService.SetPolicyContent로 넘긴다.
        [Tooltip("약관을 바꾸면 올린다. 저장된 수락 버전보다 크면 다시 묻는다. 0이면 약관 단계를 쓰지 않는다.")]
        [SerializeField, Min(0)] private int _policyVersion = 1;

        [SerializeField] private string _title = "Terms of Service & Privacy Policy";
        [SerializeField, TextArea(3, 8)] private string _message =
            "To start the game, please agree to our Terms of Service and Privacy Policy.";
        [SerializeField] private string _acceptLabel = "Agree and Start";
        [SerializeField] private string _termsLabel = "Terms of Service";
        [SerializeField] private string _termsUrl = "";
        [SerializeField] private string _privacyLabel = "Privacy Policy";
        [SerializeField] private string _privacyUrl = "";

        [Tooltip("약관 수락 창 프리팹. 루트에 PolicyAgreementView가 있어야 한다. 비우면 기본 프리팹을 쓴다.")]
        [SerializeField] private PolicyAgreementView _promptPrefab;

        [Header("Google UMP")]
        [Tooltip("켜면 에디터가 UMP 네이티브 의존성 XML을 생성하고 런타임이 UMP 동의를 받는다. " +
                 "AdMob 콘솔의 '개인 정보 보호 및 메시지'에서 GDPR 메시지를 만들어 둬야 한다.")]
        [SerializeField] private bool _useUmp;

        [Tooltip("ca-app-pub-XXXX~YYYY. LevelPlay AdMob 어댑터가 이미 넣었다면 비워 둬도 된다.")]
        [SerializeField] private string _adMobAppIdAndroid = "";
        [SerializeField] private string _adMobAppIdIos = "";

        [Tooltip("개발 빌드에서만 적용된다.")]
        [SerializeField] private UmpDebugGeography _debugGeography = UmpDebugGeography.Disabled;
        [SerializeField] private List<string> _testDeviceHashedIds = new();
        [SerializeField] private bool _tagForUnderAgeOfConsent;

        [Tooltip("UMP 동의 정보 갱신(네트워크)을 기다리는 최대 시간(초).")]
        [SerializeField, Min(1f)] private float _umpUpdateTimeoutSeconds = 10f;

        [Header("iOS Tracking (ATT)")]
        [Tooltip("UMP 다음에 ATT 다이얼로그를 띄운다. 사전 안내 화면을 먼저 보여 주는 등 시점을 직접 " +
                 "고르려면 끄고 IConsentService.TrackingAuthorization.RequestAsync()를 부른다.")]
        [SerializeField] private bool _requestTrackingAuthorization = true;

        public bool UseUmp => _useUmp;
        public string AdMobAppIdAndroid => _adMobAppIdAndroid;
        public string AdMobAppIdIos => _adMobAppIdIos;
        public PolicyAgreementView PromptPrefab => _promptPrefab;

        public ConsentServiceOptions ToOptions(bool isDebugBuild)
        {
            var content = new PolicyAgreementContent(_title, _message, _acceptLabel,
                                                     _termsLabel, _termsUrl, _privacyLabel, _privacyUrl);

            var geography = isDebugBuild ? _debugGeography : UmpDebugGeography.Disabled;
            var umpRequest = new UmpRequestOptions(geography, new List<string>(_testDeviceHashedIds),
                                                   _tagForUnderAgeOfConsent);

            return new ConsentServiceOptions(Mathf.Max(0, _policyVersion), content, _useUmp, umpRequest,
                                             _requestTrackingAuthorization,
                                             Mathf.Max(1f, _umpUpdateTimeoutSeconds));
        }

#if UNITY_EDITOR
        // 새 설정 에셋을 만들면 패키지 기본 프리팹으로 채운다. 런타임에서 Resources로 찾지 않기 위해서다.
        private void Reset()
        {
            const string defaultPrefabPath =
                "Packages/com.darknaku.foundationdi/Runtime/Services/ConsentService/Policy/PolicyAgreementView.prefab";
            const string hostPrefabPath =
                "Assets/FoundationDI/Runtime/Services/ConsentService/Policy/PolicyAgreementView.prefab";

            _promptPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<PolicyAgreementView>(defaultPrefabPath)
                            ?? UnityEditor.AssetDatabase.LoadAssetAtPath<PolicyAgreementView>(hostPrefabPath);
        }
#endif
    }
}
