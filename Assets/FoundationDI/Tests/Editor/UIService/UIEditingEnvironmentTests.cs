using NUnit.Framework;
using UnityEditor;
using DarkNaku.FoundationDI.Editor;

public class UIEditingEnvironmentTests
{
    private SceneAsset _original;

    [SetUp]
    public void SetUp() => _original = EditorSettings.prefabUIEnvironment;

    [TearDown]
    public void TearDown() => EditorSettings.prefabUIEnvironment = _original;

    [Test]
    public void Assign은_프리팹_UI_편집환경을_지정한다()
    {
        // 프로젝트에 이미 존재하는 아무 씬 에셋이나 픽스처로 쓴다.
        var guids = AssetDatabase.FindAssets("t:SceneAsset");

        Assert.Greater(guids.Length, 0, "픽스처로 쓸 씬 에셋이 프로젝트에 하나도 없다");

        var scene = AssetDatabase.LoadAssetAtPath<SceneAsset>(AssetDatabase.GUIDToAssetPath(guids[0]));

        UIEditingEnvironment.Assign(scene);

        Assert.AreSame(scene, EditorSettings.prefabUIEnvironment);
    }

    [Test]
    public void Clear는_프리팹_UI_편집환경_지정을_해제한다()
    {
        var guids = AssetDatabase.FindAssets("t:SceneAsset");
        var scene = AssetDatabase.LoadAssetAtPath<SceneAsset>(AssetDatabase.GUIDToAssetPath(guids[0]));

        UIEditingEnvironment.Assign(scene);
        UIEditingEnvironment.Clear();

        Assert.IsNull(EditorSettings.prefabUIEnvironment);
    }
}
