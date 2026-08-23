using System;
using UnityEngine;

namespace DarkNaku.FoundationDI
{
    /// <summary>
    /// 튜토리얼이 가리킬 대상. 씬에 상주하는 오브젝트는 인스펙터로 직접 드래그하고,
    /// UIService가 런타임에 만드는 UI는 <see cref="TutorialTarget"/>이 등록한 키로 가리킨다.
    /// </summary>
    [Serializable]
    public struct TutorialTargetRef
    {
        [SerializeField] private Transform _direct;
        [SerializeField] private string _key;

        /// <summary>파괴된 Transform은 null로 보인다(Unity의 fake-null).</summary>
        public Transform Direct => _direct == null ? null : _direct;

        public bool HasKey => Direct == null && !string.IsNullOrWhiteSpace(_key);

        public string Key => _key;

        public bool IsEmpty => Direct == null && string.IsNullOrWhiteSpace(_key);

        public static TutorialTargetRef Create(Transform direct, string key)
        {
            return new TutorialTargetRef { _direct = direct, _key = key };
        }

        public static TutorialTargetRef FromTransform(Transform direct) => Create(direct, null);

        public static TutorialTargetRef FromKey(string key) => Create(null, key);
    }
}
