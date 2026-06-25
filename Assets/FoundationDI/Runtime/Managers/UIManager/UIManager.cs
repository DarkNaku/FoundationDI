using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace DarkNaku.FoundationDI
{
    public sealed class UIManager : IUIManager, IUIElementHost, IDisposable
    {
        private readonly UIManagerSettings _settings;
        private readonly UIInstanceFactory _factory;
        private readonly ShowQueue _queue = new();
        private readonly InstanceCache _cache = new();
        private readonly PageController _pages = new();
        private readonly PopupController _popups = new();
        private readonly OverlayController _overlays = new();
        private readonly Dictionary<Type, UIPresenterBase> _active = new();
        private readonly UIRoot _root;
        private bool _disposed;

        internal UIManager(UIManagerSettings settings, UIInstanceFactory factory)
        {
            _settings = settings;
            _factory = factory;
            _root = new UIRoot();
        }

        public T Page<T>() where T : UIPresenterBase
            => Acquire<T>(inst => _queue.Enqueue(ct => ShowPageAsync(inst, ct)));
        public T Popup<T>() where T : UIPresenterBase
            => Acquire<T>(inst => _queue.Enqueue(ct => ShowPopupAsync(inst, ct)));
        public T Overlay<T>() where T : UIPresenterBase
            => Acquire<T>(inst => _queue.Enqueue(ct => ShowOverlayAsync(inst, ct)));

        private T Acquire<T>(Action<UIPresenterBase> enqueueShow) where T : UIPresenterBase
        {
            if (_disposed) throw new ObjectDisposedException(nameof(UIManager));
            var type = typeof(T);

            if (_active.TryGetValue(type, out var existing))
            {
                Debug.LogWarning($"[UIManager] {type.Name} 이미 활성. 중복 요청 무시.");
                return (T)existing;
            }

            UIPresenterBase instance;
            if (_cache.TryGet(type, out var cached)) instance = (UIPresenterBase)cached;
            else instance = _factory.Create(type, this);

            _active[type] = instance;
            enqueueShow(instance);
            return (T)instance;
        }

        private async UniTask ShowPageAsync(UIPresenterBase inst, CancellationToken ct)
        {
            await UniTask.Yield(PlayerLoopTiming.Update, ct);   // 체인 등록 보장
            if (_pages.Active != null && _pages.Active != inst)
            {
                await HideCoreAsync(_pages.Active, _root.PageLayer, ct);
                _pages.Clear();
            }
            _pages.SetActive(inst);
            AttachTo(inst, _root.PageLayer);
            await ShowCoreAsync(inst, ct);
        }

        private async UniTask ShowPopupAsync(UIPresenterBase inst, CancellationToken ct)
        {
            await UniTask.Yield(PlayerLoopTiming.Update, ct);
            _popups.Push(inst);
            AttachTo(inst, _root.PopupLayer);
            UpdatePopupModal();
            await ShowCoreAsync(inst, ct);
        }

        private async UniTask ShowOverlayAsync(UIPresenterBase inst, CancellationToken ct)
        {
            await UniTask.Yield(PlayerLoopTiming.Update, ct);
            var above = (inst as IOverlayPlacement)?.Above ?? true;
            _overlays.Register(inst, above);
            AttachTo(inst, above ? _root.AboveOverlayLayer : _root.BelowOverlayLayer);
            await ShowCoreAsync(inst, ct);
        }

        private void AttachTo(UIPresenterBase inst, Transform layer)
            => inst.ViewBase.RectTransform.SetParent(layer, false);

        private async UniTask ShowCoreAsync(UIPresenterBase inst, CancellationToken ct)
        {
            if (inst.TransitionOverride != null) inst.ViewBase.ShowTransition = inst.TransitionOverride;
            inst.OnBeforeShow(); inst.Fire(UIPresenterBase.LifecycleEvent.BeforeShown);
            await inst.ViewBase.PlayShow(ct);
            inst.OnShow(); inst.Fire(UIPresenterBase.LifecycleEvent.Shown);
            inst.OnAfterShow(); inst.Fire(UIPresenterBase.LifecycleEvent.AfterShown);
        }

        private async UniTask HideCoreAsync(UIPresenterBase inst, Transform layer, CancellationToken ct)
        {
            inst.OnBeforeHide(); inst.Fire(UIPresenterBase.LifecycleEvent.BeforeHidden);
            await inst.ViewBase.PlayHide(ct);
            inst.OnHide(); inst.Fire(UIPresenterBase.LifecycleEvent.Hidden);
            inst.ViewBase.RectTransform.SetParent(null, false);
            inst.OnAfterHide(); inst.Fire(UIPresenterBase.LifecycleEvent.AfterHidden);

            _active.Remove(inst.GetType());
            inst.ResetTransient();
            _cache.Register(inst.GetType(), inst);   // 항상 캐시
            inst.ViewBase.gameObject.SetActive(false);
        }

        private void UpdatePopupModal()
        {
            for (int i = 0; i < _popups.All.Count; i++)
                _popups.All[i].ViewBase.InputEnabled = (i == _popups.All.Count - 1);
        }

        void IUIElementHost.RequestHide(UIPresenterBase e) => _queue.Enqueue(ct => HandleHideAsync(e, ct));
        void IUIElementHost.RequestDestroy(UIPresenterBase e) => _queue.Enqueue(ct => HandleDestroyAsync(e, ct));

        private async UniTask HandleHideAsync(UIPresenterBase e, CancellationToken ct)
        {
            var layer = LayerOf(e);
            await HideCoreAsync(e, layer, ct);
            if (_pages.Active == e) _pages.Clear();
            _popups.Remove(e); UpdatePopupModal();
            _overlays.Unregister(e);
        }

        private async UniTask HandleDestroyAsync(UIPresenterBase e, CancellationToken ct)
        {
            await HandleHideAsync(e, ct);
            _cache.Remove(e.GetType());
            e.OnDestroyElement(); e.Fire(UIPresenterBase.LifecycleEvent.Destroyed);
            if (e.ViewBase != null) UnityEngine.Object.Destroy(e.ViewBase.gameObject);
        }

        private Transform LayerOf(UIPresenterBase e)
        {
            if (_pages.Active == e) return _root.PageLayer;
            if (_popups.All.Contains(e)) return _root.PopupLayer;
            return _overlays.IsAbove(e) ? _root.AboveOverlayLayer : _root.BelowOverlayLayer;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _queue.CancelAndClear();
            if (_root.CanvasObject != null) UnityEngine.Object.Destroy(_root.CanvasObject);
        }
    }

    internal interface IOverlayPlacement { bool Above { get; } }
}
