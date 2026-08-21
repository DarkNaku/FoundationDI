using UnityEngine;

namespace DarkNaku.FoundationDI
{
    public class PlayerPrefsAdRemovalStorage : IAdRemovalStorage
    {
        private const string KEY = "FOUNDATIONDI_ADS_REMOVED";

        public bool Load() => PlayerPrefs.GetInt(KEY, 0) != 0;

        public void Save(bool removed)
        {
            PlayerPrefs.SetInt(KEY, removed ? 1 : 0);
            PlayerPrefs.Save();
        }
    }
}
