using System;
using System.Collections.Generic;
using UnityEngine;
using VContainer;
using VContainer.Unity;

namespace DarkNaku.FoundationDI
{
    /// <summary>
    /// 씬에 배치된(컨테이너가 생성하지 않은) MonoBehaviour에 의존성을 주입하는 진입점.
    /// 컴포넌트는 <see cref="Request"/>로 자신을 등록한다. 컨테이너가 준비되어 있으면
    /// 즉시 주입하고, 아니면 보류했다가 <see cref="Start"/> 시점에 일괄 주입한다.
    /// </summary>
    public sealed class InjectorService : IStartable, IDisposable
    {
        private static IObjectResolver _resolver;
        private static readonly List<MonoBehaviour> _pending = new();

        private readonly IObjectResolver _resolverToBind;

        public InjectorService(IObjectResolver resolver)
        {
            _resolverToBind = resolver;
        }

        public void Start()
        {
            _resolver = _resolverToBind;

            foreach (var target in _pending)
            {
                if (target != null)
                {
                    _resolver.Inject(target);
                }
            }

            _pending.Clear();
        }

        public static void Request(MonoBehaviour target)
        {
            if (target == null) return;

            if (_resolver != null)
            {
                _resolver.Inject(target);
                return;
            }

            _pending.Add(target);
        }

        public void Dispose()
        {
            _resolver = null;
            _pending.Clear();
        }
    }
}
