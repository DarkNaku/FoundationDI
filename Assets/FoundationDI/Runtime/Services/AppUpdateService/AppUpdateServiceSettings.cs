using UnityEngine;

namespace DarkNaku.FoundationDI
{
    [CreateAssetMenu(fileName = "AppUpdateServiceSettings", menuName = "FoundationDI/App Update Service Settings")]
    public sealed class AppUpdateServiceSettings : ScriptableObject
    {
        [Tooltip("켜면 Play In-App Updates 라이브러리(com.google.android.play:app-update) 의존성 XML을 " +
                 "Assets/FoundationDI.Data/Editor/에 만들어 EDM4U가 받게 한다. 끄면 지우고 Android에서는 확인하지 않는다(Unknown).")]
        [SerializeField] private bool _usePlayAppUpdate = true;

        [Tooltip("App Store 조회에 쓸 스토어 국가 코드(ISO 3166-1 alpha-2, 예: kr, jp, us). 비우면 애플이 미국 스토어를 본다 — " +
                 "미국에 출시하지 않은 앱은 결과가 비어 늘 Unknown이 된다.")]
        [SerializeField] private string _appStoreCountry = "";

        public bool UsePlayAppUpdate => _usePlayAppUpdate;

        public string AppStoreCountry => _appStoreCountry;
    }
}
