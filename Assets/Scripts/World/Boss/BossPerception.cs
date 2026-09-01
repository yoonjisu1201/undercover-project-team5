using UnityEngine;
using UnityEngine.AI;

// 보스의 시각 판정. 조건 노드(Sees Survivor / Survivor Is Near)가 여기에 물어본다.
//
// 서버에서만 의미가 있다. 보스의 Behavior 그래프는 서버에서만 돌기 때문에 별도 방어를 두지 않는다.
// 소리는 NoiseSystem이 따로 담당한다. 시각과 청각을 한 컴포넌트에 합치지 않는 이유는,
// 소음은 "발생 시점에 보고"하는 푸시 구조고 시야는 "물어볼 때 계산"하는 풀 구조라 수명이 다르기 때문이다.
public class BossPerception : MonoBehaviour
{
    [Header("시야")]
    [Tooltip("보스 눈높이. 바닥에서 레이를 쏘면 문턱이나 잡동사니에 막힌다.")]
    [SerializeField, Min(0f)] private float _eyeHeight = 1.6f;

    [SerializeField, Min(0f)] private float _sightRange = 18f;

    [Tooltip("시야각 전체(도). 100이면 좌우 50도씩.")]
    [SerializeField, Range(1f, 360f)] private float _sightAngle = 100f;

    [Tooltip("시야를 막는 레이어. 플레이어와 보스 자신은 빼야 한다.")]
    [SerializeField] private LayerMask _sightBlockers = ~0;

    [Header("표적 선택")]
    [Tooltip("현재 표적보다 이만큼 더 가까운 사람이 나타날 때만 표적을 바꾼다. "
        + "0이면 항상 최근접으로 바뀌어 두 사람 사이에서 표적이 흔들린다.")]
    [SerializeField, Min(0f)] private float _targetSwitchMargin = 3.5f;

    [Header("표적 전환")]
    [Tooltip("한 사람을 이만큼 오래 쫓은 뒤부터 표적을 바꿔볼 수 있다. "
        + "이 시간이 지나야 주사위가 굴러가므로, 실제 전환은 여기에 몇 번의 주사위 시간이 더 붙는다.")]
    [SerializeField, Min(0f)] private float _switchAfterSeconds = 4f;

    [Tooltip("표적을 바꿀지 주사위를 굴리는 간격(초). 짧을수록 조건이 맞은 뒤 빨리 바뀐다.")]
    [SerializeField, Min(0.5f)] private float _switchRollInterval = 1.5f;

    [Tooltip("굴릴 때마다 바뀔 확률. 1이면 조건이 맞는 즉시 바뀐다.")]
    [SerializeField, Range(0f, 1f)] private float _switchChance = 0.35f;

    [Tooltip("표적을 바꾼 뒤 직전 사람에게 이 시간 동안은 돌아가지 않는다. "
        + "위의 '표적 유지 시간'보다 커야 의미가 있다 - 작으면 유지 시간이 먼저 막아서 효과가 없다.")]
    [SerializeField, Min(0f)] private float _switchBackCooldown = 10f;


    [Header("봐주기")]
    [Tooltip("막다른 곳에 몰린 사람을 이 확률로 못 본 척 지나친다. 0이면 끈다.")]
    [SerializeField, Range(0f, 1f)] private float _mercyChance = 0.15f;

    [Tooltip("봐주는 동안 감각이 둔해져 있는 시간(초).")]
    [SerializeField, Min(0f)] private float _mercySeconds = 6f;

    [Tooltip("봐주는 동안 시야·근접 감지 사거리에 곱하는 값. 0으로 두지 말 것 - "
        + "눈앞의 사람도 못 보게 되어 심장 박동까지 꺼진다.")]
    [SerializeField, Range(0.05f, 1f)] private float _mercySenseMultiplier = 0.3f;

    [Tooltip("한 번 봐준 뒤 다시 봐주기까지의 최소 간격(초). 자주 나오면 규칙처럼 읽힌다.")]
    [SerializeField, Min(0f)] private float _mercyCooldown = 60f;

    [Tooltip("사람 주위를 이 반경으로 훑어 도망갈 길이 있는지 본다.")]
    [SerializeField, Min(1f)] private float _corneredCheckRadius = 6f;

