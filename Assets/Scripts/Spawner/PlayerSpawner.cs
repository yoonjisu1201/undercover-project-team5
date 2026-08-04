using System.Linq;
using Unity.Netcode;
using UnityEngine;

public class PlayerSpawner : MonoBehaviour
{
    [Header("HQ Spawn Settings")]
    [SerializeField] private Transform _hqSpawnPoint;

    [Header("Field Spawn Points (차 앞 고정 스폰)")]
    [SerializeField] private Transform[] _fieldSpawnPoints;

    // 현장 역할 플레이어들 중 이 플레이어가 몇 번째인지에 따라 고정 스폰 포인트를 배정한다.
    // OwnerClientId로 정렬해 어느 클라이언트에서 계산해도 동일한 결과가 나오도록 한다.
    private int GetFieldSpawnIndex(Player player)
    {
        return Player.ActiveInstances
            .Where(p => p.PlayerRole == Role.Field)
            .OrderBy(p => p.OwnerClientId)
            .ToList()
            .IndexOf(player);
    }

    // 전달받은 플레이어 한 명을 역할에 맞는 위치로 배치한다.
    public void SpawnPlayer(NetworkObject playerObject) {
        if (playerObject == null ||
            !playerObject.TryGetComponent(out PlayerMoveSample move) ||
            !playerObject.TryGetComponent(out Player player))
        {
            Debug.LogError("[PlayerSpawner] 아직 캐릭터가 스폰되지 않았습니다.", this);
            return;
        }

        if (player.PlayerRole == Role.Headquarter) {
            if (_hqSpawnPoint == null) {
                Debug.LogError("[PlayerSpawner] 본부 HQSpawnPoint가 설정되지 않았습니다.", this);
                return;
            }
            
            move.TeleportToPosition(_hqSpawnPoint.position, _hqSpawnPoint.rotation);
            
            // 현재 라운드에서 실제 배치에 사용한 본부 위치를 기본 복귀 위치로 기록합니다.
            if (playerObject.TryGetComponent(out PlayerEmergencyEscape emergencyEscape)) {
                emergencyEscape.RecordRoundSpawnPose(
                    _hqSpawnPoint.position,
                    _hqSpawnPoint.rotation);
            }
            return;
        }
        
        int index = GetFieldSpawnIndex(player);
        if (index < 0 || index >= _fieldSpawnPoints.Length || _fieldSpawnPoints[index] == null)
        {
            Debug.LogError("[PlayerSpawner] 현장 스폰 포인트가 부족합니다.", this);
            return;
        }

        Vector3 position = _fieldSpawnPoints[index].position;
        Quaternion rotation = _fieldSpawnPoints[index].rotation;

        move.TeleportToPosition(position, rotation);

        // 현재 라운드에서 실제 배치에 사용한 현장 위치를 기본 복귀 위치로 기록합니다.
        if (playerObject.TryGetComponent(out PlayerEmergencyEscape fieldEmergencyEscape)) {
            fieldEmergencyEscape.RecordRoundSpawnPose(position, rotation);
        }
    }

    // 라운드 전환 시 전체 플레이어를 지정된 스폰 위치로 다시 배치합니다.
    public void RespawnAllPlayers()
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
                playerObject.TryGetComponent(out Player player))
            {
                SpawnPlayer(playerObject);
            }
        }
    }
}
