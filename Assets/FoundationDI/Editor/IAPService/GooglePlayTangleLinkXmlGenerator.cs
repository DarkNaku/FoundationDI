using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.UnityLinker;

namespace DarkNaku.FoundationDI.Editor
{
    // 영수증 검증기는 GooglePlayTangle을 리플렉션으로만 부른다(GooglePlayTangleLocator).
    // 링커는 그 사용을 볼 수 없어 IL2CPP 빌드에서 생성 클래스를 걷어내고, 검증기는 "Tangle 없음"
    // 경고 뒤 검증 없이 통과한다. 그래서 빌드마다 실제로 있는 Tangle 타입만 보존한다.
    //
    // 없는 타입을 적지 않는 이유: 그 어셈블리는 있지만 타입이 없을 때의 링커 동작에 기대지
    // 않으려는 것이다(ignoreIfMissing은 어셈블리 단위다). 패키지 위치와 무관하게 동작해야 하는
    // 이유는 FoundationDILinkXmlGenerator의 주석과 같다.
    public class GooglePlayTangleLinkXmlGenerator : IUnityLinkerProcessor
    {
        // FoundationDILinkXmlGenerator와 같은 폴더(Assets/ 밖)에 둔다. 그 어셈블리는 참조하지 않으므로 값을 옮겨 적는다.
        internal const string OutputDirectory = "Library/com.darknaku.foundationdi";
        internal const string OutputFileName = "link.googleplaytangle.xml";

        public int callbackOrder => 0;

        public string GenerateAdditionalLinkXmlFile(BuildReport report, UnityLinkerBuildPipelineData data)
        {
            var types = new List<(string Assembly, string Type)>();

            foreach (var name in GooglePlayTangleLocator.TypeNames)
            {
                var type = GooglePlayTangleLocator.FindInLoadedAssemblies(name);
                if (type != null) types.Add((type.Assembly.GetName().Name, name));
            }

            var path = Path.Combine(OutputDirectory, OutputFileName);

            Directory.CreateDirectory(OutputDirectory);
            File.WriteAllText(path, BuildLinkXml(types), new UTF8Encoding(false));

            return Path.GetFullPath(path);
        }

        // 순수 함수로 잘라 두어 빌드 없이 EditMode에서 검증한다.
        internal static string BuildLinkXml(IReadOnlyList<(string Assembly, string Type)> types)
        {
            var builder = new StringBuilder();

            builder.AppendLine("<?xml version=\"1.0\" encoding=\"utf-8\"?>");
            builder.AppendLine("<!-- GooglePlayTangleLinkXmlGenerator가 빌드마다 생성한다. 직접 수정하지 마라. -->");
            builder.AppendLine("<linker>");

            var byAssembly = new Dictionary<string, List<string>>();
            var order = new List<string>();

            foreach (var (assembly, type) in types)
            {
                if (!byAssembly.TryGetValue(assembly, out var list))
                {
                    list = new List<string>();
                    byAssembly[assembly] = list;
                    order.Add(assembly);
                }

                list.Add(type);
            }

            foreach (var assembly in order)
            {
                builder.AppendLine($"  <assembly fullname=\"{assembly}\" ignoreIfMissing=\"1\">");

                foreach (var type in byAssembly[assembly])
                {
                    builder.AppendLine($"    <type fullname=\"{type}\" preserve=\"all\" />");
                }

                builder.AppendLine("  </assembly>");
            }

            builder.AppendLine("</linker>");

            return builder.ToString();
        }
    }
}
