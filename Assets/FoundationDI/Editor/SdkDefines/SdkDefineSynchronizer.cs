using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.Build;
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
            // 컴파일 중에는 아직 로드되지 않은 어셈블리가 있을 수 있다. 그 상태로 판정하면
            // 있는 SDK를 없다고 보고 심볼을 지워버린다. 다음 리로드에서 다시 돌면 된다.
            if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;

            var present = DetectPresentSdks();
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

        private static Dictionary<string, bool> DetectPresentSdks()
        {
            var wanted = new Dictionary<string, string>();     // 마커 타입 → 심볼
            var present = new Dictionary<string, bool>();

            foreach (var entry in SdkDefineTable.Entries)
            {
                wanted[entry.MarkerType] = entry.Symbol;
                present[entry.Symbol] = false;
            }

            // 어셈블리별 GetType 호출은 로드된 어셈블리 수(수백)만큼만 돌고 도메인 리로드당
            // 한 번뿐이다. Type.GetType("이름, 어셈블리")를 쓰지 않는 이유는 SDK 버전에 따라
            // 어셈블리 이름이 바뀌기 때문이다.
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                foreach (var pair in wanted)
                {
                    if (present[pair.Value]) continue;

                    if (assembly.GetType(pair.Key, false) != null) present[pair.Value] = true;
                }
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
