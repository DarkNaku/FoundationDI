using DarkNaku.FoundationDI;
using NUnit.Framework;
using UnityEngine;

public class TutorialTypesTest
{
    [Test]
    public void 타깃참조가_비어있으면_IsEmpty가_참이다()
    {
        var sut = default(TutorialTargetRef);

        Assert.IsTrue(sut.IsEmpty);
        Assert.IsFalse(sut.HasKey);
    }

    [Test]
    public void 키만_채우면_HasKey가_참이고_비어있지_않다()
    {
        var sut = TutorialTargetRef.FromKey("shop.buy");

        Assert.IsFalse(sut.IsEmpty);
        Assert.IsTrue(sut.HasKey);
        Assert.AreEqual("shop.buy", sut.Key);
        Assert.IsNull(sut.Direct);
    }

    [Test]
    public void 공백문자열_키는_키가_없는_것으로_본다()
    {
        var sut = TutorialTargetRef.FromKey("   ");

        Assert.IsTrue(sut.IsEmpty);
        Assert.IsFalse(sut.HasKey);
    }

    [Test]
    public void 직접참조를_채우면_비어있지_않고_키는_없다()
    {
        var go = new GameObject("target");

        try
        {
            var sut = TutorialTargetRef.FromTransform(go.transform);

            Assert.IsFalse(sut.IsEmpty);
            Assert.IsFalse(sut.HasKey);
            Assert.AreSame(go.transform, sut.Direct);
        }
        finally
        {
            Object.DestroyImmediate(go);
        }
    }

    [Test]
    public void 직접참조가_파괴되면_다시_비어있는_것으로_본다()
    {
        var go = new GameObject("target");
        var sut = TutorialTargetRef.FromTransform(go.transform);

        Object.DestroyImmediate(go);

        Assert.IsTrue(sut.IsEmpty);
        Assert.IsNull(sut.Direct);
    }

    [Test]
    public void 직접참조가_키보다_우선한다()
    {
        var go = new GameObject("target");

        try
        {
            var sut = TutorialTargetRef.Create(go.transform, "shop.buy");

            Assert.IsFalse(sut.HasKey);
            Assert.AreSame(go.transform, sut.Direct);
        }
        finally
        {
            Object.DestroyImmediate(go);
        }
    }
}
