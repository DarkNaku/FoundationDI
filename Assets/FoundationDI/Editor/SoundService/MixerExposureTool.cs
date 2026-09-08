using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.Audio;

namespace DarkNaku.FoundationDI.Editor
{
    /// <summary>
    /// AudioMixer 그룹의 Volume을 노출 파라미터로 만들고, 파라미터 이름을 그룹 이름과 맞춰 준다.
    /// 손으로 하면 "Volume 우클릭 → Expose to script → Exposed Parameters에서 이름 바꾸기"인데,
    /// 이름이 한 글자만 달라도 <see cref="AudioMixer.SetFloat"/>가 조용히 실패하는 자리다.
    ///
    /// 유니티가 이 영역에 공개 API를 주지 않아 <c>UnityEditor.Audio</c>의 내부 타입을 리플렉션으로 쓴다.
    /// 바인딩이 깨지면 <see cref="IsAvailable"/>이 false가 되고, 호출자는 수동 안내로 폴백한다.
    /// </summary>
    internal static class MixerExposureTool
    {
        /// <summary>노출 작업이 무엇을 바꿨는지 담는다. 말없이 남의 에셋을 고치므로 로그에 남긴다.</summary>
        internal sealed class ExposureReport
        {
            /// <summary>새로 노출한 그룹 이름.</summary>
            internal List<string> Exposed { get; } = new();

            /// <summary>이미 노출돼 있었지만 파라미터 이름이 그룹명과 달라 고친 그룹 이름.</summary>
            internal List<string> Renamed { get; } = new();

            /// <summary>손댈 필요가 없던 그룹 이름.</summary>
            internal List<string> AlreadyCorrect { get; } = new();

            internal bool HasChanges => Exposed.Count > 0 || Renamed.Count > 0;

            internal string Describe()
            {
                var parts = new List<string>();

                if (Exposed.Count > 0) parts.Add($"노출 {Exposed.Count}개({string.Join(", ", Exposed)})");
                if (Renamed.Count > 0) parts.Add($"이름 정정 {Renamed.Count}개({string.Join(", ", Renamed)})");

                return parts.Count == 0 ? "변경 없음" : string.Join(" / ", parts);
            }
        }

        private const BindingFlags Instance = BindingFlags.Public | BindingFlags.Instance;
        private const BindingFlags Static = BindingFlags.Public | BindingFlags.Static;

        private static readonly Type ControllerType =
            Type.GetType("UnityEditor.Audio.AudioMixerController, UnityEditor.CoreModule");

        private static readonly Type GroupType =
            Type.GetType("UnityEditor.Audio.AudioMixerGroupController, UnityEditor.CoreModule");

        private static readonly Type ParameterPathType =
            Type.GetType("UnityEditor.Audio.AudioGroupParameterPath, UnityEditor.CoreModule");

        private static readonly Type ExposedParameterType =
            Type.GetType("UnityEditor.Audio.ExposedAudioParameter, UnityEditor.CoreModule");

        private static readonly MethodInfo CreateMixerAtPath =
            ControllerType?.GetMethod("CreateMixerControllerAtPath", Static);

        private static readonly MethodInfo CreateNewGroup = ControllerType?.GetMethod("CreateNewGroup", Instance);
        private static readonly MethodInfo AddChildToParent = ControllerType?.GetMethod("AddChildToParent", Instance);
        private static readonly MethodInfo AddExposedParameter =
            ControllerType?.GetMethod("AddExposedParameter", Instance);

        private static readonly PropertyInfo MasterGroupProperty = ControllerType?.GetProperty("masterGroup");
        private static readonly PropertyInfo ExposedParametersProperty =
            ControllerType?.GetProperty("exposedParameters");

        private static readonly MethodInfo GetGuidForVolume = GroupType?.GetMethod("GetGUIDForVolume", Instance);
        private static readonly PropertyInfo GroupChildren = GroupType?.GetProperty("children");
        private static readonly PropertyInfo GroupName = GroupType?.GetProperty("name");

        private static readonly FieldInfo ParameterGuidField = ExposedParameterType?.GetField("guid");
        private static readonly FieldInfo ParameterNameField = ExposedParameterType?.GetField("name");

        /// <summary>내부 API 바인딩이 모두 성립했는지. false면 이 도구를 쓸 수 없다.</summary>
        internal static bool IsAvailable =>
            ControllerType != null && GroupType != null && ParameterPathType != null &&
            ExposedParameterType != null && CreateMixerAtPath != null && CreateNewGroup != null &&
            AddChildToParent != null && AddExposedParameter != null && MasterGroupProperty != null &&
            ExposedParametersProperty != null && GetGuidForVolume != null && GroupChildren != null &&
            GroupName != null && ParameterGuidField != null && ParameterNameField != null;

        /// <summary>노출 파라미터 이름으로 쓸 수 있게 그룹 이름을 정규화한다(공백 제거).</summary>
        internal static string NormalizeParameterName(string groupName) =>
            string.IsNullOrEmpty(groupName) ? string.Empty : groupName.Replace(" ", "");

