using System.Threading;
using Cysharp.Threading.Tasks;
using Unity.Netcode;
using UnityEngine;

public class PlayerSpawner : MonoBehaviour, IRoundSpawner
{
    [Header("Site Spawn Settings")]
    [SerializeField] private MapRegionController _regionController;
    [SerializeField] private RoundSpawnCoordinator _spawnCoordinator;
    [SerializeField]
    private SpawnRule _spawnRule = new()
    {
        MinimumDistance = 3f,
        MaxAttempts = 50,
        HeightOffset = 0.2f,
        UseGroundPosition = true,
        ReservePosition = true
    };

    [Header("HQ Spawn Settings")]
    [SerializeField] private Transform _hqSpawnPoint;

    public int SpawnCount => NetworkManager.Singleton?.ConnectedClientsList.Count ?? 0;
    public SpawnRule Rule => _spawnRule;

    public bool TryGetSiteSpawnPose(out Vector3 position, out Quaternion rotation)
    {
        return TryGetSiteSpawnPose(_spawnCoordinator, out position, out rotation);
    }

    // 현재 활성화된 현장 구역의 실제 지면 위에서 무작위 스폰 위치를 구합니다.
    public bool TryGetSiteSpawnPose(RoundSpawnCoordinator coordinator, out Vector3 position, out Quaternion rotation)
    {
        if (_regionController != null && _regionController.RefreshSpawnAreas() && coordinator != null &&
        coordinator.TryGetSpawnPose(_regionController, Rule, this, out _, out position, out rotation))
        {
            return true;
        }

        position = default;
        rotation = Quaternion.identity;
        return false;
    }

    // 전달받은 플레이어 한 명을 역할에 맞는 위치로 배치한다.
    public void SpawnPlayer(NetworkObject playerObject)
    {
        SpawnPlayer(playerObject, _spawnCoordinator);
    }

    private void SpawnPlayer(NetworkObject playerObject, RoundSpawnCoordinator coordinator)
    {
        if (coordinator == null)
        {
            Debug.LogError("[PlayerSpawner] RoundSpawnCoordinator가 설정되지 않았습니다.", this);
            return;
        }

        if (playerObject == null ||
            !playerObject.TryGetComponent(out PlayerMoveSample move) ||
            !playerObject.TryGetComponent(out Player player))
        {
            Debug.LogError("[PlayerSpawner] 아직 캐릭터가 스폰되지 않았습니다.", this);
            return;
        }

        if (player.PlayerRole == Role.Headquarter)
        {
            if (_hqSpawnPoint == null)
            {
                Debug.LogError("[PlayerSpawner] 본부 HQSpawnPoint가 설정되지 않았습니다.", this);
                return;
            }

            move.TeleportToPosition(_hqSpawnPoint.position, _hqSpawnPoint.rotation);

            // 실제 배치에 사용한 본부 좌표를 최초 긴급 탈출 위치로 기록합니다.
            playerObject.GetComponent<PlayerEmergencyEscape>()
                .RecordInitialSpawnPosition(_hqSpawnPoint.position);

            return;
        }

        if (TryGetSiteSpawnPose(coordinator, out Vector3 position, out Quaternion rotation))
        {
            move.TeleportToPosition(position, rotation);

            // 실제 배치에 사용한 현장 좌표를 최초 긴급 탈출 위치로 기록합니다.
            playerObject.GetComponent<PlayerEmergencyEscape>()
                .RecordInitialSpawnPosition(position);

            return;
        }

        Debug.LogError("[PlayerSpawner] 활성화된 현장 스폰 구역에서 플레이어 스폰 위치를 찾지 못했습니다.", this);
    }

    // 기존 라운드 시작 흐름과 IRoundSpawner 호환을 위해 접속자를 순회하되,
    // 실제 배치 규칙은 SpawnPlayer 한 곳에서만 처리한다.
    public void SpawnClients()
    {
        SpawnClients(_spawnCoordinator);
    }

    public UniTask SpawnAsync(RoundSpawnCoordinator coordinator, CancellationToken cancellationToken)
    {
        SpawnClients(coordinator);
        return UniTask.CompletedTask;
    }

    private void SpawnClients(RoundSpawnCoordinator coordinator)
    {
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer)
        {
            Debug.LogError("[PlayerSpawner] 플레이어 배치는 서버에서만 실행할 수 있습니다.", this);
            return;
        }

        foreach (var client in NetworkManager.Singleton.ConnectedClientsList)
        {
            SpawnPlayer(client.PlayerObject, coordinator);
        }
    }

    public void ClearSpawned()
    {
        // PlayerSpawner는 기존 PlayerObject를 이동만 하므로 제거할 오브젝트가 없다.
        _spawnCoordinator?.ClearPositions(this);
    }
}
