using Unity.Netcode;
using UnityEngine;

// 보스가 누구를 인지하고 있는지를 그 사람의 클라이언트에 알려준다.
//
// 인지 판정은 서버에만 있고(BossPerception 은 서버에서만 의미가 있다), 심장 박동은 각자 자기
// 것만 들어야 한다. 그 사이를 잇는 게 이 컴포넌트다.
[RequireComponent(typeof(BossPerception))]
public class BossThreatReporter : MonoBehaviour
{
    [Tooltip("보스가 이 거리 안에 있으면 '수색 중'으로 본다. 시야(18m)·근접 감지(14m) 밖에서도 "
        + "다가오는 게 느껴져야 해서 그보다 넉넉하게 둔다.")]
    [SerializeField, Min(0f)] private float _searchDistance = 25f;

    [Header("플레이어가 보스를 봤는지")]
    [Tooltip("이 거리 안에서 보스를 눈으로 보면 '발각'과 같게 다룬다. 보스가 나를 못 봤어도 "
        + "내가 본 순간부터 도망이 시작되기 때문이다. 멀리 스쳐 보이는 것까지 세면 마주친 적도 "
        + "없이 180 이 나므로 넉넉하게 잡지 않는다.")]
    [SerializeField, Min(0f)] private float _playerSightRange = 12f;

    [Tooltip("플레이어 시야각 전체(도). 화면 구석에 걸친 것은 알아본 것으로 보기 어려워서 "
        + "화면 폭보다 좁게 둔다.")]
    [SerializeField, Range(1f, 360f)] private float _playerSightAngle = 60f;

    [Tooltip("시야를 막는 레이어. 보스의 Sight Blockers 와 같은 값을 넣는다.")]
    [SerializeField] private LayerMask _sightBlockers = ~0;

    [Tooltip("갱신 간격(초). 매 프레임 돌 필요가 없다. 페이드가 있어서 약간 늦어도 표가 안 난다.")]
    [SerializeField, Min(0f)] private float _interval = 0.2f;

    // 눈높이. 발밑끼리 이으면 문턱에 막히고, 정수리끼리 이으면 낮은 엄폐물이 무의미해진다.
    private const float PlayerEyeHeight = 1.6f;
    private const float BossChestHeight = 1.2f;

    // 시야 판정은 인원수만큼 돌아서, 그때마다 배열을 새로 만들면 GC 가 쌓인다.
    private readonly RaycastHit[] _sightHits = new RaycastHit[16];

    // 왜 이 판정이 나왔는지. 보스 디버그 표시가 읽는다.
    private readonly System.Text.StringBuilder _diagnosis = new();

    private BossPerception _perception;
    private BossTargetMemory _memory;
    private BossDormancy _dormancy;
    private float _nextUpdateTime;

    private void Awake()
    {
        _perception = GetComponent<BossPerception>();
        _memory = GetComponent<BossTargetMemory>();
        _dormancy = GetComponent<BossDormancy>();
    }

    private void Update()
    {
        // 보스는 서버에만 존재하므로 여기서 서버 여부를 따로 확인하지 않는다.
        if (Time.time < _nextUpdateTime) return;

        _nextUpdateTime = Time.time + _interval;

        NetworkManager manager = NetworkManager.Singleton;
        if (manager == null) return;

        // 잠들어 있으면 아무도 쫓지 않는다. 깨어나기 전부터 소리가 나면 위치가 새어나간다.
        bool awake = _dormancy == null || !_dormancy.IsDormant;

        // 근접 감지는 "곧 들킬 것 같다"에 가깝다. 수색과 같은 무게로 다룬다.
        GameObject sensed = null;
        if (awake)
        {
            _perception.TryGetNearbySurvivor(out sensed);
        }

        // 보스가 실제로 수색 중인지. 기억은 누군가를 본 뒤에만 생기므로, 아직 아무도 마주치지
        // 않았으면 여기서 걸러진다.
        //
        // 이걸 빼고 거리만 봤더니, 순찰 중인 보스가 25m 안을 지나가기만 해도 120 이 났다.
        // 마주친 적도 없는데 쫓기는 소리가 들리면 소리가 알려주는 게 아무것도 없게 된다.
        bool searching = awake && _memory != null && _memory.HasMemory;

        float searchDistanceSqr = _searchDistance * _searchDistance;

        // 접속자 전체를 돈다. 살아서 지하에 있는 사람만 순회하면, 본부로 들어간 사람의 위협도가
        // 마지막 값으로 남아서 안전한 곳에서도 심장 소리가 계속 난다.
        _diagnosis.Clear();
        _diagnosis.AppendLine($"깨어있음: {awake}   수색중: {searching}");

        foreach (NetworkClient client in manager.ConnectedClientsList)
        {
            NetworkObject playerObject = client.PlayerObject;
            if (playerObject == null) continue;

            PlayerHeartbeat heartbeat = playerObject.GetComponent<PlayerHeartbeat>();
            if (heartbeat == null) continue;

            _diagnosis.Append($"  {playerObject.gameObject.name}: ");
            BossThreat threat = ResolveThreat(playerObject.gameObject, sensed, awake, searching, searchDistanceSqr);
            _diagnosis.AppendLine($" -> {threat}");

            heartbeat.SetBossThreat(threat);
        }
    }

