using DarkNaku.FoundationDI.Editor;
using NUnit.Framework;

public class AndroidManifestEditorTest
{
    private const string Manifest =
        "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n" +
        "<manifest xmlns:android=\"http://schemas.android.com/apk/res/android\" package=\"com.unity3d.player\">\n" +
        "  <application>\n" +
        "    <activity android:name=\"com.unity3d.player.UnityPlayerActivity\" />\n" +
        "  </application>\n" +
        "</manifest>\n";

    [Test]
    public void meta_data를_application_안에_넣는다()
    {
        var result = AndroidManifestEditor.AddMetaData(Manifest, "com.google.android.gms.ads.APPLICATION_ID", "ca-app-pub-1~2");

        Assert.IsTrue(AndroidManifestEditor.HasMetaData(result, "com.google.android.gms.ads.APPLICATION_ID"));
        StringAssert.Contains("android:value=\"ca-app-pub-1~2\"", result);
    }

    [Test]
    public void 없으면_없다고_답한다()
    {
        Assert.IsFalse(AndroidManifestEditor.HasMetaData(Manifest, "com.google.android.gms.ads.APPLICATION_ID"));
    }
}
