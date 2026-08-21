using System;
using System.Text;

namespace DarkNaku.FoundationDI.Editor
{
    /// <summary>UI 요소 생성 마법사가 쓰는 스크립트 템플릿.</summary>
    public static class UIElementTemplates
    {
        public static string View(string ns, string name)
        {
            var body = $"public class {name}View : UIView\n{{\n}}\n";

            return Compose(ns, body);
        }

        public static string Presenter(string ns, string name, UIElementMode mode, string resourceKey)
        {
            var body = new StringBuilder()
                .Append($"[UIPrefab(\"{resourceKey}\")]\n")
                .Append($"public class {name}Presenter : {BaseTypeOf(mode)}<{name}View>\n")
                .Append("{\n")
                .Append("    // 패키지를 다른 어셈블리에서 파생하므로 protected internal override로 선언한다.\n")
                .Append("    protected internal override void OnInitialize()\n")
                .Append("    {\n")
                .Append("    }\n")
                .Append("}\n")
                .ToString();

            return Compose(ns, body);
        }

        private static string BaseTypeOf(UIElementMode mode) => mode switch
        {
            UIElementMode.Page => "UIPagePresenter",
            UIElementMode.Popup => "UIPopupPresenter",
            UIElementMode.Overlay => "UIOverlayPresenter",
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null),
        };

        private static string Compose(string ns, string body)
        {
            var sb = new StringBuilder().Append("using DarkNaku.FoundationDI;\n\n");

            if (string.IsNullOrWhiteSpace(ns)) return sb.Append(body).ToString();

            sb.Append($"namespace {ns}\n{{\n");

            foreach (var line in body.TrimEnd('\n').Split('\n'))
            {
                sb.Append(line.Length > 0 ? "    " + line : line).Append('\n');
            }

            return sb.Append("}\n").ToString();
        }
    }
}
