using System;
using System.Globalization;
using UnityEngine;

namespace DarkNaku.FoundationDI
{
    public sealed class AppStoreAppUpdateStore : IAppUpdateStore
    {
        private const string LOOKUP_URL = "https://itunes.apple.com/lookup";
        private const string STORE_URL = "itms-apps://apps.apple.com/app/id";

        private readonly string _bundleID;
        private readonly string _country;
        private readonly string _installedVersion;
        private readonly string _osVersion;
        private readonly Func<string, Awaitable<string>> _get;
        private readonly Func<DateTime> _utcNow;
        private readonly Action<string> _openUrl;

        private long _trackID;

        internal AppStoreAppUpdateStore(
            string bundleID,
            string country,
            string installedVersion,
            string osVersion,
            Func<string, Awaitable<string>> get,
            Func<DateTime> utcNow,
            Action<string> openUrl)
        {
            _bundleID = bundleID;
            _country = country;
            _installedVersion = installedVersion;
            _osVersion = osVersion;
            _get = get;
            _utcNow = utcNow;
            _openUrl = openUrl;
        }

        public async Awaitable<AppUpdateCheckResult> CheckAsync()
        {
            var url = $"{LOOKUP_URL}?bundleId={Uri.EscapeDataString(_bundleID)}";
            if (!string.IsNullOrWhiteSpace(_country)) url += $"&country={Uri.EscapeDataString(_country.Trim())}";

            var body = await _get(url);
            var response = JsonUtility.FromJson<LookupResponse>(body);

            if (response?.results == null || response.results.Length == 0) return AppUpdateCheckResult.Unknown();

            var app = response.results[0];
            if (app.trackId > 0) _trackID = app.trackId;

            var comparison = AppVersion.Compare(app.version, _installedVersion);

            if (comparison == null) return AppUpdateCheckResult.Unknown();
            if (comparison <= 0) return AppUpdateCheckResult.UpToDate();

            // 기기 OS를 알 수 없으면(null) 막지 않는다.
            if (AppVersion.Compare(_osVersion, app.minimumOsVersion) < 0) return AppUpdateCheckResult.UpToDate();

            return AppUpdateCheckResult.Available(DaysSince(app.currentVersionReleaseDate));
        }

        // 앱 ID(trackId)는 조회 응답에서 얻는다. 게임은 팝업을 띄우기 전에 확인하므로 설정에 따로 받지 않는다.
        public bool OpenStore()
        {
            if (_trackID <= 0) return false;

            _openUrl($"{STORE_URL}{_trackID}");
            return true;
        }

        private int? DaysSince(string releaseDate)
        {
            if (!DateTime.TryParse(releaseDate, CultureInfo.InvariantCulture,
                    DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var released))
            {
                return null;
            }

            return Math.Max(0, (int)(_utcNow() - released).TotalDays);
        }

        // iTunes Lookup 응답. 필드 이름은 JSON 키를 따른다(JsonUtility).
        [Serializable]
        private sealed class LookupResponse
        {
            public int resultCount;
            public LookupResult[] results;
        }

        [Serializable]
        private sealed class LookupResult
        {
            public string version;
            public string currentVersionReleaseDate;
            public string minimumOsVersion;
            public long trackId;
        }
    }
}
