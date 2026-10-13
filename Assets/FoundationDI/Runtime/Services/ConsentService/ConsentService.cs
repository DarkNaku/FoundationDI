using System;
using System.Collections.Generic;
using UnityEngine;

namespace DarkNaku.FoundationDI
{
    // 약관 수락 → 선행 작업 → Google UMP → ATT를 한 흐름으로 묻는다.
    //
    // 실패해도 게임을 막지 않는다. UMP 실패·예외는 경고만 남기고 캐시된 이전 세션 값으로 진행한다
    // (TileExplorer·VitaMahjong과 같은 정책). 약관만은 예외다 — 수락하지 않으면 PolicyAccepted=false로
    // 끝나고 광고·분석이 시작되지 않는다.
    public sealed class ConsentService : IConsentService
    {
        private readonly ConsentServiceOptions _options;
        private readonly IPolicyAgreementPrompt _prompt;
        private readonly IPolicyAgreementStorage _storage;
        private readonly IUmpConsent _ump;
        private readonly ITrackingAuthorization _tracking;
        private readonly IMainThreadDispatcher _dispatcher;
        private readonly List<IConsentPrerequisite> _prerequisites;

        // 진행 중인 흐름에 편승한 호출자들. Awaitable은 단일 사용이라 호출자마다 완료 소스를 둔다.
        private readonly List<AwaitableCompletionSource<ConsentResult>> _waiters = new();

        private PolicyAgreementContent _content;
        private ConsentResult _last;
        private bool _hasCompleted;
        private bool _running;
        private bool _prerequisitesDone;
        private bool _trackingAsked;
        private bool _disposed;

        public event Action ConsentChanged;

        public ConsentService(ConsentServiceOptions options, IPolicyAgreementPrompt prompt,
                              IPolicyAgreementStorage storage, IUmpConsent ump,
                              ITrackingAuthorization tracking, IMainThreadDispatcher dispatcher,
                              IReadOnlyList<IConsentPrerequisite> prerequisites = null)
        {
            _options = options;
            _prompt = prompt;
            _storage = storage;
            _ump = ump ?? new UnsupportedUmpConsent();
            _tracking = tracking ?? new UnsupportedTrackingAuthorization();
            _dispatcher = dispatcher;
            _prerequisites = prerequisites == null
                ? new List<IConsentPrerequisite>()
                : new List<IConsentPrerequisite>(prerequisites);
            _content = options.PolicyContent;
        }

        public ITrackingAuthorization TrackingAuthorization => _tracking;

        public bool IsPolicyAccepted =>
            _options.PolicyVersion <= 0 ||
            (_storage != null && _storage.LoadAcceptedVersion() >= _options.PolicyVersion);

        public bool IsPrivacyOptionsRequired => UsesUmp && SafeRead(() => _ump.IsPrivacyOptionsRequired, false);

        public ConsentResult Current => Snapshot();

        // UMP를 켰더라도 네이티브가 없으면(에디터·포드 누락) 쓰지 않는 것으로 본다.
        private bool UsesUmp => _options.UseUmp && _ump.IsSupported;

        public void AcceptPolicy()
        {
            if (_disposed || _options.PolicyVersion <= 0) return;

            _storage?.SaveAcceptedVersion(_options.PolicyVersion);
        }

        public void SetPolicyContent(PolicyAgreementContent content) => _content = content;

        public Awaitable<ConsentResult> RequestAsync()
        {
            if (_disposed) return Completed(Snapshot());

            if (_hasCompleted && !_running && IsSettled(_last)) return Completed(_last);

            var waiter = new AwaitableCompletionSource<ConsentResult>();
            _waiters.Add(waiter);

            if (!_running)
            {
                _running = true;
                Run();
            }

            return waiter.Awaitable;
        }

        public async Awaitable ShowPrivacyOptionsAsync()
        {
            if (_disposed || !UsesUmp) return;

            try
            {
                await _ump.ShowPrivacyOptionsAsync();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[ConsentService] 개인정보 옵션 폼 표시 중 예외가 났다: {e}");
            }

            if (_disposed) return;

            _last = Snapshot();
            ConsentChanged?.Invoke();
        }

