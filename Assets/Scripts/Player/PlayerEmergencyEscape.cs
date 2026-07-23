using Unity.Netcode;
using UnityEngine;

// 최초 스폰 위치 기록과 긴급 탈출 위치 결정을 이동 코드에서 분리해 관리한다.
[RequireComponent(typeof(PlayerMoveSample))]
public class PlayerEmergencyEscape : NetworkBehaviour
{
    private PlayerMoveSample _playerMove;
    private Vector3 _initialSpawnPosition;
    private bool _hasInitialSpawnPosition;

    private void Awake()
    {
        _playerMove = GetComponent<PlayerMoveSample>();
    }

    // 서버가 확정한 최초 스폰 위치를 플레이어 오브젝트 생명주기 동안 한 번만 기록한다.
    public void RecordInitialSpawnPosition(Vector3 position)
    {
        if (!IsServer || _hasInitialSpawnPosition)
        {
            return;
        }

        _initialSpawnPosition = position;
        _hasInitialSpawnPosition = true;
    }

    public void RequestEmergencyEscape()
    {
        // 각 클라이언트는 자신이 소유한 플레이어만 탈출 요청을 보낼 수 있다.
        if (IsOwner)
        {
            RequestEmergencyEscapeRpc();
        }
    }

    [Rpc(SendTo.Server)]
    private void RequestEmergencyEscapeRpc()
    {
        // 플레이어가 지면에 있으면 긴급 탈출을 실행하지 않는다.
        if (_playerMove.IsGrounded())
        {
            return;
        }

        // 최초 스폰 위치가 기록되어 있으면 해당 위치로 이동한다.
        if (_hasInitialSpawnPosition)
        {
            _playerMove.TeleportToPosition(_initialSpawnPosition, transform.rotation);

            return;
        }

        PlayerSpawner playerSpawner = FindAnyObjectByType<PlayerSpawner>();

        // 최초 스폰 위치가 없으면 기존 현장 스폰 위치 계산을 재사용한다.
        if (playerSpawner != null &&
            playerSpawner.TryGetSiteSpawnPose(
                out Vector3 position,
                out Quaternion rotation))
        {
            _playerMove.TeleportToPosition(position, rotation);
            return;
        }

        Debug.LogWarning("긴급탈출 위치를 찾지 못했습니다.", this);
    }
}
