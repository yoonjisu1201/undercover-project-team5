using UnityEngine;
using UnityEngine.AI;

// 사람이 아니라 "흔적"(마지막으로 확인된 자리)을 쫓는다. 눈으로 본 자리와 소리가 난 자리 둘 다.
//
// 흔적까지 간다 -> 없으면 주변을 정해진 횟수만큼 뒤진다 -> 포기하고 한 박자 선다 -> 훑고 다닌다.
//
// 소리는 간격(_noiseTraceInterval)을 두고만 받는다. 0.4초마다 오는 소음을 그대로 받으면
// 흔적이 사람을 실시간으로 따라다녀서 벽 너머까지 정확히 쫓아온다.
//
// 수색 범위도 흔적 반경 안으로 묶는다. 달아난 방향으로 계속 밀고 나가면 지도 끝까지 쫓게 된다.
public class BossTargetMemory : MonoBehaviour
{
    [Tooltip("마지막으로 '본' 뒤 이만큼 지나면 잊는다. 추격 전체의 상한이고, "
        + "소리를 아무리 많이 내도 이 시간은 늘어나지 않는다. "
        + "수색 속도(2.8m/s)로 이 시간 동안 갈 수 있는 거리가 곧 추격 사거리다. "
        + "2.5초 아래로 내리면 흔적까지 닿기 전에 잊어버려서, 흔적 주변 수색이 아예 일어나지 않는다.")]
    [SerializeField, Min(0f)] private float _memoryDuration = 4f;

    [Tooltip("소리로 생긴 흔적의 수명(초). 소리 난 곳까지 걸어갈 시간은 줘야 한다. "
        + "눈으로 본 흔적과 따로 두는 이유는, 소리는 사람을 직접 본 것이 아니라 확신이 약하기 때문이다.")]
    [SerializeField, Min(0f)] private float _noiseMemoryDuration = 6f;

    [Tooltip("소리로 흔적을 옮기는 최소 간격(초). 짧으면 흔적이 사람의 현재 위치를 "
        + "실시간으로 따라다녀서, 보스가 벽 너머로 정확히 쫓아온다.")]
    [SerializeField, Min(0f)] private float _noiseTraceInterval = 2f;

    [Tooltip("청각 배율. 소음마다 정해진 '들리는 거리'에 이 값을 곱한다.")]
    [SerializeField, Min(0f)] private float _hearingFactor = 1f;

    [Tooltip("흔적에 이만큼 가까워지면 도착으로 보고 주변 수색으로 넘어간다.")]
    [SerializeField, Min(0.5f)] private float _arriveDistance = 2f;

    [Tooltip("흔적을 중심으로 이 반경 안만 뒤진다. 흔적에서 멀어지지 않는 것이 이 값의 목적이다. "
        + "시야(7m)에 가깝게 잡으면 안 된다. 흔적 주변을 뒤진다기보다 방 하나를 통째로 훑는 것이 "
        + "되어서, 숨어 있는 사람 위로 우연히 걸어가는 일이 잦아진다.")]
    [SerializeField, Min(1f)] private float _searchRadius = 5f;

    [Tooltip("주변을 몇 군데나 뒤져보고 포기할지.")]
    [SerializeField, Min(1)] private int _searchPointCount = 4;

    [Header("포기 / 기본 수색")]
    [Tooltip("포기하고 제자리에 서 있는 시간(초). 길면 굳은 것처럼 보이니 한 박자만 준다.")]
    [SerializeField, Min(0f)] private float _restDuration = 1.5f;

    [Tooltip("훑고 다닐 때 한 번에 나아가는 거리(m). 이 거리마다 갈 곳을 새로 정한다. "
        + "실제로 걷는 거리는 여기서 도착 판정 거리를 뺀 만큼이라, 도착 판정(기본 2m)보다 "
        + "충분히 크게 잡아야 한다. 비슷하게 잡으면 몇십 cm 걷고 다시 목적지를 뽑아 "
        + "제자리에서 찔끔거린다.")]
    [SerializeField, Min(1f)] private float _wanderStepMin = 6f;

    [SerializeField, Min(1f)] private float _wanderStepMax = 12f;

    // 지점 후보를 몇 번까지 뽑아볼지. 좁은 방에서는 대부분 첫 시도에 걸린다.
    private const int SamplingAttempts = 8;

    // 후보 좌표를 NavMesh 위로 끌어당길 때 허용하는 거리. 문 하나 폭 정도.
    private const float NavSampleRadius = 3f;

