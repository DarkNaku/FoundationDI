using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;

namespace DarkNaku.FoundationDI
{
    internal delegate Awaitable OperationQueueWork(CancellationToken cancellationToken);

    internal sealed class OperationQueue
    {
        /// <summary>
        /// 큐가 작업을 시작(true)/모두 소진(false)할 때 통지한다.
        /// 진입 통지는 Enqueue와 같은 프레임에 동기로 발생한다 — 전환을 요청한 바로 그 프레임에
        /// 입력을 막아야 버튼 연타가 큐에 쌓이는 것을 끊을 수 있다.
        /// </summary>
        internal Action<bool> BusyChanged;

        private bool _processing;
        private CancellationTokenSource _cts = new();
        private readonly Queue<OperationQueueWork> _pending = new();

        public void Enqueue(OperationQueueWork work)
        {
            if (work == null) throw new ArgumentNullException(nameof(work));

            _pending.Enqueue(work);

            if (!_processing) ProcessLoop();
        }

        // fire-and-forget: 예외는 루프 내부에서 처리하므로 async void가 안전하다.
        private async void ProcessLoop()
        {
            _processing = true;
            BusyChanged?.Invoke(true);

            try
            {
                while (_pending.Count > 0)
                {
                    var next = _pending.Dequeue();

                    try
                    {
                        await next(_cts.Token);
                    }
                    catch (OperationCanceledException) { }
                    catch (Exception e)
                    {
                        Debug.LogException(e);
                    }
                }
            }
            finally
            {
                _processing = false;
                BusyChanged?.Invoke(false);
            }
        }

        public void CancelAndClear()
        {
            _cts.Cancel();
            _pending.Clear();
            _cts = new CancellationTokenSource();
        }
    }
}
