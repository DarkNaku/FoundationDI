using System;
using UnityEngine;

namespace DarkNaku.FoundationDI
{
    [Serializable]
    public class IapProductEntry
    {
        [Tooltip("게임 코드가 쓰는 공용 ID. 상수 생성기가 이 값으로 IapProducts 상수를 만든다.")]
        [SerializeField] private string _id;

        [SerializeField] private IapProductType _type = IapProductType.Consumable;

        [Tooltip("스토어에 실제로 올린 ID가 공용 ID와 다를 때만 채운다. 비우면 공용 ID를 그대로 쓴다.")]
        [SerializeField] private IapProductId _storeId;

        public IapProductEntry() { }

        public IapProductEntry(string id, IapProductType type, IapProductId storeId)
        {
            _id = id;
            _type = type;
            _storeId = storeId;
        }

        public string Id => _id;
        public IapProductType Type => _type;
        public IapProductId StoreId => _storeId;

        public IapProductDefinition ToDefinition() => new(_id, _storeId.Resolve(_id), _type);
    }
}
