using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace  DarkNaku.FoundationDI
{
    public class SoundData : IDisposable
    {
        public AudioClip Clip { get; private set; }
        
        private AsyncOperationHandle<AudioClip> _handle;
        
        public SoundData(AudioClip clip, AsyncOperationHandle<AudioClip> handle)
        {
            Clip = clip;
            _handle = handle;
        }

        public void Dispose()
        {
            Clip = null;
            
            if (_handle.IsValid())
            {
                Addressables.Release(_handle);
            }
        }
    }
}
