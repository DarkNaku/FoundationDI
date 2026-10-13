using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DarkNaku.FoundationDI
{
    // 기본 약관 수락 창의 View. 스킨을 바꾸려면 프리팹을 복제해 디자인만 고치고
    // ConsentServiceSettings.PromptPrefab에 지정한다 — 루트에 이 컴포넌트만 있으면 된다.
    //
    // 리스너를 Awake가 아니라 Bind에서 건다. EditMode에서는 AddComponent/Instantiate에 Awake가
    // 불리지 않아 테스트가 버튼을 눌러 볼 수 없다.
    public class PolicyAgreementView : MonoBehaviour
    {
        [SerializeField] private TMP_Text _title;
        [SerializeField] private TMP_Text _message;
        [SerializeField] private Button _acceptButton;
        [SerializeField] private TMP_Text _acceptLabel;
        [SerializeField] private Button _termsButton;
        [SerializeField] private TMP_Text _termsLabel;
        [SerializeField] private Button _privacyButton;
        [SerializeField] private TMP_Text _privacyLabel;

        private PolicyAgreementContent _content;

        public event Action Accepted;
        public event Action<string> LinkRequested;

        internal TMP_Text TitleText => _title;
        internal TMP_Text AcceptLabelText => _acceptLabel;
        internal Button AcceptButton => _acceptButton;
        internal Button TermsButton => _termsButton;
        internal Button PrivacyButton => _privacyButton;

        public void Bind(PolicyAgreementContent content)
        {
            _content = content;

            SetText(_title, content.Title);
            SetText(_message, content.Message);
            SetText(_acceptLabel, content.AcceptLabel);
            SetText(_termsLabel, content.TermsLabel);
            SetText(_privacyLabel, content.PrivacyLabel);

            // URL이 없는 링크는 눌러도 아무 일이 없으므로 숨긴다.
            SetActive(_termsButton, !string.IsNullOrEmpty(content.TermsUrl));
            SetActive(_privacyButton, !string.IsNullOrEmpty(content.PrivacyUrl));

            Rewire(_acceptButton, OnAccept);
            Rewire(_termsButton, OnTerms);
            Rewire(_privacyButton, OnPrivacy);
        }

        // 프리팹 빌더와 테스트가 참조를 채우는 데 쓴다.
        internal void SetReferences(TMP_Text title, TMP_Text message, Button acceptButton, TMP_Text acceptLabel,
                                    Button termsButton, TMP_Text termsLabel, Button privacyButton, TMP_Text privacyLabel)
        {
            _title = title;
            _message = message;
            _acceptButton = acceptButton;
            _acceptLabel = acceptLabel;
            _termsButton = termsButton;
            _termsLabel = termsLabel;
            _privacyButton = privacyButton;
            _privacyLabel = privacyLabel;
        }

        private void OnAccept() => Accepted?.Invoke();
        private void OnTerms() => LinkRequested?.Invoke(_content.TermsUrl);
        private void OnPrivacy() => LinkRequested?.Invoke(_content.PrivacyUrl);

        private static void Rewire(Button button, UnityEngine.Events.UnityAction action)
        {
            if (button == null) return;

            button.onClick.RemoveListener(action);
            button.onClick.AddListener(action);
        }

        private static void SetText(TMP_Text text, string value)
        {
            if (text != null) text.text = value ?? string.Empty;
        }

        private static void SetActive(Component component, bool active)
        {
            if (component != null) component.gameObject.SetActive(active);
        }
    }
}
