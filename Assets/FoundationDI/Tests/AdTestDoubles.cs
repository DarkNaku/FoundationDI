using System;
using System.Collections.Generic;
using DarkNaku.FoundationDI;

// 정책 계층 테스트용 시계. 실제 시간을 쓰지 않고 Advance/TickFrames로 손으로 돌린다.
public class FakeAdDispatcher : IAdDispatcher
{
    private class Entry
    {
        public float DueAt;        // Delay용 (누적 시간 기준)
        public int FramesLeft;     // NextFrames용
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

    private readonly List<Entry> _entries = new();
    private float _now;

    public int PendingCount
    {
        get
        {
            var count = 0;
            foreach (var e in _entries) if (!e.Cancelled) count++;
            return count;
        }
    }

    // Post는 즉시 실행한다. 테스트에서 마샬링 지연을 재현할 이유가 없다.
    public void Post(Action action) => action?.Invoke();

    public IDisposable Delay(float seconds, Action action)
    {
        var entry = new Entry { DueAt = _now + seconds, IsFrameBased = false, Action = action };
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

    public void Advance(float seconds)
    {
        _now += seconds;
        Flush(e => !e.IsFrameBased && e.DueAt <= _now);
    }

    public void TickFrames(int count)
    {
        for (var i = 0; i < count; i++)
        {
            foreach (var e in _entries) if (e.IsFrameBased) e.FramesLeft--;
            Flush(e => e.IsFrameBased && e.FramesLeft <= 0);
        }
    }

    // 실행 중에 새 작업이 예약될 수 있으므로(자동 재로드 등) 스냅샷을 뜬 뒤 순회한다.
    private void Flush(Func<Entry, bool> isDue)
    {
        var due = new List<Entry>();
        foreach (var e in _entries) if (!e.Cancelled && isDue(e)) due.Add(e);
        foreach (var e in due) e.Cancelled = true;
        _entries.RemoveAll(e => e.Cancelled);
        foreach (var e in due) e.Action?.Invoke();
    }
}

public class FakeFullScreenAdapter : IFullScreenAdapter
{
    public int LoadCount { get; private set; }
    public int ShowCount { get; private set; }
    public bool IsReady { get; set; }
    public bool IsDisposed { get; private set; }

    public void Load() => LoadCount++;
    public void Show() => ShowCount++;
    public void Dispose() => IsDisposed = true;

    public event Action Loaded;
    public event Action<AdError> LoadFailed;
    public event Action Displayed;
    public event Action<AdError> DisplayFailed;
    public event Action Closed;
    public event Action<AdReward> Rewarded;
    public event Action<AdImpression> Paid;

    // 준비 상태를 함께 바꿔주는 편의 발화기. 테스트가 IsReady를 따로 세팅할 필요를 없앤다.
    public void RaiseLoaded() { IsReady = true; Loaded?.Invoke(); }
    public void RaiseLoadFailed(AdError error) { IsReady = false; LoadFailed?.Invoke(error); }
    public void RaiseDisplayed() => Displayed?.Invoke();
    public void RaiseDisplayFailed(AdError error) { IsReady = false; DisplayFailed?.Invoke(error); }
    public void RaiseClosed() { IsReady = false; Closed?.Invoke(); }
    public void RaiseRewarded(AdReward reward) => Rewarded?.Invoke(reward);
    public void RaisePaid(AdImpression impression) => Paid?.Invoke(impression);
}

public class FakeBannerAdapter : IBannerAdapter
{
    public int ShowCount { get; private set; }
    public int HideCount { get; private set; }
    public bool IsDisposed { get; private set; }
    public float Height { get; private set; }

    public void Show() => ShowCount++;
    public void Hide() => HideCount++;
    public void Dispose() => IsDisposed = true;

    public event Action<float> HeightChanged;
    public event Action<AdImpression> Paid;

    public void SetHeight(float height) { Height = height; HeightChanged?.Invoke(height); }
    public void RaisePaid(AdImpression impression) => Paid?.Invoke(impression);
}
