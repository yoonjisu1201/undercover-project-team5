using UnityEngine;

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

    [Header("근접 감지")]
    [Tooltip("이 거리 안이면 보고 있지 않아도 알아챈다. 시야각을 무시하므로 등 뒤도 걸린다.")]
    [SerializeField, Min(0f)] private float _senseRadius = 14f;

    // 가슴 높이. 발밑을 노리면 문턱에도 가려지고, 머리를 노리면 낮은 엄폐물이 무의미해진다.
    private const float ChestHeight = 1.2f;

    // 시야 판정은 매 그래프 틱마다 인원수만큼 돌아서, 그때마다 배열을 새로 만들면 GC가 쌓인다.
    private readonly RaycastHit[] _sightHits = new RaycastHit[16];

    // 프레임 단위 캐시. 같은 프레임에 같은 판정을 다시 계산하지 않는다.
    private int _visibleFrame = -1;
    private bool _visibleResult;
    private GameObject _visibleSurvivor;
    private int _nearbyFrame = -1;
    private bool _nearbyResult;
    private GameObject _nearbySurvivor;

    // 지금 붙잡고 있는 표적. 시야·근접 어느 쪽으로 잡았든 보스는 한 명만 쫓는다.
    private GameObject _lockedTarget;

    private Vector3 EyePosition => transform.position + Vector3.up * _eyeHeight;

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
        => FindSurvivor(_sightRange, requireCone: true, out survivor);

    private bool FindNearbySurvivor(out GameObject survivor)
        => FindSurvivor(_senseRadius, requireCone: false, out survivor);

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

        // 지금 표적이 아직 조건을 통과하면, 다른 사람이 임계값 이상 가까워야 바꾼다.
        if (lockedSqr < float.MaxValue)
        {
            float switchDistance = Mathf.Max(0f, Mathf.Sqrt(lockedSqr) - _targetSwitchMargin);
            if (Mathf.Sqrt(nearestSqr) > switchDistance)
            {
                survivor = _lockedTarget;
                return true;
            }
        }

        _lockedTarget = nearest;
        survivor = nearest;
        return true;
    }

    // 플레이어와 벽이 같은 레이어(Default)에 있어서 "뭐든 맞으면 가려짐"으로 볼 수 없다.
    // 표적 자신에게 막혀 아무도 못 보게 된다. 그래서 맞은 것을 모두 받아 가장 가까운 것을 찾고,
    // 그게 표적이면 보이는 것으로 판정한다.
    private bool HasLineOfSight(Vector3 eye, Transform target)
    {
        // 발밑이 아니라 가슴 높이를 노려야 낮은 장애물 뒤에 서 있을 때만 가려진다.
        Vector3 targetPoint = target.position + Vector3.up * ChestHeight;
        Vector3 direction = targetPoint - eye;
        float distance = direction.magnitude;
        if (distance <= 0.01f)
        {
            return true;
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

        // 아무것도 없거나, 가장 가까운 것이 표적 본인이면 보인다.
        return nearest == null || nearest.GetComponentInParent<PlayerHealth>() == target.GetComponent<PlayerHealth>();
    }

    // 개발용. 가장 가까운 생존자에 대해 시야 판정이 어느 단계에서 막혔는지 문자열로 돌려준다.
    // "안 보인다"만으로는 거리·각도·가림 중 무엇이 문제인지 알 수 없어서 만들었다.
    public string DescribeSightCheck()
    {
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

            if (distance > _sightRange)
            {
                result = $"{distance:0.0}m > 거리 {_sightRange:0} 초과";
                continue;
            }

            if (flat.sqrMagnitude > 0.0001f && Vector3.Dot(transform.forward, flat.normalized) < cosHalfSight)
            {
                result = $"{distance:0.0}m 안, 각도 {angle:0}° > {_sightAngle * 0.5f:0}° 초과";
                continue;
            }

            result = HasLineOfSight(eye, target)
                ? $"{distance:0.0}m / {angle:0}° 보임"
                : $"{distance:0.0}m / {angle:0}° 가림: {DescribeBlocker(eye, target)}";
        }

        return result;
    }

    // 시선을 막고 있는 콜라이더 이름. 무엇 때문에 안 보이는지 바로 알 수 있게 한다.
    private string DescribeBlocker(Vector3 eye, Transform target)
    {
        Vector3 targetPoint = target.position + Vector3.up * ChestHeight;
        Vector3 direction = targetPoint - eye;
        float distance = direction.magnitude;

        int count = Physics.RaycastNonAlloc(
            eye, direction / distance, _sightHits, distance, _sightBlockers, QueryTriggerInteraction.Ignore);

        Collider nearest = null;
        float nearestDistance = float.MaxValue;

        for (int i = 0; i < count; i++)
        {
            RaycastHit hit = _sightHits[i];
            if (hit.collider == null || hit.distance >= nearestDistance ||
                hit.collider.transform.IsChildOf(transform) ||
                hit.collider.GetComponentInParent<MissionInteractable>() != null ||
                hit.collider.GetComponentInParent<BreakerLeverInteractable>() != null)
            {
                continue;
            }

            nearest = hit.collider;
            nearestDistance = hit.distance;
        }

        return nearest == null ? "?" : $"{nearest.gameObject.name} ({nearestDistance:0.0}m)";
    }

}
