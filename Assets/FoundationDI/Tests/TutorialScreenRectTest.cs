using DarkNaku.FoundationDI;
using NUnit.Framework;
using UnityEngine;

public class TutorialScreenRectTest
{
    private Camera _camera;

    [SetUp]
    public void SetUp()
    {
        var go = new GameObject("cam", typeof(Camera));

        _camera = go.GetComponent<Camera>();
        _camera.orthographic = true;
        _camera.orthographicSize = 5f;
        _camera.transform.position = new Vector3(0f, 0f, -10f);
    }

    [TearDown]
    public void TearDown() => Object.DestroyImmediate(_camera.gameObject);

    [Test]
    public void 타깃이_null이면_실패한다()
    {
        Assert.IsFalse(TutorialScreenRect.TryGet(null, _camera, out _));
    }

    [Test]
    public void RectTransform은_코너로_rect를_만든다()
    {
        var canvasGo = new GameObject("canvas", typeof(RectTransform), typeof(Canvas));
        var canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;

        var go = new GameObject("target", typeof(RectTransform));
        var rt = go.GetComponent<RectTransform>();
        rt.SetParent(canvasGo.transform, false);
        rt.sizeDelta = new Vector2(100f, 50f);
        rt.anchoredPosition = Vector2.zero;

        try
        {
            Assert.IsTrue(TutorialScreenRect.TryGet(rt, null, out var rect));
            Assert.AreEqual(100f, rect.width, 0.01f);
            Assert.AreEqual(50f, rect.height, 0.01f);
        }
        finally
        {
            Object.DestroyImmediate(go);
            Object.DestroyImmediate(canvasGo);
        }
    }

    [Test]
    public void 렌더러가_없는_일반_Transform은_점_rect를_만든다()
    {
        var go = new GameObject("target");
        go.transform.position = Vector3.zero;

        try
        {
            Assert.IsTrue(TutorialScreenRect.TryGet(go.transform, _camera, out var rect));
            Assert.AreEqual(0f, rect.width, 0.01f);
            Assert.AreEqual(0f, rect.height, 0.01f);
        }
        finally
        {
            Object.DestroyImmediate(go);
        }
    }

    [Test]
    public void 렌더러가_있으면_바운즈로_rect를_만든다()
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.transform.position = Vector3.zero;

        try
        {
            Assert.IsTrue(TutorialScreenRect.TryGet(go.transform, _camera, out var rect));
            Assert.Greater(rect.width, 0f);
            Assert.Greater(rect.height, 0f);
        }
        finally
        {
            Object.DestroyImmediate(go);
        }
    }

    [Test]
    public void 카메라가_없으면_일반_Transform은_실패한다()
    {
        var go = new GameObject("target");

        try
        {
            Assert.IsFalse(TutorialScreenRect.TryGet(go.transform, null, out _));
        }
        finally
        {
            Object.DestroyImmediate(go);
        }
    }
}
