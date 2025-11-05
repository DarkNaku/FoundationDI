using System;
using System.Collections.Generic;
using UnityEngine;

namespace FoundationDI
{
    [Serializable]
    public class UIEntry
    {
        [SerializeField] private string _name;
        [SerializeField] private UIView _prefab;
        
        public string Name => _name;
        public UIView Prefab => _prefab;

        public Type PresenterType
        {
            get
            {
                var ns = GetType().Namespace;
                var className = Prefab.GetType().Name.Replace("View", "Presenter");

                return Type.GetType(string.IsNullOrEmpty(ns) ? className : $"{ns}.{className}");
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