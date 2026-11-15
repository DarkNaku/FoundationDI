using System.Text.RegularExpressions;
using DarkNaku.FoundationDI;
using NUnit.Framework;
using Reflex.Core;
using UnityEngine;
using UnityEngine.TestTools;

public class AppUpdateServiceRegistrationTest
{
    private AppUpdateServiceSettings _settings;

    [SetUp]
    public void SetUp() => _settings = ScriptableObject.CreateInstance<AppUpdateServiceSettings>();

    [TearDown]
    public void TearDown() => Object.DestroyImmediate(_settings);

    [Test]
    public void 등록하면_IAppUpdateService가_싱글턴으로_해석된다()
    {
        var builder = new ContainerBuilder();
        builder.RegisterAppUpdateService(_settings);

        using var container = builder.Build();

        var first = container.Resolve<IAppUpdateService>();

        Assert.IsInstanceOf<AppUpdateService>(first);
        Assert.AreSame(first, container.Resolve<IAppUpdateService>());
    }

    [Test]
    public void 설정이_null이면_에러를_남기고_등록하지_않는다()
    {
        var builder = new ContainerBuilder();

        LogAssert.Expect(LogType.Error, new Regex("AppUpdateServiceSettings"));
        builder.RegisterAppUpdateService(null);

        using var container = builder.Build();

        Assert.IsFalse(container.HasBinding<IAppUpdateService>());
    }
}
