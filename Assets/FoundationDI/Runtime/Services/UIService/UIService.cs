using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using UnityEngine.SceneManagement;
using VContainer;

namespace DarkNaku.FoundationDI
{
    public sealed class UIService : IUIService, IUIElementHost, IDisposable
    {
        private readonly UIServiceSettings _settings;
        private readonly UIInstanceFactory _factory;
        private readonly IResourceService _resource;
        private readonly OperationQueue _queue = new();
        private readonly PageController _pages = new();
        private readonly PopupController _popups = new();
        private readonly OverlayController _overlays = new();
        private readonly HashSet<UIPresenter> _active = new();
        // persistent WithOverlay로 현재 화면에 상주 중인 오버레이(타입키 단일 인스턴스). 페이지 전환 이전 대상.
        private readonly Dictionary<Type, UIPresenter> _persistentOverlays = new();
        private UIRoot _root;
        private PoolManager _pool;
        private bool _disposed;

        internal UIService(UIServiceSettings settings, UIInstanceFactory factory, IResourceService resource)
        {
            _settings = settings;
            _factory = factory;
            _resource = resource;
            SceneManager.activeSceneChanged += OnActiveSceneChanged;
        }

        private UIRoot Root
        {
            get
            {
                // 캔버스는 DontDestroyOnLoad라 정상적으로는 파괴되지 않는다.
                // 예외적으로 GO가 파괴되면(fake-null) 참조를 버리고 재구성한다.
                if (_root != null && _root.GO == null) DiscardRoot();
                return _root ??= new UIRoot(_settings != null ? _settings.ReferenceResolution : default);
            }
        }

        // 전용 풀: 상주 Canvas 아래에 위치한다. 씬 전환 시 dispose되고 다음 표시에서 재구성된다.
        private PoolManager Pool => _pool ??= new PoolManager(_resource, null, Root.GO.transform);

        public bool IsPopupVisible => _popups.All.Count > 0;

        public T Page<T>() where T : UIPresenter
        {
            if (_disposed) throw new ObjectDisposedException(nameof(UIService));
            var presenter = (T)_factory.CreatePresenter(typeof(T), this);
            _queue.Enqueue(ct => ShowPageAsync(presenter, ct));
            return presenter;
        }

        public T Popup<T>() where T : UIPresenter
        {
            if (_disposed) throw new ObjectDisposedException(nameof(UIService));
            var presenter = (T)_factory.CreatePresenter(typeof(T), this);
            _queue.Enqueue(ct => ShowPopupAsync(presenter, ct));
            return presenter;
        }

        public T Overlay<T>() where T : UIPresenter
        {
            if (_disposed) throw new ObjectDisposedException(nameof(UIService));
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
                    $"[UIService] '{key}' View 로드 실패(프리팹 없음 또는 UIView 부재). ({presenter.GetType().Name})");

            presenter.BindView(view);
            presenter.OnInitialize();
            _active.Add(presenter);
        }

        private async Awaitable ShowPageAsync(UIPresenter presenter, CancellationToken ct)
        {
            await Awaitable.NextFrameAsync(ct);   // 빌더 체인 등록 보장

            if (_pages.Current != null && _pages.Current != presenter)
            {
                // 새 페이지가 persistent로 재요청하는 오버레이 타입은 teardown하지 않고 이전한다.
                await HideHostAsync(_pages.Current, CarryOverTypes(presenter), ct);
                _pages.Clear();
            }

            AcquireView(presenter);
            _pages.SetCurrent(presenter);
            AttachTo(presenter, Root.PageLayer);

            await ShowHostWithOverlaysAsync(presenter, ct);
        }

        // 들어오는 호스트가 persistent로 요청하며 현재 상주 중인 오버레이 타입 집합(전환 시 유지 대상).
        private HashSet<Type> CarryOverTypes(UIPresenter incoming)
        {
            HashSet<Type> set = null;
            var reqs = incoming.OverlayRequests;
            if (reqs != null)
                for (int i = 0; i < reqs.Count; i++)
                    if (reqs[i].persistent && _persistentOverlays.ContainsKey(reqs[i].type))
                        (set ??= new()).Add(reqs[i].type);
            return set;
        }

