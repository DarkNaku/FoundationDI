using UnityEngine;

namespace DarkNaku.FoundationDI
{
    [CreateAssetMenu(fileName = "ReviewServiceSettings", menuName = "FoundationDI/Review Service Settings")]
    public sealed class ReviewServiceSettings : ScriptableObject
    {
        [Tooltip("켜면 Play In-App Review 라이브러리(com.google.android.play:review) 의존성 XML을 " +
                 "Assets/FoundationDI.Data/Editor/에 만들어 EDM4U가 받게 한다. 끄면 지우고 Android 리뷰 창을 띄우지 않는다.")]
        [SerializeField] private bool _usePlayInAppReview = true;

        public bool UsePlayInAppReview => _usePlayInAppReview;
    }
}
