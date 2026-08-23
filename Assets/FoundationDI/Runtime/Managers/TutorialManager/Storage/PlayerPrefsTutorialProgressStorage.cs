using System.Collections.Generic;
using UnityEngine;

namespace DarkNaku.FoundationDI
{
    /// <summary>
    /// 기본 진행도 저장소. SoundService의 ISoundVolumeStorage, AdService의 IAdRemovalStorage와 같은 자리다.
    /// </summary>
    public sealed class PlayerPrefsTutorialProgressStorage : ITutorialProgressStorage
    {
        private const string Prefix = "foundationdi.tutorial";

        private readonly string _saveKey;

        // Clear가 지워야 할 키를 알아야 하는데 PlayerPrefs는 열거를 지원하지 않는다.
        // 그래서 건드린 시퀀스 ID 목록을 따로 적어둔다.
        private readonly HashSet<string> _known = new();

        public PlayerPrefsTutorialProgressStorage(string saveKey)
        {
            _saveKey = string.IsNullOrWhiteSpace(saveKey) ? "default" : saveKey;

            LoadKnown();
        }

        public bool AllSkipped
        {
            get => PlayerPrefs.GetInt(AllSkippedKey, 0) == 1;
            set
            {
                PlayerPrefs.SetInt(AllSkippedKey, value ? 1 : 0);
                PlayerPrefs.Save();
            }
        }

        private string AllSkippedKey => $"{Prefix}.{_saveKey}.allSkipped";

        private string KnownKey => $"{Prefix}.{_saveKey}.known";

        public TutorialState GetState(string sequenceId)
        {
            if (string.IsNullOrEmpty(sequenceId)) return TutorialState.NotStarted;

            return (TutorialState)PlayerPrefs.GetInt(StateKey(sequenceId),
                                                     (int)TutorialState.NotStarted);
        }

        public void SetState(string sequenceId, TutorialState state)
        {
            if (string.IsNullOrEmpty(sequenceId)) return;

            Remember(sequenceId);
            PlayerPrefs.SetInt(StateKey(sequenceId), (int)state);
            PlayerPrefs.Save();
        }

        public int GetStepIndex(string sequenceId)
        {
            if (string.IsNullOrEmpty(sequenceId)) return 0;

            return PlayerPrefs.GetInt(StepKey(sequenceId), 0);
        }

        public void SetStepIndex(string sequenceId, int index)
        {
            if (string.IsNullOrEmpty(sequenceId)) return;

            Remember(sequenceId);
            PlayerPrefs.SetInt(StepKey(sequenceId), index);
            PlayerPrefs.Save();
        }

        public void Clear()
        {
            foreach (var id in _known)
            {
                PlayerPrefs.DeleteKey(StateKey(id));
                PlayerPrefs.DeleteKey(StepKey(id));
            }

            _known.Clear();

            PlayerPrefs.DeleteKey(KnownKey);
            PlayerPrefs.DeleteKey(AllSkippedKey);
            PlayerPrefs.Save();
        }

        private string StateKey(string sequenceId) => $"{Prefix}.{_saveKey}.{sequenceId}.state";

        private string StepKey(string sequenceId) => $"{Prefix}.{_saveKey}.{sequenceId}.step";

        private void LoadKnown()
        {
            var raw = PlayerPrefs.GetString(KnownKey, string.Empty);

            if (string.IsNullOrEmpty(raw)) return;

            foreach (var id in raw.Split('\n'))
            {
                if (!string.IsNullOrEmpty(id)) _known.Add(id);
            }
        }

        private void Remember(string sequenceId)
        {
            if (!_known.Add(sequenceId)) return;

            PlayerPrefs.SetString(KnownKey, string.Join("\n", _known));
            PlayerPrefs.Save();
        }
    }
}
