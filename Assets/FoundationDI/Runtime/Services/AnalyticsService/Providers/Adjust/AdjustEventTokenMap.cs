using System.Collections.Generic;
using UnityEngine;

namespace DarkNaku.FoundationDI
{
    // 이벤트 이름을 Adjust 토큰으로 옮긴다.
    //
    // 이 표가 어댑터 쪽에 있는 이유는 토큰이 Adjust 고유의 개념이기 때문이다. 정책 계층
    // (AnalyticsService)은 토큰의 존재조차 모르고, 게임 코드는 Firebase에 보내던 이름 그대로
    // LogEvent를 부른다(AnalyticsService README 2.3).
    //
    // 표에 없는 이름은 버린다. Adjust는 이름을 받지 않으므로 "그냥 보내기"라는 선택지가 없고,
    // 등록되지 않은 토큰을 지어내 보내면 SDK가 서버에서 조용히 버린다. 대신 이름당 한 번만
    // 경고한다 — 매 프레임 도는 이벤트에서 로그가 터지면 콘솔이 못 쓰게 된다.
    internal sealed class AdjustEventTokenMap
    {
        private readonly Dictionary<string, string> _tokens;
        private readonly bool _treatUnmappedNamesAsTokens;
        private readonly HashSet<string> _warned = new();

        public AdjustEventTokenMap(AdjustAnalyticsSettings settings)
        {
            _treatUnmappedNamesAsTokens = settings != null && settings.TreatUnmappedNamesAsTokens;
            _tokens = new Dictionary<string, string>();

            if (settings?.EventTokens == null) return;

            foreach (var entry in settings.EventTokens)
            {
                if (string.IsNullOrEmpty(entry.EventName)) continue;

                if (string.IsNullOrEmpty(entry.Token))
                {
                    Debug.LogWarning($"[Analytics/Adjust] 이벤트 '{entry.EventName}' 에 토큰이 비어 있다. 무시한다.");
                    continue;
                }

                // 같은 이름이 두 번 있으면 마지막이 이긴다. 조용히 덮으면 어느 쪽이 살았는지
                // 알 수 없으므로 알린다.
                if (_tokens.ContainsKey(entry.EventName))
                {
                    Debug.LogWarning($"[Analytics/Adjust] 이벤트 '{entry.EventName}' 이 표에 두 번 있다. " +
                                     $"마지막 토큰 '{entry.Token}' 을 쓴다.");
                }

                _tokens[entry.EventName] = entry.Token;
            }
        }

        public bool TryResolve(string eventName, out string token)
        {
            token = null;

            if (string.IsNullOrEmpty(eventName)) return false;

            if (_tokens.TryGetValue(eventName, out token)) return true;

            if (_treatUnmappedNamesAsTokens)
            {
                token = eventName;
                return true;
            }

            WarnOnce(eventName);
            return false;
        }

        private void WarnOnce(string eventName)
        {
            if (!_warned.Add(eventName)) return;

            Debug.LogWarning($"[Analytics/Adjust] 이벤트 '{eventName}' 의 토큰이 없어 전송하지 않는다. " +
                             "AdjustAnalyticsSettings의 Event Tokens에 대시보드 토큰을 등록하라.");
        }
    }
}
