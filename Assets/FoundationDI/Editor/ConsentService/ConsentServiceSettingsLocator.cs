using System.Collections.Generic;
using UnityEditor;

namespace DarkNaku.FoundationDI.Editor
{
    internal static class ConsentServiceSettingsLocator
    {
        internal static List<ConsentServiceSettings> FindAll()
        {
            var result = new List<ConsentServiceSettings>();

            foreach (var guid in AssetDatabase.FindAssets("t:" + nameof(ConsentServiceSettings)))
            {
                var settings = AssetDatabase.LoadAssetAtPath<ConsentServiceSettings>(AssetDatabase.GUIDToAssetPath(guid));
                if (settings != null) result.Add(settings);
            }

            return result;
        }
    }
}