    [Tooltip("보스 반대쪽으로 갈 수 있는 길이 이 개수 이하면 몰린 것으로 본다.")]
    [SerializeField, Range(0, 6)] private int _corneredEscapeCount = 2;

    [Header("근접 감지")]
    [Tooltip("이 거리 안이면 보고 있지 않아도 알아챈다. 시야각을 무시하므로 등 뒤도 걸린다.")]
    [SerializeField, Min(0f)] private float _senseRadius = 14f;

    // 표적의 몸을 위아래로 훑는 지점들(발밑 기준 높이).
    //
    // 한 점만 겨누면 하필 그 높이에 무엇이 걸렸느냐로 판정이 갈린다. 가슴 한 점만 보던 때는
    // 낮은 턱 하나에 몸 전체가 가려진 것으로 처리되고, 반대로 발밑이 훤히 드러나 있어도
    // 가슴만 트였으면 보이는 것으로 처리됐다. 머리·가슴·무릎을 함께 보고 하나라도 트여 있으면
    // 보이는 것으로 친다.
    //
    // 판정의 한계는 높이가 아니라 거리(_sightRange)다. 위아래는 전부 훑고, 얼마나 멀리까지
    // 보이는지만 거리로 자른다.
    private static readonly float[] BodySampleHeights = { 1.7f, 1.1f, 0.5f };

    // 시야 판정은 매 그래프 틱마다 인원수만큼 돌아서, 그때마다 배열을 새로 만들면 GC가 쌓인다.
    private readonly RaycastHit[] _sightHits = new RaycastHit[16];

    // 개발용 진단 문자열 버퍼. 표시가 켜져 있는 동안 매번 새로 만들지 않게 재사용한다.
    private readonly System.Text.StringBuilder _sampleText = new();

    // 프레임 단위 캐시. 같은 프레임에 같은 판정을 다시 계산하지 않는다.
    private int _visibleFrame = -1;
    private bool _visibleResult;
    private GameObject _visibleSurvivor;
    private int _nearbyFrame = -1;
    private bool _nearbyResult;
    private GameObject _nearbySurvivor;

    // 지금 붙잡고 있는 표적. 시야·근접 어느 쪽으로 잡았든 보스는 한 명만 쫓는다.
    private GameObject _lockedTarget;
    private float _lockedSince;

    // 마지막으로 표적을 잡은 시각. 감지가 끊겼다 다시 잡힌 것을 가려낸다.
    private float _lastLockTime = float.NegativeInfinity;

    // 직전에 놓아준 표적과 그쪽으로 돌아가지 않을 기한.
    private GameObject _previousTarget;
    private float _previousTargetUntil;


    // 확률로 동작하는 두 규칙. 굴리는 간격과 성공 뒤 잠금을 여기가 들고 있다.
    private ChanceGate _switchGate;
    private ChanceGate _mercyGate;

    // 감각이 둔해져 있는 기한. 특정 사람을 지목하지 않고 사거리 자체를 줄인다.
    private float _mercyUntil;

    // 몰렸는지 볼 때 훑는 방향 수. 8이면 45도 간격.
    private const int CorneredSamples = 8;

    private Vector3 EyePosition => transform.position + Vector3.up * _eyeHeight;

    private void Awake()
    {
        // 표적 전환은 성공해도 잠그지 않는다. 다음 굴림까지의 간격만으로 충분히 드물다.
        _switchGate = new ChanceGate(_switchRollInterval, 0f);

        // 봐주기는 성공 뒤 오래 잠근다. 자주 나오면 규칙처럼 읽혀서 역이용된다.
        // 굴리는 간격(2초)은 공격 시도마다 굴려서 쿨다운이 무의미해지는 것을 막는다.
        _mercyGate = new ChanceGate(2f, _mercyCooldown);
    }

    // 디버그 표시용. 지금 표적을 얼마나 오래 붙잡고 있는지와 두 규칙의 남은 시간.
    public float TargetHeldSeconds => _lockedTarget == null ? 0f : Time.time - _lockedSince;
    public float SwitchRollRemaining => _switchGate?.RollRemaining(Time.time) ?? 0f;
    public float MercyLockRemaining => _mercyGate?.LockRemaining(Time.time) ?? 0f;
    public bool IsMercyActive => Time.time < _mercyUntil;

