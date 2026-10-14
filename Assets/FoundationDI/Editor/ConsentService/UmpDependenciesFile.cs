using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace DarkNaku.FoundationDI.Editor
{
    // EDM4U가 읽는 UMP 의존성 선언 파일.
    //
    // 패키지 안에 고정으로 두지 않는다. EDM4U는 패키지 폴더도 스캔하므로, 그러면 UMP를 쓰지 않는
    // 프로젝트에도 네이티브 의존성이 강제로 들어간다. 설정의 Use UMP를 켠 프로젝트에만 Assets 아래에 만든다.
    internal static class UmpDependenciesFile
    {
        internal const string AssetPath = "Assets/FoundationDI.Data/Editor/FoundationDIUmpDependencies.xml";
        internal const string AndroidPackage = "com.google.android.ump:user-messaging-platform:4.0.0";
        internal const string IosPod = "GoogleUserMessagingPlatform";
        internal const string IosPodVersion = "~> 3.1";

        internal static string BuildXml()
        {
            return "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n" +
                   "<!-- FoundationDI ConsentService가 생성한다. ConsentServiceSettings의 Use UMP로 켜고 끈다. 손으로 고치지 않는다. -->\n" +
                   "<dependencies>\n" +
                   "  <androidPackages>\n" +
                   $"    <androidPackage spec=\"{AndroidPackage}\">\n" +
                   "      <repositories>\n" +
                   "        <repository>https://maven.google.com/</repository>\n" +
                   "      </repositories>\n" +
                   "    </androidPackage>\n" +
                   "  </androidPackages>\n" +
                   "  <iosPods>\n" +
                   $"    <iosPod name=\"{IosPod}\" version=\"{IosPodVersion}\" addToAllTargets=\"false\" />\n" +
                   "  </iosPods>\n" +
                   "</dependencies>\n";
        }

        internal static bool ShouldExist(IEnumerable<bool> useUmpFlags) => useUmpFlags.Any(flag => flag);

        // 값이 실제로 바뀔 때만 쓴다. 매번 쓰면 임포트 → 동기화 → 임포트 루프가 생긴다.
        internal static bool Sync(bool shouldExist, string projectRoot)
        {
            var path = Path.Combine(projectRoot, AssetPath);

            if (!shouldExist)
            {
                if (!File.Exists(path)) return false;

                File.Delete(path);
                if (File.Exists(path + ".meta")) File.Delete(path + ".meta");
                return true;
            }

            var xml = BuildXml();

            if (File.Exists(path) && File.ReadAllText(path) == xml) return false;

            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, xml);
            return true;
        }
    }
}