    // 평소에 진행 방향에서 트는 각도(±). 이 안에서만 고르면 왔던 길로 되돌아가지 않는다.
    private const float TurnSpread = 60f;

    // 앞이 막혔을 때 시도마다 넓히는 각도. 막다른 곳에서는 결국 돌아 나올 수 있어야 한다.
    private const float BlockedTurnStep = 45f;

    // 소음을 확인하는 간격(초). 매 프레임 볼 필요가 없다.
    private const float NoiseCheckInterval = 0.25f;

    // 흔적이 이보다 조금이라도 옮겨가면 그 방향을 사람이 가던 방향으로 본다. 제자리 흔들림은 무시한다.
    private const float MovedThreshold = 0.3f;

    // 수명·갱신 규칙은 전부 이쪽이 판단한다. 여기 남는 것은 좌표와 수색 진행뿐이다.
    private TraceLifetime _lifetime;

    private GameObject _survivor;
    private Vector3 _trace;
    private bool _hasTrace;

    // 흔적까지 갔는지. 도착하기 전에는 흔적 자체가 목적지고, 도착한 뒤부터 주변 수색이 시작된다.
    private bool _reachedTrace;

    // 흔적이 옮겨간 방향 = 사람이 가던 방향. 수색을 그 앞쪽부터 하기 위해 들고 있는다.
    private Vector3 _traceDirection;

    private Vector3 _searchPoint;
    private bool _hasSearchPoint;
    private int _searchPointsLeft;

    private float _restUntil;
    private float _nextNoiseCheckTime;
    private Vector3 _wanderPoint;
    private bool _hasWanderPoint;

    // 지금 나아가고 있는 방향. 다음 지점을 이 방향 기준으로 골라서 왔다 갔다 하지 않게 한다.
    private Vector3 _heading;

    private void Awake()
    {
        // 눈으로 본 직후 0.5초는 소리가 흔적을 덮지 않는다. 소음은 몇 초 전 자리라,
        // 보이는 동안 끼어들면 흔적이 뒤로 끌려간다.
        _lifetime = new TraceLifetime(_memoryDuration, _noiseMemoryDuration, _noiseTraceInterval, 0.5f);
    }

    // 흔적이 살아 있는지. 그래프의 추격 가지가 이 값으로 묶여 있다.
    //
    // 만료와 표적 이탈을 여기서 함께 처리한다. 조건 노드가 매 주기 물어보는 유일한 통로라
    // 별도 감시 없이도 포기 시점을 놓치지 않는다.
    //
    // 표적이 다운되거나 본부로 빠지면 시간이 남았어도 버린다. 그냥 두면 보스가 쓰러진 사람
    // 자리를 왕복하며 시신을 밀어낸다.
    public bool HasMemory
    {
        get
        {
            if (!_hasTrace)
            {
                return false;
            }

            if (!_lifetime.IsAlive(Time.time) ||
                (_survivor != null && !SurvivorRegistry.IsActive(_survivor)))
            {
                GiveUp();
                return false;
            }

            return true;
        }
    }

    // 포기한 직후 잠깐 서 있는 중인지. 그래프의 휴식 가지가 이 값으로 묶여 있다.
    public bool IsResting => Time.time < _restUntil;

    // 마지막으로 확인된 사람. 흔적을 따라가는 데는 쓰지 않고, 그 사람이 쓰러졌는지만 확인한다.
    public GameObject Survivor => _survivor;

    // 지금 쫓고 있는 흔적의 자리. 디버그 표시가 읽는다.
    public Vector3 Trace => _trace;

    // 이 흔적이 어디서 왔는지. 표시용이고 판정에는 쓰지 않는다.
    public string TraceSource => _lifetime.FromNoise ? "소리" : "시야";

    // 디버그 표시용. 흔적 수명과 소리 갱신 제한이 얼마나 남았는지.
    public float TraceRemaining => _lifetime.RemainingSeconds(Time.time);
    public float NoiseGateRemaining => _lifetime.NoiseGateRemaining(Time.time);
    public float RestRemaining => Mathf.Max(0f, _restUntil - Time.time);

    // 추적이 어디까지 왔는지. 디버그 표시용이라 상태를 바꾸지 않는다.
    public string SearchProgress
        => _reachedTrace ? $"주변 수색 {_searchPointsLeft}회 남음" : "흔적으로 이동 중";

