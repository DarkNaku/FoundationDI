using System.Collections;
using DarkNaku.FoundationDI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

/// <summary>
/// 더미 광고 캔버스가 플레이 모드 종료와 함께 지워지는지 본다.
///
/// DontSave 계열 플래그가 붙은 오브젝트는 플레이 모드를 끝내도 Unity가 지우지 않는다
/// (DontDestroyOnLoad 씬의 오브젝트만 지운다). 전면광고가 떠 있는 채로 플레이를 멈추면
/// 캔버스가 에디트 모드에 남아 Game 뷰에 멈춘 카운트다운이 계속 보였다.
/// </summary>
public class DummyAdCanvasPlayModeTests
{
    [UnityTest]
    public IEnumerator 플레이_모드에서_만든_루트는_DontSave_없이_DontDestroyOnLoad_씬에_둔다()
    {
        var sut = new DummyAdCanvas();

        sut.ShowFullScreen(AdFormat.Interstitial, 5f, null, null);

        var root = sut.Root;
        Assert.AreEqual(HideFlags.None, root.hideFlags & HideFlags.DontSave,
            "DontSave가 붙으면 플레이 모드를 끝내도 남는다");
        Assert.AreEqual("DontDestroyOnLoad", root.scene.name);

        sut.Dispose();

        yield return null;

        Assert.IsTrue(root == null);
    }
}
