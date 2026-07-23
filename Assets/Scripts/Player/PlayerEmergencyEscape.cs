using Unity.Netcode;
using UnityEngine;

// 최근 안전 위치 기록과 긴급 탈출 위치 결정을 이동 코드에서 분리해 관리한다.
[RequireComponent(typeof(PlayerMoveSample))]
public class PlayerEmergencyEscape : NetworkBehaviour
{
    [SerializeField, Min(0.1f)] private float _stableGroundDuration = 1f;   // 플레이어가 안정적으로 지면에 서 있는 것으로 간주되는 최소 시간 1s
    [SerializeField, Min(0.1f)] private float _minimumRecordDistance = 3f;  // 안전 위치 기록을 갱신하기 위해 플레이어가 이동해야 하는 최소 거리 3M
    [SerializeField, Min(0.1f)] private float _requiredAirborneDuration = 3f;   // 긴급 탈출 요청을 허용하기 위해 플레이어가 공중에 떠 있어야 하는 최소 시간 3s

    private PlayerMoveSample _playerMove;
    private Pose _roundSpawnPose;
    private Pose _lastSafePose;
    private Pose _previousSafePose;
    private bool _hasRoundSpawnPose;    // 라운드 시작 시 서버가 확정한 스폰 위치
    private bool _hasLastSafePose;      // 최근 안정적인 지면 위치 기록
    private bool _hasPreviousSafePose;  // 최근 안정적인 지면 위치 기록 이전의 기록
    private float _stableGroundElapsed; // 플레이어가 안정적으로 지면에 서 있는 것으로 간주되는 시간 누적
    private float _airborneElapsed;     // 플레이어가 공중에 떠 있는 것으로 간주되는 시간 누적

    private void Awake()
    {
        _playerMove = GetComponent<PlayerMoveSample>();
    }

    private void FixedUpdate()
    {
        if (!IsSpawned)
        {
            return;
        }

        bool isGrounded = _playerMove.IsGrounded();

        if (isGrounded)
        {
            _airborneElapsed = 0f;
        }
        else
        {
            _airborneElapsed += Time.fixedDeltaTime;
        }

        if (!IsServer)
        {
            return;
        }

        if (!isGrounded)
        {
            _stableGroundElapsed = 0f;
            return;
        }

        _stableGroundElapsed += Time.fixedDeltaTime;

        if (_stableGroundElapsed < _stableGroundDuration)
        {
            return;
        }

        _stableGroundElapsed = 0f;
        TryRecordSafePose(transform.position, transform.rotation);
    }

    // 라운드마다 서버가 확정한 스폰 위치를 최종 복귀 위치로 기록한다.
    public void RecordRoundSpawnPose(Vector3 position, Quaternion rotation)
    {
        if (!IsServer)
        {
            return;
        }

        _roundSpawnPose = new Pose(position, rotation);
        _hasRoundSpawnPose = true;
        _lastSafePose = _roundSpawnPose;
        _hasLastSafePose = true;
        _hasPreviousSafePose = false;
        _stableGroundElapsed = 0f;
        _airborneElapsed = 0f;
    }

    public bool RequestEmergencyEscape()
    {
        // 각 클라이언트는 자신이 소유한 플레이어만 탈출 요청을 보낼 수 있다.
        if (!IsOwner || _airborneElapsed < _requiredAirborneDuration)
        {
            return false;
        }

        RequestEmergencyEscapeRpc();
        return true;
    }

    [Rpc(SendTo.Server)]
    private void RequestEmergencyEscapeRpc()    // 클라이언트가 서버에 긴급 탈출 요청을 보낸다.
    {
        if (_playerMove.IsGrounded() ||
            _airborneElapsed < _requiredAirborneDuration)
        {
            return;
        }

        // 서버는 클라이언트가 요청한 긴급 탈출을 승인하고, 승인된 탈출 위치로 플레이어를 이동시킨다.
        if (_hasPreviousSafePose)
        {
            TeleportToPose(_previousSafePose);
            _lastSafePose = _previousSafePose;
            _hasLastSafePose = true;
            _hasPreviousSafePose = false;
            return;
        }

        if (_hasLastSafePose)
        {
            TeleportToPose(_lastSafePose);
            return;
        }

        if (_hasRoundSpawnPose)
        {
            TeleportToPose(_roundSpawnPose);
            return;
        }

        Debug.LogWarning("긴급탈출 위치를 찾지 못했습니다.", this);
    }

    // 플레이어가 안정적인 지면에 서 있는 위치를 기록한다. 이전 기록과의 거리가 최소 기록 거리보다 가까우면 갱신하지 않는다.
    private void TryRecordSafePose(Vector3 position, Quaternion rotation)
    {
        if (_hasLastSafePose)
        {
            float minimumDistanceSqr = _minimumRecordDistance * _minimumRecordDistance;

            if ((position - _lastSafePose.position).sqrMagnitude < minimumDistanceSqr)
            {
                return;
            }

            _previousSafePose = _lastSafePose;
            _hasPreviousSafePose = true;
        }

        _lastSafePose = new Pose(position, rotation);
        _hasLastSafePose = true;
    }

    private void TeleportToPose(Pose pose)
    {
        _stableGroundElapsed = 0f;
        _airborneElapsed = 0f;
        _playerMove.TeleportToPosition(pose.position, pose.rotation);
    }
}
