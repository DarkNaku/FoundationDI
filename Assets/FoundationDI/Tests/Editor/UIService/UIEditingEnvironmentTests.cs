using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using DarkNaku.FoundationDI;
using DarkNaku.FoundationDI.Editor;

public class UIEditingEnvironmentTests
{
    private const string PrefabPath = "Assets/__UIEditingEnvironmentTests__.prefab";

    private SceneAsset _original;
    private GameObject _sceneInstance;
    private GameObject _looseSceneObject;

    [SetUp]
    public void SetUp() => _original = EditorSettings.prefabUIEnvironment;

    [TearDown]
    public void TearDown()
    {
        EditorSettings.prefabUIEnvironment = _original;
        Selection.activeGameObject = null;

        if (_sceneInstance != null) Object.DestroyImmediate(_sceneInstance);
        if (_looseSceneObject != null) Object.DestroyImmediate(_looseSceneObject);

        AssetDatabase.DeleteAsset(PrefabPath);
    }

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

    [Test]
    public void ResolveSelectedRootPrefabAsset은_프리팹_에셋_선택을_그대로_반환한다()
    {
        var asset = UIRootPrefabCreator.CreateAt(PrefabPath);
        Selection.activeGameObject = asset.gameObject;

        var resolved = UIEditingEnvironment.ResolveSelectedRootPrefabAsset();

        Assert.AreSame(asset, resolved);
    }

    [Test]
    public void ResolveSelectedRootPrefabAsset은_씬_안의_프리팹_인스턴스를_원본_에셋으로_되짚는다()
    {
        var asset = UIRootPrefabCreator.CreateAt(PrefabPath);
        _sceneInstance = (GameObject)PrefabUtility.InstantiatePrefab(asset.gameObject);
        Selection.activeGameObject = _sceneInstance;

        var resolved = UIEditingEnvironment.ResolveSelectedRootPrefabAsset();

        Assert.AreSame(asset, resolved,
            "씬 인스턴스를 그대로 쓰면 이후 PrefabUtility.InstantiatePrefab이 null을 돌려줘 NRE로 죽는다");
    }

    [Test]
    public void ResolveSelectedRootPrefabAsset은_프리팹과_무관한_씬_오브젝트면_null을_반환한다()
    {
        // 디자인 스펙이 언급하는 "UIRoot를 직접 자신의 캔버스에 붙인" 시나리오 — 원본 에셋이 없다.
        _looseSceneObject = new GameObject(
            "LooseRoot", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler),
            typeof(UnityEngine.UI.GraphicRaycaster), typeof(UIRoot));
        Selection.activeGameObject = _looseSceneObject;

        var resolved = UIEditingEnvironment.ResolveSelectedRootPrefabAsset();

        Assert.IsNull(resolved);
    }

    [Test]
    public void ResolveSelectedRootPrefabAsset은_선택이_없으면_null을_반환한다()
    {
        Selection.activeGameObject = null;

        Assert.IsNull(UIEditingEnvironment.ResolveSelectedRootPrefabAsset());
    }
}