    // 감지 조건이 대상을 찾았을 때 부른다. 보이는 동안 매 주기 갱신되므로 기억이 만료되지 않는다.
    public void Record(GameObject survivor)
    {
        if (survivor == null)
        {
            return;
        }

        _survivor = survivor;
        _lifetime.RecordSight(Time.time);
        SetTrace(survivor.transform.position);
    }

    // 소리를 단서로 받는다. 서버에서만 소음 목록이 채워지므로 클라이언트에서는 저절로 아무 일도 없다.
    //
    // 그래프 노드가 아니라 여기서 직접 확인한다. 소리든 시야든 "단서"라는 점에서 같고,
    // 둘을 한곳에서 흔적으로 모아야 보스가 쫓을 대상이 하나로 유지된다. 나뉘어 있으면
    // 소리 쪽으로 갔다가 오래된 목격 지점으로 되돌아가는 왕복이 생긴다.
    private void Update()
    {
        if (Time.time < _nextNoiseCheckTime)
        {
            return;
        }

        _nextNoiseCheckTime = Time.time + NoiseCheckInterval;

        if (NoiseSystem.TryGetLoudest(transform.position, _hearingFactor, out Vector3 noise))
        {
            RecordNoise(noise);
        }
    }

    // 소리 난 자리를 흔적으로 남긴다. 흔적이 없으면 새로 만들고, 있으면 옮긴다.
    //
    // 간격 제한이 핵심이다. 이게 없으면 사람이 0.4초마다 내는 소음이 그대로 흔적이 되어
    // 실시간 추적이 된다. 제한을 두면 보스가 향하는 곳은 늘 몇 초 전 자리다.
    private void RecordNoise(Vector3 position)
    {
        // 옮겨도 되는지(시야 우선 / 간격 제한)는 전부 TraceLifetime 이 판단한다.
        if (_lifetime.TryRecordNoise(Time.time))
        {
            SetTrace(position);
        }
    }

    // 지금 향할 지점. 흔적에 닿기 전에는 흔적, 닿은 뒤에는 그 주변의 수색 지점이다.
    public Vector3 GetSearchPoint()
    {
        Vector3 position = transform.position;

        // 1단계. 사람이 A 구간까지 갔으면 A 구간까지는 따라간다.
        if (!_reachedTrace)
        {
            if (FlatDistance(position, _trace) > _arriveDistance)
            {
                return _trace;
            }

            _reachedTrace = true;
            _hasSearchPoint = false;
        }

        // 2단계. 가는 중이면 목표를 바꾸지 않는다. 매번 바꾸면 방향이 흔들려 제자리를 맴돈다.
        if (_hasSearchPoint && FlatDistance(position, _searchPoint) > _arriveDistance)
        {
            return _searchPoint;
        }

        // 정해진 횟수를 다 뒤졌으면 포기한다.
        if (_searchPointsLeft <= 0)
        {
            GiveUp();
            return position;
        }

        _searchPointsLeft--;
        _searchPoint = PickPointAroundTrace();
        _hasSearchPoint = true;
        return _searchPoint;
    }

    // 단서가 없을 때 보스가 향할 지점. 이게 보스의 기본 이동 방식이다.
    //
    // 정해진 목적지(방 중앙 웨이포인트)를 순서대로 도는 방식을 걷어내고 이걸로 대체했다.
    // 목적지가 미리 정해져 있으면 보스는 그 점까지 한 번에 걸어가 버려서, 가는 길에 아무것도
    // 살피지 않는다. 마침 그 직선이 숨은 사람 쪽이면 증거 없이 정확히 찾아오는 것처럼 보인다.
    //
    // 대신 지금 서 있는 자리에서 진행 방향으로 한 걸음씩만 정한다. 멀리 있는 목표가 없으니
    // 어디로 갈지는 매번 새로 정해지고, 결과적으로 훑고 다니는 모양이 된다.
    public Vector3 GetWanderPoint()
    {
        Vector3 position = transform.position;

        // 가는 중이면 목표를 바꾸지 않는다.
        if (_hasWanderPoint && FlatDistance(position, _wanderPoint) > _arriveDistance)
        {
            return _wanderPoint;
        }

        _wanderPoint = PickWanderPoint();
        _hasWanderPoint = true;
        return _wanderPoint;
    }

