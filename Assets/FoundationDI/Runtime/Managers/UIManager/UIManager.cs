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
        private readonly OperationQueue _queue = new();
        private readonly InstanceCache _cache = new();
        private readonly PageController _pages = new();
        private readonly PopupController _popups = new();
        private readonly OverlayController _overlays = new();
        private readonly Dictionary<Type, UIPresenterBase> _active = new();
        private UIRoot _root;
        private bool _disposed;

        internal UIManager(UIManagerSettings settings, UIInstanceFactory factory)
        {
            _settings = settings;
            _factory = factory;
        }

        private UIRoot Root => _root ??= new UIRoot();

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
            if (_pages.Current != null && _pages.Current != inst)
            {
                await HideCoreAsync(_pages.Current, Root.PageLayer, ct);
                _pages.Clear();
            }
            _pages.SetCurrent(inst);
            AttachTo(inst, Root.PageLayer);
            await ShowCoreAsync(inst, _settings?.DefaultPageTransition, ct);
        }

        private async UniTask ShowPopupAsync(UIPresenterBase inst, CancellationToken ct)
        {
            await UniTask.Yield(PlayerLoopTiming.Update, ct);
            _popups.Add(inst);
            AttachTo(inst, Root.PopupLayer);
            UpdatePopupModal();
            await ShowCoreAsync(inst, _settings?.DefaultPopupTransition, ct);
        }

        private async UniTask ShowOverlayAsync(UIPresenterBase inst, CancellationToken ct)
        {
            await UniTask.Yield(PlayerLoopTiming.Update, ct);
            var above = (inst as IOverlayPlacement)?.Above ?? true;
            _overlays.Register(inst, above);
            AttachTo(inst, above ? Root.AboveOverlayLayer : Root.BelowOverlayLayer);
            await ShowCoreAsync(inst, _settings?.DefaultOverlayTransition, ct);
        }

        private void AttachTo(UIPresenterBase inst, Transform layer)
            => inst.ViewBase.RectTransform.SetParent(layer, false);

        private async UniTask ShowCoreAsync(UIPresenterBase inst, IUITransition defaultTransition, CancellationToken ct)
        {
            inst.ViewBase.gameObject.SetActive(true); // FIX C1: 캐시 재사용 시 비활성 GameObject 복구
            // 매 표시마다 트랜지션 슬롯을 명시적으로 재설정해 캐시 재사용 시 직전 오버라이드가 잔류하지 않게 한다.
            // 오버라이드는 show/hide 양쪽에 적용하고, 모드 기본값은 settings에서 주입한다.
            inst.ViewBase.ShowTransition = inst.TransitionOverride;
            inst.ViewBase.HideTransition = inst.TransitionOverride;
            inst.ViewBase.DefaultTransition = defaultTransition;
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

        private async UniTask HandleHideAsync(UIPresenterBase e, CancellationToken ct)
        {
            // 이미 숨겨졌거나(_active에서 제거됨) 다른 인스턴스로 교체된 경우 중복 Hide를 무시한다.
            // (라이프사이클 이벤트 재발화 및 InstanceCache 중복 등록 방지)
            if (!_active.TryGetValue(e.GetType(), out var current) || current != e) return;
            var layer = LayerOf(e);
            await HideCoreAsync(e, layer, ct);
            if (_pages.Current == e) _pages.Clear();
            _popups.Remove(e); UpdatePopupModal();
            _overlays.Unregister(e);
        }

        private Transform LayerOf(UIPresenterBase e)
        {
            if (_pages.Current == e) return Root.PageLayer;
            if (_popups.All.Contains(e)) return Root.PopupLayer;
            return _overlays.IsAbove(e) ? Root.AboveOverlayLayer : Root.BelowOverlayLayer;
        }

        private static void DestroyView(UIPresenterBase inst)
        {
            if (inst.ViewBase != null) UnityEngine.Object.Destroy(inst.ViewBase.gameObject);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _queue.CancelAndClear();
            // FIX I1: 활성/캐시 presenter에 OnDestroyElement + Destroyed 이벤트 발화 (spec §6)
            // 캐시된 인스턴스의 뷰는 Hide 시 SetParent(null)로 Canvas에서 분리되므로 Canvas 파괴만으로는
            // 정리되지 않는다. 활성/캐시 모두 GameObject를 명시적으로 파괴해 누수를 막는다.
            foreach (var e in _active.Values)
            {
                e.OnDestroyElement();
                e.Fire(UIPresenterBase.LifecycleEvent.Destroyed);
                DestroyView(e);
            }
            foreach (var e in _cache.AllInstances)
            {
                var p = (UIPresenterBase)e;
                p.OnDestroyElement();
                p.Fire(UIPresenterBase.LifecycleEvent.Destroyed);
                DestroyView(p);
            }
            _active.Clear();
            _cache.Clear();
            _pages.Clear();
            _popups.Clear();
            _overlays.Clear();
            if (_root != null && _root.GO != null) UnityEngine.Object.Destroy(_root.GO);
        }
    }

    internal interface IOverlayPlacement { bool Above { get; } }
}
