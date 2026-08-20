using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace DarkNaku.FoundationDI.Editor
{
    /// <summary>GameObject 메뉴에서 SoundService 관련 컴포넌트를 빠르게 배치한다.</summary>
    public static class SoundServiceQuickMenu
    {
        [MenuItem("GameObject/DarkNaku/FoundationDI/Sound/Music Zone", false, 10)]
        private static void CreateMusicZone(MenuCommand command)
        {
            var go = new GameObject("Music Zone");

            go.AddComponent<MusicZone>();

            Place(go, command.context, "Create Music Zone");
        }

        [MenuItem("GameObject/DarkNaku/FoundationDI/Sound/UI/Output Volume Slider", false, 11)]
        private static void CreateOutputVolumeSlider(MenuCommand command)
        {
            var go = CreateSliderObject("Output Volume Slider");

            go.AddComponent<OutputVolumeSlider>();

            Place(go, command.context, "Create Output Volume Slider");
        }

        [MenuItem("GameObject/DarkNaku/FoundationDI/Sound/UI/Volume Slider", false, 12)]
        private static void CreateVolumeSlider(MenuCommand command)
        {
            var go = CreateSliderObject("Volume Slider");

            go.AddComponent<VolumeSlider>();

            Place(go, command.context, "Create Volume Slider");
        }

        private static GameObject CreateSliderObject(string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var slider = go.AddComponent<Slider>();

            slider.minValue = 0f;
            slider.maxValue = 1f;
            slider.value = 1f;

            return go;
        }

        private static void Place(GameObject go, Object context, string undoName)
        {
            Undo.RegisterCreatedObjectUndo(go, undoName);

            if (context is GameObject parent)
            {
                go.transform.SetParent(parent.transform, false);
            }

            Selection.activeGameObject = go;

            EditorSceneManager.MarkSceneDirty(go.scene);
        }
    }
}