    // 흔적을 버리고 한 박자 선 뒤 훑고 다닌다. 수색을 다 하고도 못 찾았을 때와
    // 표적이 사라졌을 때 부른다.
    public void GiveUp()
    {
        _survivor = null;
        _hasTrace = false;
        _lifetime.Clear();
        _traceDirection = Vector3.zero;
        _reachedTrace = false;
        _hasSearchPoint = false;
        _searchPointsLeft = 0;

        _restUntil = Time.time + _restDuration;
        _hasWanderPoint = false;

        // 훑기는 지금 보고 있는 쪽에서 이어간다. 포기하자마자 뒤로 도는 것을 막는다.
        _heading = transform.forward;
    }

    // 만료 시각(_forgetTime)은 Record 가 정한다. 흔적은 "본 자리"이므로 둘 다 눈만 갱신한다.
    private void SetTrace(Vector3 position)
    {
        // 흔적이 옮겨간 방향이 곧 사람이 간 방향이다. 눈으로 따라갈 때든 소리가 이어질 때든 같다.
        if (_hasTrace)
        {
            Vector3 delta = position - _trace;
            delta.y = 0f;
            if (delta.sqrMagnitude > MovedThreshold * MovedThreshold)
            {
                _traceDirection = delta.normalized;
            }
        }

        _trace = position;
        _hasTrace = true;

        // 새 흔적이 생겼으면 이전 수색 진행은 의미가 없다. 처음부터 다시 쫓는다.
        _reachedTrace = false;
        _hasSearchPoint = false;
        _searchPointsLeft = _searchPointCount;

        // 쫓을 것이 생겼으니 휴식은 끝난다.
        _restUntil = 0f;
        _hasWanderPoint = false;
    }

    // 흔적 주변의 한 점. 사람이 가던 방향 앞쪽부터 뒤진다.
    //
    // 예전에는 흔적을 중심으로 아무 방향이나 뽑고 나서 "너무 뒤로 돌지 않았나"를 검사했다.
    // 그런데 무작위로 뽑으면 앞쪽 후보가 잘 안 나와서 검사에 계속 걸리고, 걸릴 때마다 허용
    // 각도가 넓어져 결국 뒤쪽이 뽑혔다. 사람이 저쪽으로 갔는데 보스가 반대편을 뒤지는 것이
    // 그래서 생겼다. 그래서 검사로 거르지 않고 처음부터 그 방향으로 뽑는다.
    //
    // 반경(_searchRadius)은 그대로 지킨다. 방향만 보고 계속 밀고 나가면 흔적을 뒤지는 게 아니라
    // 사람을 지도 끝까지 쫓는 것이 된다. 예전 방식이 그랬다.
    private Vector3 PickPointAroundTrace()
    {
        Vector3 position = transform.position;
        Vector3 forward = ResolveTraceDirection();

        for (int attempt = 0; attempt < SamplingAttempts; attempt++)
        {
            // 처음에는 가던 방향 앞쪽(±60도)만 본다. 막히면 각도를 넓혀 결국 뒤까지 본다.
            Vector3 direction = Quaternion.Euler(0f, RandomTurn(attempt), 0f) * forward;
            Vector3 candidate = _trace + direction * Random.Range(_searchRadius * 0.4f, _searchRadius);

            if (!NavMesh.SamplePosition(candidate, out NavMeshHit hit, NavSampleRadius, NavMesh.AllAreas))
            {
                continue;
            }

            // 지금 서 있는 자리와 거의 같으면 이동이 0m로 끝나고 훑어보기만 반복된다.
            if (FlatDistance(hit.position, position) < _arriveDistance)
            {
                continue;
            }

            UpdateHeading(position, hit.position);
            return hit.position;
        }

        // 주변이 전부 막혔으면 흔적으로 되돌아간다. 적어도 제자리 반복은 아니다.
        return _trace;
    }

    // 사람이 가던 방향. 모르면(한 자리에서 놓친 경우) 보스가 흔적으로 다가온 방향을 그대로
    // 이어간다. 저쪽에서 와서 여기서 사라졌으면 계속 저쪽으로 갔다고 보는 것이 자연스럽다.
    private Vector3 ResolveTraceDirection()
    {
        if (_traceDirection.sqrMagnitude > 0.0001f)
        {
            return _traceDirection;
        }

        Vector3 approach = _trace - transform.position;
        approach.y = 0f;
        return approach.sqrMagnitude > 0.0001f ? approach.normalized : ResolveHeading();
    }

