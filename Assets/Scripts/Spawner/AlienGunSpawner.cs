using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

// 본부의 고정된 스폰 지점에 검거도구를 배치하고, 씬 재로드/라운드 전환마다 서버 권한으로 다시 생성하는 클래스
public sealed class AlienGunSpawner : MonoBehaviour
{
    [Header("검거도구 데이터")]
    [SerializeField] private ItemData _captureGun;

    [Header("본부 스폰 지점")]
    [SerializeField] private Transform[] _spawnPoints;

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

    // 라운드 전환 시점에 RoundManager가 호출한다. 기존 검거도구를 정리하고 본부 스폰 지점에 새로 생성한다.
    public void RespawnTools()
    {
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer)
        {
            Debug.LogWarning("[AlienGunSpawner] 서버에서만 검거도구를 재생성할 수 있습니다.", this);
            return;
        }

        ClearSpawned();
        SpawnTools();
    }

    public void SpawnTools()
    {
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer || !ValidateSettings())
        {
            return;
        }

        _hasSpawned = true;

        foreach (Transform spawnPoint in _spawnPoints)
        {
            if (spawnPoint == null)
            {
                Debug.LogWarning("[AlienGunSpawner] 비어 있는 스폰 지점이 있어 건너뜁니다.", this);
                continue;
            }

            GameObject toolObject = Instantiate(_captureGun.WorldPrefab, spawnPoint.position, spawnPoint.rotation);

            if (!toolObject.TryGetComponent(out PickupItem pickupItem) ||
                !toolObject.TryGetComponent(out NetworkObject networkObject))
            {
                Debug.LogError($"[AlienGunSpawner] '{_captureGun.WorldPrefab.name}' 프리팹에 PickupItem 또는 NetworkObject가 없습니다.", this);
                Destroy(toolObject);
                continue;
            }

            pickupItem.Configure(_captureGun);
            networkObject.Spawn(destroyWithScene: true);
        }
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
        // 본부에서 최초 스폰된 것뿐만 아니라, 플레이어가 필드에 다시 버린 검거도구까지 찾는다.
        PickupItem[] fieldItems = FindObjectsByType<PickupItem>(FindObjectsSortMode.None);

        foreach (PickupItem fieldItem in fieldItems)
        {
            if (fieldItem.ItemId != ArrestChaseManager.CaptureToolItemId)
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
        if (_captureGun == null || _captureGun.WorldPrefab == null)
        {
            Debug.LogError("[AlienGunSpawner] 검거도구 아이템 데이터를 설정해야 합니다.", this);
            return false;
        }

        if (_spawnPoints == null || _spawnPoints.Length == 0)
        {
            Debug.LogError("[AlienGunSpawner] 본부 스폰 지점을 하나 이상 등록해야 합니다.", this);
            return false;
        }

        return true;
    }
}