    // 보고 있는지·들리는지를 따지지 않고 거리만 본다. 잠들어 있는 보스를 깨우는 데 쓴다.
    // 등 뒤로 몰래 지나가도 깨어나야 하므로 시야 판정을 넣지 않는다.
    public bool IsSurvivorWithin(float radius)
    {
        float radiusSqr = radius * radius;

        foreach (PlayerHealth candidate in SurvivorRegistry.Active())
        {
            if ((candidate.transform.position - transform.position).sqrMagnitude <= radiusSqr)
            {
                return true;
            }
        }

        return false;
    }

    // 시야 안의 가장 가까운 생존자. 여러 명이 보이면 가까운 쪽을 쫓는 게 자연스럽다.
    //
    // 같은 프레임에 여러 번 물어와도 한 번만 계산한다. 조건 노드가 감시(Observer Abort)로도,
    // 가지 진입으로도 같은 판정을 물어보기 때문에 한 프레임에 여러 번 불린다.
    // 판정마다 인원수만큼 레이캐스트가 나가므로 중복 계산이 그대로 비용이 된다.
    public bool TryGetVisibleSurvivor(out GameObject survivor)
    {
        if (_visibleFrame != Time.frameCount)
        {
            _visibleFrame = Time.frameCount;
            _visibleResult = FindVisibleSurvivor(out _visibleSurvivor);
        }

        survivor = _visibleSurvivor;
        return _visibleResult;
    }

    // 손전등 빛에 노출됐는지와 같은 판정. 프레임 단위로 캐시한다.
    public bool TryGetNearbySurvivor(out GameObject survivor)
    {
        if (_nearbyFrame != Time.frameCount)
        {
            _nearbyFrame = Time.frameCount;
            _nearbyResult = FindNearbySurvivor(out _nearbySurvivor);
        }

        survivor = _nearbySurvivor;
        return _nearbyResult;
    }

    // 시야와 근접 감지는 "거리 한계"와 "시야각을 보는지"만 다르다. 한 함수로 묶어서
    // 표적을 고르는 규칙이 두 곳에서 갈라지지 않게 한다.
    private bool FindVisibleSurvivor(out GameObject survivor)
        => FindSurvivor(_sightRange * SenseMultiplier, requireCone: true, out survivor);

    private bool FindNearbySurvivor(out GameObject survivor)
        => FindSurvivor(_senseRadius * SenseMultiplier, requireCone: false, out survivor);

    // 봐주는 동안 감각이 둔해지는 배율.
    //
    // 감지에서 사람을 통째로 빼면 안 된다. 그러면 보스가 눈앞의 사람을 못 보는 것이 되고,
    // 그 판정을 읽는 쪽(심장 박동, 디버그 표시)까지 "아무도 없다"가 되어 쫓기는 사람에게
    // 안전하다는 거짓 신호가 간다. 대신 사거리만 줄인다.
    //
    // 그래서 봐주기는 "안 보이게 되는 것"이 아니라 "가만히 있으면 지나친다"가 된다.
    // 구석에서 숨을 죽이면 넘어가지만, 움직이거나 소리를 내면 다시 걸린다.
    private float SenseMultiplier => Time.time < _mercyUntil ? _mercySenseMultiplier : 1f;

