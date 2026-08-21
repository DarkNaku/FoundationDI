using System;
using System.Text.RegularExpressions;
using UnityEditor;

namespace DarkNaku.FoundationDI.Editor
{
    /// <summary>UI 요소 이름 검증과 프리팹 경로 → 로드 키 도출.</summary>
    public static class UIElementNaming
    {
        private const string ResourcesFolder = "/Resources/";
        private static readonly Regex Identifier = new(@"^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.Compiled);

        // C# 예약어 중 타입 이름으로 쓰일 만한 것들. 전체 목록이 필요하면 CodeDomProvider를 쓸 수 있으나
        // 에디터 어셈블리에서 System.CodeDom 의존을 늘리지 않기 위해 최소 집합으로 둔다.
        private static readonly string[] Keywords =
        {
            "abstract", "as", "base", "bool", "break", "byte", "case", "catch", "char", "checked",
            "class", "const", "continue", "decimal", "default", "delegate", "do", "double", "else",
            "enum", "event", "explicit", "extern", "false", "finally", "fixed", "float", "for",
            "foreach", "goto", "if", "implicit", "in", "int", "interface", "internal", "is", "lock",
            "long", "namespace", "new", "null", "object", "operator", "out", "override", "params",
            "private", "protected", "public", "readonly", "ref", "return", "sbyte", "sealed",
            "short", "sizeof", "stackalloc", "static", "string", "struct", "switch", "this", "throw",
            "true", "try", "typeof", "uint", "ulong", "unchecked", "unsafe", "ushort", "using",
            "virtual", "void", "volatile", "while",
        };

        public static bool TryValidate(string name, out string error)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                error = "이름이 비어 있습니다.";
                return false;
            }

            if (char.IsDigit(name[0]))
            {
                error = "이름은 숫자로 시작할 수 없습니다.";
                return false;
            }

            if (!Identifier.IsMatch(name))
            {
                error = "이름은 영문/숫자/밑줄만 쓸 수 있는 C# 식별자여야 합니다(공백·기호 불가).";
                return false;
            }

            if (Array.IndexOf(Keywords, name) >= 0)
            {
                error = $"'{name}'은(는) C# 예약어라 타입 이름으로 쓸 수 없습니다.";
                return false;
            }

            error = string.Empty;

            return true;
        }

        /// <summary>
        /// 프리팹 에셋 경로에서 IResourceService 로드 키를 도출한다.
        /// Resources 폴더 아래면 그 기준 상대 경로(확장자 제거), 아니면 경로 전체(Addressables 주소).
        /// </summary>
        public static string ResolveResourceKey(string prefabAssetPath)
        {
            var normalized = prefabAssetPath.Replace('\\', '/');
            var index = normalized.LastIndexOf(ResourcesFolder, StringComparison.Ordinal);

            if (index < 0) return normalized;

            var relative = normalized[(index + ResourcesFolder.Length)..];
            var dot = relative.LastIndexOf('.');

            return dot >= 0 ? relative[..dot] : relative;
        }

        /// <summary>
        /// '{name}View' 타입이 이미 로드된 어셈블리(다른 폴더/네임스페이스 포함)에 존재하면 그 타입을 반환한다.
        /// 마법사는 File.Exists만으로는 파일 위치만 다른 기존 타입과의 충돌(CS0101)을 잡지 못하므로
        /// 파일 존재 검사와 별개로 타입 존재 여부를 확인해야 한다.
        /// </summary>
        public static Type FindExistingViewType(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;

            var typeName = $"{name}View";

            foreach (var type in TypeCache.GetTypesDerivedFrom<UIView>())
            {
                if (type.Name == typeName) return type;
            }

            return null;
        }

        /// <summary>'{name}Presenter' 타입이 이미 로드된 어셈블리에 존재하면 그 타입을 반환한다.</summary>
        public static Type FindExistingPresenterType(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;

            var typeName = $"{name}Presenter";

            foreach (var type in TypeCache.GetTypesDerivedFrom<UIPresenter>())
            {
                if (type.Name == typeName) return type;
            }

            return null;
        }
    }
}
