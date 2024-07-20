using System;
using System.Collections.Generic;
using System.ComponentModel.Design;
using System.Linq;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.AddressableAssets.ResourceLocators;
using UnityEngine.ResourceManagement.ResourceLocations;
using UnityEngine.ResourceManagement.AsyncOperations;
using WizardAddressables.Runtime.Exceptions;
using WizardOptimizations.Runtime.Singelton;
using Object = UnityEngine.Object;

namespace WizardAddressables.Runtime.AssetManagement
{
    public class AssetManager : MonoBehaviorSingleton<AssetManager>
    {
        //TODO: could probably have the whole error thing as a separate package
        private const string BASE_ERROR = "<color=#ffa500>" + nameof(AssetManager) + " Error:</color> ";
        
        public delegate void DelegateAssetLoaded(object key, AsyncOperationHandle handle);
        public event DelegateAssetLoaded OnAssetLoaded;
        
        public delegate void DelegateAssetUnloaded(object runtimeKey);
        public event DelegateAssetUnloaded OnAssetUnloaded;
        
        private readonly Dictionary<object, AsyncOperationHandle> m_loadingAssets = new Dictionary<object, AsyncOperationHandle>(20);

        private readonly Dictionary<object, AsyncOperationHandle> m_loadedAssets =
            new Dictionary<object, AsyncOperationHandle>(100);
        public IReadOnlyList<object> LoadedAssets => m_loadedAssets.Values.Select(x => x.Result).ToList();
        private readonly Dictionary<object, List<GameObject>> m_instantiatedObjects =
            new Dictionary<object, List<GameObject>>(10);
        
        public int LoadedAssetsCount => m_loadedAssets.Count;
        public int LoadingAssetsCount => m_loadingAssets.Count;
        public int InstantiatedObjectsCount => m_instantiatedObjects.Values.SelectMany(x => x).Count();
        
        public bool IsLoaded(AssetReference reference) => m_loadedAssets.ContainsKey(reference.RuntimeKey);
        public bool IsLoaded(object runTimeKey) => m_loadedAssets.ContainsKey(runTimeKey);
        
        public bool IsLoading(AssetReference reference) => m_loadingAssets.ContainsKey(reference.RuntimeKey);
        public bool IsLoading(object runTimeKey) => m_loadingAssets.ContainsKey(runTimeKey);
        
        public bool IsInstantiated(AssetReference reference) => m_instantiatedObjects.ContainsKey(reference.RuntimeKey);
        public bool IsInstantiated(object runTimeKey) => m_instantiatedObjects.ContainsKey(runTimeKey);

        public int InstantiatedCount(AssetReference reference) => !IsInstantiated(reference) ? 0 : m_instantiatedObjects[reference.RuntimeKey].Count;
        public int InstantiatedCount(object runTimeKey) => !IsInstantiated(runTimeKey) ? 0 : m_instantiatedObjects[runTimeKey].Count;

        #region Load/Unload

        public bool TryGetOrLoadObjectAsync<TObjectType>(AssetReference reference,
            out AsyncOperationHandle<TObjectType> handle) where TObjectType : UnityEngine.Object
        {
            CheckRuntimeKey(reference);

            object runTimeKey = reference.RuntimeKey;
            if (m_loadedAssets.ContainsKey(runTimeKey))
            {
                try
                {
                    handle = m_loadedAssets[runTimeKey].Convert<TObjectType>();
                }
                catch (Exception e)
                {
                    handle = Addressables.ResourceManager.CreateCompletedOperation(m_loadedAssets[runTimeKey].Result as TObjectType, string.Empty);
                }
                return true;
            }

            if (m_loadingAssets.ContainsKey(runTimeKey))
            {
                try
                {
                    handle = m_loadingAssets[runTimeKey].Convert<TObjectType>();
                }
                catch (Exception e)
                {
                    handle = Addressables.ResourceManager.CreateChainOperation(m_loadingAssets[runTimeKey],
                        operationHandle => Addressables.ResourceManager.CreateCompletedOperation(operationHandle.Result as TObjectType, string.Empty));
                }

                return false;
            }

            handle = Addressables.LoadAssetAsync<TObjectType>(reference);
            
            m_loadingAssets.Add(runTimeKey, handle);

            handle.Completed += op2 =>
            {
                m_loadedAssets.Add(runTimeKey, op2);
                m_loadingAssets.Remove(runTimeKey);

                OnAssetLoaded?.Invoke(runTimeKey, op2);
            };
            return false;
        }

