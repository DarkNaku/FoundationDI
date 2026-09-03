using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.UnityLinker;

namespace DarkNaku.FoundationDI.Editor
{
    // IL2CPP 빌드에서 FoundationDI의 옵셔널 어댑터 어셈블리와 그 뒤의 SDK 어셈블리가
    // 통째로 스트리핑되는 것을 막는다.
    //
    // **왜 그냥 link.xml 파일을 패키지에 두지 않는가**
    //
    // 에디터가 사용자 link.xml을 긁는 곳은 UnityEditorInternal.AssemblyStripper 한 곳뿐이고,
    // 그 구현은 Directory.GetFiles("Assets", "link.xml", SearchOption.AllDirectories)다.
    // 즉 Assets/ 아래만 본다. 이 패키지를 UPM(git URL)으로 설치한 소비 프로젝트에서는
    // 패키지가 Packages/ 또는 Library/PackageCache/ 아래에 놓이므로, 거기 넣어 둔 link.xml은
    // 영원히 읽히지 않는다. 개발 호스트 프로젝트(패키지가 Assets/FoundationDI/에 있는 이 리포)
    // 에서만 동작하고 정작 소비 프로젝트에서 조용히 실패하는, 가장 나쁜 종류의 차이가 된다.
    //
    // IUnityLinkerProcessor.GenerateAdditionalLinkXmlFile은 같은 AssemblyStripper가 그 Assets
    // 스캔 바로 앞에서 호출하며, 패키지 위치와 무관하게 동작한다. Unity 자신도 패키지에서
    // 같은 방법을 쓴다(Unity.Services.Core.Editor, Unity.InputSystem).
    public class FoundationDILinkXmlGenerator : IUnityLinkerProcessor
    {
        // 생성물을 Assets/ 밖에 둔다. Assets/ 안에 쓰면 빌드마다 에셋 임포트가 돌고
        // 소비 프로젝트의 작업 트리에 생성 파일이 끼어든다.
        internal const string OutputDirectory = "Library/com.darknaku.foundationdi";
        internal const string OutputFileName = "link.xml";

        public int callbackOrder => 0;

        public string GenerateAdditionalLinkXmlFile(BuildReport report, UnityLinkerBuildPipelineData data)
        {
            var path = Path.Combine(OutputDirectory, OutputFileName);

            Directory.CreateDirectory(OutputDirectory);
            File.WriteAllText(path, BuildLinkXml(SdkDefineTable.Entries), new UTF8Encoding(false));

            return Path.GetFullPath(path);
        }

        // 순수 함수로 잘라 두어 빌드 없이 EditMode에서 검증한다.
        //
        // 모든 항목에 ignoreIfMissing="1"이 필요하다. 어댑터 어셈블리는 defineConstraints
        // 때문에 심볼이 없으면 아예 존재하지 않고, SDK 어셈블리도 그 SDK를 안 쓰는 프로젝트에는
        // 없다. 이 속성이 없으면 심볼을 켜지 않은 프로젝트에서 링커가 에러를 낸다.
        internal static string BuildLinkXml(IReadOnlyList<SdkDefineEntry> entries)
        {
            var builder = new StringBuilder();

            builder.AppendLine("<?xml version=\"1.0\" encoding=\"utf-8\"?>");
            builder.AppendLine("<!-- FoundationDILinkXmlGenerator가 빌드마다 생성한다. 직접 수정하지 마라. -->");
            builder.AppendLine("<linker>");

            // 같은 어셈블리가 두 어댑터에 걸쳐 있어도 한 번만 쓴다.
            var written = new HashSet<string>();

            foreach (var entry in entries)
            {
                builder.AppendLine($"  <!-- {entry.DisplayName} ({entry.Symbol}) -->");

                AppendAssembly(builder, written, entry.AdapterAssembly);

                foreach (var assembly in entry.PreservedAssemblies)
                {
                    AppendAssembly(builder, written, assembly);
                }
            }

            builder.AppendLine("</linker>");

            return builder.ToString();
        }

        private static void AppendAssembly(StringBuilder builder, HashSet<string> written, string assembly)
        {
            if (string.IsNullOrEmpty(assembly)) return;
            if (!written.Add(assembly)) return;

            builder.AppendLine($"  <assembly fullname=\"{assembly}\" preserve=\"all\" ignoreIfMissing=\"1\" />");
        }
    }
}