        private async Awaitable ShowOverlayAsync(UIPresenter presenter, CancellationToken ct)
        {
            await Awaitable.NextFrameAsync(ct);

            AcquireView(presenter);
            var above = (presenter as IOverlayPlacement)?.Above ?? true;
            _overlays.Register(presenter, above);
            AttachTo(presenter, above ? Root.AboveOverlayLayer : Root.BelowOverlayLayer);
            RefreshInputBlocking();
            await ShowAsync(presenter, ct);
        }

        private async Awaitable ShowPopupAsync(UIPresenter presenter, CancellationToken ct)
        {
            await Awaitable.NextFrameAsync(ct);

            AcquireView(presenter);
            _popups.Add(presenter);
            AttachTo(presenter, Root.PopupLayer);
            await ShowHostWithOverlaysAsync(presenter, ct);
        }

        // 호스트(Page/Popup)와 WithOverlay로 링크된 오버레이들을 동시에(concurrent) 표시한다.
        private async Awaitable ShowHostWithOverlaysAsync(UIPresenter host, CancellationToken ct)
        {
            var shows = new List<Awaitable>();
            shows.Add(ShowAsync(host, ct)); // 호스트 표시 시작(첫 await까지 동기 실행 → 이후 프레임루프에서 진행)

            var reqs = host.OverlayRequests;
            if (reqs != null)
            {
                for (int i = 0; i < reqs.Count; i++)
                {
                    var (type, persistent, configure) = reqs[i];

                    // persistent 이전: 이미 상주 중인 동일 타입 인스턴스를 재사용(재표시/재초기화 없음).
                    if (persistent && _persistentOverlays.TryGetValue(type, out var existing))
                    {
                        host.LinkOverlay(existing); // 소유권만 이전
                        continue;
                    }

                    var overlay = _factory.CreatePresenter(type, this);
                    configure?.Invoke(overlay); // params만(View 바인딩/OnInitialize 전)
                    AcquireView(overlay);
                    var above = (overlay as IOverlayPlacement)?.Above ?? true;
                    _overlays.Register(overlay, above);
                    AttachTo(overlay, above ? Root.AboveOverlayLayer : Root.BelowOverlayLayer);
                    overlay.SetTransitionOverride(host.TransitionOverride); // 호스트와 동일 트랜지션(오버라이드 시)
                    host.LinkOverlay(overlay);
                    if (persistent) _persistentOverlays[type] = overlay;
                    shows.Add(ShowAsync(overlay, ct)); // 오버레이 표시 시작
                }
            }

            RefreshInputBlocking();
            for (int i = 0; i < shows.Count; i++) await shows[i]; // 모두 동시 진행 후 완료 대기
        }

        // 호스트를 숨기고, 링크된 오버레이도 함께(concurrent) 숨긴다.
        // carryTypes에 속한 타입의 오버레이는 teardown하지 않고 상주시킨다(다음 페이지로 이전).
        private async Awaitable HideHostAsync(UIPresenter host, HashSet<Type> carryTypes, CancellationToken ct)
        {
            var linked = host.LinkedOverlays;

            var hides = new List<Awaitable>();
            hides.Add(HideAsync(host, ct));
            if (linked != null)
                for (int i = 0; i < linked.Count; i++)
                {
                    var ov = linked[i];
                    if (carryTypes != null && carryTypes.Contains(ov.GetType())) continue; // 이전 대상 → 유지
                    hides.Add(HideAsync(ov, ct));
                }

            for (int i = 0; i < hides.Count; i++) await hides[i];

            if (linked != null)
            {
                for (int i = 0; i < linked.Count; i++)
                {
                    var ov = linked[i];
                    if (carryTypes != null && carryTypes.Contains(ov.GetType())) continue; // 유지된 인스턴스는 정리 안 함
                    _overlays.Unregister(ov);
                    _persistentOverlays.Remove(ov.GetType()); // persistent였다면 상주 목록에서 제거(no-op if 없음)
                }
                host.ClearLinkedOverlays();
            }
        }

