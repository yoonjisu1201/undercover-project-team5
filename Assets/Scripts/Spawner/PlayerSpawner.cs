using System.Linq;
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

    [Header("Field Spawn Points (차 앞 고정 스폰)")]
    [SerializeField] private Transform[] _fieldSpawnPoints;

    public int SpawnCount => NetworkManager.Singleton?.ConnectedClientsList.Count ?? 0;
    public SpawnRule Rule => _spawnRule;

    // 현장 역할 플레이어들 중 이 플레이어가 몇 번째인지에 따라 고정 스폰 포인트를 배정한다.
    private bool TryGetFixedFieldSpawnPose(Player player, out Vector3 position, out Quaternion rotation)
    {
        if (_fieldSpawnPoints == null || _fieldSpawnPoints.Length == 0)
        {
            position = default;
            rotation = Quaternion.identity;
            return false;
        }

        var fieldPlayers = FindObjectsByType<Player>(FindObjectsSortMode.None)
            .Where(p => p.PlayerRole == Role.Field)
            .OrderBy(p => p.OwnerClientId)
            .ToList();

        int index = fieldPlayers.IndexOf(player);
        if (index < 0 || index >= _fieldSpawnPoints.Length || _fieldSpawnPoints[index] == null)
        {
            position = default;
            rotation = Quaternion.identity;
            return false;
        }

        position = _fieldSpawnPoints[index].position;
        rotation = _fieldSpawnPoints[index].rotation;
        return true;
    }

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

            _hqSpawnPoint.rotation = Quaternion.Euler(0f, 90f, 0f);

            move.TeleportToPosition(_hqSpawnPoint.position, _hqSpawnPoint.rotation);

            // 현재 라운드에서 실제 배치에 사용한 본부 위치를 기본 복귀 위치로 기록합니다.
            if (playerObject.TryGetComponent(out PlayerEmergencyEscape emergencyEscape))
            {
                emergencyEscape.RecordRoundSpawnPose(
                    _hqSpawnPoint.position,
                    _hqSpawnPoint.rotation);
            }

            return;
        }

        if (TryGetFixedFieldSpawnPose(player, out Vector3 fixedPosition, out Quaternion fixedRotation))
        {
            move.TeleportToPosition(fixedPosition, fixedRotation);

            if (playerObject.TryGetComponent(out PlayerEmergencyEscape fixedEmergencyEscape))
            {
                fixedEmergencyEscape.RecordRoundSpawnPose(fixedPosition, fixedRotation);
            }

            return;
        }

        if (TryGetSiteSpawnPose(coordinator, out Vector3 position, out Quaternion rotation))
        {
            move.TeleportToPosition(position, rotation);

            // 현재 라운드에서 실제 배치에 사용한 현장 위치를 기본 복귀 위치로 기록합니다.
            if (playerObject.TryGetComponent(out PlayerEmergencyEscape emergencyEscape))
            {
                emergencyEscape.RecordRoundSpawnPose(position, rotation);
            }

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

    // 라운드 전환 시 본부 역할 플레이어만 지정된 본부 스폰 위치로 다시 배치합니다.
    public void RespawnHeadquarterPlayers()
    {
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer)
        {
            Debug.LogError("[PlayerSpawner] 본부 플레이어 재배치는 서버에서만 실행할 수 있습니다.", this);
            return;
        }

        foreach (NetworkClient client in NetworkManager.Singleton.ConnectedClientsList)
        {
            NetworkObject playerObject = client.PlayerObject;
            if (playerObject != null &&
                playerObject.TryGetComponent(out Player player) &&
                player.PlayerRole == Role.Headquarter)
            {
                SpawnPlayer(playerObject, _spawnCoordinator);
            }
        }
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
