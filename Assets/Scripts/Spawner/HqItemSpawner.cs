using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

// 본부의 고정된 스폰 지점에 아이템을 배치하고, 씬 재로드/라운드 전환마다 서버 권한으로 다시 생성하는 클래스
public sealed class HqItemSpawner : MonoBehaviour
{
    [Header("스폰할 아이템 데이터")]
    [SerializeField] private ItemData _item;

    [Header("본부 스폰 지점")]
    [SerializeField] private Transform[] _spawnPoints;

    // #664: 스폰을 통째로 막을 때 쓴다. 컴포넌트를 꺼도 RoundManager가 RespawnTools()를 직접 호출하므로
    // 라운드 전환 스폰까지 막으려면 이 값을 꺼야 한다.
    [SerializeField] private bool _spawnEnabled = true;

    private bool _hasSpawned;

    private void OnEnable()
    {
        if (NetworkManager.Singleton?.SceneManager == null) return;

        NetworkManager.Singleton.SceneManager.OnLoadEventCompleted += HandleSceneLoaded;
    }

    private void OnDisable()
    {
        if (NetworkManager.Singleton?.SceneManager == null) return;

        NetworkManager.Singleton.SceneManager.OnLoadEventCompleted -= HandleSceneLoaded;
    }

    private void HandleSceneLoaded(string sceneName, LoadSceneMode loadSceneMode, List<ulong> clientsCompleted, List<ulong> clientsTimedOut)
    {
        if (_hasSpawned || sceneName != gameObject.scene.name || !NetworkManager.Singleton.IsServer)
        {
            return;
        }

        SpawnTools();
    }

    // 라운드 전환 시점에 RoundManager가 호출한다. 기존 아이템을 정리하고 본부 스폰 지점에 새로 생성한다.
    public void RespawnTools()
    {
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer)
        {
            Debug.LogWarning("[HqItemSpawner] 서버에서만 아이템을 재생성할 수 있습니다.", this);
            return;
        }

        ClearSpawned();
        SpawnTools();
    }

    public void SpawnTools()
    {
        if (!_spawnEnabled || NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer || !ValidateSettings())
        {
            return;
        }

        _hasSpawned = true;
        Quaternion spawnRotation = GetInitialSpawnRotation();

        foreach (Transform spawnPoint in _spawnPoints)
        {
            if (spawnPoint == null)
            {
                Debug.LogWarning("[HqItemSpawner] 비어 있는 스폰 지점이 있어 건너뜁니다.", this);
                continue;
            }

            GameObject toolObject = Instantiate(
                _item.WorldPrefab,
                spawnPoint.position,
                spawnRotation);

            if (!toolObject.TryGetComponent(out ItemBase pickupItem) ||
                !toolObject.TryGetComponent(out NetworkObject networkObject))
            {
                Debug.LogError($"[HqItemSpawner] '{_item.WorldPrefab.name}' 프리팹에 ItemBase 또는 NetworkObject가 없습니다.", this);
                Destroy(toolObject);
                continue;
            }

            pickupItem.Configure(_item);
            networkObject.Spawn(destroyWithScene: true);

            // 본부 선반에 최초 배치되는 제압기는 물리 충돌로 튀지 않도록 고정한다.
            // 플레이어가 주웠다가 버릴 때는 기존 Rearm()에서 물리가 다시 활성화된다.
            if (_item.ItemId == ItemType.AlienCaptureGun &&
                toolObject.TryGetComponent(out ItemRigidbodySetter rigidbodySetter))
            {
                rigidbodySetter.Freeze();
            }
        }
    }

    private Quaternion GetInitialSpawnRotation()
    {
        Transform prefabTransform = _item.WorldPrefab.transform;

        if (_item.ItemId != ItemType.AlienCaptureGun)
        {
            return prefabTransform.rotation;
        }

        Vector3 eulerAngles = prefabTransform.eulerAngles;
        eulerAngles.x = -90f;
        eulerAngles.z = -90f;
        return Quaternion.Euler(eulerAngles);
    }

    public void ClearSpawned()
    {
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer)
        {
            return;
        }

        DespawnAllFieldTools();
        _hasSpawned = false;
    }

    private void DespawnAllFieldTools()
    {
        // 본부에서 최초 스폰된 것뿐만 아니라, 플레이어가 필드에 다시 버린 아이템까지 찾는다.
        ItemBase[] fieldItems = FindObjectsByType<ItemBase>(FindObjectsSortMode.None);

        foreach (ItemBase fieldItem in fieldItems)
        {
            if (fieldItem.ItemId != _item.ItemId)
            {
                continue;
            }

            // 누군가 인벤토리에 들고 있는 도구는 필드에 있는 게 아니므로 건드리지 않는다.
            if (fieldItem.IsStored)
            {
                continue;
            }

            NetworkObject networkObject = fieldItem.NetworkObject;

            if (networkObject != null && networkObject.IsSpawned)
            {
                networkObject.Despawn(destroy: true);
            }
        }
    }

    private bool ValidateSettings()
    {
        if (_item == null || _item.WorldPrefab == null)
        {
            Debug.LogError("[HqItemSpawner] 스폰할 아이템 데이터를 설정해야 합니다.", this);
            return false;
        }

        if (_spawnPoints == null || _spawnPoints.Length == 0)
        {
            Debug.LogError("[HqItemSpawner] 본부 스폰 지점을 하나 이상 등록해야 합니다.", this);
            return false;
        }

        return true;
    }
}
