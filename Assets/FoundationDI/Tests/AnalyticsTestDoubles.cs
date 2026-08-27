using System;
using System.Collections.Generic;
using DarkNaku.FoundationDI;
using UnityEngine;

// AdTestDoubles.cs 의 FakeAdProvider 와 같은 모양이다. NSubstitute 대신 손으로 쓴 이유는
// 버퍼 flush 순서 검증이 "무엇이 몇 번" 이 아니라 "무엇 다음에 무엇" 이라, 호출을 직접
// 순서대로 기록하는 편이 훨씬 명확하기 때문이다.
public class FakeAnalyticsProvider : IAnalyticsProvider
{
    public FakeAnalyticsProvider(string name = "Fake")
    {
        Name = name;
    }

    public string Name { get; }

    // 모든 호출을 순서대로 누적한다. 버퍼 flush 순서 검증용.
    public readonly List<string> Calls = new();

    public readonly List<(string Name, AnalyticsParams Parameters)> Events = new();
    public readonly List<PurchaseInfo> Purchases = new();
    public readonly List<AdImpression> Impressions = new();
    public readonly List<(string Name, string Value)> Properties = new();
    public readonly List<string> UserIds = new();
    public readonly List<bool> CollectionFlags = new();

    public int InitializeCount { get; private set; }
    public int DisposeCount { get; private set; }

    public bool InitializeResult = true;
    public bool ThrowOnInitialize;
    public bool ThrowOnLogEvent;

    // 재진입(동시 InitializeAsync) 테스트용. true면 InitializeAsync가 즉시 완료되지 않고
    // CompleteInitialize 호출을 기다린다. Awaitable은 단일 사용이므로 대기 중인 호출자마다
    // 별도의 AwaitableCompletionSource를 만들어 둔다.
    public bool DeferInitialize;

    private readonly List<AwaitableCompletionSource<bool>> _pendingInitializations = new();

    public Awaitable<bool> InitializeAsync()
    {
        InitializeCount++;
        Calls.Add("InitializeAsync");

        if (ThrowOnInitialize) throw new InvalidOperationException($"{Name} 초기화 실패");

        var source = new AwaitableCompletionSource<bool>();

        if (DeferInitialize)
        {
            _pendingInitializations.Add(source);
        }
        else
        {
            source.SetResult(InitializeResult);
        }

        return source.Awaitable;
    }

    // DeferInitialize로 보류 중인 모든 InitializeAsync 호출을 같은 결과로 완료시킨다.
    public void CompleteInitialize(bool result)
    {
        var waiters = new List<AwaitableCompletionSource<bool>>(_pendingInitializations);
        _pendingInitializations.Clear();
        foreach (var waiter in waiters) waiter.SetResult(result);
    }

    public void SetCollectionEnabled(bool enabled)
    {
        Calls.Add($"SetCollectionEnabled:{enabled}");
        CollectionFlags.Add(enabled);
    }

    public void LogEvent(string name, AnalyticsParams parameters)
    {
        Calls.Add($"LogEvent:{name}");

        if (ThrowOnLogEvent) throw new InvalidOperationException($"{Name} 이벤트 실패");

        Events.Add((name, parameters));
    }

    public void LogPurchase(PurchaseInfo purchase)
    {
        Calls.Add($"LogPurchase:{purchase.ProductId}");
        Purchases.Add(purchase);
    }

    public void LogAdImpression(AdImpression impression)
    {
        Calls.Add($"LogAdImpression:{impression.AdUnitId}");
        Impressions.Add(impression);
    }

    public void SetUserId(string userId)
    {
        Calls.Add($"SetUserId:{userId}");
        UserIds.Add(userId);
    }

    public void SetUserProperty(string name, string value)
    {
        Calls.Add($"SetUserProperty:{name}={value}");
        Properties.Add((name, value));
    }

    public void Dispose()
    {
        DisposeCount++;
        Calls.Add("Dispose");
    }
}

// 어댑터 고유 설정 seam 검증용. 실제 어댑터 설정(AdjustAnalyticsSettings 등)은 게이트된
// 옵셔널 어셈블리 안에 있어 테스트에서 참조할 수 없으므로, 같은 모양의 가짜를 둘 세운다.
public class FakeAnalyticsProviderSettings : AnalyticsProviderSettings
{
    public string Token;
}

public class OtherAnalyticsProviderSettings : AnalyticsProviderSettings
{
}
