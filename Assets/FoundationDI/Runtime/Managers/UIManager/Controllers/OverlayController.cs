using System.Collections.Generic;

namespace DarkNaku.FoundationDI
{
    internal sealed class OverlayController
    {
        private readonly List<UIPresenterBase> _above = new();
        private readonly List<UIPresenterBase> _below = new();

        public IReadOnlyList<UIPresenterBase> All
        {
            get { var l = new List<UIPresenterBase>(_above); l.AddRange(_below); return l; }
        }

        public void Register(UIPresenterBase o, bool above) => (above ? _above : _below).Add(o);
        public void Unregister(UIPresenterBase o) { _above.Remove(o); _below.Remove(o); }
        public bool IsAbove(UIPresenterBase o) => _above.Contains(o);
        public void Clear() { _above.Clear(); _below.Clear(); }
    }
}