    // 조건을 통과한 사람 중 하나를 고른다.
    //
    // 매번 "가장 가까운 사람"을 새로 고르면, 비슷한 거리의 두 사람 사이에서 표적이 계속 흔들린다.
    // 보스가 갈팡질팡하는 것처럼 보이고, 협동 게임에서는 팀원이 "지금 누가 쫓기는지"를 읽을 수 없다.
    // 그래서 한 번 잡은 표적을 계속 들고 있고, 다른 사람이 확실히 더 가까울 때만 바꾼다.
    private bool FindSurvivor(float range, bool requireCone, out GameObject survivor)
    {
        survivor = null;

        Vector3 eye = EyePosition;
        float rangeSqr = range * range;
        float cosHalfSight = Mathf.Cos(_sightAngle * 0.5f * Mathf.Deg2Rad);

        GameObject nearest = null;
        float nearestSqr = float.MaxValue;
        float lockedSqr = float.MaxValue;

        // 지금 표적을 뺀 나머지 중 가장 가까운 사람. 표적을 바꿀 때 이쪽으로 넘어간다.
        GameObject nearestOther = null;
        float nearestOtherSqr = float.MaxValue;

        foreach (PlayerHealth candidate in SurvivorRegistry.Active())
        {
            Transform target = candidate.transform;
            Vector3 offset = target.position - transform.position;
            float distanceSqr = offset.sqrMagnitude;
            if (distanceSqr > rangeSqr)
            {
                continue;
            }

            // 위아래로 벌어진 각도까지 시야각에 넣으면 계단에서 부자연스럽게 놓친다.
            if (requireCone)
            {
                Vector3 flat = offset;
                flat.y = 0f;
                if (flat.sqrMagnitude > 0.0001f &&
                    Vector3.Dot(transform.forward, flat.normalized) < cosHalfSight)
                {
                    continue;
                }
            }

            if (!HasLineOfSight(eye, target))
            {
                continue;
            }

            if (target.gameObject == _lockedTarget)
            {
                lockedSqr = distanceSqr;
            }
            else if (distanceSqr < nearestOtherSqr &&
                     !(target.gameObject == _previousTarget && Time.time < _previousTargetUntil))
            {
                nearestOtherSqr = distanceSqr;
                nearestOther = target.gameObject;
            }

            if (distanceSqr < nearestSqr)
            {
                nearestSqr = distanceSqr;
                nearest = target.gameObject;
            }
        }

        if (nearest == null)
        {
            return false;
        }

        // 지금 표적이 아직 조건을 통과하는 경우.
        if (lockedSqr < float.MaxValue)
        {
            // 한참 쫓았는데 다른 사람이 눈에 들어오면, 낮은 확률로 그쪽으로 갈아탄다.
            //
            // 거리로만 표적을 고르면 한 사람이 계속 쫓기고 나머지는 안전해진다. 협동 게임에서는
            // 쫓기는 쪽이 돌아가며 바뀌어야 팀이 움직일 여지가 생긴다. 확률로 두는 이유는,
            // 조건이 맞자마자 바뀌면 "오래 쫓기면 풀린다"는 규칙으로 읽혀서 역이용되기 때문이다.
            if (nearestOther != null && ShouldSwitchTarget())
            {
                // 방금 놓아준 사람은 한동안 후보에서 뺀다. 이게 없으면 두 사람이 계속 보이는
                // 동안 A -> B -> A 로 무한히 뒤집힌다. 유지 시간(8초)만으로는 못 막는다.
                _previousTarget = _lockedTarget;
                _previousTargetUntil = Time.time + _switchBackCooldown;

                LockTarget(nearestOther);
                survivor = nearestOther;
                return true;
            }

            // 평소에는 다른 사람이 임계값 이상 가까워야 바꾼다.
            float switchDistance = Mathf.Max(0f, Mathf.Sqrt(lockedSqr) - _targetSwitchMargin);
            if (Mathf.Sqrt(nearestSqr) > switchDistance)
            {
                survivor = _lockedTarget;
                return true;
            }
        }

        LockTarget(nearest);
        survivor = nearest;
        return true;
    }

    // 감지가 이만큼 끊겼다 다시 잡히면 새 추격으로 본다. 프레임 단위 흔들림은 넘긴다.
    private const float LockGapSeconds = 1f;

    private void LockTarget(GameObject target)
    {
        // 표적이 바뀌었거나, 한동안 아무도 못 잡다가 다시 잡은 경우 시계를 새로 시작한다.
        //
        // 바뀔 때만 갱신하면 놓친 시간까지 "쫓은 시간"에 들어간다. 60초 배회하다 같은 사람을
        // 다시 발견하면 그 순간 이미 전환 조건이 차 있어서, 보자마자 다른 사람에게 넘어간다.
        if (_lockedTarget != target || Time.time - _lastLockTime > LockGapSeconds)
        {
            _lockedSince = Time.time;
        }

        _lockedTarget = target;
        _lastLockTime = Time.time;
    }

