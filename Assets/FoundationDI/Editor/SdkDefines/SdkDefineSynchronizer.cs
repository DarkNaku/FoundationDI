using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Compilation;
using UnityEngine;

namespace DarkNaku.FoundationDI.Editor
{
    // SDK가 프로젝트에 임포트되면 해당 스크립팅 심볼을 켜고, 사라지면 끈다.
    //
    // 이게 없으면 심볼과 SDK가 따로 논다. 실제로 이 리포에서 그런 일이 있었다 —
    // FOUNDATIONDI_FIREBASE는 ProjectSettings에 커밋됐는데 Firebase SDK는 커밋되지 않아,
    // 새로 클론하면 FoundationDI.Firebase가 참조 없이 컴파일을 시도하다 깨지는 상태였다.
    [InitializeOnLoad]
    public static class SdkDefineSynchronizer
    {
        private const string AutoManageKey = "FoundationDI.SdkDefines.AutoManage";
        private const string AutoManageMenu = "Tools/FoundationDI/SDK Defines/Auto Manage";
        private const string SyncNowMenu = "Tools/FoundationDI/SDK Defines/Sync Now";

        // 플랫폼을 전환해도 일관되게 유지되도록 세 타깃을 함께 갱신한다.
        // 활성 타깃만 건드리면 iOS로 바꾼 순간 심볼이 사라진 것처럼 보인다.
        private static readonly NamedBuildTarget[] Targets =
        {
            NamedBuildTarget.Android,
            NamedBuildTarget.iOS,
            NamedBuildTarget.Standalone,
        };

        static SdkDefineSynchronizer()
        {
            // 도메인 리로드 직후에는 PlayerSettings 쓰기가 무시될 수 있다. 한 틱 미룬다.
            EditorApplication.delayCall += () => Sync(verbose: false);
        }

        // 에셋이 들어오고 나가는 시점에도 확인한다. 도메인 리로드만으로는 부족하다 —
        // SDK를 지우면 심볼이 아직 켜져 있어 어댑터 컴파일이 실패하고, 컴파일이 실패하면
        // 도메인이 리로드되지 않아 [InitializeOnLoad]가 다시 돌지 않는다. 이 훅은 삭제가
        // 반영되는 순간(아직 낡은 도메인이 살아 있을 때) 실행되므로 그 교착을 미리 끊는다.
        private class AssetHook : AssetPostprocessor
        {
            private static void OnPostprocessAllAssets(string[] imported, string[] deleted,
                                                       string[] moved, string[] movedFrom)
            {
                EditorApplication.delayCall += () => Sync(verbose: false);
            }
        }

        public static bool AutoManage
        {
            get => EditorPrefs.GetBool(AutoManageKey, true);
            set => EditorPrefs.SetBool(AutoManageKey, value);
        }

        [MenuItem(AutoManageMenu)]
        private static void ToggleAutoManage()
        {
            AutoManage = !AutoManage;

            if (AutoManage) Sync(verbose: true);
        }

        [MenuItem(AutoManageMenu, true)]
        private static bool ToggleAutoManageValidate()
        {
            Menu.SetChecked(AutoManageMenu, AutoManage);
            return true;
        }

        // 자동 관리를 꺼둔 상태에서도 한 번은 맞추고 싶을 때가 있다.
        [MenuItem(SyncNowMenu)]
        private static void SyncNow() => Apply(verbose: true);

        private static void Sync(bool verbose)
        {
            if (!AutoManage) return;

            Apply(verbose);
        }

        private static void Apply(bool verbose)
        {
            // 임포트가 끝나지 않은 상태에서는 CompilationPipeline이 아직 반영되지 않은
            // 어셈블리를 빼놓고 답할 수 있다. 그 상태로 판정하면 있는 SDK를 없다고 보고
            // 심볼을 지운다. 다음 리로드나 다음 임포트 훅에서 다시 돌면 된다.
            if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;

            var present = DetectPresent(CollectAvailableAssemblyNames());
            var changes = new List<string>();

            foreach (var target in Targets)
            {
                string current;

                try
                {
                    current = PlayerSettings.GetScriptingDefineSymbols(target);
                }
                catch (Exception e)
                {
                    // 해당 모듈이 설치되지 않은 타깃은 조회 자체가 실패할 수 있다.
                    if (verbose) Debug.LogWarning($"[FoundationDI] {target.TargetName} 심볼을 읽지 못했다: {e.Message}");
                    continue;
                }

                var resolved = Resolve(current, present);

                // 같으면 쓰지 않는다. 쓰면 재컴파일이 걸리고, 매 리로드마다 쓰면 무한 루프가 된다.
                if (resolved == current) continue;

                PlayerSettings.SetScriptingDefineSymbols(target, resolved);
                changes.Add($"{target.TargetName}: \"{current}\" → \"{resolved}\"");
            }

            if (changes.Count > 0)
            {
                Debug.Log($"[FoundationDI] SDK 스크립팅 심볼을 갱신했다.\n{string.Join("\n", changes)}");
            }
            else if (verbose)
            {
                Debug.Log("[FoundationDI] SDK 스크립팅 심볼이 이미 최신이다. " + DescribePresent(present));
            }
        }