    // 수색 중 판정을 "보스가 누군가를 찾고 있고, 그 보스가 내 근처에 있다"로 잡는다.
    //
    // 기억의 주인이 나인지는 보지 않는다. 기억은 한 사람만 담고 10초면 만료되므로, 내 기억만
    // 따지면 숨어 있는 동안 소리가 자꾸 끊긴다. 보스가 팀원을 쫓다가 내 옆을 지나가는 상황도
    // 조여드는 순간인데 그때 조용해진다.
    //
    // 반대로 기억 자체를 빼고 거리만 보면, 아직 아무도 마주치지 않았는데도 순찰 중인 보스가
    // 근처를 지나갈 때마다 소리가 난다. 그래서 "수색 중"과 "가깝다"를 둘 다 요구한다.
    private BossThreat ResolveThreat(
        GameObject player, GameObject sensed,
        bool awake, bool searching, float searchDistanceSqr)
    {
        // 쓰러졌거나 본부에 있으면 보스의 판정 대상이 아니다.
        if (!SurvivorRegistry.IsActive(player)) return BossThreat.None;

        if (!awake) return BossThreat.None;

        // 발각은 "내가 외계인을 봤을 때"다. 보스가 나를 본 것은 여기에 넣지 않는다.
        //
        // 심장 소리는 내가 느끼는 것이라, 내가 모르는 사실에 반응하면 안 된다. 등 뒤에서
        // 보스가 나를 봤다는 걸 소리로 알려주면, 소리가 내 감각이 아니라 정보 누출이 된다.
        // 보스가 나를 봤으면 기억이 생기므로 아래 수색 판정에서 120 으로 잡힌다.
        if (PlayerSeesBoss(player.transform)) return BossThreat.Spotted;

        // 여기부터는 보스가 누군가를 찾고 있는 중일 때만 소리가 난다.
        if (!searching) return BossThreat.None;

        if (player == sensed) return BossThreat.Searching;

        return (player.transform.position - transform.position).sqrMagnitude <= searchDistanceSqr
            ? BossThreat.Searching
            : BossThreat.None;
    }

    // 이 플레이어가 지금 보스를 눈으로 보고 있는지.
    //
    // 위아래 각은 보지 않고 좌우만 본다. 보스는 바닥을 걸어다니고 플레이어도 그렇기 때문에
    // 수평 판정만으로 거의 같은 결과가 나온다. 위아래를 넣으려면 네트워크로 오는 시선 각의
    // 부호 규칙까지 맞춰야 하는데, 그 대가로 얻는 정확도가 크지 않다.
    private bool PlayerSeesBoss(Transform player)
    {
        Vector3 eye = player.position + Vector3.up * PlayerEyeHeight;
        Vector3 target = transform.position + Vector3.up * BossChestHeight;
        Vector3 delta = target - eye;
        float distance = delta.magnitude;

        if (distance > _playerSightRange)
        {
            _diagnosis.Append($"{distance:0.0}m > {_playerSightRange:0}m 너무 멂");
            return false;
        }

        if (distance <= 0.01f) return true;

        // 몸통이 마우스를 따라 돌아가므로(PlayerCameraController) forward 가 곧 보는 방향이다.
        Vector3 flat = delta;
        flat.y = 0f;
        float angle = Vector3.Angle(player.forward, flat);

        if (angle > _playerSightAngle * 0.5f)
        {
            _diagnosis.Append($"{distance:0.0}m, 각도 {angle:0}° > {_playerSightAngle * 0.5f:0}° 시야 밖");
            return false;
        }

        Collider blocker = FindBlocker(eye, delta / distance, distance, player);

        if (blocker != null)
        {
            _diagnosis.Append($"{distance:0.0}m / {angle:0}° 가림: {blocker.gameObject.name}");
            return false;
        }

        _diagnosis.Append($"{distance:0.0}m / {angle:0}° 보임");
        return true;
    }

    // 시선을 막는 콜라이더. 없으면 null.
    //
    // 눈은 플레이어 몸 안에 있고 겨냥한 점은 보스 몸 안에 있다. 둘의 콜라이더를 빼지 않으면
    // 레이가 출발하자마자, 또는 도착 직전에 자기 몸에 맞아서 항상 "가려짐"이 된다.
    // BossPerception.HasLineOfSight 가 같은 이유로 같은 예외를 두고 있다.
    private Collider FindBlocker(Vector3 eye, Vector3 direction, float distance, Transform player)
    {
        int count = Physics.RaycastNonAlloc(
            eye, direction, _sightHits, distance, _sightBlockers, QueryTriggerInteraction.Ignore);

        for (int i = 0; i < count; i++)
        {
            Collider hit = _sightHits[i].collider;
            if (hit == null) continue;

            if (hit.transform.IsChildOf(player) || hit.transform.IsChildOf(transform)) continue;

            // 미션 장치는 상호작용용 콜라이더다. 시야를 끊으면 장치 앞에서만 소리가 사라진다.
            if (hit.GetComponentInParent<MissionInteractable>() != null ||
                hit.GetComponentInParent<BreakerLeverInteractable>() != null) continue;

            return hit;
        }

        return null;
    }

    // 보스 디버그 표시가 읽는 진단 내용.
    public string Diagnosis => _diagnosis.ToString();
}
