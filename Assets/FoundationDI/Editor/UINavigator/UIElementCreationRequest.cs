using System;
using UnityEngine;

namespace DarkNaku.FoundationDI.Editor
{
    /// <summary>도메인 리로드를 넘어 전달되는 생성 요청. SessionState에 JSON으로 보관된다.</summary>
    [Serializable]
    public sealed class UIElementCreationRequest
    {
        public string Name;
        public UIElementMode Mode;
        public string Namespace;
        public string PrefabPath;

        public string ToJson() => JsonUtility.ToJson(this);

        public static UIElementCreationRequest FromJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;

            try
            {
                var request = JsonUtility.FromJson<UIElementCreationRequest>(json);

                return string.IsNullOrEmpty(request?.Name) ? null : request;
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
