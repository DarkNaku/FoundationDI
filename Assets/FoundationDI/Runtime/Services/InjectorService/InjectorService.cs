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
                    TryInject(target);
                }
            }

            _pending.Clear();
        }

        public static void Request(MonoBehaviour target)
        {
            if (target == null) return;

            if (_resolver != null)
            {
                TryInject(target);
                return;
            }

            _pending.Add(target);
        }

        /// <summary>
        /// 주입 실패를 대상 하나에 가둔다. 예외를 삼키는 게 아니라 치명적이지 않게 만든다 —
        /// 콘솔에는 그대로 남고, 나머지 보류분과 호출자(컴포넌트의 Awake 등)는 계속 진행한다.
        /// 격리가 없으면 미등록 서비스를 요구하는 컴포넌트 하나가 뒤 순번 전체의 주입을 막는다.
        /// </summary>
        private static void TryInject(MonoBehaviour target)
        {
            try
            {
                _resolver.Inject(target);
            }
            catch (Exception e)
            {
                Debug.LogException(e, target);
            }
        }

        public void Dispose()
        {
            _resolver = null;
            _pending.Clear();
        }
    }

    public static class InjectorVContainerExtensions
    {
        /// <summary>
        /// 씬 컴포넌트 주입 인프라(InjectorService)를 EntryPoint로 등록한다.
        /// 호스트 LifetimeScope의 Configure에서 호출한다.
        /// </summary>
        /// <remarks>
        /// 반드시 루트 LifetimeScope에서 한 번만 호출한다. InjectorService는 정적 컨테이너
        /// 참조를 공유하므로(단일 컨테이너 모델), 자식 스코프에서 중복 등록하면 루트의
        /// 주입 대상이 깨질 수 있다.
        /// </remarks>
        public static void RegisterInjector(this IContainerBuilder builder)
        {
            builder.RegisterEntryPoint<InjectorService>();
        }
    }
}
