using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Cysharp.Threading.Tasks;
using DarkNaku.FoundationDI;
using NSubstitute;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using VContainer;

public class InitializeServiceTest
{
    // 호출 여부/순서/resolver/예외를 기록하는 fake 초기화 항목.
    private class FakeItem : InitializeItem
    {
        public int CallCount;
        public IObjectResolver LastResolver;
        public Exception ToThrow;
        public List<string> OrderLog;
        public string Id;

        public override Awaitable InitializeAsync(IObjectResolver resolver)
        {
            CallCount++;
            LastResolver = resolver;
            OrderLog?.Add(Id);
            var acs = new AwaitableCompletionSource();
            if (ToThrow != null) acs.SetException(ToThrow);
            else acs.SetResult();
            return acs.Awaitable;
        }
    }

    private static FakeItem NewItem(string id = null, List<string> log = null, Exception throwOn = null)
    {
        var item = ScriptableObject.CreateInstance<FakeItem>();
        item.Id = id;
        item.OrderLog = log;
        item.ToThrow = throwOn;
        return item;
    }

    // private 직렬화 필드 _items에 리플렉션으로 항목을 주입 → 런타임 API를 오염시키지 않는다.
    private static InitializeCatalog NewCatalog(params InitializeItem[] items)
    {
        var catalog = ScriptableObject.CreateInstance<InitializeCatalog>();
        typeof(InitializeCatalog)
            .GetField("_items", BindingFlags.NonPublic | BindingFlags.Instance)
            .SetValue(catalog, new List<InitializeItem>(items));
        return catalog;
    }

    [UnityTest]
    public IEnumerator 카탈로그_아이템을_선언_순서대로_초기화한다() => UniTask.ToCoroutine(async () =>
    {
        var log = new List<string>();
        var a = NewItem("A", log);
        var b = NewItem("B", log);
        var catalog = NewCatalog(a, b);
        var sut = new InitializeService(Substitute.For<IObjectResolver>());

        await sut.InitializeAsync(catalog);

        Assert.AreEqual(new[] { "A", "B" }, log.ToArray());
    });

    [UnityTest]
    public IEnumerator 각_아이템에_resolver를_전달한다() => UniTask.ToCoroutine(async () =>
    {
        var resolver = Substitute.For<IObjectResolver>();
        var a = NewItem("A");
        var catalog = NewCatalog(a);
        var sut = new InitializeService(resolver);

        await sut.InitializeAsync(catalog);

        Assert.AreSame(resolver, a.LastResolver);
    });

    [UnityTest]
    public IEnumerator 이미_초기화된_아이템은_다시_초기화하지_않는다() => UniTask.ToCoroutine(async () =>
    {
        var a = NewItem("A");
        var catalog = NewCatalog(a);
        var sut = new InitializeService(Substitute.For<IObjectResolver>());

        await sut.InitializeAsync(catalog);
        await sut.InitializeAsync(catalog);

        Assert.AreEqual(1, a.CallCount);
    });

    [UnityTest]
    public IEnumerator 완료된_카탈로그_재호출은_조기반환한다() => UniTask.ToCoroutine(async () =>
    {
        var a = NewItem("A");
        var catalog = NewCatalog(a);
        var sut = new InitializeService(Substitute.For<IObjectResolver>());
        await sut.InitializeAsync(catalog);

        // 완료 후 카탈로그에 새 항목을 추가해도, 카탈로그가 완료로 표시되어 순회하지 않는다.
        var b = NewItem("B");
        typeof(InitializeCatalog).GetField("_items", BindingFlags.NonPublic | BindingFlags.Instance)
            .SetValue(catalog, new List<InitializeItem> { a, b });

        await sut.InitializeAsync(catalog);

        Assert.AreEqual(0, b.CallCount); // 조기 반환 → b는 실행되지 않음
    });

    [UnityTest]
    public IEnumerator 두_카탈로그에_겹치는_아이템은_한번만_초기화된다() => UniTask.ToCoroutine(async () =>
    {
        var shared = NewItem("S");
        var catalog1 = NewCatalog(shared);
        var catalog2 = NewCatalog(shared);
        var sut = new InitializeService(Substitute.For<IObjectResolver>());

        await sut.InitializeAsync(catalog1);
        await sut.InitializeAsync(catalog2);

        Assert.AreEqual(1, shared.CallCount);
    });

    [UnityTest]
    public IEnumerator 아이템이_예외를_던지면_중단하고_예외를_전파한다() => UniTask.ToCoroutine(async () =>
    {
        var boom = new InvalidOperationException("boom");
        var a = NewItem("A", throwOn: boom);
        var b = NewItem("B");
        var catalog = NewCatalog(a, b);
        var sut = new InitializeService(Substitute.For<IObjectResolver>());

        Exception caught = null;
        try { await sut.InitializeAsync(catalog); }
        catch (Exception e) { caught = e; }

        Assert.AreSame(boom, caught);   // 예외 전파
        Assert.AreEqual(0, b.CallCount); // 뒤 항목 미실행(즉시 중단)
    });

    [UnityTest]
    public IEnumerator 실패후_재호출하면_완료된_아이템은_스킵하고_실패지점부터_재개한다() => UniTask.ToCoroutine(async () =>
    {
        var a = NewItem("A");
        var b = NewItem("B", throwOn: new InvalidOperationException("boom"));
        var c = NewItem("C");
        var catalog = NewCatalog(a, b, c);
        var sut = new InitializeService(Substitute.For<IObjectResolver>());

        try { await sut.InitializeAsync(catalog); } catch { /* b에서 중단 */ }

        Assert.AreEqual(1, a.CallCount);
        Assert.AreEqual(1, b.CallCount);
        Assert.AreEqual(0, c.CallCount);

        b.ToThrow = null; // b가 이제 성공하도록 수정
        await sut.InitializeAsync(catalog);

        Assert.AreEqual(1, a.CallCount); // 완료된 A는 스킵
        Assert.AreEqual(2, b.CallCount); // 실패했던 B부터 재개
        Assert.AreEqual(1, c.CallCount); // 이어서 C 실행
    });
}
