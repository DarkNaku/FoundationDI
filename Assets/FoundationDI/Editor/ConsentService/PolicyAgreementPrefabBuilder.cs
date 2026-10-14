using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace DarkNaku.FoundationDI.Editor
{
    // 기본 약관 창 프리팹을 코드로 만든다. 디자인을 바꾸려면 만들어진 프리팹을 복제해 고친다.
    internal static class PolicyAgreementPrefabBuilder
    {
        internal const string PrefabPath =
            "Assets/FoundationDI/Runtime/Services/ConsentService/Policy/PolicyAgreementView.prefab";

        [MenuItem("Tools/FoundationDI/Consent/Rebuild Default Policy Prompt Prefab")]
        private static void Rebuild()
        {
            var root = Build();
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Object.DestroyImmediate(root);
            AssetDatabase.Refresh();
            Debug.Log($"[ConsentService] 기본 약관 창 프리팹을 만들었다: {PrefabPath}");
        }

        internal static GameObject Build()
        {
            var root = new GameObject("PolicyAgreementView", typeof(RectTransform), typeof(Canvas),
                                      typeof(CanvasScaler), typeof(GraphicRaycaster));

            var canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            var scaler = root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080, 1920);
            scaler.matchWidthOrHeight = 0.5f;

            // 뒤쪽 입력을 막는 반투명 배경.
            var dim = CreateImage("Dim", root.transform, new Color(0f, 0f, 0f, 0.6f));
            Stretch(dim.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

            var panel = CreateImage("Panel", root.transform, Color.white);
            Stretch(panel.rectTransform, new Vector2(0.08f, 0.25f), new Vector2(0.92f, 0.75f), Vector2.zero, Vector2.zero);

            var layout = panel.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(48, 48, 48, 48);
            layout.spacing = 24;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandHeight = false;

            var title = CreateText("Title", panel.transform, 56, FontStyles.Bold);
            var message = CreateText("Message", panel.transform, 40, FontStyles.Normal);
            message.gameObject.AddComponent<LayoutElement>().flexibleHeight = 1;

            var links = new GameObject("Links", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            links.transform.SetParent(panel.transform, false);
            var linksLayout = links.GetComponent<HorizontalLayoutGroup>();
            linksLayout.spacing = 24;
            linksLayout.childAlignment = TextAnchor.MiddleCenter;
            linksLayout.childControlWidth = true;
            linksLayout.childControlHeight = true;

            var (termsButton, termsLabel) = CreateButton("Terms", links.transform, new Color(0.92f, 0.92f, 0.92f), 36);
            var (privacyButton, privacyLabel) = CreateButton("Privacy", links.transform, new Color(0.92f, 0.92f, 0.92f), 36);
            var (acceptButton, acceptLabel) = CreateButton("Accept", panel.transform, new Color(0.18f, 0.48f, 0.95f), 44);
            acceptLabel.color = Color.white;
            acceptButton.gameObject.AddComponent<LayoutElement>().minHeight = 140;

            var view = root.AddComponent<PolicyAgreementView>();
            view.SetReferences(title, message, acceptButton, acceptLabel, termsButton, termsLabel, privacyButton, privacyLabel);

            return root;
        }

        private static Image CreateImage(string name, Transform parent, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var image = go.GetComponent<Image>();
            image.color = color;
            return image;
        }

        private static TMP_Text CreateText(string name, Transform parent, float size, FontStyles style)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, false);
            var text = go.GetComponent<TextMeshProUGUI>();
            text.fontSize = size;
            text.fontStyle = style;
            text.color = new Color(0.1f, 0.1f, 0.1f);
            text.alignment = TextAlignmentOptions.Center;
            text.raycastTarget = false;
            return text;
        }

        private static (Button, TMP_Text) CreateButton(string name, Transform parent, Color color, float fontSize)
        {
            var image = CreateImage(name, parent, color);
            var button = image.gameObject.AddComponent<Button>();
            image.gameObject.AddComponent<LayoutElement>().minHeight = 110;

            var label = CreateText("Label", image.transform, fontSize, FontStyles.Normal);
            Stretch(label.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

            return (button, label);
        }

        private static void Stretch(RectTransform rect, Vector2 min, Vector2 max, Vector2 offsetMin, Vector2 offsetMax)
        {
            rect.anchorMin = min;
            rect.anchorMax = max;
            rect.offsetMin = offsetMin;
            rect.offsetMax = offsetMax;
        }
    }
}
