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
}
