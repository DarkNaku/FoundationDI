using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;

namespace DarkNaku.FoundationDI
{
    /// <summary>
    /// 키로 등록된 타깃을 해석하고, 아직 없으면 나타날 때까지 기다린다.
    /// "팝업이 열리면 그때 하이라이트하라"가 이 대기의 부수효과로 풀린다.
    /// 메인 스레드 전제(잠금 없음).
    /// </summary>
    public sealed class TutorialTargetRegistry : ITutorialTargetRegistry
    {
        private readonly ITutorialClock _clock;

        // 같은 키가 여러 번 등록될 수 있다(풀에서 나온 View + 새 View). LIFO로 마지막이 이긴다.
        private readonly Dictionary<string, List<Transform>> _targets = new();

        // 키마다 그 키를 보고 있는 핸들들. 등록/해제 시 Current를 갱신한다.
        private readonly Dictionary<string, List<TutorialTargetHandle>> _watchers = new();

        public TutorialTargetRegistry(ITutorialClock clock)
        {
            _clock = clock;
        }

        public void Register(string key, Transform target)
        {
            if (string.IsNullOrWhiteSpace(key) || target == null) return;

            if (!_targets.TryGetValue(key, out var stack))
            {
                stack = new List<Transform>();
                _targets.Add(key, stack);
            }

            stack.Remove(target);
            stack.Add(target);

            Notify(key);
        }

        public void Unregister(string key, Transform target)
        {
            if (string.IsNullOrWhiteSpace(key)) return;
            if (!_targets.TryGetValue(key, out var stack)) return;

            stack.Remove(target);

            if (stack.Count == 0) _targets.Remove(key);

            Notify(key);
        }

        public bool TryResolve(TutorialTargetRef reference, out Transform target)
        {
            target = reference.Direct;

            if (target != null) return true;

            if (!reference.HasKey) return false;

            target = Peek(reference.Key);

            return target != null;
        }

        public async Awaitable<TutorialTargetHandle> ResolveAsync(TutorialTargetRef reference,
                                                                  float timeoutSeconds,
                                                                  CancellationToken token)
        {
            if (reference.IsEmpty) return new TutorialTargetHandle(null);

            if (reference.Direct != null) return new TutorialTargetHandle(reference.Direct);

            var key = reference.Key;
            var deadline = timeoutSeconds > 0f
                ? Time.realtimeSinceStartupAsDouble + timeoutSeconds
                : double.MaxValue;

            while (true)
            {
                token.ThrowIfCancellationRequested();

                var target = Peek(key);

                if (target != null) return Watch(key, target);

                if (Time.realtimeSinceStartupAsDouble >= deadline) return null;

                await _clock.NextFrameAsync(token);
            }
        }

        private Transform Peek(string key)
        {
            if (!_targets.TryGetValue(key, out var stack)) return null;

            // 파괴된 항목은 여기서 걷어낸다(Unregister 없이 사라지는 경우가 있다).
            for (var i = stack.Count - 1; i >= 0; i--)
            {
                if (stack[i] != null) return stack[i];

                stack.RemoveAt(i);
            }

            return null;
        }

        private TutorialTargetHandle Watch(string key, Transform target)
        {
            var handle = new TutorialTargetHandle(target);

            if (!_watchers.TryGetValue(key, out var list))
            {
                list = new List<TutorialTargetHandle>();
                _watchers.Add(key, list);
            }

            list.Add(handle);

            return handle;
        }

        private void Notify(string key)
        {
            if (!_watchers.TryGetValue(key, out var list)) return;

            var current = Peek(key);

            for (var i = list.Count - 1; i >= 0; i--)
            {
                var handle = list[i];

                // Dispose된 핸들은 SetCurrent를 무시하지만, 무한히 쌓이지 않게 여기서 걷어낸다.
                if (handle.IsDisposed)
                {
                    list.RemoveAt(i);
                    continue;
                }

                try
                {
                    handle.SetCurrent(current);
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }
            }

            if (list.Count == 0) _watchers.Remove(key);
        }
    }
}
