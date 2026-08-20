using System.IO;

namespace DarkNaku.FoundationDI.Editor
{
    /// <summary>
    /// 태그 목록을 <c>public static readonly</c> 상수를 가진 partial struct 코드로 생성한다.
    /// 생성 파일은 asmref로 런타임 어셈블리에 합류한다.
    /// </summary>
    internal static class PseudoEnumGenerator
    {
        internal static void Generate(string typeName, string[] tags, string filePath)
        {
            const string indent = "    ";

            using var writer = new StreamWriter(filePath);

            writer.WriteLine("// 이 파일은 SoundService 에디터 도구가 자동 생성합니다. 직접 수정하지 마세요.");
            writer.WriteLine("namespace DarkNaku.FoundationDI");
            writer.WriteLine("{");
            writer.WriteLine(indent + "public partial struct " + typeName);
            writer.WriteLine(indent + "{");

            if (tags is { Length: > 0 })
            {
                foreach (var tag in tags)
                {
                    if (string.IsNullOrEmpty(tag)) continue;

                    writer.WriteLine(indent + indent + "public static readonly " + typeName + " " + tag +
                                     " = new " + typeName + "(\"" + tag + "\");");
                }
            }

            writer.WriteLine(indent + "}");
            writer.WriteLine("}");
        }
    }
}
