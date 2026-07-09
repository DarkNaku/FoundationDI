using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using VContainer;

namespace DarkNaku.FoundationDI
{
    public sealed class UIManager : IUIManager, IUIElementHost, IDisposable
    {
        private readonly UIManagerSettings _settings;
        private readonly UIInstanceFactory _factory;
        private readonly IResourceService _resource;
        private readonly OperationQueue _queue = new();
        private readonly PageController _pages = new();
        private readonly PopupController _popups = new();
        private readonly OverlayController _overlays = new();
        private readonly HashSet<UIPresenter> _active = new();
        private UIRoot _root;
        private PoolManager _pool;
        private bool _disposed;

        internal UIManager(UIManagerSettings settings, UIInstanceFactory factory, IResourceService resource)
        {
            _settings = settings;
            _factory = factory;
            _resource = resource;
        }

        private UIRoot Root => _root ??= new UIRoot(_settings != null ? _settings.ReferenceResolution : default);

        // 전용 풀: 루트를 Canvas(DontDestroyOnLoad) 아래에 둬 UIManager와 수명을 함께한다.
        private PoolManager Pool => _pool ??= new PoolManager(_resource, Root.GO.transform);

        public T Page<T>() where T : UIPresenter
        {
            if (_disposed) throw new ObjectDisposedException(nameof(UIManager));
            var presenter = (T)_factory.CreatePresenter(typeof(T), this);
            _queue.Enqueue(ct => ShowPageAsync(presenter, ct));
            return presenter;
        }

        public T Popup<T>() where T : UIPresenter
        {
            if (_disposed) throw new ObjectDisposedException(nameof(UIManager));
            var presenter = (T)_factory.CreatePresenter(typeof(T), this);
            _queue.Enqueue(ct => ShowPopupAsync(presenter, ct));
            return presenter;
        }

        public T Overlay<T>() where T : UIPresenter
        {
            if (_disposed) throw new ObjectDisposedException(nameof(UIManager));
            var presenter = (T)_factory.CreatePresenter(typeof(T), this);
            _queue.Enqueue(ct => ShowOverlayAsync(presenter, ct));
            return presenter;
        }

        // View 획득을 큐 안에서 수행 → 이전 Page Hide 완료(View 반환) 후 Pool.Get 보장
        private void AcquireView(UIPresenter presenter)
        {
            var key = UIPrefabKeyResolver.Resolve(presenter.GetType());
            var view = Pool.Get<UIView>(key);
            if (view == null)
                throw new InvalidOperationException(
                    $"[UIManager] '{key}' View 로드 실패(프리팹 없음 또는 UIView 부재). ({presenter.GetType().Name})");

            presenter.BindView(view);
            presenter.OnInitialize();
            _active.Add(presenter);
        }

        private async UniTask ShowPageAsync(UIPresenter presenter, CancellationToken ct)
        {
            await UniTask.Yield(PlayerLoopTiming.Update, ct);   // 빌더 체인 등록 보장

            if (_pages.Current != null && _pages.Current != presenter)
            {
                await HideAsync(_pages.Current, ct);
                _pages.Clear();
            }

            AcquireView(presenter);
            _pages.SetCurrent(presenter);
            AttachTo(presenter, Root.PageLayer);
            RefreshInputBlocking();

            await ShowAsync(presenter, ct);
        }

        private async UniTask ShowOverlayAsync(UIPresenter presenter, CancellationToken ct)
        {
            await UniTask.Yield(PlayerLoopTiming.Update, ct);

            AcquireView(presenter);
            var above = (presenter as IOverlayPlacement)?.Above ?? true;
            _overlays.Register(presenter, above);
            AttachTo(presenter, above ? Root.AboveOverlayLayer : Root.BelowOverlayLayer);
            RefreshInputBlocking();
            await ShowAsync(presenter, ct);
        }

        private async UniTask ShowPopupAsync(UIPresenter presenter, CancellationToken ct)
        {
            await UniTask.Yield(PlayerLoopTiming.Update, ct);

            AcquireView(presenter);
            _popups.Add(presenter);
            AttachTo(presenter, Root.PopupLayer);
            RefreshInputBlocking();
            await ShowAsync(presenter, ct);
        }