        /// <summary>
        /// Master와 지정한 자식 그룹을 가진 믹서를 만들고, 모든 그룹의 Volume을 노출한다.
        /// </summary>
        /// <param name="assetPath">'Assets/'로 시작하는 .mixer 경로.</param>
        /// <param name="childGroupNames">Master 아래에 만들 그룹 이름들.</param>
        internal static AudioMixer CreateDefaultMixer(string assetPath, IReadOnlyList<string> childGroupNames)
        {
            if (!EnsureAvailable()) return null;

            var controller = CreateMixerAtPath.Invoke(null, new object[] { assetPath });

            if (controller == null)
            {
                Debug.LogError($"[SoundService] AudioMixer를 만들지 못했습니다: {assetPath}");
                return null;
            }

            var master = MasterGroupProperty.GetValue(controller);

            if (childGroupNames != null)
            {
                foreach (var name in childGroupNames)
                {
                    if (string.IsNullOrWhiteSpace(name)) continue;

                    var group = CreateNewGroup.Invoke(controller, new object[] { name, false });

                    AddChildToParent.Invoke(controller, new[] { group, master });
                }
            }

            var mixer = (AudioMixer)controller;

            ExposeAllGroupVolumes(mixer);

            EditorUtility.SetDirty(mixer);
            AssetDatabase.SaveAssets();

            return mixer;
        }

        /// <summary>
        /// 믹서의 모든 그룹에 대해 Volume 노출을 보장하고, 노출 파라미터 이름을 그룹명으로 맞춘다.
        /// 이미 올바른 그룹은 건드리지 않는다.
        /// </summary>
        internal static ExposureReport ExposeAllGroupVolumes(AudioMixer mixer)
        {
            var report = new ExposureReport();

            if (mixer == null || !EnsureAvailable()) return report;

            // GUID -> 원하는 파라미터 이름. ResolveExposedParameterPath는 " (of BGM)" 같은 값을
            // 돌려줘 쓸 수 없어서, 그룹을 직접 훑으며 매핑을 만든다.
            var wantedNames = new Dictionary<object, string>();
            var groupsByGuid = new Dictionary<object, object>();

            CollectGroups(MasterGroupProperty.GetValue(mixer), wantedNames, groupsByGuid);

            var exposedBefore = new HashSet<object>();

            foreach (var parameter in (Array)ExposedParametersProperty.GetValue(mixer))
            {
                exposedBefore.Add(ParameterGuidField.GetValue(parameter));
            }

            foreach (var pair in groupsByGuid)
            {
                if (exposedBefore.Contains(pair.Key)) continue;

                AddExposedParameter.Invoke(mixer,
                    new[] { Activator.CreateInstance(ParameterPathType, new[] { pair.Value, pair.Key }) });

                report.Exposed.Add(wantedNames[pair.Key]);
            }

            // 노출 직후 유니티가 붙이는 이름은 'MyExposedParam 1' 형태라 반드시 고쳐야 한다.
            var parameters = (Array)ExposedParametersProperty.GetValue(mixer);

            for (int i = 0; i < parameters.Length; i++)
            {
                var parameter = parameters.GetValue(i);
                var guid = ParameterGuidField.GetValue(parameter);

                if (!wantedNames.TryGetValue(guid, out string wanted)) continue;

                string current = (string)ParameterNameField.GetValue(parameter);

                if (current == wanted)
                {
                    if (!report.Exposed.Contains(wanted)) report.AlreadyCorrect.Add(wanted);
                    continue;
                }

                ParameterNameField.SetValue(parameter, wanted);
                parameters.SetValue(parameter, i);

                if (!report.Exposed.Contains(wanted)) report.Renamed.Add(wanted);
            }

            ExposedParametersProperty.SetValue(mixer, parameters);

            if (report.HasChanges) EditorUtility.SetDirty(mixer);

            return report;
        }

        /// <summary>Master부터 재귀로 훑어 그룹의 볼륨 GUID와 원하는 파라미터 이름을 모은다.</summary>
        private static void CollectGroups(object group, IDictionary<object, string> wantedNames,
            IDictionary<object, object> groupsByGuid)
        {
            if (group == null) return;

            var guid = GetGuidForVolume.Invoke(group, null);

            wantedNames[guid] = NormalizeParameterName((string)GroupName.GetValue(group));
            groupsByGuid[guid] = group;

            if (GroupChildren.GetValue(group) is not Array children) return;

            foreach (var child in children)
            {
                CollectGroups(child, wantedNames, groupsByGuid);
            }
        }

        private static bool EnsureAvailable()
        {
            if (IsAvailable) return true;

            Debug.LogError("[SoundService] AudioMixer 노출 파라미터를 자동 설정할 수 없습니다. " +
                           "유니티 내부 API가 바뀐 것으로 보입니다. " +
                           "Output Manager의 'How to add an Output' 안내를 따라 수동으로 설정하세요.");

            return false;
        }
    }
}
