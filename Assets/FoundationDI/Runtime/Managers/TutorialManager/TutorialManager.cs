using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;

namespace DarkNaku.FoundationDI
{
    /// <summary>
    /// 조건 기반 튜토리얼 진행 엔진. 순수 C#이라 씬·프리팹 없이 EditMode에서 전부 테스트된다.
    ///
    /// 시퀀스를 줄세우지 않고 조건부 후보 집합으로 들고 있는 것이 핵심이다 —
    /// "3레벨 튜토리얼"과 "5레벨 튜토리얼"은 앞의 게 끝나서 뜨는 게 아니라 조건이 맞아서 뜬다.
    /// 메인 스레드 전제(잠금 없음).
    /// </summary>
    public sealed class TutorialManager : ITutorialManager
    {
        private readonly IMessageService _message;
        private readonly ITutorialTargetRegistry _targets;
        private readonly ITutorialProgressStorage _storage;
        private readonly ITutorialClock _clock;

        private readonly Dictionary<string, TutorialSequence> _sequences = new();
        private readonly List<TutorialSequence> _pending = new();
        private readonly HashSet<string> _armed = new();

        private CancellationTokenSource _runCts;
        private TutorialSequence _running;
        private bool _disposed;

        public TutorialManager(IMessageService message,
                               ITutorialTargetRegistry targets,
                               ITutorialProgressStorage storage,
                               ITutorialClock clock)
        {
            _message = message;
            _targets = targets;
            _storage = storage;
            _clock = clock;
        }

        public bool IsRunning => _running != null;

        public event Action<string> SequenceStarted;

        public event Action<string> SequenceCompleted;

        private TutorialTriggerContext Context => new(_message, _targets);

        public bool IsCompleted(string sequenceId)
        {
            if (string.IsNullOrEmpty(sequenceId)) return false;
            if (_storage.AllSkipped) return true;

            return _storage.GetState(sequenceId) == TutorialState.Completed;
        }

        public void Register(TutorialSequence sequence)
        {
            if (_disposed || sequence == null) return;

            if (string.IsNullOrWhiteSpace(sequence.Id))
            {
                Debug.LogError("[TutorialManager] 시퀀스 ID가 비어 있어 등록을 건너뛴다.");
                return;
            }

            if (_sequences.ContainsKey(sequence.Id))
            {
                Debug.LogError($"[TutorialManager] 시퀀스 ID가 중복이라 등록을 건너뛴다: {sequence.Id}");
                return;
            }

            _sequences.Add(sequence.Id, sequence);

            if (IsCompleted(sequence.Id)) return;

            ArmGate(sequence);
        }

        public void Unregister(string sequenceId)
        {
            if (string.IsNullOrEmpty(sequenceId)) return;
            if (!_sequences.Remove(sequenceId, out var sequence)) return;

            DisarmGate(sequence);
            _pending.RemoveAll(s => s.Id == sequenceId);
        }

        public void Skip()
        {
            var running = _running;

            if (running == null) return;

            _storage.SetState(running.Id, TutorialState.Completed);
            CancelRun();
        }

        public void SkipAll()
        {
            _storage.AllSkipped = true;

            foreach (var sequence in _sequences.Values) DisarmGate(sequence);

            _pending.Clear();
            CancelRun();
        }

        public void Complete(string stepId)
        {
            if (string.IsNullOrEmpty(stepId)) return;

            if (!ManualTrigger.Fire(stepId))
            {
                Debug.LogWarning($"[TutorialManager] 대기 중인 ManualTrigger가 없다: {stepId}");
            }
        }

        public void Dispose()
        {
            if (_disposed) return;

            _disposed = true;

            foreach (var sequence in _sequences.Values) DisarmGate(sequence);

            _sequences.Clear();
            _pending.Clear();
            CancelRun();

            SequenceStarted = null;
            SequenceCompleted = null;
        }

        private void ArmGate(TutorialSequence sequence)
        {
            if (!_armed.Add(sequence.Id)) return;

            try
            {
                sequence.StartTrigger.Arm(Context, () => OnGateFired(sequence));
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                _armed.Remove(sequence.Id);
            }
        }