        public void Dispose()
        {
            if (_disposed) return;

            _disposed = true;

            try
            {
                _prompt?.Close();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[ConsentService] 약관 창 정리 중 예외: {e}");
            }

            CompleteWaiters(Snapshot());
            ConsentChanged = null;
        }

        // 직전 결과로 충분한가. 광고를 요청할 수 있고 분석 판정이 났으면 다시 물을 이유가 없다.
        private static bool IsSettled(ConsentResult result) =>
            result.PolicyAccepted && result.CanRequestAds && result.AnalyticsConsent != AnalyticsConsent.Unknown;

        // async void인 이유: 결과는 _waiters로 전달하고, 예외는 안에서 전부 잡는다.
        private async void Run()
        {
            try
            {
                if (!IsPolicyAccepted)
                {
                    if (_prompt == null)
                    {
                        throw new InvalidOperationException("약관 수락 창(IPolicyAgreementPrompt)이 등록되지 않았다.");
                    }

                    await _prompt.ShowAsync(_content);

                    if (_disposed) return;

                    _storage?.SaveAcceptedVersion(_options.PolicyVersion);
                }

                await RunPrerequisitesAsync();
                if (_disposed) return;

                await RunUmpAsync();
                if (_disposed) return;

                await RunTrackingAsync();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[ConsentService] 동의 흐름이 중단됐다. 현재 상태로 완료한다: {e}");
            }
            finally
            {
                Finish();
            }
        }

        private Awaitable RunPrerequisitesAsync() => Completed();

        private Awaitable RunUmpAsync() => Completed();

        private Awaitable RunTrackingAsync() => Completed();

        private void Finish()
        {
            _running = false;

            // Dispose가 대기자를 이미 완료했다.
            if (_disposed) return;

            var hadCompleted = _hasCompleted;
            var previous = _last;

            _last = Snapshot();
            _hasCompleted = true;

            CompleteWaiters(_last);

            if (hadCompleted && !previous.Equals(_last)) ConsentChanged?.Invoke();
        }

        private ConsentResult Snapshot()
        {
            var trackingStatus = SafeRead(() => _tracking.Status, TrackingAuthorizationStatus.NotDetermined);

            if (!IsPolicyAccepted)
            {
                return new ConsentResult(false, false, AnalyticsConsent.Unknown, trackingStatus, TcfSignals.None);
            }

            if (!UsesUmp)
            {
                return new ConsentResult(true, true, AnalyticsConsent.Granted, trackingStatus, TcfSignals.None);
            }

            var tcf = SafeRead(() => _ump.ReadTcf(), TcfSignals.None);
            var status = SafeRead(() => _ump.Status, UmpConsentStatus.Unknown);
            var canRequestAds = SafeRead(() => _ump.CanRequestAds, false);

            return new ConsentResult(true, canRequestAds, AnalyticsConsentJudge.Judge(status, tcf),
                                     trackingStatus, tcf);
        }

        // 네이티브 읽기는 JNI·P/Invoke라 던질 수 있다. 상태 조회가 흐름을 깨지 않게 한다.
        private static T SafeRead<T>(Func<T> read, T fallback)
        {
            try
            {
                return read();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[ConsentService] 동의 상태를 읽지 못했다: {e.Message}");
                return fallback;
            }
        }

        private void CompleteWaiters(ConsentResult result)
        {
            if (_waiters.Count == 0) return;

            var waiters = new List<AwaitableCompletionSource<ConsentResult>>(_waiters);
            _waiters.Clear();

            foreach (var waiter in waiters) waiter.TrySetResult(result);
        }

        private static Awaitable<ConsentResult> Completed(ConsentResult value)
        {
            var source = new AwaitableCompletionSource<ConsentResult>();
            source.SetResult(value);
            return source.Awaitable;
        }

        private static Awaitable Completed()
        {
            var source = new AwaitableCompletionSource();
            source.SetResult();
            return source.Awaitable;
        }
    }
}
