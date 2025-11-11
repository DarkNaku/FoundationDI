using System;
using System.Collections.Generic;
using UnityEngine;

namespace FoundationDI
{
    [Serializable]
    public class UIEntry
    {
        [SerializeField] private string _name;
        [SerializeField] private string _presenterName;
        [SerializeField] private UIView _prefab;
        
        public string Name => _name;
        public UIView Prefab => _prefab;

        public Type PresenterType
        {
            get
            {
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    var type = asm.GetType(_presenterName);
                    
                    if (type != null) return type;
                }
                
                return null;
            }
        }
    }
    
    public interface IUISetting
    {
        UIEntry Find(string pageName);
    }

    [CreateAssetMenu(fileName = "UISetting", menuName = "DarkNaku/UISetting")]
    public class UISetting : ScriptableObject, IUISetting
    {
        [SerializeField] private List<UIEntry> _entries;

        public UIEntry Find(string pageName) => _entries.Find(entry => entry.Name == pageName);
    }
}