        private void AttachTo(UIPresenter presenter, Transform layer) => presenter.ViewBase.RectTransform.SetParent(layer, false);

        private async Awaitable ShowAsync(UIPresenter presenter, CancellationToken ct)
        {
            presenter.ViewBase.gameObject.SetActive(true); // 풀에서 나온 비활성 View 활성화
            presenter.ViewBase.Transition = presenter.TransitionOverride;
            presenter.OnBeforeShow();
            presenter.Fire(UIPresenter.LifecycleEvent.BeforeShow);
            await presenter.ViewBase.ShowAsync(ct);
            presenter.OnAfterShow();
            presenter.Fire(UIPresenter.LifecycleEvent.AfterShow);
        }

        private async Awaitable HideAsync(UIPresenter presenter, CancellationToken ct)
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

        private async Awaitable HandleHideAsync(UIPresenter presenter, CancellationToken ct)
        {
            // 이미 숨겨졌거나 교체된 경우 중복 Hide 무시.
            if (!_active.Contains(presenter)) return;
            // 명시적 hide(교체 아님) → 이전 대상 없음. persistent 오버레이도 함께 숨긴다.
            await HideHostAsync(presenter, null, ct);
            if (_pages.Current == presenter) _pages.Clear();
            _popups.Remove(presenter);
            _overlays.Unregister(presenter);
            RefreshInputBlocking();
        }

        private void OnActiveSceneChanged(Scene previous, Scene next)
        {
            if (_disposed || _root == null) return;
            // 캔버스(DontDestroyOnLoad)는 유지하고 자식 UI만 전부 clear한다.
            ClearContent();
        }

        // 씬 전환 시: 활성 UI를 전부 teardown하고 풀을 dispose한다. 캔버스는 유지한다.
        private void ClearContent()
        {
            _queue.CancelAndClear();

            foreach (var p in _active)
            {
                p.OnBeforeHide(); p.Fire(UIPresenter.LifecycleEvent.BeforeHide);
                p.OnAfterHide(); p.Fire(UIPresenter.LifecycleEvent.AfterHide);
            }

            _active.Clear();
            _pages.Clear();
            _popups.Clear();
            _overlays.Clear();
            _persistentOverlays.Clear();

            _pool?.Dispose(); // 풀은 캔버스 아래에서 재구성되므로 비운다(다음 표시에서 재생성).
            _pool = null;
        }

        // GO가 외부에서 파괴된 경우(fake-null): 이미 없는 GO를 Destroy하지 않고 참조만 버린다.
        private void DiscardRoot()
        {
            _pool?.Dispose();
            _pool = null;
            _root = null;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            SceneManager.activeSceneChanged -= OnActiveSceneChanged;

            // 서비스 종료(=지속 루트 스코프 dispose) 시에만 상주 캔버스를 실제로 파괴한다.
            ClearContent();
            if (_root != null && _root.GO != null) UnityEngine.Object.Destroy(_root.GO);
            _root = null;
        }
    }

    internal interface IOverlayPlacement { bool Above { get; } }

    public static class UIServiceVContainerExtensions
    {
        /// <summary>
        /// UIService를 컨테이너에 등록한다.
        /// 전제: 호출 전에 <see cref="IResourceService"/>가 이미 등록되어 있어야 한다
        /// (UIService 전용 풀과 UIInstanceFactory가 이를 사용).
        /// </summary>
        public static void RegisterUIService(this IContainerBuilder builder, UIServiceSettings settings)
        {
            builder.RegisterInstance(settings);
            builder.Register<UIInstanceFactory>(Lifetime.Singleton);
            builder.Register<UIService>(Lifetime.Singleton).As<IUIService>();
        }
    }
}
