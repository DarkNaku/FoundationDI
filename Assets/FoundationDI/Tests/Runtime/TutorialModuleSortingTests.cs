using DarkNaku.FoundationDI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 연출 모듈의 캔버스·레이캐스트 배선을 고정한다.
/// UIService의 UIRoot는 sortingOrder 0짜리 캔버스 하나에 모든 UI를 담으므로
/// (레이어들은 자기 Canvas가 없는 RectTransform이다) 튜토리얼은 자기 캔버스를
/// 더 높은 sortingOrder로 띄워야 팝업 위에 그려진다.
///
/// Awake가 돌아야 검증되는 내용이라 PlayMode에 둔다.
/// </summary>
public class TutorialModuleSortingTests
{
    [Test]
    public void 하이라이트는_UIRoot보다_높은_오더의_독립_오버레이_캔버스를_갖는다()
    {
        var go = new GameObject("highlight", typeof(RectTransform), typeof(HighlightModule));

        try
        {
            var canvas = go.GetComponent<Canvas>();

            Assert.AreEqual(RenderMode.ScreenSpaceOverlay, canvas.renderMode);
            Assert.Greater(canvas.sortingOrder, 0,
                           "UIRoot 캔버스가 sortingOrder 0이므로 그보다 높아야 위에 그려진다.");

            // overrideSorting은 단언하지 않는다. 루트 캔버스에서는 Unity가 무시하고 false로
            // 되돌린다(중첩 캔버스에서만 의미가 있다). 모듈이 코드에서 켜두는 이유는
            // 프리팹을 다른 Canvas 밑에 넣었을 때를 대비한 것이고, 루트로 쓰면
            // sortingOrder 하나로 정렬이 결정된다.
        }
        finally
        {
            Object.DestroyImmediate(go);
        }
    }

    [Test]
    public void 하이라이트는_바깥클릭을_막을_GraphicRaycaster를_갖는다()
    {
        var go = new GameObject("highlight", typeof(RectTransform), typeof(HighlightModule));

        try
        {
            // 딤 패널의 raycastTarget만으로는 아무것도 막지 못한다.
            // 그 캔버스에 레이캐스터가 있어야 그래픽이 레이캐스트 대상이 된다.
            Assert.IsNotNull(go.GetComponent<GraphicRaycaster>());
        }
        finally
        {
            Object.DestroyImmediate(go);
        }
    }

    [Test]
    public void 손가락은_하이라이트보다_위에_그려진다()
    {
        var hand = new GameObject("hand", typeof(RectTransform), typeof(HandPointerModule));
        var highlight = new GameObject("highlight", typeof(RectTransform), typeof(HighlightModule));

        try
        {
            Assert.Greater(hand.GetComponent<Canvas>().sortingOrder,
                           highlight.GetComponent<Canvas>().sortingOrder);
        }
        finally
        {
            Object.DestroyImmediate(hand);
            Object.DestroyImmediate(highlight);
        }
    }

    [Test]
    public void 손가락은_가리키는_버튼의_클릭을_먹지_않는다()
    {
        // 비활성 상태에서는 Awake가 돌지 않는다. _hand를 먼저 꽂고 활성화해야
        // Awake가 실제 배선을 태운다.
        var go = new GameObject("hand", typeof(RectTransform));
        go.SetActive(false);

        var handVisual = new GameObject("visual", typeof(RectTransform), typeof(Image));
        handVisual.transform.SetParent(go.transform, false);

        var image = handVisual.GetComponent<Image>();

        Assert.IsTrue(image.raycastTarget, "Image의 기본값은 raycastTarget = true다.");

        try
        {
            var module = go.AddComponent<HandPointerModule>();

            typeof(HandPointerModule)
                .GetField("_hand", System.Reflection.BindingFlags.Instance |
                                   System.Reflection.BindingFlags.NonPublic)
                .SetValue(module, handVisual.GetComponent<RectTransform>());

            go.SetActive(true);

            Assert.IsFalse(image.raycastTarget,
                           "손가락이 레이캐스트를 먹으면 정작 눌러야 할 버튼이 막힌다.");
        }
        finally
        {
            Object.DestroyImmediate(go);
        }
    }
}
