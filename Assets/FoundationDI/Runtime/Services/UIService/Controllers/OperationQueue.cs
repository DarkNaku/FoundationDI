using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace DarkNaku.FoundationDI
{
    internal delegate UniTask OperationQueueWork(CancellationToken cancellationToken);

    internal sealed class OperationQueue
    {
        private bool _processing;
        private CancellationTokenSource _cts = new();
        private readonly Queue<OperationQueueWork> _pending = new();

        public void Enqueue(OperationQueueWork work)
        {
            if (work == null) throw new ArgumentNullException(nameof(work));

            _pending.Enqueue(work);

            if (!_processing) ProcessLoopAsync().Forget();
        }

        private async UniTaskVoid ProcessLoopAsync()
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
