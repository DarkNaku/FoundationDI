using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace DarkNaku.FoundationDI.Editor
{
    // EDM4U가 읽는 Play In-App Review 의존성 선언 파일.
    //
    // 패키지 안에 고정으로 두지 않는다. EDM4U는 패키지 폴더도 스캔하므로, 그러면 리뷰를 쓰지 않는
    // 프로젝트에도 라이브러리가 강제로 들어간다(UmpDependenciesFile과 같은 이유).
    // iOS는 StoreKit(OS 내장)이라 선언할 것이 없다.
    internal static class PlayReviewDependenciesFile
    {
        internal const string AssetPath = "Assets/FoundationDI.Data/Editor/FoundationDIPlayReviewDependencies.xml";
        internal const string AndroidPackage = "com.google.android.play:review:2.0.2";

        internal static string BuildXml()
        {
            return "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n" +
                   "<!-- FoundationDI ReviewService가 생성한다. ReviewServiceSettings의 Use Play In-App Review로 켜고 끈다. 손으로 고치지 않는다. -->\n" +
                   "<dependencies>\n" +
                   "  <androidPackages>\n" +
                   $"    <androidPackage spec=\"{AndroidPackage}\">\n" +
                   "      <repositories>\n" +
                   "        <repository>https://maven.google.com/</repository>\n" +
                   "      </repositories>\n" +
                   "    </androidPackage>\n" +
                   "  </androidPackages>\n" +
                   "</dependencies>\n";
        }

        internal static bool ShouldExist(IEnumerable<bool> useFlags) => useFlags.Any(flag => flag);

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
