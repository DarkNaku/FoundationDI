using DarkNaku.FoundationDI;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace DarkNaku.FoundationDI.Editor
{
    [CustomEditor(typeof(UIScaleButton), true)]
    [CanEditMultipleObjects]
    public class UIScaleButtonEditor : UIButtonEditor
    {
        private SerializedProperty _scaleTarget;
        private SerializedProperty _highlightedScale;
        private SerializedProperty _pressedScale;
        private SerializedProperty _overrideDisabledScale;
        private SerializedProperty _disabledScale;
        private SerializedProperty _duration;
        private SerializedProperty _curve;
        private SerializedProperty _unscaledTime;

        protected override void OnEnable()
        {
            base.OnEnable();

            _scaleTarget = serializedObject.FindProperty("_scaleTarget");
            _highlightedScale = serializedObject.FindProperty("_highlightedScale");
            _pressedScale = serializedObject.FindProperty("_pressedScale");
            _overrideDisabledScale = serializedObject.FindProperty("_overrideDisabledScale");
            _disabledScale = serializedObject.FindProperty("_disabledScale");
            _duration = serializedObject.FindProperty("_duration");
            _curve = serializedObject.FindProperty("_curve");
            _unscaledTime = serializedObject.FindProperty("_unscaledTime");
        }

        public override void OnInspectorGUI()
        {
            base.OnInspectorGUI();

            serializedObject.Update();

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Scale", EditorStyles.boldLabel);

            DrawScaleTargetWarnings();

            EditorGUILayout.PropertyField(_scaleTarget);
            EditorGUILayout.PropertyField(_highlightedScale);
            EditorGUILayout.PropertyField(_pressedScale);
            EditorGUILayout.PropertyField(_overrideDisabledScale);

            using (new EditorGUI.DisabledScope(!_overrideDisabledScale.boolValue))
            {
                EditorGUILayout.PropertyField(_disabledScale);
            }

            EditorGUILayout.Space();
            EditorGUILayout.PropertyField(_duration);
            EditorGUILayout.PropertyField(_curve);
            EditorGUILayout.PropertyField(_unscaledTime);

            serializedObject.ApplyModifiedProperties();

            DrawCreateContentButton();
        }

        private void DrawScaleTargetWarnings()
        {
            var button = target as UIScaleButton;
            if (button == null) return;

            var scaleTarget = _scaleTarget.objectReferenceValue as RectTransform;

            if (scaleTarget == null)
            {
                EditorGUILayout.HelpBox(
                    "Scale Target이 비어 있습니다. 지정할 때까지 이 버튼은 크기를 바꾸지 않습니다.",
                    MessageType.Warning);
                return;
            }

            if (scaleTarget == button.transform)
            {
                EditorGUILayout.HelpBox(
                    "Scale Target이 버튼 자신입니다. 레이캐스트 영역이 스케일과 함께 변해, 축소될 때 " +
                    "커서가 영역을 벗어난 것으로 판정되어 확대/축소가 반복될 수 있습니다. " +
                    "자식 오브젝트를 지정하세요.",
                    MessageType.Error);
                return;
            }

            if (!scaleTarget.IsChildOf(button.transform))
            {
                EditorGUILayout.HelpBox(
                    "Scale Target이 이 버튼의 자식이 아닙니다. 의도한 배선인지 확인하세요.",
                    MessageType.Warning);
            }

            if (HasRaycastTarget(scaleTarget))
            {
                EditorGUILayout.HelpBox(
                    "Scale Target 하위에 Raycast Target이 켜진 Graphic이 있습니다. 그만큼 히트 영역이 " +
                    "스케일을 따라 변합니다(TMP 텍스트는 기본으로 켜져 있습니다). 해당 Graphic의 " +
                    "Raycast Target을 끄고, 클릭을 받는 Image는 버튼 본체에 두세요.",
                    MessageType.Warning);
            }
        }

        private static bool HasRaycastTarget(RectTransform root)
        {
            var graphics = root.GetComponentsInChildren<Graphic>(true);

            for (int i = 0; i < graphics.Length; i++)
            {
                if (graphics[i].raycastTarget) return true;
            }

            return false;
        }

        // 기존 Button 프리팹은 자식들이 버튼 본체에 바로 붙어 있다. 손으로 래퍼를 만들고
        // 자식을 옮기는 작업을 한 번에 끝내기 위한 버튼이다.
        private void DrawCreateContentButton()
        {
            if (targets.Length != 1) return;

            var button = target as UIScaleButton;
            if (button == null) return;
            if (_scaleTarget.objectReferenceValue != null) return;
            if (button.transform.childCount == 0) return;

            EditorGUILayout.Space();

            if (!GUILayout.Button("Create Scale Content")) return;

            CreateScaleContent(button);
        }

        private void CreateScaleContent(UIScaleButton button)
        {
            var root = (RectTransform)button.transform;

            var contentGo = new GameObject("Content", typeof(RectTransform));
            Undo.RegisterCreatedObjectUndo(contentGo, "Create Scale Content");

            var content = (RectTransform)contentGo.transform;
            Undo.SetTransformParent(content, root, "Create Scale Content");

            content.localScale = Vector3.one;
            content.localRotation = Quaternion.identity;
            content.anchorMin = Vector2.zero;
            content.anchorMax = Vector2.one;
            content.pivot = new Vector2(0.5f, 0.5f);
            content.offsetMin = Vector2.zero;
            content.offsetMax = Vector2.zero;

            // 뒤에서부터 옮겨야 인덱스가 밀리지 않는다. Content 자신은 마지막 자식이라 건너뛴다.
            for (int i = root.childCount - 1; i >= 0; i--)
            {
                var child = root.GetChild(i);
                if (child == content) continue;

                Undo.SetTransformParent(child, content, "Create Scale Content");
                child.SetSiblingIndex(0);
            }

            serializedObject.Update();
            _scaleTarget.objectReferenceValue = content;
            serializedObject.ApplyModifiedProperties();

            Selection.activeGameObject = button.gameObject;
        }
    }
}
