using Unity.Netcode;
using UnityEngine;

// 안전 위치 기록과 긴급 탈출 위치 결정을 이동 코드에서 분리해 관리한다.
[RequireComponent(typeof(PlayerMoveSample))]
public class PlayerEmergencyEscape : NetworkBehaviour
{
    // 끼인 현재 위치를 다시 선택하지 않도록 일정 거리 이상 떨어진 위치만 사용한다.
    [SerializeField, Min(0.1f)] private float _recordIntervalSec = 25f;
    [SerializeField, Min(0f)] private float _minimumEscapeDistance = 2f;

    private PlayerMoveSample _playerMove;
    private float _recordElapsedSec;
    private Vector3 _safePosition;
    private bool _hasSafePosition;

    private void Awake()
    {
        _playerMove = GetComponent<PlayerMoveSample>();
    }

    private void FixedUpdate()
    {
        // 안전 위치는 탈출을 결정하는 서버에서만 기록한다.
        if (!IsServer)
        {
            return;
        }

        _recordElapsedSec += Time.fixedDeltaTime;

        if (_recordElapsedSec >= _recordIntervalSec && _playerMove.IsGrounded())
        {
            _safePosition = transform.position;
            _hasSafePosition = true;
            _recordElapsedSec = 0f;
        }
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
        // 현재 위치와 충분히 떨어진 안전 위치가 있으면 우선 사용한다.
        if (_hasSafePosition &&
            IsSafePositionUsable(_safePosition, transform.position, _minimumEscapeDistance))
        {
            _playerMove.TeleportToPosition(_safePosition, transform.rotation);
            return;
        }

        PlayerSpawner playerSpawner = FindAnyObjectByType<PlayerSpawner>();

        // 사용할 안전 위치가 없으면 기존 현장 스폰 위치 계산을 재사용한다.
        if (playerSpawner != null &&
            playerSpawner.TryGetSiteSpawnPose(out Vector3 position, out Quaternion rotation))
        {
            _playerMove.TeleportToPosition(position, rotation);
            return;
        }

        Debug.LogWarning("긴급탈출 위치를 찾지 못했습니다.");
    }

    private static bool IsSafePositionUsable(Vector3 safePosition, Vector3 currentPosition, float minimumDistance)
    {
        // 제곱 거리를 비교해 불필요한 제곱근 계산을 피한다.
        return (safePosition - currentPosition).sqrMagnitude >= minimumDistance * minimumDistance;
    }

    public void ClearSafePosition()
    {
        _hasSafePosition = false;
        _recordElapsedSec = 0f;
    }
}