        public bool TryGetOrLoadComponentAsync<TComponentType>(AssetReference reference,
            out AsyncOperationHandle<TComponentType> handle) where TComponentType : UnityEngine.Component
        {
            CheckRuntimeKey(reference);
            
            var key = reference.RuntimeKey;

            if (m_loadedAssets.ContainsKey(key))
            {
                handle = ConvertHandleToComponent<TComponentType>(m_loadedAssets[key]);
                return true;
            }

            if (m_loadingAssets.ContainsKey(key))
            {
                handle = Addressables.ResourceManager.CreateChainOperation(m_loadingAssets[key],
                    operationHandle => ConvertHandleToComponent<TComponentType>(operationHandle));
                return false;
            }

            var op = Addressables.LoadAssetAsync<GameObject>(reference);
            
            m_loadingAssets.Add(key, op);

            op.Completed += op2 =>
            {
                m_loadedAssets.Add(key, op2);
                m_loadingAssets.Remove(key);
                OnAssetLoaded?.Invoke(key, op2);
            };

            handle = Addressables.ResourceManager.CreateChainOperation<TComponentType, GameObject>(op, chainOp =>
            {
                var go = chainOp.Result;
                var comp = go.GetComponent<TComponentType>();
                return Addressables.ResourceManager.CreateCompletedOperation(comp, String.Empty);
            });
            return false;
        }

        public bool TryGetObjectSync<TObjectType>(AssetReference reference, out TObjectType result)
            where TObjectType : UnityEngine.Object
        {
            CheckRuntimeKey(reference);
            var key = reference.RuntimeKey;

            if (m_loadedAssets.ContainsKey(key))
            {
                result = m_loadedAssets[key].Convert<TObjectType>().Result;
                return true;
            }
            
            result = null;
            return false;
        }

        public bool TryGetComponentSync<TComponentType>(AssetReference reference, out TComponentType result)
            where TComponentType : UnityEngine.Component
        {
            CheckRuntimeKey(reference);
            var key = reference.RuntimeKey;
            
            if(m_loadedAssets.ContainsKey(key))
            {
                var handle = m_loadedAssets[key];
                result = null;
                var go = handle.Result as GameObject;
                if(!go)
                    throw new ConversionException($"Cannot convert {nameof(handle.Result)} to {nameof(GameObject)}");
                result = go.GetComponent<TComponentType>();
                if(!result)
                    throw new CheckoutException($"Cannot convert {nameof(go)} to {nameof(TComponentType)}");
                return true;
            }
            
            result = null;
            return false;
        }
        
        public AsyncOperationHandle<List<AsyncOperationHandle<Object>>> LoadAssetsByLabelAsync(string label)
        {
            var handle = Addressables.ResourceManager.StartOperation(
                new LoadAssetsByLabelOperation(m_loadedAssets, m_loadingAssets, label, AssetLoadedCallback), default);
            return handle;
        }
        
        public AsyncOperationHandle<List<AsyncOperationHandle<T>>> LoadAssetsByLabelAsync<T>(string label) where T : UnityEngine.Object
        {
            var handle = Addressables.ResourceManager.StartOperation(
                new LoadAssetsByLabelOperation<T>(m_loadedAssets, m_loadingAssets, label, AssetLoadedCallback), default);
            return handle;
        }

        private void AssetLoadedCallback(object key, AsyncOperationHandle handle)
        {
            OnAssetLoaded?.Invoke(key, handle);
        }

        public void Unload(AssetReference reference)
        {
            CheckRuntimeKey(reference);

            var key = reference.RuntimeKey;
            
            Unload(key);
        }

        private void Unload(object key)
        {
            CheckRuntimeKey(key);

            AsyncOperationHandle handle;
            if (m_loadingAssets.ContainsKey(key))
            {
                handle = m_loadingAssets[key];
                m_loadingAssets.Remove(key);
            }else if (m_loadedAssets.ContainsKey(key))
            {
                handle = m_loadedAssets[key];
                m_loadedAssets.Remove(key);
            }
            else
            {
                Debug.LogWarning($"{BASE_ERROR} Cannot {nameof(Unload)} RuntimeKey '{key}': it is not loading or loaded");
                return;
            }

            if (IsInstantiated(key))
            {
                DestroyAllInstances(key);
            }
            
            Addressables.Release(handle);
            
            OnAssetUnloaded?.Invoke(key);
        }
        

