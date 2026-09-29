#if UNITY_IOS
using System.IO;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.iOS.Xcode;
#endif

namespace DarkNaku.FoundationDI.Editor
{
    // iOS 빌드의 Info.plist에 수출 규정 신고(ITSAppUsesNonExemptEncryption)를 넣는다.
    //
    // 이 키가 없으면 TestFlight에 올린 빌드마다 App Store Connect가 "수출 규정 관리 문서
    // 누락"을 띄우고, 웹에서 손으로 암호화 질문에 답하기 전까지 그 빌드는 제출도 외부 테스터
    // 배포도 할 수 없다. 유니티 플레이어 설정에는 이 키를 넣을 자리가 없어 빌드 후처리로만
    // 못 박을 수 있다.
    public static class IosExportCompliance
    {
        internal const string UsesNonExemptEncryptionKey = "ITSAppUsesNonExemptEncryption";

        // false = "면제 대상 외 암호화를 쓰지 않는다". HTTPS와 OS가 제공하는 암호화만 쓰는
        // 앱의 신고이며 이 패키지를 쓰는 대부분의 게임이 여기 속한다. true로 신고하려면
        // 애플이 발급한 ITSEncryptionExportComplianceCode가 따로 필요해 여기서 감당하지
        // 않는다 — 그런 프로젝트는 Enabled를 끄고 자기 후처리로 넣는다.
        internal const bool UsesNonExemptEncryption = false;

        // 아무 설정도 하지 않은 프로젝트가 그대로 빌드했을 때의 동작이다. 프로퍼티의 초기화식이
        // 아니라 상수로 둬야 테스트가 관찰할 수 있다 — 테스트가 값을 되돌리는 순간 프로퍼티의
        // 초기값은 가려지기 때문이다.
        internal const bool EnabledByDefault = true;

        // 자체 암호화(세이브 파일 AES 등)를 넣은 프로젝트에는 위 신고가 거짓이 되므로 끌 수 있다.
        //
        // 스위치가 스크립팅 심볼도 설정 에셋도 아닌 정적 프로퍼티인 이유는 빌드 스크립트가
        // 제어 지점이기 때문이다. BuildPipeline.BuildPlayer 앞에 한 줄 대입하면 같은 도메인
        // 안에서 후처리까지 그대로 이어진다. 심볼이면 값을 바꿀 때마다 도메인 리로드를
        // 기다려야 하고, 에셋이면 로드·수정·저장을 거쳐야 한다.
        public static bool Enabled { get; set; } = EnabledByDefault;

        // 이미 신고된 plist는 덮지 않는다. 프로젝트가 자기 후처리나 Info.plist 템플릿으로
        // true와 컴플라이언스 코드를 넣었을 수 있고, 그걸 false로 뒤집으면 조용히 허위
        // 신고가 된다 — 되돌아간 것처럼 보이지도 않아 발견이 늦다.
        internal static bool ShouldDeclare(bool enabled, bool alreadyDeclared)
        {
            return enabled && !alreadyDeclared;
        }

#if UNITY_IOS
        // AttIosPostProcess(1000)와 FDI_HapticiOSPostProcess(999)가 같은 산출물을 만진다.
        // Info.plist를 각자 읽고 쓰므로 그 뒤에 선다.
        [PostProcessBuild(1001)]
        public static void OnPostprocessBuild(BuildTarget target, string path)
        {
            if (target != BuildTarget.iOS) return;

            var plistPath = Path.Combine(path, "Info.plist");

            if (!File.Exists(plistPath)) return;

            var plist = new PlistDocument();
            plist.ReadFromFile(plistPath);

            var alreadyDeclared = plist.root.values.ContainsKey(UsesNonExemptEncryptionKey);

            if (!ShouldDeclare(Enabled, alreadyDeclared)) return;

            plist.root.SetBoolean(UsesNonExemptEncryptionKey, UsesNonExemptEncryption);
            plist.WriteToFile(plistPath);
        }
#endif
    }
}
