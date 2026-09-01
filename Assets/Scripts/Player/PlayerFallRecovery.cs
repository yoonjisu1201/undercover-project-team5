using System;
using UnityEngine;
using UnityEngine.AI;

// 맵 밖으로 떨어졌을 때 되돌릴 자리를 들고 있는다.
//
// 밀려나서 벽을 통과하면 스스로 돌아올 방법이 없다. 조작으로 올라올 수 없고,
// NetworkTransform 이 소유자 권한이라 서버가 교정해 주지도 않는다. 낙사하거나 라운드가
// 끝날 때까지 갇힌다. 통과 자체를 막는 것은 넉백 쪽에서 하고, 여기는 그래도 뚫렸을 때의
// 안전망이다.
//
// 옮기는 것은 직접 하지 않는다. 순간이동은 카메라와 속도까지 함께 손봐야 해서 이미
// PlayerMoveSample 이 하고 있고, 여기서 또 하면 같은 일이 두 군데로 갈린다.
[Serializable]
public class PlayerFallRecovery
{
    // 떨어졌는지 확인하는 간격(초).
    private const float CheckInterval = 0.5f;

    // 되돌리는 조건. 이 시간 동안 계속 떨어지고, 안전 지점보다 이만큼 아래여야 한다.
    private const float FallSeconds = 1.5f;
    private const float FallDepth = 5f;

    // 안전 지점을 찾을 때 NavMesh 위로 끌어당기는 거리(m).
    private const float SampleRadius = 1.5f;

    private Transform _owner;
    private Rigidbody _rigidbody;

    private Vector3 _safePosition;
    private bool _hasSafePosition;
    private float _lastCheckTime;
    private float _fallingSeconds;

    public void Initialize(Transform owner, Rigidbody rigidbody)
    {
        _owner = owner;
        _rigidbody = rigidbody;
    }

    // 정상 이동으로 옮겨갔을 때 부른다. 옮겨간 곳은 다른 구역이라 이전 자리가 의미가 없다.
    // 그대로 두면 지하에서 기억한 자리가 지상까지 따라와서, 나간 사람을 도로 끌어내린다.
    public void Forget()
    {
        _hasSafePosition = false;
        _fallingSeconds = 0f;
    }

    // 되돌려야 하면 true. 안전망이 정상 이동을 되돌리는 일이 있어서는 안 되므로,
    // 공중에 떠 있고 계속 아래로 떨어지고 기억해 둔 자리보다 한참 아래일 때만이다.
    public bool NeedsRecovery(bool grounded, out Vector3 safePosition)
    {
        safePosition = _safePosition;

        float elapsed = Time.time - _lastCheckTime;
        if (elapsed < CheckInterval)
        {
            return false;
        }

        _lastCheckTime = Time.time;

        Vector3 position = _owner.position;

        // 바닥을 딛고 NavMesh 위에 있으면 그 자리를 기억해 둔다.
        if (grounded &&
            NavMesh.SamplePosition(position, out NavMeshHit hit, SampleRadius, NavMesh.AllAreas))
        {
            _safePosition = hit.position;
            _hasSafePosition = true;
            _fallingSeconds = 0f;
            return false;
        }

        // 아래로 떨어지는 중일 때만 센다. 점프해서 올라가는 중이거나 떠 있기만 하면 아니다.
        if (_rigidbody.linearVelocity.y >= 0f)
        {
            _fallingSeconds = 0f;
            return false;
        }

        _fallingSeconds += elapsed;

        safePosition = _safePosition;

        return _hasSafePosition
            && _fallingSeconds >= FallSeconds
            && position.y < _safePosition.y - FallDepth;
    }
}
