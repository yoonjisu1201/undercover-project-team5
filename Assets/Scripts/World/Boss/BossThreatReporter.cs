using Unity.Netcode;
using UnityEngine;

// 보스가 누구를 인지하고 있는지를 그 사람의 클라이언트에 알려준다.
//
// 인지 판정은 서버에만 있고(BossPerception 은 서버에서만 의미가 있다), 심장 박동은 각자 자기
// 것만 들어야 한다. 그 사이를 잇는 게 이 컴포넌트다.
public class BossThreatReporter : MonoBehaviour
{
    [Header("발각 뒤 여운")]
    [Tooltip("외계인을 본 뒤 180이 유지되는 시간(초). 시야에서 사라져도 이만큼은 이어진다.")]
    [SerializeField, Min(0f)] private float _spottedSeconds = 5f;

    [Tooltip("180이 끝난 뒤 120으로 이어지는 시간(초). 이게 지나면 소리가 멎는다.")]
    [SerializeField, Min(0f)] private float _afterSpottedSeconds = 5f;

    [Header("플레이어가 보스를 봤는지")]
    [Tooltip("이 거리 안에서 보스를 눈으로 보면 '발각'과 같게 다룬다. 보스가 나를 못 봤어도 "
        + "내가 본 순간부터 도망이 시작되기 때문이다. 멀리 스쳐 보이는 것까지 세면 마주친 적도 "
        + "없이 180 이 나므로 넉넉하게 잡지 않는다.")]
    [SerializeField, Min(0f)] private float _playerSightRange = 8f;

    [Tooltip("화면 중앙을 기준으로 한 시야각 전체(도). 좌우·위아래를 함께 본다. "
        + "화면 구석에 걸친 것은 알아본 것으로 보기 어려워서 실제 화면 화각보다 좁게 둔다.")]
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

    // 플레이어별로 외계인을 마지막으로 본 시각. 발각 여운을 각자 따로 센다.
    private readonly System.Collections.Generic.Dictionary<ulong, float> _lastSeenTime = new();

    private BossTargetMemory _memory;
    private BossDormancy _dormancy;
    private float _nextUpdateTime;

    private void Awake()
    {
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

        // 보스가 실제로 사람을 찾고 있는지.
        //
        // 흔적은 심장 소리 판정에 쓰지 않는다. 표시로만 남긴다.
        //
        // 보스가 무엇을 쥐고 있는지는 디버깅에 필요하지만, 그것이 소리로 새어나가면 안 된다.
        // 내가 모르는 사실을 소리로 알려주는 것은 감각이 아니라 정보 누출이다.
        bool hasTrace = _memory != null && _memory.HasMemory;

        // 접속자 전체를 돈다. 살아서 지하에 있는 사람만 순회하면, 본부로 들어간 사람의 위협도가
        // 마지막 값으로 남아서 안전한 곳에서도 심장 소리가 계속 난다.
        // "단서 있음"은 보스가 쫓을 것을 쥐고 있다는 뜻이지 심장 소리 조건이 아니다.
        // 보스는 단서가 없어도 늘 수색하고 있다.
        _diagnosis.Clear();
        _diagnosis.AppendLine(
            $"깨어있음: {awake}   보스 단서: {hasTrace}"
            + (hasTrace && _memory != null ? $" ({_memory.TraceSource})" : string.Empty)
            + "   ※ 심장 소리는 발각으로만 난다");

        foreach (NetworkClient client in manager.ConnectedClientsList)
        {
            NetworkObject playerObject = client.PlayerObject;
            if (playerObject == null) continue;

            PlayerHeartbeat heartbeat = playerObject.GetComponent<PlayerHeartbeat>();
            if (heartbeat == null) continue;

            _diagnosis.Append($"  {playerObject.gameObject.name}: ");
            BossThreat threat = ResolveThreat(client.ClientId, playerObject.gameObject, awake);
            _diagnosis.AppendLine($" -> {threat}");

            heartbeat.SetBossThreat(threat);
        }
    }

