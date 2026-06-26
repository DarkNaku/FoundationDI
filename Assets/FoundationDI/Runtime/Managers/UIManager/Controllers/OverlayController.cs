using System.Collections.Generic;

namespace DarkNaku.FoundationDI
{
    internal sealed class OverlayController
    {
        private readonly List<UIPresenterBase> _above = new();
        private readonly List<UIPresenterBase> _below = new();

        public void Register(UIPresenterBase presenter, bool above) => (above ? _above : _below).Add(presenter);

        public void Unregister(UIPresenterBase presenter) { 
            _above.Remove(presenter); 
            _below.Remove(presenter); 
        }

        public bool IsAbove(UIPresenterBase presenter) => _above.Contains(presenter);

        public void Clear() { 
            _above.Clear(); 
            _below.Clear(); 
        }
    }
}
