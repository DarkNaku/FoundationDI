using System;
using System.Threading;
using UnityEngine;
using VContainer;

namespace DarkNaku.FoundationDI
{
    public interface IHapticService : IDisposable
    {
        void Impact(HapticImpact style, float cooldown = 0.02f);
        void Notification(HapticNotification type, float cooldown = 0.02f);
        void Selection(float cooldown = 0.02f);

        Awaitable Play(HapticCurve curve);
        Awaitable Play(HapticPattern pattern);
        void Stop();
        bool IsPlaying { get; }

        bool Enabled { get; set; }
        void Prewarm();
    }

    public class HapticService : IHapticService
    {
        private const string HAPTIC_ENABLED = "HAPTIC_ENABLED";

        private readonly IHapticProvider _provider;
        private readonly Func<float> _now;

        // 모터는 하나라 프리셋 전체가 쿨다운 타임스탬프를 공유한다.
        private float _lastPresetTime = float.MinValue;

        private CancellationTokenSource _cts;
        private Awaitable _active;

        [Inject]
        public HapticService() : this(CreatePlatformProvider())
        {
        }

        public HapticService(IHapticProvider provider, Func<float> nowSeconds = null)
        {
            _provider = provider;
            _now = nowSeconds ?? (() => Time.unscaledTime);
        }

        private static IHapticProvider CreatePlatformProvider()
        {
#if UNITY_IOS && !UNITY_EDITOR
            return new iOSHapticProvider();
#elif UNITY_ANDROID && !UNITY_EDITOR
            return new AndroidHapticProvider();
#else
            return new NoopHapticProvider();
#endif
        }

        public bool Enabled
        {
            get => PlayerPrefs.GetInt(HAPTIC_ENABLED, 1) != 0;
            set { PlayerPrefs.SetInt(HAPTIC_ENABLED, value ? 1 : 0); PlayerPrefs.Save(); }
        }

        public void Impact(HapticImpact style, float cooldown = 0.02f)
        {
            if (!Enabled || !TryConsumeCooldown(cooldown)) return;
            _provider.Impact(style);
        }

        public void Notification(HapticNotification type, float cooldown = 0.02f)
        {
            if (!Enabled || !TryConsumeCooldown(cooldown)) return;
            _provider.Notification(type);
        }

        public void Selection(float cooldown = 0.02f)
        {
            if (!Enabled || !TryConsumeCooldown(cooldown)) return;
            _provider.Selection();
        }

        private bool TryConsumeCooldown(float cooldown)
        {
            float now = _now();
            if (now - _lastPresetTime < cooldown) return false;
            _lastPresetTime = now;
            return true;
        }

        public async Awaitable Play(HapticCurve curve)
        {
            if (!Enabled) return;
            Stop();
            var cts = _cts = new CancellationTokenSource();
            try { _active = _provider.PlayAsync(curve, cts.Token); await _active; }
            catch (OperationCanceledException) { }
            finally { if (_cts == cts) { _cts = null; _active = null; } cts.Dispose(); }
        }

        public async Awaitable Play(HapticPattern pattern)
        {
            if (!Enabled) return;
            Stop();
            var cts = _cts = new CancellationTokenSource();
            try { _active = _provider.PlayAsync(pattern, cts.Token); await _active; }
            catch (OperationCanceledException) { }
            finally { if (_cts == cts) { _cts = null; _active = null; } cts.Dispose(); }
        }

        public bool IsPlaying => _active != null && !_active.IsCompleted;

        public void Stop()
        {
            if (_cts == null) return;
            _cts.Cancel();
            _provider.Stop();
        }

        public void Prewarm() => _provider.Prewarm();

        public void Dispose() => Stop();
    }

    public static class HapticServiceVContainerExtensions
    {
        /// <summary>HapticService를 컨테이너에 등록한다. 외부 리소스 의존이 없어 추가 인자는 불필요하다.</summary>
        public static void RegisterHapticService(this IContainerBuilder builder)
        {
            builder.Register<IHapticService, HapticService>(Lifetime.Singleton);
        }
    }
}