    // 심장 소리는 "내가 외계인을 봤는가" 하나로만 정해진다.
    //
    // 보고 있는 동안 180, 시야에서 사라진 뒤로도 _spottedSeconds 동안 180, 그 다음
    // _afterSpottedSeconds 동안 120. 마주친 순간 바로 멎으면 고개만 돌려도 조용해져서
    // 발각이 사건으로 남지 않는다.
    private BossThreat ResolveThreat(ulong clientId, GameObject player, bool awake)
    {
        // 쓰러졌거나 본부에 있으면 보스의 판정 대상이 아니다.
        if (!SurvivorRegistry.IsActive(player)) return BossThreat.None;

        if (!awake)
        {
            _lastSeenTime.Remove(clientId);
            return BossThreat.None;
        }

        // 발각은 "내가 외계인을 봤을 때"다. 보스가 나를 본 것은 여기에 넣지 않는다.
        //
        // 심장 소리는 내가 느끼는 것이라, 내가 모르는 사실에 반응하면 안 된다. 등 뒤에서
        // 보스가 나를 봤다는 걸 소리로 알려주면, 소리가 내 감각이 아니라 정보 누출이 된다.
        if (PlayerSeesBoss(player))
        {
            _lastSeenTime[clientId] = Time.time;
        }

        // 본 뒤에는 시야에서 사라져도 한동안 이어진다. 마주친 순간 바로 멎으면 "봤다"가
        // 소리로 남지 않고, 고개만 돌려도 조용해져서 발각이 사건으로 느껴지지 않는다.
        if (_lastSeenTime.TryGetValue(clientId, out float lastSeen))
        {
            float since = Time.time - lastSeen;
            if (since < _spottedSeconds) return BossThreat.Spotted;
            if (since < _spottedSeconds + _afterSpottedSeconds) return BossThreat.Searching;

            // 여운이 다 끝났으면 기록을 지운다. 안 지우면 다음 판정에서 매번 뺄셈만 한다.
            _lastSeenTime.Remove(clientId);
        }

        // 여기까지 왔으면 아무 소리도 내지 않는다.
        //
        // 예전에는 "보스가 단서를 쥔 채 25m 안에 있으면" 120 을 냈다. 팀원이 쫓기는 중에
        // 보스가 내 옆을 지나가는 것도 조여드는 순간이라는 의도였는데, 벽 뒤에 숨어 아무것도
        // 보지 못한 사람에게까지 울렸다. 그건 내 감각이 아니라 내가 모르는 사실을 소리로
        // 알려주는 것이라 정보 누출에 가깝다.
        //
        // 그래서 심장 소리는 "내가 외계인을 봤는가" 하나로만 난다.
        return BossThreat.None;
    }

    // 이 플레이어가 지금 보스를 화면으로 보고 있는지.
    //
    // 몸통 yaw 만 보면 바닥이나 천장을 보고 있어도 "봤다"가 된다. 화면에 없는 것 때문에 180 이
    // 나므로, 카메라가 실제로 향한 방향으로 판정한다. 좌우·위아래를 한 번에 보는 원뿔이다.
    private bool PlayerSeesBoss(GameObject player)
    {
        Transform body = player.transform;
        Vector3 eye = body.position + Vector3.up * PlayerEyeHeight;
        Vector3 target = transform.position + Vector3.up * BossChestHeight;
        Vector3 delta = target - eye;
        float distance = delta.magnitude;

        if (distance > _playerSightRange)
        {
            _diagnosis.Append($"{distance:0.0}m > {_playerSightRange:0}m 너무 멂");
            return false;
        }

        if (distance <= 0.01f) return true;

        // yaw 는 몸통이 마우스를 따라 돌아가므로 트랜스폼에 그대로 들어 있고(PlayerCameraController),
        // pitch 는 오너가 NetworkVariable 로 올려주는 값이라 서버에서도 읽을 수 있다.
        float yaw = body.eulerAngles.y;
        float pitch = player.TryGetComponent(out PlayerCameraController camera) ? camera.ViewPitch : 0f;
        Vector3 look = Quaternion.Euler(pitch, yaw, 0f) * Vector3.forward;

        float angle = Vector3.Angle(look, delta);
        string facing = $"yaw {yaw:0}° pitch {pitch:0}°";

        if (angle > _playerSightAngle * 0.5f)
        {
            _diagnosis.Append(
                $"{distance:0.0}m, 각도 {angle:0}° > {_playerSightAngle * 0.5f:0}° 화면 밖 ({facing})");
            return false;
        }

        Collider blocker = FindBlocker(eye, delta / distance, distance, body);

        if (blocker != null)
        {
            _diagnosis.Append($"{distance:0.0}m / {angle:0}° 가림: {blocker.gameObject.name} ({facing})");
            return false;
        }

        _diagnosis.Append($"{distance:0.0}m / {angle:0}° 보임 ({facing})");
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
