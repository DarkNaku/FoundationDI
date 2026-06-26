using System.Collections.Generic;

namespace DarkNaku.FoundationDI
{
    internal sealed class OverlayController
    {
        private readonly List<UIPresenter> _above = new();
        private readonly List<UIPresenter> _below = new();

        public IReadOnlyList<UIPresenter> Above => _above;
        public IReadOnlyList<UIPresenter> Below => _below;

        public void Register(UIPresenter presenter, bool above) => (above ? _above : _below).Add(presenter);

        public void Unregister(UIPresenter presenter) { 
            _above.Remove(presenter); 
            _below.Remove(presenter); 
        }

        public bool IsAbove(UIPresenter presenter) => _above.Contains(presenter);

        public void Clear() { 
            _above.Clear(); 
            _below.Clear(); 
        }
    }
}
