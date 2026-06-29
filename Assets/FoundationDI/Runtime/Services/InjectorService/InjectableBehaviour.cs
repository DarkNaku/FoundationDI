using UnityEngine;

namespace DarkNaku.FoundationDI
{
    /// <summary>
    /// 씬에 배치되어 의존성 주입이 필요한 MonoBehaviour의 베이스.
    /// Awake에서 자신을 InjectorService에 1회 등록한다(멱등).
    /// </summary>
    public abstract class InjectableBehaviour : MonoBehaviour
    {
        private bool _requested;

        protected virtual void Awake()
        {
            EnsureInjected();
        }

        protected void EnsureInjected()
        {
            if (_requested) return;
            _requested = true;
            InjectorService.Request(this);
        }
    }
}
