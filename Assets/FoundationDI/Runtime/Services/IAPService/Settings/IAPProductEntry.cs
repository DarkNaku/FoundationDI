using System;
using UnityEngine;

namespace DarkNaku.FoundationDI
{
    [Serializable]
    public class IAPProductEntry
    {
        [Tooltip("게임 코드가 쓰는 공용 ID. 상수 생성기가 이 값으로 IAPProducts 상수를 만든다.")]
        [SerializeField] private string _id;

        [SerializeField] private IAPProductType _type = IAPProductType.Consumable;

        [Tooltip("스토어에 실제로 올린 ID가 공용 ID와 다를 때만 채운다. 비우면 공용 ID를 그대로 쓴다.")]
        [SerializeField] private IAPProductId _storeId;

        public IAPProductEntry() { }

        public IAPProductEntry(string id, IAPProductType type, IAPProductId storeId)
        {
            _id = id;
            _type = type;
            _storeId = storeId;
        }

        public string Id => _id;
        public IAPProductType Type => _type;
        public IAPProductId StoreId => _storeId;

        public IAPProductDefinition ToDefinition() => new(_id, _storeId.Resolve(_id), _type);
    }
}
