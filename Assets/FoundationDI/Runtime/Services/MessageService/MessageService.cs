using System;
using System.Collections.Generic;
using UnityEngine;

namespace DarkNaku.FoundationDI
{
    public interface IMessageService : IDisposable
    {
        void Publish<T>(T message);
        IDisposable Subscribe<T>(Action<T> handler);
    }

    /// <summary>
    /// 타입을 채널로 삼는 인-메모리 pub-sub. 메시지 타입에 제약은 없다.
    /// 메인 스레드 전제이므로 여러 스레드에서 동시에 호출하면 안 된다.
    /// </summary>
    public class MessageService : IMessageService
    {
        // 값은 항상 Action<key 타입>인 멀티캐스트 델리게이트다. 구독자가 0이 되면 키를 지운다.
        private readonly Dictionary<Type, Delegate> _handlers = new();

        private bool _disposed;

        public void Publish<T>(T message)
        {
            ThrowIfDisposed();

            if (!_handlers.TryGetValue(typeof(T), out var combined)) return;

            // 스냅샷을 떠 두면 핸들러 안에서 구독/해제가 일어나도 현재 발행이 흔들리지 않는다.
            // 또 핸들러를 하나씩 호출해야 앞선 핸들러의 예외가 뒤따르는 핸들러를 막지 않는다.
            var invocations = combined.GetInvocationList();

            for (var i = 0; i < invocations.Length; i++)
            {
                try
                {
                    ((Action<T>)invocations[i]).Invoke(message);
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }
            }
        }

        public IDisposable Subscribe<T>(Action<T> handler)
        {
            ThrowIfDisposed();

            if (handler == null) throw new ArgumentNullException(nameof(handler));

            _handlers.TryGetValue(typeof(T), out var combined);
            _handlers[typeof(T)] = (Action<T>)combined + handler;

            return new Subscription<T>(this, handler);
        }

        public void Dispose()
        {
            if (_disposed) return;

            _disposed = true;
            _handlers.Clear();
        }

        private void Unsubscribe<T>(Action<T> handler)
        {
            if (_disposed) return;

            if (!_handlers.TryGetValue(typeof(T), out var combined)) return;

            var remaining = (Action<T>)combined - handler;

            if (remaining == null)
            {
                _handlers.Remove(typeof(T));
            }
            else
            {
                _handlers[typeof(T)] = remaining;
            }
        }

        private void ThrowIfDisposed()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(MessageService));
        }

        // 해제에 필요한 정보를 직접 들고 있어 클로저를 만들지 않는다. 중복 Dispose는 무해하다.
        private sealed class Subscription<T> : IDisposable
        {
            private MessageService _service;
            private Action<T> _handler;

            public Subscription(MessageService service, Action<T> handler)
            {
                _service = service;
                _handler = handler;
            }

            public void Dispose()
            {
                if (_service == null) return;

                _service.Unsubscribe(_handler);

                _service = null;
                _handler = null;
            }
        }
    }
}
