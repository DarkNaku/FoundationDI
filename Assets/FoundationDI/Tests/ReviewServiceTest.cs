using System;
using System.Text.RegularExpressions;
using DarkNaku.FoundationDI;
using NUnit.Framework;
using Reflex.Core;
using UnityEngine;
using UnityEngine.TestTools;

public class ReviewServiceTest
{
    private class FakeStoreReview : IStoreReview
    {
        public int PrepareCount;
        public int RequestCount;
        public bool Result = true;
        public Exception Error;

        public void Prepare()
        {
            PrepareCount++;
            if (Error != null) throw Error;
        }

        public bool Request()
        {
            RequestCount++;
            if (Error != null) throw Error;
            return Result;
        }
    }

    [Test]
    public void Request는_OS_창구에_요청을_넘긴다()
    {
        var store = new FakeStoreReview { Result = false };
        var sut = new ReviewService(() => store);

        sut.Prepare();
        var requested = sut.Request();

        Assert.AreEqual(1, store.PrepareCount);
        Assert.AreEqual(1, store.RequestCount);
        Assert.IsFalse(requested);
    }

    [Test]
    public void OS_창구가_예외를_던지면_false를_돌려주고_경고만_남긴다()
    {
        // 리뷰 창 때문에 게임 흐름(승리 → 메뉴 등)이 끊기면 안 된다.
        var sut = new ReviewService(() => new FakeStoreReview { Error = new InvalidOperationException("boom") });

        LogAssert.Expect(LogType.Warning, new Regex("boom"));

        Assert.IsFalse(sut.Request());
    }

    [Test]
    public void Prepare가_예외를_던져도_삼킨다()
    {
        var sut = new ReviewService(() => new FakeStoreReview { Error = new InvalidOperationException("boom") });

        LogAssert.Expect(LogType.Warning, new Regex("boom"));

        Assert.DoesNotThrow(() => sut.Prepare());
    }

    [Test]
    public void OS_창구를_만들다_실패해도_요청은_false로_끝난다()
    {
        // Play Review 라이브러리가 빌드에 없으면 JNI가 ClassNotFound를 던진다.
        var sut = new ReviewService(() => throw new InvalidOperationException("no class"));

        LogAssert.Expect(LogType.Warning, new Regex("no class"));

        Assert.IsFalse(sut.Request());
    }

    [Test]
    public void 등록하면_IReviewService가_해석된다()
    {
        var settings = ScriptableObject.CreateInstance<ReviewServiceSettings>();
        var builder = new ContainerBuilder();
        builder.RegisterReviewService(settings);

        using var container = builder.Build();

        Assert.IsInstanceOf<ReviewService>(container.Resolve<IReviewService>());

        UnityEngine.Object.DestroyImmediate(settings);
    }

    [Test]
    public void 에디터에서는_요청을_로그로만_남기는_창구를_쓴다()
    {
        Assert.IsInstanceOf<UnsupportedStoreReview>(StoreReviewFactory.Create(usePlayInAppReview: true));
    }
}
