using System;
using System.Collections.Generic;
using UnityEngine;

namespace DarkNaku.FoundationDI
{
    /// <summary>
    /// 게임 코드가 ITutorialManager.Complete(id)를 부를 때 발동한다.
    /// 메시지로 표현하기 애매한 일회성 지점에만 쓴다 — 대부분은 MessageTrigger가 낫다.
    /// </summary>
    [Serializable]
    public sealed class ManualTrigger : ITutorialTrigger
    {
        // arm된 트리거를 ID로 찾아야 하는데 트리거는 [SerializeReference] 객체라
        // 매니저가 인스턴스를 미리 알 수 없다. arm 시점에 스스로 등록한다.
        private static readonly Dictionary<string, ManualTrigger> Armed = new();

        [SerializeField] private string _id;

        private Action _onFired;

        public ManualTrigger()
        {
        }

        public ManualTrigger(string id)
        {
            _id = id;
        }

        public string Id => _id;

        /// <summary>arm된 트리거를 발동시킨다. 매칭되는 트리거가 없으면 false.</summary>
        public static bool Fire(string id)
        {
            if (string.IsNullOrEmpty(id)) return false;
            if (!Armed.TryGetValue(id, out var trigger)) return false;

            var onFired = trigger._onFired;

            if (onFired == null) return false;

            onFired.Invoke();
            return true;
        }

        public void Arm(TutorialTriggerContext context, Action onFired)
        {
            if (string.IsNullOrWhiteSpace(_id))
            {
                Debug.LogWarning("[ManualTrigger] ID가 비어 있어 영원히 발동하지 않는다.");
                return;
            }

            _onFired = onFired;
            Armed[_id] = this;
        }

        public void Disarm()
        {
            _onFired = null;

            if (string.IsNullOrWhiteSpace(_id)) return;

            if (Armed.TryGetValue(_id, out var trigger) && ReferenceEquals(trigger, this))
            {
                Armed.Remove(_id);
            }
        }
    }
}
