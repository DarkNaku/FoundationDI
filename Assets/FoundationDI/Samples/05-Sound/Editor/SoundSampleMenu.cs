using UnityEditor;

namespace DarkNaku.FoundationDI.SamplesEditor
{
    /// <summary>
    /// 샘플별 오디오 설치/제거 메뉴. 샘플을 추가하면 이 파일에 항목 한 쌍을 더 만든다.
    /// (MenuItem 경로는 컴파일 타임 상수여야 해서 샘플마다 명시적으로 적는다.)
    /// </summary>
    public static class SoundSampleMenu
    {
        private const string Sound05Root = "Tools/FoundationDI/Sound/Sample Data/05 Sound/";

        [MenuItem(Sound05Root + "Install Into Project", false, 80)]
        private static void InstallSound05() => SoundSampleDataInstaller.Install(SoundSampleData.Sound);

        [MenuItem(Sound05Root + "Install Into Project", true)]
        private static bool ValidateInstallSound05() => !SoundSampleDataInstaller.IsInstalled(SoundSampleData.Sound);

        [MenuItem(Sound05Root + "Remove From Project", false, 81)]
        private static void RemoveSound05() => SoundSampleDataInstaller.Uninstall(SoundSampleData.Sound);

        [MenuItem(Sound05Root + "Remove From Project", true)]
        private static bool ValidateRemoveSound05() => SoundSampleDataInstaller.IsInstalled(SoundSampleData.Sound);
    }
}
