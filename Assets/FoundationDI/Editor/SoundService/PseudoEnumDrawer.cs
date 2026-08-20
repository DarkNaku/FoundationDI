using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace DarkNaku.FoundationDI.Editor
{
    /// <summary>
    /// <see cref="SFX"/>/<see cref="Track"/>/<see cref="Output"/> 같은 유사 enum을 드롭다운으로 그린다.
    /// 항목이 많을 때를 위해 접이식 검색 필터를 제공한다.
    /// </summary>
    public abstract class PseudoEnumDrawer : PropertyDrawer
    {
        private static readonly Dictionary<string, string> SearchFilters = new();
        private static readonly Dictionary<string, bool> SearchVisible = new();

        private string[] _names;
        private string[] _tags;
        private bool _cached;

        /// <summary>드롭다운 항목을 수집할 유사 enum 타입.</summary>
        protected abstract Type TagType { get; }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            Cache();

            EditorGUI.BeginProperty(position, label, property);

            var stringProp = property.FindPropertyRelative("value");
            string key = property.propertyPath;
            string current = stringProp.stringValue;

            if (!SearchFilters.TryGetValue(key, out string filter))
            {
                filter = string.Empty;
            }

            if (!SearchVisible.TryGetValue(key, out bool isVisible))
            {
                isVisible = false;
            }

            const float buttonWidth = 22f;
            const float spacing = 2f;

            float searchWidth = isVisible ? 90f : 0f;
            float closeWidth = isVisible ? 22f : 0f;
            float popupWidth = position.width - buttonWidth - spacing - searchWidth -
                               (isVisible ? spacing + closeWidth : 0f);

            if (popupWidth < 50f)
            {
                popupWidth = 50f;
            }

            var popupRect = new Rect(position.x, position.y, popupWidth, position.height);
            var searchButtonRect = new Rect(popupRect.xMax + spacing, position.y, buttonWidth, position.height);
            var searchRect = new Rect(searchButtonRect.xMax + spacing, position.y, searchWidth, position.height);
            var closeRect = new Rect(searchRect.xMax + spacing, position.y, closeWidth, position.height);

            var filteredIndices = BuildFilteredIndices(filter);
            var displayedNames = filteredIndices.Select(i => _names[i]).ToArray();

            int originalIndex = Array.IndexOf(_tags, current);

            if (originalIndex < 0)
            {
                originalIndex = Array.IndexOf(_names, "Null");

                if (originalIndex < 0)
                {
                    originalIndex = 0;
                }
            }

            int displayedIndex = filteredIndices.IndexOf(originalIndex);

            if (displayedIndex < 0)
            {
                displayedIndex = 0;
            }

            if (!isVisible)
            {
                if (GUI.Button(searchButtonRect, new GUIContent("s", "검색 열기")))
                {
                    SearchVisible[key] = true;
                    GUI.FocusControl(null);
                }
            }
            else
            {
                EditorGUI.BeginChangeCheck();

                string newFilter = EditorGUI.TextField(searchRect, GUIContent.none, filter);

                if (EditorGUI.EndChangeCheck())
                {
                    SearchFilters[key] = newFilter ?? string.Empty;
                }

                if (GUI.Button(closeRect, new GUIContent("x", "검색 닫기")))
                {
                    SearchVisible[key] = false;
                    SearchFilters[key] = string.Empty;
                    GUI.FocusControl(null);
                }
            }

            EditorGUI.BeginChangeCheck();

            int selected = EditorGUI.Popup(popupRect, label.text, displayedIndex, displayedNames);

            if (EditorGUI.EndChangeCheck() && selected >= 0 && selected < filteredIndices.Count)
            {
                stringProp.stringValue = _tags[filteredIndices[selected]];
                property.serializedObject.ApplyModifiedProperties();
            }

            EditorGUI.EndProperty();
        }

        private List<int> BuildFilteredIndices(string filter)
        {
            var indices = new List<int>();

            if (string.IsNullOrEmpty(filter))
            {
                for (int i = 0; i < _names.Length; i++)
                {
                    indices.Add(i);
                }

                return indices;
            }

            string lower = filter.ToLowerInvariant();

            for (int i = 0; i < _names.Length; i++)
            {
                if (_names[i].ToLowerInvariant().StartsWith(lower) || _tags[i].ToLowerInvariant().StartsWith(lower))
                {
                    indices.Add(i);
                }
            }

            if (indices.Count == 0)
            {
                int nullIndex = Array.IndexOf(_names, "Null");

                indices.Add(nullIndex < 0 ? 0 : nullIndex);
            }

            return indices;
        }

        private void Cache()
        {
            if (_cached) return;

            var fields = TagType
                .GetFields(BindingFlags.Public | BindingFlags.Static)
                .Where(field => field.FieldType == TagType)
                .OrderBy(field => field.Name == "Null" ? 0 : 1)
                .ThenBy(field => field.Name)
                .ToArray();

            _names = fields.Select(field => field.Name).ToArray();
            _tags = fields.Select(field => field.GetValue(null).ToString()).ToArray();

            if (_names.Length == 0)
            {
                _names = new[] { "-----" };
                _tags = new[] { string.Empty };
            }

            _cached = true;
        }
    }

    [CustomPropertyDrawer(typeof(SFX))]
    public class SFXDrawer : PseudoEnumDrawer
    {
        protected override Type TagType => typeof(SFX);
    }

    [CustomPropertyDrawer(typeof(Track))]
    public class TrackDrawer : PseudoEnumDrawer
    {
        protected override Type TagType => typeof(Track);
    }

    [CustomPropertyDrawer(typeof(Output))]
    public class OutputDrawer : PseudoEnumDrawer
    {
        protected override Type TagType => typeof(Output);
    }
}
