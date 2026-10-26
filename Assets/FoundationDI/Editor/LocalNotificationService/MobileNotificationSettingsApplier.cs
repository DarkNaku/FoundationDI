using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using Unity.Notifications;
using Unity.Notifications.iOS;

namespace DarkNaku.FoundationDI.Editor
{
    // 모바일 알림 패키지의 프로젝트 설정을 서비스가 기대하는 값으로 맞춘다. 빌드 직전과 메뉴에서 부른다.
    //
    // - iOS는 앱을 켤 때 권한을 묻지 않는다. 서비스의 TryRequestPermissionOnce가 게임이 정한 때 묻는다 —
    //   첫 실행에 맥락 없이 물으면 거절률이 높고, 거절하면 다시 물을 수 없다.
    // - Android는 재부팅 뒤 다시 예약하고(안 하면 재부팅 이후 알림이 전부 사라진다), 정확한 시각 예약은
    //   쓰지 않는다(SCHEDULE_EXACT_ALARM 권한이 필요하고 리마인더에는 근사 시각이면 충분하다).
    // - 작은 아이콘은 LocalNotificationServiceSettings에 넣은 텍스처를 등록한다.
    internal sealed class MobileNotificationSettingsApplier : IPreprocessBuildWithReport
    {
        // 패키지의 NotificationSettingsManager.k_SettingsPath(internal)와 같은 값이다.
        private const string SETTINGS_PATH = "ProjectSettings/NotificationsSettings.asset";

        public int callbackOrder => 0;

        public void OnPreprocessBuild(BuildReport report) => Apply();

        [MenuItem("Tools/FoundationDI/Notifications/Apply Mobile Notification Settings")]
        internal static void Apply()
        {
            NotificationSettings.iOSSettings.RequestAuthorizationOnAppLaunch = false;
            NotificationSettings.iOSSettings.DefaultAuthorizationOptions =
                AuthorizationOption.Alert | AuthorizationOption.Sound;
            NotificationSettings.AndroidSettings.RescheduleOnDeviceRestart = true;
            NotificationSettings.AndroidSettings.ExactSchedulingOption = 0;

            ApplySmallIcon(FindSettings());
            AssetDatabase.SaveAssets();
        }

        private static void ApplySmallIcon(LocalNotificationServiceSettings settings)
        {
            var id = LocalNotificationServiceSettings.ANDROID_SMALL_ICON_ID;

            // 같은 ID로 다시 넣으면 목록에 겹쳐 쌓이므로 먼저 지운다. 없는 ID를 지우면 패키지가 경고를 내는데
            // 목록을 읽는 공개 API가 없어 설정 파일(JSON) 텍스트로 있는지만 확인한다.
            if (IsDrawableRegistered(id)) NotificationSettings.AndroidSettings.RemoveDrawableResource(id);

            if (settings == null || settings.AndroidSmallIcon == null) return;

            // 패키지가 빌드 때 그림을 읽어 res/drawable로 옮기므로 읽기 가능·무압축이어야 한다.
            var path = AssetDatabase.GetAssetPath(settings.AndroidSmallIcon);

            if (AssetImporter.GetAtPath(path) is TextureImporter importer &&
                (!importer.isReadable || importer.textureCompression != TextureImporterCompression.Uncompressed))
            {
                importer.isReadable = true;
                importer.mipmapEnabled = false;
                importer.alphaIsTransparency = true;
                importer.npotScale = TextureImporterNPOTScale.None;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.SaveAndReimport();
            }

            NotificationSettings.AndroidSettings.AddDrawableResource(id, settings.AndroidSmallIcon,
                                                                     NotificationIconType.Small);
        }

        private static bool IsDrawableRegistered(string id)
        {
            return File.Exists(SETTINGS_PATH) && File.ReadAllText(SETTINGS_PATH).Contains($"\"{id}\"");
        }

        private static LocalNotificationServiceSettings FindSettings()
        {
            var guids = AssetDatabase.FindAssets("t:" + nameof(LocalNotificationServiceSettings));

            if (guids.Length > 1)
            {
                Debug.LogWarning("[LocalNotificationService] LocalNotificationServiceSettings가 여럿이다. 첫 번째 것의 아이콘을 쓴다.");
            }

            return guids.Length == 0
                ? null
                : AssetDatabase.LoadAssetAtPath<LocalNotificationServiceSettings>(AssetDatabase.GUIDToAssetPath(guids[0]));
        }
    }
}
