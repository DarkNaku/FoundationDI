using UnityEngine;
using VContainer;

namespace DarkNaku.FoundationDI
{
    /// <summary>
    /// 런타임에 생성되는 UI 요소를 튜토리얼이 키로 가리킬 수 있게 한다.
    /// UI 프리팹의 버튼 등에 붙여두면 UIService가 그 View를 띄울 때마다 자동으로 등록된다.
    /// UIService는 이 컴포넌트의 존재를 모르고, 튜토리얼도 UIService에 의존하지 않는다.
    /// </summary>
    public sealed class TutorialTarget : InjectableBehaviour
    {
        [SerializeField] private string _key;

        [Inject] private ITutorialTargetRegistry _registry;

        private bool _registered;

        public string Key => _key;

        private void OnEnable()
        {
            EnsureInjected();
            TryRegister();
        }

        private void OnDisable()
        {
            if (!_registered) return;

            _registered = false;
            _registry?.Unregister(_key, transform);
        }

        // 주입 시점은 컨테이너 준비에 달려 있어 OnEnable보다 늦을 수 있다.
        // 등록이 끝날 때까지만 폴링한다(끝나면 아래 첫 줄에서 바로 반환).
        // enabled를 끄지 않는 이유: OnDisable이 불려 방금 한 등록을 스스로 취소하기 때문이다.
        private void Update()
        {
            if (_registered) return;

            TryRegister();
        }

        private void TryRegister()
        {
            if (_registered) return;
            if (_registry == null) return;
            if (string.IsNullOrWhiteSpace(_key)) return;
            if (!isActiveAndEnabled) return;

            _registered = true;
            _registry.Register(_key, transform);
        }
    }
}
