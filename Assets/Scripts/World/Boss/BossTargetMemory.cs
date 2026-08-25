using UnityEngine;
using UnityEngine.AI;

// 마지막으로 본 생존자와 그 위치·달아난 방향을 잠시 기억한다.
//
// 이게 없으면 시야가 끊기는 즉시 추격이 끝난다. 기둥 뒤로 한 발 비킨 것과 완전히 도망친 것이
// 같아져서, 잠깐 숨었다 나오면 보스가 눈앞의 사람을 무시한다.
//
// 수색은 "달아난 방향으로 계속 밀고 나가는" 방식이다. 목격 지점 주변을 무작위로 흩어 돌면
// 왔다 갔다 하는 것처럼 보여서 쫓기는 느낌이 나지 않는다. 사람이 저쪽으로 갔으니 저쪽으로
// 더 들어가 본다는 흐름이어야 자연스럽고, 도망친 쪽에 계속 압박이 걸린다.
public class BossTargetMemory : MonoBehaviour
{
    [Tooltip("마지막으로 본 뒤 이만큼 지나면 잊는다. 이 시간이 곧 수색 시간이다.")]
    [SerializeField, Min(0f)] private float _memoryDuration = 10f;

    [Tooltip("한 번에 앞으로 나아가는 거리. 이만큼씩 전진하며 훑는다.")]
    [SerializeField, Min(1f)] private float _searchStride = 7f;

    [Tooltip("직진 경로에서 좌우로 흔드는 폭. 0이면 완전히 일직선이라 기계적으로 보인다.")]
    [SerializeField, Min(0f)] private float _searchSpread = 3f;

    [Tooltip("앞이 막혔을 때 방향을 이 각도씩 틀어 다시 시도한다. 막힌 자리에서 맴돌지 않게 한다.")]
    [SerializeField, Range(15f, 60f)] private float _blockedTurnAngle = 35f;

    [Tooltip("한 번에 틀 수 있는 최대 각도. 이보다 크게 틀면 왔던 길로 되돌아가 도는 것처럼 보인다.")]
    [SerializeField, Range(30f, 150f)] private float _maxTurnAngle = 100f;

    [Tooltip("수색 지점에 이만큼 가까워지면 다음 지점을 새로 뽑는다.")]
    [SerializeField, Min(0.5f)] private float _searchArriveDistance = 2f;

    // 앞이 막혔을 때 방향을 틀어가며 다시 시도하는 횟수. 한 바퀴를 다 훑을 만큼만 둔다.
    private const int ForwardAttempts = 7;

    // 이보다 조금이라도 움직였으면 그 방향을 달아난 방향으로 본다. 제자리 흔들림은 무시한다.
    private const float MovedThreshold = 0.05f;

    private GameObject _survivor;
    private Vector3 _lastKnownPosition;
    private Vector3 _lastKnownDirection;
    private float _forgetTime;
    private Vector3 _searchPoint;
    private bool _hasSearchPoint;
    private bool _hasPreviousPosition;

    // 수색이 진행되며 앞으로 밀려나는 기준점. 고정된 목격 지점만 기준으로 삼으면 앞이 막힐 때마다
    // 그 자리로 되돌아와 제자리에서 맴돌게 된다.
    private Vector3 _searchOrigin;

    // 표적이 다운되거나 본부로 빠지면 시간이 남았어도 기억을 버린다.
    //
    // 감지(SurvivorRegistry)는 그 사람을 이미 제외하지만 기억은 별도로 남아서, 그냥 두면
    // 추격 가지가 최대 _memoryDuration 동안 유지된다. 그동안 보스가 쓰러진 사람 자리를
    // 왕복하며 시신을 밀어낸다.
    public bool HasMemory
    {
        get
        {
            if (_survivor != null && !SurvivorRegistry.IsActive(_survivor))
            {
                Forget();
            }

            return _survivor != null && Time.time < _forgetTime;
        }
    }

    public GameObject Survivor => _survivor;

    // 감지 조건이 대상을 찾았을 때 부른다. 볼 때마다 기억이 갱신되므로,
    // 계속 보이는 동안에는 기억이 만료되지 않는다.
    public void Record(GameObject survivor)
    {
        if (survivor == null)
        {
            return;
        }

        Vector3 position = survivor.transform.position;

        // 보이는 동안 매 틱 갱신되므로, 위치 변화가 그대로 그 사람이 가던 방향이 된다.
        if (_hasPreviousPosition && survivor == _survivor)
        {
            Vector3 delta = position - _lastKnownPosition;
            delta.y = 0f;
            if (delta.sqrMagnitude > MovedThreshold * MovedThreshold)
            {
                _lastKnownDirection = delta.normalized;
            }
        }

        _survivor = survivor;
        _lastKnownPosition = position;
        _forgetTime = Time.time + _memoryDuration;
        _hasPreviousPosition = true;

        // 다시 보였으면 이전 수색 진행은 의미가 없다. 기준점을 본 자리로 되돌린다.
        _searchOrigin = position;
        _hasSearchPoint = false;
    }

