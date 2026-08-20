using System;
using System.Collections.Generic;
using UnityEngine;
using VContainer;

namespace DarkNaku.FoundationDI
{
    // 큐/타이머 로직을 전부 여기 두고 MonoBehaviour는 Pump만 호출한다.
    // 그래야 EditMode에서 MonoBehaviour 없이 이 클래스를 테스트할 수 있다.
    public class UnityAdDispatcher : IAdDispatcher, IDisposable
    {
        private class Entry
        {
            public float SecondsLeft;
            public int FramesLeft;
            public bool IsFrameBased;
            public Action Action;
            public bool Cancelled;
        }

        private class Handle : IDisposable
        {
            private readonly Entry _entry;
            public Handle(Entry entry) { _entry = entry; }
            public void Dispose() { _entry.Cancelled = true; }
        }

        // 광고 SDK 콜백은 네이티브 스레드에서 올 수 있다. Post만 락으로 보호하면 충분하다 —
        // Delay/NextFrames는 이미 메인 스레드인 정책 계층에서만 호출된다.
        private readonly object _postLock = new();
        private readonly Queue<Action> _posted = new();
        private readonly List<Action> _drained = new();

        private readonly List<Entry> _entries = new();
        private readonly List<Entry> _due = new();

        private AdServiceRunner _runner;
        private bool _isDisposed;

        // [Inject]가 없으면 VContainer가 파라미터가 더 많은 (bool) 생성자를 고르고
        // bool을 해석하지 못해 등록이 실패한다. 반드시 붙인다.
        [Inject]
        public UnityAdDispatcher() : this(true) { }

        public UnityAdDispatcher(bool createRunner)
        {
            if (createRunner) _runner = AdServiceRunner.Create(this);
        }

        public void Post(Action action)
        {
            if (action == null || _isDisposed) return;
            lock (_postLock) _posted.Enqueue(action);
        }

        public IDisposable Delay(float seconds, Action action)
        {
            var entry = new Entry { SecondsLeft = seconds, IsFrameBased = false, Action = action };
            _entries.Add(entry);
            return new Handle(entry);
        }

        public IDisposable NextFrames(int count, Action action)
        {
            if (count <= 0)
            {
                action?.Invoke();
                return new Handle(new Entry { Cancelled = true });
            }

            var entry = new Entry { FramesLeft = count, IsFrameBased = true, Action = action };
            _entries.Add(entry);
            return new Handle(entry);
        }

        public void Pump(float deltaTime)
        {
            if (_isDisposed) return;

            DrainPosted();
            AdvanceEntries(deltaTime);
        }

        private void DrainPosted()
        {
            _drained.Clear();

            lock (_postLock)
            {
                while (_posted.Count > 0) _drained.Add(_posted.Dequeue());
            }

            // 락 밖에서 실행한다. 콜백이 다시 Post를 부를 수 있어 락 안에서 실행하면 데드락이다.
            foreach (var action in _drained) SafeInvoke(action);
        }

        private void AdvanceEntries(float deltaTime)
        {
            _due.Clear();

            foreach (var entry in _entries)
            {
                if (entry.Cancelled) continue;

                if (entry.IsFrameBased) entry.FramesLeft--;
                else entry.SecondsLeft -= deltaTime;

                var isDue = entry.IsFrameBased ? entry.FramesLeft <= 0 : entry.SecondsLeft <= 0f;
                if (isDue) _due.Add(entry);
            }

            // 실행 중에 새 항목이 예약될 수 있으므로(자동 재로드 등) 먼저 목록에서 걷어낸 뒤 실행한다.
            foreach (var entry in _due) entry.Cancelled = true;
            _entries.RemoveAll(e => e.Cancelled);
            foreach (var entry in _due) SafeInvoke(entry.Action);
        }

        // 하나의 콜백이 던진 예외가 나머지 큐를 막지 않게 한다.
        private static void SafeInvoke(Action action)
        {
            try { action?.Invoke(); }
            catch (Exception e) { Debug.LogException(e); }
        }

        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;

            _entries.Clear();
            lock (_postLock) _posted.Clear();

            if (_runner != null)
            {
                _runner.Detach();
                _runner = null;
            }
        }
    }
}