        public void UnloadByLabel(string label)
        {
            if (string.IsNullOrEmpty(label) || string.IsNullOrWhiteSpace(label))
            {
                Debug.LogError("Label cannot be null or empty");
                return;
            }
            
            var locationsHandle = Addressables.LoadResourceLocationsAsync(label);
            locationsHandle.Completed += op =>
            {
                if (locationsHandle.Status != AsyncOperationStatus.Succeeded)
                {
                    Debug.LogError($"Cannot unload by label {label}");
                    return;
                }

                var keys = GetKeysFromLocations(op.Result);
                foreach (var key in keys)
                {
                    if (IsLoaded(key) || IsLoading(key))
                        Unload(key);
                }
            };
        }
        

        #endregion

        #region Instantiation

        public bool TryInstantiateOrLoadAsync(AssetReference reference, Vector3 position, Quaternion rotation,
            Transform parent, out AsyncOperationHandle<GameObject> handle)
        {
            if(TryGetOrLoadObjectAsync(reference, out AsyncOperationHandle<GameObject> loadHandle))
            {
                var instance = InstantiateInternal(reference, loadHandle.Result, position, rotation, parent);
                handle = Addressables.ResourceManager.CreateCompletedOperation(instance, string.Empty);
                return true;
            }

            if (!loadHandle.IsValid())
            {
                Debug.LogError($"Load Operation was invalid: {loadHandle}");
                handle = Addressables.ResourceManager.CreateCompletedOperation<GameObject>(null, $"Load Operation was invalid: {loadHandle}");
                return false;
            }

            handle = Addressables.ResourceManager.CreateChainOperation(loadHandle, operationHandle =>
            {
                var instance = InstantiateInternal(reference, operationHandle.Result, position, rotation, parent);
                return Addressables.ResourceManager.CreateCompletedOperation(instance, string.Empty);
            });
            return false;
        }

        public bool TryInstantiateOrLoadAsync<TComponentType>(AssetReference reference, Vector3 postion,
            Quaternion rotation, Transform parent,
            out AsyncOperationHandle<TComponentType> handle) where TComponentType : Component
        {
            if (TryGetOrLoadComponentAsync(reference, out AsyncOperationHandle<TComponentType> loadHandle))
            {
                var instance = InstantiateInternal(reference, loadHandle.Result, postion, rotation, parent);
                handle = Addressables.ResourceManager.CreateCompletedOperation(instance, string.Empty);
                return true;
            }

            if (!loadHandle.IsValid())
            {
                Debug.LogError($"Load Operation was invalid: {loadHandle}");
                handle = Addressables.ResourceManager.CreateCompletedOperation<TComponentType>(null, $"Load Operation was invalid: {loadHandle}");
                return false;
            }
            
            //Create a chain that waits for loadHandle to finish, then instantiates and returns the instance GO.
            handle = Addressables.ResourceManager.CreateChainOperation(loadHandle, operationHandle =>
            {
                var instance = InstantiateInternal(reference, operationHandle.Result, postion, rotation, parent);
                return Addressables.ResourceManager.CreateCompletedOperation(instance, string.Empty);
            });
            return false;
        }

        public bool TryInstantiateMultiOrLoadAsync(AssetReference reference, int count, Vector3 position,
            Quaternion rotation, Transform parent,
            out AsyncOperationHandle<List<GameObject>> handle)
        {
            if (TryGetOrLoadObjectAsync(reference, out AsyncOperationHandle<GameObject> loadHandle))
            {
                var list = new List<GameObject>(count);
                for (int i = 0; i < count; i++)
                {
                    var instance = InstantiateInternal(reference, loadHandle.Result, position, rotation, parent);
                    list.Add(instance);
                }

                handle = Addressables.ResourceManager.CreateCompletedOperation(list, string.Empty);
            }

            if (!loadHandle.IsValid())
            {
                Debug.LogError($"Load operation was invalid: {loadHandle}");
                handle = Addressables.ResourceManager.CreateCompletedOperation<List<GameObject>>(null, $"Load operation was invalid: {loadHandle}");
                return false;
            }

            handle = Addressables.ResourceManager.CreateChainOperation(loadHandle, operationHandle =>
            {
                var list = new List<GameObject>(count);
                for (int i = 0; i < count; i++)
                {
                    var instance = InstantiateInternal(reference, operationHandle.Result, position, rotation, parent);
                    list.Add(instance);
                }

                return Addressables.ResourceManager.CreateCompletedOperation(list, string.Empty);
            });
            return false;
        }

