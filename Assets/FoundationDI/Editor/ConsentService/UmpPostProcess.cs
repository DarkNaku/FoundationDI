using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Android;
using UnityEditor.Build;
using UnityEngine;
#if UNITY_IOS
using UnityEditor.Callbacks;
using UnityEditor.iOS.Xcode;
#endif

namespace DarkNaku.FoundationDI.Editor
{
    // UMP가 요구하는 AdMob 앱 ID와 Basic 모드의 Firebase 수집 기본값을 넣는다.
    // 이미 있는 키는 덮지 않는다(LevelPlay AdMob 어댑터·Firebase 설정이 넣었을 수 있다).
    internal sealed class UmpPostProcess : IPostGenerateGradleAndroidProject
    {
        public int callbackOrder => 1002;

        public void OnPostGenerateGradleAndroidProject(string unityLibraryPath)
        {
            var settings = ConsentServiceSettingsLocator.FindAll().FirstOrDefault();
            var projectRoot = Directory.GetParent(unityLibraryPath).FullName;
            var manifests = Directory.GetFiles(projectRoot, "AndroidManifest.xml", SearchOption.AllDirectories)
                                     .Where(UmpBuildRules.IsSourceManifest)
                                     .ToArray();
            var target = Path.Combine(unityLibraryPath, "src", "main", "AndroidManifest.xml");

            if (!File.Exists(target)) return;

            var xml = File.ReadAllText(target);
            var dirty = false;

            bool AnyHas(string key) => manifests.Any(m => AndroidManifestEditor.HasMetaData(File.ReadAllText(m), key));

            if (settings != null)
            {
                switch (UmpBuildRules.DecideAppId(settings.UseUmp, settings.AdMobAppIdAndroid,
                                                  AnyHas(UmpBuildRules.AndroidAppIdKey)))
                {
                    case AppIdAction.Inject:
                        xml = AndroidManifestEditor.AddMetaData(xml, UmpBuildRules.AndroidAppIdKey, settings.AdMobAppIdAndroid);
                        dirty = true;
                        break;

                    case AppIdAction.Fail:
                        throw new BuildFailedException("[ConsentService] Use UMP가 켜져 있지만 AdMob 앱 ID(Android)가 없다. " +
                                                       "ConsentServiceSettings에 넣거나 LevelPlay AdMob 어댑터 설정에 넣는다.");
                }
            }

            if (UmpBuildRules.ShouldDisableFirebaseCollection(AnyAnalyticsWaitsForConsent(), IsFirebaseActive(),
                                                              AnyHas(UmpBuildRules.AndroidFirebaseCollectionKey)))
            {
                xml = AndroidManifestEditor.AddMetaData(xml, UmpBuildRules.AndroidFirebaseCollectionKey, "false");
                dirty = true;
            }

            if (dirty) WriteUtf8(target, xml);
        }

#if UNITY_IOS
        // FDI_HapticiOSPostProcess(999) → AttIosPostProcess(1000) → IosExportCompliance(1001) 다음.
        [PostProcessBuild(1002)]
        public static void OnPostprocessBuild(BuildTarget target, string path)
        {
            if (target != BuildTarget.iOS) return;

            var plistPath = Path.Combine(path, "Info.plist");
            if (!File.Exists(plistPath)) return;

            var plist = new PlistDocument();
            plist.ReadFromFile(plistPath);

            var settings = ConsentServiceSettingsLocator.FindAll().FirstOrDefault();
            var dirty = false;

            if (settings != null)
            {
                switch (UmpBuildRules.DecideAppId(settings.UseUmp, settings.AdMobAppIdIos,
                                                  plist.root.values.ContainsKey(UmpBuildRules.IosAppIdKey)))
                {
                    case AppIdAction.Inject:
                        plist.root.SetString(UmpBuildRules.IosAppIdKey, settings.AdMobAppIdIos);
                        dirty = true;
                        break;

                    case AppIdAction.Fail:
                        throw new BuildFailedException("[ConsentService] Use UMP가 켜져 있지만 AdMob 앱 ID(iOS)가 없다. " +
                                                       "ConsentServiceSettings에 넣거나 LevelPlay AdMob 어댑터 설정에 넣는다.");
                }
            }

            if (UmpBuildRules.ShouldDisableFirebaseCollection(AnyAnalyticsWaitsForConsent(), IsFirebaseActive(),
                                                              plist.root.values.ContainsKey(UmpBuildRules.IosFirebaseCollectionKey)))
            {
                plist.root.SetBoolean(UmpBuildRules.IosFirebaseCollectionKey, false);
                dirty = true;
            }

            if (dirty) plist.WriteToFile(plistPath);
        }
#endif

        private static bool AnyAnalyticsWaitsForConsent()
        {
            return AssetDatabase.FindAssets("t:" + nameof(AnalyticsServiceSettings))
                                .Select(g => AssetDatabase.LoadAssetAtPath<AnalyticsServiceSettings>(AssetDatabase.GUIDToAssetPath(g)))
                                .Any(s => s != null && s.WaitForConsent);
        }

        private static bool IsFirebaseActive()
        {
#if FOUNDATIONDI_FIREBASE
            return true;
#else
            return false;
#endif
        }

        private static void WriteUtf8(string path, string xml)
        {
            var doc = new System.Xml.XmlDocument();
            doc.LoadXml(xml);

            var settings = new System.Xml.XmlWriterSettings { Encoding = new UTF8Encoding(false), Indent = true };

            using var writer = System.Xml.XmlWriter.Create(path, settings);
            doc.Save(writer);
        }
    }
}