    // 진행 방향 앞쪽의 한 점. 기준점이 없으므로 지금 자리에서 뻗어 나간다.
    //
    // 실제로 걷는 거리는 (뽑힌 거리 - 도착 판정 거리)다. 후보가 코앞이면 몇십 cm 걷고 곧바로
    // 다음 지점을 새로 뽑게 되어, 밖에서 보면 제자리에서 찔끔거리는 것으로 보인다.
    // 그래서 최소 걸음(_wanderStepMin) 이상 떨어진 후보만 받는다.
    private Vector3 PickWanderPoint()
    {
        Vector3 position = transform.position;
        Vector3 heading = ResolveHeading();

        // 다 실패했을 때를 대비해 그나마 가장 멀리 떨어진 후보를 들고 있는다.
        Vector3 farthest = position;
        float farthestDistance = 0f;

        for (int attempt = 0; attempt < SamplingAttempts; attempt++)
        {
            Vector3 direction = Quaternion.Euler(0f, RandomTurn(attempt), 0f) * heading;
            Vector3 candidate = position + direction * Random.Range(_wanderStepMin, _wanderStepMax);

            if (!NavMesh.SamplePosition(candidate, out NavMeshHit hit, NavSampleRadius, NavMesh.AllAreas))
            {
                continue;
            }

            // NavMesh 로 끌려오면서 코앞으로 당겨질 수 있다.
            float distance = FlatDistance(hit.position, position);
            if (distance > farthestDistance)
            {
                farthest = hit.position;
                farthestDistance = distance;
            }

            if (distance < _wanderStepMin)
            {
                continue;
            }

            UpdateHeading(position, hit.position);
            return hit.position;
        }

        // 좁은 방이라 최소 걸음을 채우는 후보가 없었다. 그래도 도착 판정보다는 멀어야
        // 한 걸음이라도 걷는다. 그마저도 없으면 제자리를 돌려주고 위에서 다시 판단하게 한다.
        if (farthestDistance > _arriveDistance)
        {
            UpdateHeading(position, farthest);
            return farthest;
        }

        return position;
    }

    // 이번 시도에서 진행 방향으로부터 틀어볼 각도.
    //
    // 매번 아무 방향이나 고르면 왔던 길로 되돌아갔다가 다시 돌아서기를 반복한다. 이동 거리는
    // 짧은데 회전만 계속 일어나서, 밖에서 보면 제자리에서 빙빙 도는 것으로 보인다.
    // 그래서 평소에는 앞쪽 ±60° 안에서만 고르고, 막혔을 때만 시도마다 넓혀서 결국 뒤까지 본다.
    private static float RandomTurn(int attempt)
    {
        float spread = Mathf.Min(TurnSpread + BlockedTurnStep * attempt, 180f);
        return Random.Range(-spread, spread);
    }

    private Vector3 ResolveHeading()
        => _heading.sqrMagnitude > 0.0001f ? _heading : transform.forward;

    private void UpdateHeading(Vector3 from, Vector3 to)
    {
        Vector3 moved = to - from;
        moved.y = 0f;

        if (moved.sqrMagnitude > 0.0001f)
        {
            _heading = moved.normalized;
        }
    }

    // 높이는 무시한다. NavMeshAgent가 움직이는 평면과 기준을 맞춰야 도착 판정이 어긋나지 않는다.
    private static float FlatDistance(Vector3 a, Vector3 b)
    {
        a.y = 0f;
        b.y = 0f;
        return Vector3.Distance(a, b);
    }

    private void OnDrawGizmosSelected()
    {
        if (_hasTrace)
        {
            // 흔적과 그 주변 수색 반경, 지금 향하는 지점.
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(_trace, 0.8f);
            Gizmos.DrawWireSphere(_trace, _searchRadius);

            // 사람이 가던 방향. 수색은 이 앞쪽부터 이루어진다.
            Gizmos.color = new Color(1f, 0.5f, 0f);
            Vector3 direction = ResolveTraceDirection();
            Gizmos.DrawLine(_trace, _trace + direction * _searchRadius);
            Gizmos.DrawWireSphere(_trace + direction * _searchRadius, 0.4f);

            if (_hasSearchPoint)
            {
                Gizmos.color = Color.magenta;
                Gizmos.DrawWireSphere(_searchPoint, 0.6f);
                Gizmos.DrawLine(_trace, _searchPoint);
            }

            return;
        }

        if (_hasWanderPoint)
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(_wanderPoint, 0.6f);
            Gizmos.DrawLine(transform.position, _wanderPoint);
        }
    }
}