        public bool TryInstantiateMultiOrLoadAsync<TComponentType>(AssetReference reference, int count,
            Vector3 position, Quaternion rotation, Transform parent,
            out AsyncOperationHandle<List<TComponentType>> handle) where TComponentType : Component
        {
            if (TryGetOrLoadComponentAsync(reference, out AsyncOperationHandle<TComponentType> loadHandle))
            {
                var list = new List<TComponentType>(count);
                for (int i = 0; i < count; i++)
                {
                    var instance = InstantiateInternal(reference, loadHandle.Result, position, rotation, parent);
                    list.Add(instance);
                }

                handle = Addressables.ResourceManager.CreateCompletedOperation(list, string.Empty);
                return true;
            }

            if (!loadHandle.IsValid())
            {
                Debug.LogError($"Load operation was invalid: {loadHandle}");
                handle = Addressables.ResourceManager.CreateCompletedOperation<List<TComponentType>>(null, $"Load operation was invalid: {loadHandle}");
                return false;
            }
            
            handle = Addressables.ResourceManager.CreateChainOperation(loadHandle, operationHandle =>
            {
                var list = new List<TComponentType>(count);
                for (int i = 0; i < count; i++)
                {
                    var instance = InstantiateInternal(reference, operationHandle.Result, position, rotation, parent);
                    list.Add(instance);
                }

                return Addressables.ResourceManager.CreateCompletedOperation(list, string.Empty);
            });
            return false;
        }
        
        public bool TryInstantiateSync(AssetReference reference, Vector3 position, Quaternion rotation, Transform parent,
            out GameObject result)
        {
            if(!TryGetObjectSync(reference, out GameObject loadedAsset))
            {
                result = null;
                return false;
            }
            result = InstantiateInternal(reference, loadedAsset, position, rotation, parent);
            return true;
        }
        
        public bool TryInstantiateSync<TComponentType>(AssetReference reference, Vector3 position, Quaternion rotation, Transform parent,
            out TComponentType result) where TComponentType : Component
        {
           if(!TryGetComponentSync(reference, out TComponentType loadedAsset))
           {
               result = null;
               return false;
           }
           
           result = InstantiateInternal(reference, loadedAsset, position, rotation, parent);
           return true;
        }
        
        public bool TryInstantiateMultiSync(AssetReference reference, int count, Vector3 position, Quaternion rotation,
            Transform parent, out List<GameObject> result)
        {
            if (!TryGetObjectSync(reference, out GameObject loadedAsset))
            {
                result = null;
                return false;
            }

            result = new List<GameObject>(count);
            for (int i = 0; i < count; i++)
            {
                var instance = InstantiateInternal(reference, loadedAsset, position, rotation, parent);
                result.Add(instance);
            }

            return true;
        }
        
        public bool TryInstantiateMultiSync<TComponentType>(AssetReference reference, int count, Vector3 position,
            Quaternion rotation, Transform parent, out List<TComponentType> result) where TComponentType : Component
        {
            if (!TryGetComponentSync(reference, out TComponentType loadedAsset))
            {
                result = null;
                return false;
            }

            result = new List<TComponentType>(count);
            for (int i = 0; i < count; i++)
            {
                var instance = InstantiateInternal(reference, loadedAsset, position, rotation, parent);
                result.Add(instance);
            }

            return true;
        }
        
        public void DestroyAllInstances(AssetReference reference)
        {
            CheckRuntimeKey(reference);
            if (!m_instantiatedObjects.ContainsKey(reference.RuntimeKey))
            {
                Debug.LogWarning($"{nameof(AssetReference)} '{reference}' has no instantiated objects");
                return;
            }
            
            var key = reference.RuntimeKey;
            DestroyAllInstances(key);
        }
        
        private void DestroyAllInstances(object key)
        {
            var instanceList = m_instantiatedObjects[key];
            for (int i = instanceList.Count - 1; i >= 0; i--)
            {
                DestroyInternal(instanceList[i]);
            }
            m_instantiatedObjects[key].Clear();
            m_instantiatedObjects.Remove(key);
        }

