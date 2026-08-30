using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using DarkNaku.FoundationDI;

public class UINavigatorSettingsTests
{
    [Test]
    public void UINavigatorSettings는_루트프리팹을_설정값으로_반환한다()
    {
        var settings = ScriptableObject.CreateInstance<UINavigatorSettings>();
        var root = UIRoot.CreateDefault();

        var so = new SerializedObject(settings);
        so.FindProperty("_rootPrefab").objectReferenceValue = root;
        so.ApplyModifiedPropertiesWithoutUndo();

        Assert.AreEqual(root, settings.RootPrefab);

        Object.DestroyImmediate(settings);
        Object.DestroyImmediate(root.GO);
    }
}
