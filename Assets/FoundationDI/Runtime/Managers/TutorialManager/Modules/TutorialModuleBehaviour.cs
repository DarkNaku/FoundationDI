using System.Threading;
using UnityEngine;

namespace DarkNaku.FoundationDI
{
    /// <summary>
    /// 연출 모듈의 MonoBehaviour 기반. 프레임 추적은 여기(LateUpdate)에서만 한다 —
    /// 진행 엔진에는 프레임 펌프가 들어가지 않는다.
    ///
    /// 타깃을 자식으로 삼거나 리페어런팅하지 않고 스크린 rect만 읽는다.
    /// 그래서 타깃이 UIRoot(DontDestroyOnLoad) 안에 있든 씬 캔버스에 있든 3D 월드에 있든
    /// 동일하게 동작한다.
    /// </summary>
    public abstract class TutorialModuleBehaviour : MonoBehaviour, ITutorialModule
    {
        [SerializeField] private Camera _targetCamera;

        private TutorialTargetHandle _handle;

        protected Camera TargetCamera => _targetCamera != null ? _targetCamera : Camera.main;

        protected Transform Target => _handle?.Current;

        public virtual Awaitable ShowAsync(TutorialTargetHandle target, CancellationToken token)
        {
            _handle = target;

            gameObject.SetActive(true);

            Track();

            return Completed();
        }

        public virtual Awaitable HideAsync(CancellationToken token)
        {
            _handle = null;

            gameObject.SetActive(false);

            return Completed();
        }

        protected virtual void LateUpdate() => Track();

        /// <summary>타깃의 스크린 rect가 유효할 때 매 프레임 호출된다.</summary>
        protected abstract void OnTrack(Rect screenRect);

        /// <summary>타깃이 사라졌을 때 호출된다. 연출을 감추는 게 보통이다.</summary>
        protected abstract void OnTargetLost();

        protected static Awaitable Completed()
        {
            var source = new AwaitableCompletionSource();
            source.SetResult();

            return source.Awaitable;
        }

        private void Track()
        {
            if (_handle == null) return;

            if (TutorialScreenRect.TryGet(_handle.Current, TargetCamera, out var rect))
            {
                OnTrack(rect);
                return;
            }

            OnTargetLost();
        }
    }
}