        private void DestroyInternal(Object instance)
        {
            if (instance is Component component)
            {
                Object.Destroy(component.gameObject);
            }
            else
            {
                var go = instance as GameObject;
                if(go)
                    Object.Destroy(go);
            }
        }

        private GameObject InstantiateInternal(AssetReference reference, GameObject loadedAsset, Vector3 position,
            Quaternion rotation, Transform parent)
        {
            var key = reference.RuntimeKey;
            
            var instance = Object.Instantiate(loadedAsset, position, rotation, parent);
            if(!instance)
                throw new NullReferenceException($"Instantiated object of type '{typeof(GameObject)}' is null");
            var monoTracker = instance.gameObject.AddComponent<MonoTracker>();
            monoTracker.Key = key;
            monoTracker.OnDestroyed += TrackerDestroyed;
            
            if(!m_instantiatedObjects.ContainsKey(key))
                m_instantiatedObjects.Add(key, new List<GameObject>(20));
            m_instantiatedObjects[key].Add(instance);
            return instance;
        }

        private TComponentType InstantiateInternal<TComponentType>(AssetReference reference, TComponentType loadedAsset,
            Vector3 position, Quaternion rotation, Transform parent) where TComponentType: Component
        {
            var key = reference.RuntimeKey;
            
            var instance = Object.Instantiate(loadedAsset, position, rotation, parent);
            
            if(!instance)
                throw new NullReferenceException($"Instantiated object of type '{typeof(TComponentType)}' is null");
            
            var monoTracker = instance.gameObject.AddComponent<MonoTracker>();
            monoTracker.Key = key;
            monoTracker.OnDestroyed += TrackerDestroyed;

            if (!m_instantiatedObjects.ContainsKey(key))
            {
                m_instantiatedObjects.Add(key, new List<GameObject>(20));
            }
            m_instantiatedObjects[key].Add(instance.gameObject);
            return instance;
        }

        private void TrackerDestroyed(MonoTracker tracker)
        {
            if(m_instantiatedObjects.TryGetValue(tracker.Key, out var instanceList))
            {
                instanceList.Remove(tracker.gameObject);
            }
        }

        #endregion

        #region Utiities

        private void CheckRuntimeKey(AssetReference reference)
        {
            if (!reference.RuntimeKeyIsValid())
                throw new InvalidKeyException(
                    $"{BASE_ERROR}{nameof(reference.RuntimeKey)} is not valid for '{reference}'");
        }

        private bool CheckRuntimeKey(object runtimeKey)
        {
            return Guid.TryParse(runtimeKey.ToString(), out var result);
        }
        
        private AsyncOperationHandle<TComponentType> ConvertHandleToComponent<TComponentType>(AsyncOperationHandle handle)
            where TComponentType : UnityEngine.Component
        {
            GameObject go = handle.Result as GameObject;
            if (!go)
                throw new ConversionException(
                    $"Cannot convert {nameof(handle.Result)} to {nameof(GameObject)}");
            TComponentType component = go.GetComponent<TComponentType>();
            if (!component)
                throw new ConversionException(
                    $"Cannot convert {nameof(go)} to {nameof(TComponentType)}");
            var result = Addressables.ResourceManager.CreateCompletedOperation(component, string.Empty);
            return result;
        }
        
        private List<object> GetKeysFromLocations(IList<IResourceLocation> locations)
        {
            List<object> keys = new List<object>(locations.Count);

            foreach (IResourceLocator resourceLocator in Addressables.ResourceLocators)
            {
                foreach (var locatorKey in resourceLocator.Keys)
                {
                    bool isGuid = Guid.TryParse(locatorKey.ToString(), out var guid);
                    if (!isGuid)
                        continue;

                    if (!TryGetKeyLocationId(resourceLocator, locatorKey, out var keyLocationId))
                        continue;

                    var locationMatched = locations.Select(x => x.InternalId).ToList().Exists(x => x == keyLocationId);
                    if(!locationMatched)
                        continue;
                    keys.Add(locatorKey);

                }
            }

            return keys;
        }
        
        private bool TryGetKeyLocationId(IResourceLocator locator, object key, out string internalId)
        {
            internalId = string.Empty;
            var hasLocation = locator.Locate(key, typeof(Object), out var keyLocations);

            if (!hasLocation)
                return false;
            if (keyLocations.Count == 0)
                return false;
            if (keyLocations.Count > 1)
                return false;

            internalId = keyLocations[0].InternalId;
            return true;
        }
        
        #endregion
    }
}