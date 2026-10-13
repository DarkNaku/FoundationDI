using System;
using System.Collections.Generic;
using DarkNaku.FoundationDI;
using UnityEngine;

// 순서 검증이 핵심이라 NSubstitute 대신 호출을 공유 로그에 기록하는 손 대역을 쓴다
// (AnalyticsTestDoubles와 같은 이유).
public class FakePolicyPrompt : IPolicyAgreementPrompt
{
    private readonly List<string> _log;
    private AwaitableCompletionSource _pending;

    public FakePolicyPrompt(List<string> log) { _log = log; }

    public int ShowCount { get; private set; }
    public int CloseCount { get; private set; }
    public PolicyAgreementContent LastContent { get; private set; }
    public bool IsShowing => _pending != null;
    public bool Throws;

    public Awaitable ShowAsync(PolicyAgreementContent content)
    {
        ShowCount++;
        LastContent = content;
        _log.Add("policy");

        if (Throws) throw new InvalidOperationException("프리팹이 없다");

        _pending = new AwaitableCompletionSource();
        return _pending.Awaitable;
    }

    public void Accept()
    {
        var source = _pending;
        _pending = null;
        source?.SetResult();
    }

    public void Close() => CloseCount++;
}

public class FakePolicyStorage : IPolicyAgreementStorage
{
    public int Version;
    public int SaveCount { get; private set; }

    public int LoadAcceptedVersion() => Version;

    public void SaveAcceptedVersion(int version)
    {
        Version = version;
        SaveCount++;
    }
}

public class FakeUmpConsent : IUmpConsent
{
    private readonly List<string> _log;
    private AwaitableCompletionSource<bool> _pendingUpdate;
    private AwaitableCompletionSource<bool> _pendingForm;

    public FakeUmpConsent(List<string> log) { _log = log; }

    public bool IsSupported { get; set; } = true;
    public UmpConsentStatus Status { get; set; } = UmpConsentStatus.NotRequired;
    public bool CanRequestAds { get; set; } = true;
    public bool IsPrivacyOptionsRequired { get; set; }
    public TcfSignals Tcf { get; set; } = TcfSignals.None;

    public bool DeferUpdate;
    public bool DeferForm;
    public bool UpdateResult = true;
    public bool ThrowOnUpdate;
    public bool ThrowOnForm;
    public bool ThrowOnRead;

    // 폼이 닫힐 때 상태를 바꾸는 데 쓴다(사용자가 동의/거부한 결과를 흉내).
    public Action OnForm;
    public Action OnPrivacyOptions;

    public int UpdateCount { get; private set; }
    public int FormCount { get; private set; }
    public int PrivacyOptionsCount { get; private set; }
    public UmpRequestOptions LastOptions { get; private set; }
    public bool IsUpdatePending => _pendingUpdate != null;
    public bool IsFormPending => _pendingForm != null;

    public TcfSignals ReadTcf()
    {
        if (ThrowOnRead) throw new InvalidOperationException("네이티브 읽기 실패");
        return Tcf;
    }

    public Awaitable<bool> UpdateAsync(UmpRequestOptions options)
    {
        UpdateCount++;
        LastOptions = options;
        _log.Add("ump.update");

        if (ThrowOnUpdate) throw new InvalidOperationException("네이티브 예외");

        var source = new AwaitableCompletionSource<bool>();

        if (DeferUpdate) _pendingUpdate = source;
        else source.SetResult(UpdateResult);

        return source.Awaitable;
    }

    public void CompleteUpdate(bool ok)
    {
        var source = _pendingUpdate;
        _pendingUpdate = null;
        source?.SetResult(ok);
    }

    public Awaitable<bool> LoadAndShowIfRequiredAsync()
    {
        FormCount++;
        _log.Add("ump.form");

        if (ThrowOnForm) throw new InvalidOperationException("폼 예외");

        var source = new AwaitableCompletionSource<bool>();

        if (DeferForm)
        {
            _pendingForm = source;
        }
        else
        {
            OnForm?.Invoke();
            source.SetResult(true);
        }

        return source.Awaitable;
    }

    public void CompleteForm()
    {
        var source = _pendingForm;
        _pendingForm = null;
        OnForm?.Invoke();
        source?.SetResult(true);
    }

    public Awaitable<bool> ShowPrivacyOptionsAsync()
    {
        PrivacyOptionsCount++;
        _log.Add("ump.options");
        OnPrivacyOptions?.Invoke();

        var source = new AwaitableCompletionSource<bool>();
        source.SetResult(true);
        return source.Awaitable;
    }
}

