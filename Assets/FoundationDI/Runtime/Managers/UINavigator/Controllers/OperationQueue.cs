using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;

namespace DarkNaku.FoundationDI
{
    internal delegate Awaitable OperationQueueWork(CancellationToken cancellationToken);

    internal sealed class OperationQueue
    {
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