    // 지금 향할 수색 지점. 기준점에서 달아난 방향으로 한 걸음씩 전진하며 이어진다.
    public Vector3 GetSearchPoint()
    {
        if (!_hasSearchPoint)
        {
            _searchPoint = ResolveForwardPoint();
            _hasSearchPoint = true;
            return _searchPoint;
        }

        // 아직 가는 중이면 목표를 바꾸지 않는다. 매번 바꾸면 방향이 흔들려 제자리를 맴돈다.
        if (Vector3.Distance(transform.position, _searchPoint) > _searchArriveDistance)
        {
            return _searchPoint;
        }

        _searchPoint = ResolveForwardPoint();
        return _searchPoint;
    }

    // 지금 기준점에서 진행 방향 앞쪽의 한 점.
    //
    // 앞이 막히면 거리를 줄이는 대신 "방향을 튼다". 거리를 줄이면 결국 기준점으로 수렴해서
    // 제자리에서 맴돌게 되고, 그게 빙글빙글 도는 것처럼 보이는 원인이었다.
    // 성공하면 기준점을 그 자리로 옮겨서 다음 수색이 더 앞에서 시작된다.
    private Vector3 ResolveForwardPoint()
    {
        Vector3 forward = ResolveSearchDirection();

        for (int attempt = 0; attempt < ForwardAttempts; attempt++)
        {
            Vector3 lateral = Vector3.Cross(Vector3.up, forward);
            Vector3 candidate = _searchOrigin
                + forward * _searchStride
                + lateral * Random.Range(-_searchSpread, _searchSpread);

            if (NavMesh.SamplePosition(candidate, out NavMeshHit hit, _searchSpread + 2f, NavMesh.AllAreas))
            {
                // 지금 서 있는 자리와 거의 같으면 이동이 0m로 끝나고 훑어보기만 반복된다.
                // 그게 제자리에서 도는 것처럼 보이는 원인이라 다음 후보로 넘긴다.
                if ((hit.position - transform.position).sqrMagnitude
                    < _searchArriveDistance * _searchArriveDistance)
                {
                    continue;
                }

                // 다음 전진은 여기서부터. 이래야 계속 새 영역으로 나아간다.
                _searchOrigin = hit.position;
                _lastKnownDirection = forward;
                return hit.position;
            }

            // 막혔으면 좌우로 번갈아 각도를 넓혀가며 다른 길을 찾는다.
            // 최대 각도로 제한해서 왔던 길로 되돌아가지 않게 한다. 뒤로 돌면 수색이 아니라
            // 같은 자리를 도는 것으로 보인다.
            float sign = (attempt % 2 == 0) ? 1f : -1f;
            float angle = Mathf.Min(_blockedTurnAngle * ((attempt / 2) + 1), _maxTurnAngle) * sign;
            forward = Quaternion.Euler(0f, angle, 0f) * ResolveSearchDirection();
        }

        // 사방이 막힌 경우. 기준점을 그대로 두고 지금 서 있는 자리를 돌려줘서
        // 이동 노드가 즉시 끝나고 위쪽에서 다시 판단하게 한다.
        return transform.position;
    }

    // 달아난 방향을 모르면(제자리에서 놓친 경우) 보스가 보던 쪽으로 계속 나아간다.
    private Vector3 ResolveSearchDirection()
    {
        if (_lastKnownDirection.sqrMagnitude > 0.0001f)
        {
            return _lastKnownDirection;
        }

        Vector3 fromBoss = _searchOrigin - transform.position;
        fromBoss.y = 0f;
        return fromBoss.sqrMagnitude > 0.0001f ? fromBoss.normalized : transform.forward;
    }

    public void Forget()
    {
        _survivor = null;
        _hasSearchPoint = false;
        _hasPreviousPosition = false;
        _lastKnownDirection = Vector3.zero;
    }

    private void OnDrawGizmosSelected()
    {
        if (!HasMemory)
        {
            return;
        }

        // 기준점에서 진행 방향으로 뻗은 선과, 지금 향하는 지점.
        Gizmos.color = Color.magenta;
        Gizmos.DrawLine(_searchOrigin, _searchOrigin + ResolveSearchDirection() * _searchStride);
        Gizmos.DrawWireSphere(_searchPoint, 0.6f);
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(_lastKnownPosition, 0.8f);
    }
}