// 다이얼로그를 손으로 돌리는 가짜 ATT. RequestAsync는 Respond를 부를 때까지 완료되지 않는다.
public class FakeTrackingAuthorization : ITrackingAuthorization
{
    private readonly List<string> _log;
    private AwaitableCompletionSource<TrackingAuthorizationStatus> _pending;

    public FakeTrackingAuthorization(List<string> log = null) { _log = log ?? new List<string>(); }

    public bool IsSupported { get; set; } = true;
    public TrackingAuthorizationStatus Status { get; set; } = TrackingAuthorizationStatus.NotDetermined;
    public int RequestCount { get; private set; }
    public bool Throws { get; set; }
    public bool IsWaiting => _pending != null;

    public Awaitable<TrackingAuthorizationStatus> RequestAsync()
    {
        RequestCount++;
        _log.Add("att");

        if (Throws) throw new InvalidOperationException("요청이 이미 진행 중이다");

        _pending = new AwaitableCompletionSource<TrackingAuthorizationStatus>();
        return _pending.Awaitable;
    }

    public void Respond(TrackingAuthorizationStatus status)
    {
        var source = _pending;
        _pending = null;
        Status = status;
        source?.SetResult(status);
    }
}

public class FakeConsentPrerequisite : IConsentPrerequisite
{
    private readonly List<string> _log;
    private readonly string _name;
    private AwaitableCompletionSource _pending;

    public FakeConsentPrerequisite(List<string> log, string name = "prereq")
    {
        _log = log;
        _name = name;
    }

    public bool Defer;
    public bool Throws;
    public int PrepareCount { get; private set; }
    public bool IsPending => _pending != null;

    public Awaitable PrepareAsync()
    {
        PrepareCount++;
        _log.Add(_name);

        if (Throws) throw new InvalidOperationException("선행 작업 실패");

        var source = new AwaitableCompletionSource();

        if (Defer) _pending = source;
        else source.SetResult();

        return source.Awaitable;
    }

    public void Complete()
    {
        var source = _pending;
        _pending = null;
        source?.SetResult();
    }
}

// AdService·AnalyticsService 테스트용. 동의 흐름 자체는 ConsentService 테스트가 검증한다.
public class FakeConsentService : IConsentService
{
    private readonly List<AwaitableCompletionSource<ConsentResult>> _pending = new();

    public ConsentResult Result = ConsentResults.Granted();
    public bool Defer;
    public int RequestCount { get; private set; }
    public bool IsDisposed { get; private set; }

    public event Action ConsentChanged;

    public Awaitable<ConsentResult> RequestAsync()
    {
        RequestCount++;

        var source = new AwaitableCompletionSource<ConsentResult>();

        if (Defer) _pending.Add(source);
        else source.SetResult(Result);

        return source.Awaitable;
    }

    public void Complete(ConsentResult result)
    {
        Result = result;

        var pending = new List<AwaitableCompletionSource<ConsentResult>>(_pending);
        _pending.Clear();

        foreach (var source in pending) source.SetResult(result);
    }

    public void RaiseChanged(ConsentResult result)
    {
        Result = result;
        ConsentChanged?.Invoke();
    }

    public ConsentResult Current => Result;
    public bool IsPolicyAccepted => Result.PolicyAccepted;
    public void AcceptPolicy() { }
    public void SetPolicyContent(PolicyAgreementContent content) { }
    public bool IsPrivacyOptionsRequired => false;

    public Awaitable ShowPrivacyOptionsAsync()
    {
        var source = new AwaitableCompletionSource();
        source.SetResult();
        return source.Awaitable;
    }

    public ITrackingAuthorization TrackingAuthorization { get; } = new UnsupportedTrackingAuthorization();

    public void Dispose() => IsDisposed = true;
}

public static class ConsentResults
{
    public static ConsentResult Granted(TrackingAuthorizationStatus att = TrackingAuthorizationStatus.NotDetermined) =>
        new(true, true, AnalyticsConsent.Granted, att, TcfSignals.None);

    public static ConsentResult Denied() =>
        new(true, true, AnalyticsConsent.Denied, TrackingAuthorizationStatus.NotDetermined, new TcfSignals(true, "0000000000"));

    public static ConsentResult Unknown() =>
        new(true, false, AnalyticsConsent.Unknown, TrackingAuthorizationStatus.NotDetermined, TcfSignals.None);

    public static ConsentResult PolicyNotAccepted() =>
        new(false, false, AnalyticsConsent.Unknown, TrackingAuthorizationStatus.NotDetermined, TcfSignals.None);
}
