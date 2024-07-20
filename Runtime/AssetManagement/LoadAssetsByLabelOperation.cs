using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine.AddressableAssets;
using UnityEngine.AddressableAssets.ResourceLocators;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.ResourceLocations;
using Object = UnityEngine.Object;

namespace WizardAddressables.Runtime.AssetManagement
{
    public class LoadAssetsByLabelOperation : AsyncOperationBase<List<AsyncOperationHandle<Object>>>
    {
        private string m_label;
        private Dictionary<object, AsyncOperationHandle> m_loadedDictionary;
        private Dictionary<object , AsyncOperationHandle> m_loadingDictionary;
        Action<object, AsyncOperationHandle> m_loadedCallback;

        public LoadAssetsByLabelOperation(Dictionary<object, AsyncOperationHandle> loadedDictionary,
            Dictionary<object, AsyncOperationHandle> loadingDictionary,
            string label, Action<object, AsyncOperationHandle> loadedCallback)
        {
            m_loadedDictionary = loadedDictionary;
            if (m_loadedDictionary == null)
                m_loadedDictionary = new Dictionary<object, AsyncOperationHandle>();
            m_loadingDictionary = loadingDictionary;
            if (m_loadingDictionary == null)
                m_loadingDictionary = new Dictionary<object, AsyncOperationHandle>();

            m_loadedCallback = loadedCallback;
            
            m_label = label;
        }

        protected override void Execute()
        {
            #pragma warning disable CS4014
            DoTask();
            #pragma warning restore CS4014
        }

        public async Task DoTask()
        {
            var locationsHandle = Addressables.LoadResourceLocationsAsync(m_label);
            var locations = await locationsHandle.Task;

            var loadingInternalIdDic = new Dictionary<string, AsyncOperationHandle<Object>>();
            var loadedInternalIdDic = new Dictionary<string, AsyncOperationHandle<Object>>();

            var operationsHandles = new List<AsyncOperationHandle<Object>>();
            
            foreach (IResourceLocation resourceLocation in locations)
            {
                AsyncOperationHandle<Object> loadingHandle =
                    Addressables.LoadAssetAsync<Object>(resourceLocation.PrimaryKey);
                
                operationsHandles.Add(loadingHandle);
                
                if(!loadingInternalIdDic.ContainsKey(resourceLocation.InternalId))
                    loadingInternalIdDic.Add(resourceLocation.InternalId, loadingHandle);

                loadingHandle.Completed += assetOp =>
                {
                    if (!loadedInternalIdDic.ContainsKey(resourceLocation.InternalId))
                        loadedInternalIdDic.Add(resourceLocation.InternalId, assetOp);
                };
            }
            
            foreach (IResourceLocator locator in Addressables.ResourceLocators)
            {
                foreach (var locatorKey in locator.Keys)
                {
                    bool isGUID = Guid.TryParse(locatorKey.ToString(), out var guid);
                    if(!isGUID)
                        continue;
                    
                    if(!TryGetKeyLocationId(locator, locatorKey, out var keyLocationId))
                        continue;

                    var locationMatched = loadingInternalIdDic.TryGetValue(keyLocationId, out var loadingHandle);
                    if(!locationMatched)
                        continue;
                    
                    if(!m_loadingDictionary.ContainsKey(locatorKey))
                        m_loadingDictionary.Add(locatorKey, loadingHandle);
                }
            }
            
            foreach (AsyncOperationHandle<Object> handle in operationsHandles)
            {
                await handle.Task;
            }
            
            foreach (IResourceLocator resourceLocator in Addressables.ResourceLocators)
            {
                foreach (var locatorKey in resourceLocator.Keys)
                {
                    bool isGuid = Guid.TryParse(locatorKey.ToString(), out var guid);
                    if(!isGuid)
                        continue;
                    
                    if(!TryGetKeyLocationId(resourceLocator, locatorKey, out var keyLocationId))
                        continue;
                    
                    var locationMatched = loadedInternalIdDic.TryGetValue(keyLocationId, out var loadedHandle);
                    if(!locationMatched)
                        continue;
                    
                    if(m_loadingDictionary.ContainsKey(locatorKey))
                        m_loadingDictionary.Remove(locatorKey);
                    if (!m_loadedDictionary.ContainsKey(locatorKey))
                    {
                        m_loadedDictionary.Add(locatorKey, loadedHandle);
                        m_loadedCallback?.Invoke(locatorKey, loadedHandle);
                    }
                }
            }
            Complete(operationsHandles, true, string.Empty);
        }

        private bool TryGetKeyLocationId(IResourceLocator locator, object key, out string internalId)
        {
            internalId = string.Empty;
            var hasLocation = locator.Locate(key, typeof(Object), out var keyLocations);
            if (!hasLocation)
                return false;
            if(keyLocations.Count == 0)
                return false;
            if (keyLocations.Count > 1)
                return false;
            
            internalId = keyLocations[0].InternalId;
            return true;
        }
    }
}