        private void DisarmGate(TutorialSequence sequence)
        {
            if (!_armed.Remove(sequence.Id)) return;

            try
            {
                sequence.StartTrigger.Disarm();
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }

        private void OnGateFired(TutorialSequence sequence)
        {
            if (_disposed) return;
            if (IsCompleted(sequence.Id)) return;

            if (_armed.Remove(sequence.Id))
            {
                try
                {
                    sequence.StartTrigger.Disarm();
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }
            }

            if (IsRunning)
            {
                // 연출이 겹치면 화면이 엉킨다. Order 오름차순 대기열로 직렬화한다.
                _pending.Add(sequence);
                _pending.Sort((a, b) => a.Order.CompareTo(b.Order));
                return;
            }

            StartSequence(sequence);
        }

        private void StartSequence(TutorialSequence sequence)
        {
            _running = sequence;
            _runCts = new CancellationTokenSource();

            RunSequence(sequence, _runCts.Token);
        }

        private async void RunSequence(TutorialSequence sequence, CancellationToken token)
        {
            var completed = false;

            try
            {
                _storage.SetState(sequence.Id, TutorialState.Running);
                SequenceStarted?.Invoke(sequence.Id);

                var start = ResolveStartIndex(sequence);

                for (var i = start; i < sequence.Steps.Count; i++)
                {
                    await RunStep(sequence, sequence.Steps[i], token);

                    _storage.SetStepIndex(sequence.Id, i + 1);
                }

                _storage.SetState(sequence.Id, TutorialState.Completed);
                completed = true;
            }
            catch (OperationCanceledException)
            {
                // Skip/Dispose/씬 언로드. 상태는 취소를 요청한 쪽이 이미 정했다.
            }
            catch (Exception e)
            {
                Debug.LogException(e);

                // 타깃을 못 찾는 등으로 중단되면 NotStarted로 되돌려 다음 기회에 재시도한다.
                _storage.SetState(sequence.Id, TutorialState.NotStarted);
            }
            finally
            {
                // CTS는 Dispose하지 않는다. 취소 콜백 안에서 이 finally가 실행될 수 있는데
                // 그 시점의 Dispose는 Cancel과 경합한다. 타이머가 없는 CTS라 GC로 충분하다.
                _runCts = null;
                _running = null;

                if (completed) SequenceCompleted?.Invoke(sequence.Id);

                RunNextPending();
            }
        }

        private int ResolveStartIndex(TutorialSequence sequence)
        {
            if (sequence.ResumeMode != ResumeMode.ResumeFromStep) return 0;

            var saved = _storage.GetStepIndex(sequence.Id);

            if (saved < 0) return 0;
            if (saved > sequence.Steps.Count) return sequence.Steps.Count;

            return saved;
        }

        private async Awaitable RunStep(TutorialSequence sequence, TutorialStep step,
                                        CancellationToken token)
        {
            await TutorialTriggerAwaiter.WaitAsync(step.StartTrigger, Context, token);

            token.ThrowIfCancellationRequested();

            if (step.StartDelay > 0f) await _clock.DelayAsync(step.StartDelay, token);

            TutorialTargetHandle handle = null;

            try
            {
                if (!step.Target.IsEmpty)
                {
                    handle = await _targets.ResolveAsync(step.Target, sequence.TargetTimeout, token);

                    if (handle == null)
                    {
                        // 타깃을 못 찾으면 유저를 가두는 대신 중단하고 다음 기회에 재시도한다.
                        throw new TutorialTargetTimeoutException(sequence.Id, step.Id);
                    }
                }

                await ShowModules(step, handle, token);

                await TutorialTriggerAwaiter.WaitAsync(step.EndTrigger, Context, token);

                token.ThrowIfCancellationRequested();

                if (step.EndDelay > 0f) await _clock.DelayAsync(step.EndDelay, token);

                await HideModules(step, token);
            }
            finally
            {
                handle?.Dispose();
            }
        }

        private async Awaitable ShowModules(TutorialStep step, TutorialTargetHandle handle,
                                            CancellationToken token)
        {
            foreach (var module in step.Modules)
            {
                token.ThrowIfCancellationRequested();

                try
                {
                    await module.ShowAsync(handle, token);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception e)
                {
                    // 한 연출이 터져도 Step은 진행한다(MessageService의 핸들러 격리와 같은 방식).
                    Debug.LogException(e);
                }
            }
        }

        private async Awaitable HideModules(TutorialStep step, CancellationToken token)
        {
            foreach (var module in step.Modules)
            {
                try
                {
                    await module.HideAsync(token);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }
            }
        }

        private void CancelRun()
        {
            var cts = _runCts;

            if (cts == null) return;

            try
            {
                cts.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }
        }

        private void RunNextPending()
        {
            if (_disposed) return;

            while (_pending.Count > 0)
            {
                var next = _pending[0];
                _pending.RemoveAt(0);

                if (IsCompleted(next.Id)) continue;

                StartSequence(next);
                return;
            }
        }
    }

    internal sealed class TutorialTargetTimeoutException : Exception
    {
        public TutorialTargetTimeoutException(string sequenceId, string stepId)
            : base($"[TutorialManager] 타깃을 찾지 못해 시퀀스를 중단한다: {sequenceId}/{stepId}")
        {
        }
    }
}