        // 로드된 어셈블리(AppDomain)로 판정하면 안 된다. .NET 어셈블리는 DLL을 지워도
        // 도메인이 살아 있는 한 언로드되지 않기 때문이다. 게다가 SDK를 지우면 심볼이 아직
        // 켜져 있어 어댑터 컴파일이 실패하고, 컴파일이 실패하면 도메인이 리로드되지 않는다 —
        // 그 낡은 도메인에는 지운 SDK가 그대로 로드돼 있으므로 "있음"으로 오판하고,
        // 심볼이 영영 안 빠지는 데드락이 된다.
        //
        // CompilationPipeline은 도메인이 아니라 디스크 현재 상태를 본다. 컴파일이 깨진
        // 상태에서도 정확하다.
        private static List<string> CollectAvailableAssemblyNames()
        {
            var names = new List<string>();

            // precompiled DLL로 오는 SDK (Firebase 등)
            names.AddRange(CompilationPipeline.GetPrecompiledAssemblyNames());

            // asmdef로 오는 SDK (Unity.Purchasing, MaxSdk.Scripts 등)
            foreach (var assembly in CompilationPipeline.GetAssemblies(AssembliesType.Editor))
            {
                names.Add(assembly.name);
            }

            return names;
        }

        // 순수 함수. 어셈블리 이름 목록만 주면 심볼별 존재 여부를 낸다.
        internal static Dictionary<string, bool> DetectPresent(IEnumerable<string> availableAssemblyNames)
        {
            var available = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var raw in availableAssemblyNames)
            {
                if (string.IsNullOrEmpty(raw)) continue;

                // GetPrecompiledAssemblyNames는 "Firebase.Analytics.dll"처럼 확장자를 달고 온다.
                var name = raw.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
                    ? raw.Substring(0, raw.Length - 4)
                    : raw;

                available.Add(name);
            }

            var present = new Dictionary<string, bool>();

            foreach (var entry in SdkDefineTable.Entries)
            {
                present[entry.Symbol] = available.Contains(entry.AssemblyName);
            }

            return present;
        }

        private static string DescribePresent(IReadOnlyDictionary<string, bool> present)
        {
            var parts = new List<string>();

            foreach (var entry in SdkDefineTable.Entries)
            {
                var found = present.TryGetValue(entry.Symbol, out var value) && value;
                parts.Add($"{entry.DisplayName}={(found ? "있음" : "없음")}");
            }

            return string.Join(", ", parts);
        }

        // 순수 함수. PlayerSettings를 건드리지 않으므로 EditMode에서 그대로 검증할 수 있다.
        //
        // 규칙은 하나다: present에 들어 있는 심볼만 건드린다. 남이 넣은 심볼
        // (LEVELPLAY_DEPENDENCIES_INSTALLED 같은)은 순서까지 그대로 보존한다.
        internal static string Resolve(string currentSymbols, IReadOnlyDictionary<string, bool> present)
        {
            var kept = new List<string>();
            var seen = new HashSet<string>();

            foreach (var raw in (currentSymbols ?? string.Empty).Split(';'))
            {
                var symbol = raw.Trim();

                if (symbol.Length == 0) continue;
                if (!seen.Add(symbol)) continue;

                // 관리 대상인데 SDK가 없으면 떨어뜨린다. 관리 대상이 아니면 무조건 남긴다.
                if (present.TryGetValue(symbol, out var isPresent) && !isPresent) continue;

                kept.Add(symbol);
            }

            // 새로 켜야 하는 심볼은 표 순서대로 뒤에 붙인다. 기존 항목의 순서는 건드리지 않는다.
            foreach (var entry in SdkDefineTable.Entries)
            {
                if (!present.TryGetValue(entry.Symbol, out var isPresent) || !isPresent) continue;
                if (!seen.Add(entry.Symbol)) continue;

                kept.Add(entry.Symbol);
            }

            // present에 있지만 표에 없는 심볼(테스트가 직접 넣는 경우). 이게 없으면 제거는
            // 되는데 추가는 안 되는 비대칭이 생긴다 — present를 진실의 원천으로 두려면 필요하다.
            // 사전 순회 순서는 보장되지 않으므로 결정적이도록 정렬한다.
            var extras = new List<string>();

            foreach (var pair in present)
            {
                if (!pair.Value) continue;
                if (seen.Contains(pair.Key)) continue;

                extras.Add(pair.Key);
            }

            extras.Sort(StringComparer.Ordinal);

            foreach (var symbol in extras)
            {
                if (!seen.Add(symbol)) continue;

                kept.Add(symbol);
            }

            var builder = new StringBuilder();

            for (var i = 0; i < kept.Count; i++)
            {
                if (i > 0) builder.Append(';');
                builder.Append(kept[i]);
            }

            return builder.ToString();
        }
    }
}
