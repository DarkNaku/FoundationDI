using System;
using UnityEngine;

namespace DarkNaku.FoundationDI
{
    /// <summary>
    /// 살아있는 타깃 참조. 팝업이 닫혀 타깃이 사라졌다 다시 나타나는 것을 이 핸들이 흡수한다.
    /// 소비자(모듈·트리거)는 Current를 읽고 Changed를 구독한다.
    /// </summary>
    public sealed class TutorialTargetHandle : IDisposable
    {
        private Transform _current;
        private bool _disposed;

        public TutorialTargetHandle(Transform current)
        {
            _current = current;
        }

        /// <summary>파괴된 Transform은 null로 보인다(Unity의 fake-null).</summary>
        public Transform Current => _current == null ? null : _current;

        public bool IsDisposed => _disposed;

        public event Action<Transform> Changed;

        public void SetCurrent(Transform target)
        {
            if (_disposed) return;
            if (ReferenceEquals(_current, target)) return;

            _current = target;
            Changed?.Invoke(Current);
        }

        public void Dispose()
        {
            if (_disposed) return;

            _disposed = true;
            Changed = null;
            _current = null;
        }
    }
}
