using System;
using UnityEngine;

namespace WizardAddressables.Runtime.AssetManagement
{
    public class MonoTracker : MonoBehaviour
    {
        public delegate void DelegateDestroyed(MonoTracker tracker);
        public event DelegateDestroyed OnDestroyed;
        
        public object Key { get; set; }

        private void OnDestroy()
        {
            OnDestroyed?.Invoke(this);
        }
    }
}