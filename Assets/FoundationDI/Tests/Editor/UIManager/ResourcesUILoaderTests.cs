using System.IO;
using NUnit.Framework;
using DarkNaku.FoundationDI;

public class ResourcesUILoaderTests
{
    [Test]
    public void 존재하지_않는_키는_FileNotFound를_던진다()
    {
        var loader = new ResourcesUILoader();
        Assert.Throws<FileNotFoundException>(() => loader.Load("__no_such_ui_prefab__"));
    }
}