    // 오래 쫓았고, 주사위 간격이 됐고, 확률에 걸렸을 때만 참.
    private bool ShouldSwitchTarget()
        => Time.time - _lockedSince >= _switchAfterSeconds
            && _switchGate.TryPass(Time.time, _switchChance, Random.value);

    // 막다른 곳에 몰아넣은 참이면 낮은 확률로 흥미를 잃는다.
    //
    // 사람을 안 보이게 만드는 것이 아니라 감각을 잠깐 둔하게 한다(SenseMultiplier). 숨을 죽이면
    // 지나가지만 움직이거나 소리를 내면 다시 걸린다.
    //
    // 항상 살려주면 구석이 오히려 안전지대가 되어, 도망치는 대신 벽을 등지고 서 있는 것이
    // 최선의 수가 된다. 그래서 확률이어야 한다. BossAttack 이 공격 직전에 물어본다 - 그때가
    // "몰렸다"가 확정되는 유일한 순간이다.
    public bool TryGrantMercy(GameObject survivor)
    {
        // 몰림 판정이 먼저다. 주사위를 먼저 굴리면 몰리지도 않았는데 잠금만 소모된다.
        if (survivor == null ||
            !IsCornered(survivor.transform.position) ||
            !_mercyGate.TryPass(Time.time, _mercyChance, Random.value))
        {
            return false;
        }

        _mercyUntil = Time.time + _mercySeconds;

        // 붙잡고 있던 표적을 놓는다. 안 그러면 기한이 끝나자마자 그대로 다시 붙는다.
        _lockedTarget = null;
        return true;
    }

    // 보스 반대쪽으로 빠져나갈 길이 거의 없으면 몰린 것으로 본다.
    //
    // 보스 쪽으로 난 길은 도망길이 아니다. 그래서 지금 보스와의 거리보다 멀어지는 방향만 센다.
    private bool IsCornered(Vector3 position)
    {
        float fromBoss = FlatDistance(position, transform.position);
        int escapes = 0;

        for (int i = 0; i < CorneredSamples; i++)
        {
            float angle = Mathf.PI * 2f * i / CorneredSamples;
            Vector3 candidate = position
                + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * _corneredCheckRadius;

            if (!NavMesh.SamplePosition(candidate, out NavMeshHit hit, 1.5f, NavMesh.AllAreas))
            {
                continue;
            }

            if (FlatDistance(hit.position, transform.position) > fromBoss)
            {
                escapes++;
            }
        }

        return escapes <= _corneredEscapeCount;
    }

    private static float FlatDistance(Vector3 a, Vector3 b)
    {
        a.y = 0f;
        b.y = 0f;
        return Vector3.Distance(a, b);
    }

    // 몸 위아래 지점 중 하나라도 시선이 트여 있으면 보이는 것으로 본다.
    private bool HasLineOfSight(Vector3 eye, Transform target)
    {
        PlayerHealth targetHealth = target.GetComponent<PlayerHealth>();

        foreach (float height in BodySampleHeights)
        {
            if (IsPointClear(eye, target.position + Vector3.up * height, targetHealth))
            {
                return true;
            }
        }

        return false;
    }

    // 플레이어와 벽이 같은 레이어(Default)에 있어서 "뭐든 맞으면 가려짐"으로 볼 수 없다.
    // 표적 자신에게 막혀 아무도 못 보게 된다. 그래서 가장 가까운 것을 찾고, 그게 표적이면 트인 것이다.
    private bool IsPointClear(Vector3 eye, Vector3 point, PlayerHealth targetHealth)
    {
        Collider blocker = FindBlocker(eye, point);
        return blocker == null || blocker.GetComponentInParent<PlayerHealth>() == targetHealth;
    }