        private void AttachTo(UIPresenter presenter, Transform layer) => presenter.ViewBase.RectTransform.SetParent(layer, false);

        private async UniTask ShowAsync(UIPresenter presenter, CancellationToken ct)
        {
            presenter.ViewBase.gameObject.SetActive(true); // 풀에서 나온 비활성 View 활성화
            presenter.ViewBase.Transition = presenter.TransitionOverride;
            presenter.OnBeforeShow();
            presenter.Fire(UIPresenter.LifecycleEvent.BeforeShow);
            await presenter.ViewBase.ShowAsync(ct);
            presenter.OnAfterShow();
            presenter.Fire(UIPresenter.LifecycleEvent.AfterShow);
        }

        private async UniTask HideAsync(UIPresenter presenter, CancellationToken ct)
        {
            presenter.OnBeforeHide(); presenter.Fire(UIPresenter.LifecycleEvent.BeforeHide);
            await presenter.ViewBase.HideAsync(ct);
            presenter.OnAfterHide(); presenter.Fire(UIPresenter.LifecycleEvent.AfterHide); // teardown 지점

            _active.Remove(presenter);
            Pool.Release(presenter.ViewBase.gameObject);   // OnReleaseItem: SetActive(false)
        }

        private void RefreshInputBlocking()
        {
            bool hasModal = _popups.All.Count > 0;

            if (_pages.Current != null)
            {
                _pages.Current.ViewBase.InputEnabled = !hasModal;
            }

            var below = _overlays.Below;
            for (int i = 0; i < below.Count; i++)
            {
                below[i].ViewBase.InputEnabled = !hasModal;
            }

            var above = _overlays.Above;
            for (int i = 0; i < above.Count; i++)
            {
                above[i].ViewBase.InputEnabled = true;
            }

            var popups = _popups.All;
            for (int i = 0; i < popups.Count; i++)
            {
                popups[i].ViewBase.InputEnabled = (i == popups.Count - 1);
            }
        }

        void IUIElementHost.RequestHide(UIPresenter presenter) => _queue.Enqueue(ct => HandleHideAsync(presenter, ct));

        private async UniTask HandleHideAsync(UIPresenter presenter, CancellationToken ct)
        {
            // 이미 숨겨졌거나 교체된 경우 중복 Hide 무시.
            if (!_active.Contains(presenter)) return;
            await HideAsync(presenter, ct);
            if (_pages.Current == presenter) _pages.Clear();
            _popups.Remove(presenter);
            _overlays.Unregister(presenter);
            RefreshInputBlocking();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _queue.CancelAndClear();

            // 활성 presenter teardown: 트랜지션 없이 OnBeforeHide→OnAfterHide 동기 발화(구독 해제).
            foreach (var p in _active)
            {
                p.OnBeforeHide(); p.Fire(UIPresenter.LifecycleEvent.BeforeHide);
                p.OnAfterHide(); p.Fire(UIPresenter.LifecycleEvent.AfterHide);
            }

            _active.Clear();
            _pages.Clear();
            _popups.Clear();
            _overlays.Clear();

            _pool?.Dispose(); // OnDestroyItem으로 전 View 파괴 + IResourceService.Release
            if (_root != null && _root.GO != null) UnityEngine.Object.Destroy(_root.GO);
        }
    }

    internal interface IOverlayPlacement { bool Above { get; } }

    public static class UIManagerVContainerExtensions
    {
        /// <summary>
        /// UIManager를 컨테이너에 등록한다.
        /// 전제: 호출 전에 <see cref="IResourceService"/>가 이미 등록되어 있어야 한다
        /// (UIManager 전용 풀과 UIInstanceFactory가 이를 사용).
        /// </summary>
        public static void RegisterUIManager(this IContainerBuilder builder, UIManagerSettings settings)
        {
            builder.RegisterInstance(settings);
            builder.Register<UIInstanceFactory>(Lifetime.Singleton);
            builder.Register<UIManager>(Lifetime.Singleton).As<IUIManager>();
        }
    }
}