    // 눈과 한 점 사이를 가로막는 가장 가까운 콜라이더. 없으면 null.
    private Collider FindBlocker(Vector3 eye, Vector3 point)
    {
        Vector3 direction = point - eye;
        float distance = direction.magnitude;
        if (distance <= 0.01f)
        {
            return null;
        }

        int count = Physics.RaycastNonAlloc(
            eye,
            direction / distance,
            _sightHits,
            distance,
            _sightBlockers,
            QueryTriggerInteraction.Ignore);

        Collider nearest = null;
        float nearestDistance = float.MaxValue;

        for (int i = 0; i < count; i++)
        {
            RaycastHit hit = _sightHits[i];
            if (hit.collider == null || hit.distance >= nearestDistance)
            {
                continue;
            }

            // 보스 자신의 콜라이더는 눈이 몸 안에 있어서 잡힐 수 있다.
            if (hit.collider.transform.IsChildOf(transform))
            {
                continue;
            }

            // 미션 장치는 플레이어 상호작용에 필요한 콜라이더지만, 시야를 끊으면 추적이 부자연스럽게 멈춘다.
            if (hit.collider.GetComponentInParent<MissionInteractable>() != null ||
                hit.collider.GetComponentInParent<BreakerLeverInteractable>() != null)
            {
                continue;
            }

            nearest = hit.collider;
            nearestDistance = hit.distance;
        }

        return nearest;
    }

    // 개발용. 가장 가까운 생존자에 대해 시야 판정이 어느 단계에서 막혔는지 문자열로 돌려준다.
    // "안 보인다"만으로는 거리·각도·가림 중 무엇이 문제인지 알 수 없어서 만들었다.
    public string DescribeSightCheck()
    {
        // 봐주는 동안 줄어든 사거리를 그대로 써야 한다. 원래 값으로 표시하면 실제 판정과
        // 어긋나서, 디버그 표시만 보고는 왜 못 봤는지 알 수 없다.
        float range = _sightRange * SenseMultiplier;
        Vector3 eye = EyePosition;
        float cosHalfSight = Mathf.Cos(_sightAngle * 0.5f * Mathf.Deg2Rad);
        string result = "대상 없음";
        float nearest = float.MaxValue;

        foreach (PlayerHealth candidate in SurvivorRegistry.Active())
        {
            Transform target = candidate.transform;
            Vector3 offset = target.position - transform.position;
            float distance = offset.magnitude;
            if (distance >= nearest)
            {
                continue;
            }

            nearest = distance;

            Vector3 flat = offset;
            flat.y = 0f;
            float angle = flat.sqrMagnitude > 0.0001f
                ? Vector3.Angle(transform.forward, flat)
                : 0f;

            if (distance > range)
            {
                result = $"{distance:0.0}m > 거리 {range:0.0} 초과"
                    + (SenseMultiplier < 1f ? " (봐주는 중)" : string.Empty);
                continue;
            }

            if (flat.sqrMagnitude > 0.0001f && Vector3.Dot(transform.forward, flat.normalized) < cosHalfSight)
            {
                result = $"{distance:0.0}m 안, 각도 {angle:0}° > {_sightAngle * 0.5f:0}° 초과";
                continue;
            }

            string samples = DescribeBodySamples(eye, target);
            result = HasLineOfSight(eye, target)
                ? $"{distance:0.0}m / {angle:0}° 보임 [{samples}]"
                : $"{distance:0.0}m / {angle:0}° 가림 [{samples}]";
        }

        return result;
    }

    // 몸의 어느 높이가 트였고 어느 높이가 무엇에 막혔는지.
    //
    // "보임/안 보임"만으로는 왜 그렇게 됐는지 알 수 없다. 낮은 엄폐물 뒤에 서 있으면
    // 무릎만 막히고 머리는 트여서 보이는 것이 정상인데, 그게 버그인지 아닌지 이 줄로 구분한다.
    private string DescribeBodySamples(Vector3 eye, Transform target)
    {
        PlayerHealth targetHealth = target.GetComponent<PlayerHealth>();
        _sampleText.Clear();

        foreach (float height in BodySampleHeights)
        {
            if (_sampleText.Length > 0)
            {
                _sampleText.Append(", ");
            }

            Collider blocker = FindBlocker(eye, target.position + Vector3.up * height);
            bool clear = blocker == null || blocker.GetComponentInParent<PlayerHealth>() == targetHealth;

            _sampleText.Append(clear
                ? $"{height:0.0}m 트임"
                : $"{height:0.0}m 막힘({blocker.gameObject.name})");
        }

        return _sampleText.ToString();
    }
}
