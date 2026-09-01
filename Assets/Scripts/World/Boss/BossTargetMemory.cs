using System.Collections.Generic;
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
    [Tooltip("소리로 흔적을 옮기는 최소 간격(초). 짧으면 흔적이 사람의 현재 위치를 "
        + "실시간으로 따라다녀서, 보스가 벽 너머로 정확히 쫓아온다.")]
    [SerializeField, Min(0f)] private float _noiseTraceInterval = 2f;

    [Tooltip("청각 배율. 소음마다 정해진 '들리는 거리'에 이 값을 곱한다.")]
    [SerializeField, Min(0f)] private float _hearingFactor = 1f;

    [Tooltip("흔적에 이만큼 가까워지면 도착으로 보고 주변 수색으로 넘어간다.")]
    [SerializeField, Min(0.5f)] private float _arriveDistance = 2f;

    [Tooltip("흔적이 있던 방에서 문으로 몇 칸 떨어진 방까지 뒤질지. 1이면 바로 옆방까지다. "
        + "거리(m)로 자르면 방 크기에 따라 옆방이 들어오기도 하고 빠지기도 해서, "
        + "뒤질 방이 하나도 안 남고 수색이 곧바로 끝나는 일이 생긴다.")]
    [SerializeField, Min(1)] private int _searchDepth = 2;

    [Tooltip("흔적 주변을 몇 군데나 뒤져보고 포기할지. 단서가 있는 자리이므로 제대로 뒤진다. "
        + "단서 없이 훑고 다니는 기본 수색과는 상관없다 - 그쪽은 방마다 한 곳씩만 들른다.")]
    [SerializeField, Min(1)] private int _searchPointCount = 5;

    [Tooltip("한 번 서서 살펴본 자리를 얼마나 넓게 '다 봤다'로 칠지(m). 다음 수색 지점도, "
        + "훑고 다닐 때 고르는 방도 이 안쪽은 피한다. 흔적을 쫓아가는 것만은 막지 않는다 - "
        + "새 단서가 나온 곳이라면 다시 가야 한다.")]
    [SerializeField, Min(0f)] private float _sweptRadius = 4f;

    [Tooltip("수색을 마친 자리를 몇 초 동안 피할지. 영영 막으면 결국 갈 곳이 없어진다.")]
    [SerializeField, Min(0f)] private float _sweptSeconds = 90f;

    [Header("포기 / 기본 수색")]
    [Tooltip("포기하고 제자리에 서 있는 시간(초). 길면 굳은 것처럼 보이니 한 박자만 준다.")]
    [SerializeField, Min(0f)] private float _restDuration = 1.5f;

    [Tooltip("훑고 다닐 때 한 번에 나아가는 거리(m). 이 거리마다 갈 곳을 새로 정한다. "
        + "실제로 걷는 거리는 여기서 도착 판정 거리를 뺀 만큼이라, 도착 판정(기본 2m)보다 "
        + "충분히 크게 잡아야 한다. 비슷하게 잡으면 몇십 cm 걷고 다시 목적지를 뽑아 "
        + "제자리에서 찔끔거린다.")]
    [SerializeField, Min(1f)] private float _wanderStepMin = 6f;

    [SerializeField, Min(1f)] private float _wanderStepMax = 12f;

    [Tooltip("포기한 뒤 사람이 가던 쪽으로 훑기를 끌어당기는 시간(초). "
        + "이 시간이 지나면 방향을 놓고 아무 쪽이나 훑는다.")]
    [SerializeField, Min(0f)] private float _wanderBiasDuration = 20f;

    [Tooltip("한 걸음마다 진행 방향을 위 방향 쪽으로 얼마나 당길지. 0이면 안 당기고 "
        + "1이면 매번 그 방향으로만 간다. 반쯤 당기면 좌우로 흔들리되 결국 그쪽으로 나아간다.")]
    [SerializeField, Range(0f, 1f)] private float _wanderBiasWeight = 0.5f;

    // 지점 후보를 몇 번까지 뽑아볼지. 좁은 방에서는 대부분 첫 시도에 걸린다.
    private const int SamplingAttempts = 8;

    // 후보 좌표를 NavMesh 위로 끌어당길 때 허용하는 거리. 문 하나 폭 정도.
    private const float NavSampleRadius = 3f;

    // 평소에 진행 방향에서 트는 각도(±). 이 안에서만 고르면 왔던 길로 되돌아가지 않는다.
    private const float TurnSpread = 60f;

    // 앞이 막혔을 때 시도마다 넓히는 각도. 막다른 곳에서는 결국 돌아 나올 수 있어야 한다.
    private const float BlockedTurnStep = 45f;

    // 지도가 없을 때 흔적 둘레를 도는 반경(m)과, 벽에 걸릴 때 물러나 볼 배율.
    private const float FallbackSearchRadius = 7f;
    private static readonly float[] SearchRadiusScales = { 1f, 0.6f };

    // 두 방이 같은 문을 쓰는지 볼 때 소켓끼리 허용하는 거리(m). 같은 문 자리에 겹쳐 있다.
    private const float DoorMatchDistance = 1.5f;

    // 긴 쪽이 짧은 쪽의 이 배 이상이면 복도로 본다. 복도는 뒤질 공간이 아니라 지나가는 길이다.
    private const float CorridorAspect = 2.5f;

    // 방 안 목적지를 한가운데에서 얼마나 물릴지(방 반지름 대비). 지나가는 쪽으로 민다.
    private const float RoomCrossFactor = 0.45f;

    // 수색할 방을 고를 때 "가던 방향 쪽"을 거리 몇 m 만큼으로 칠지.
    private const float SearchDirectionBonus = 15f;

    // 수색에서 받아줄 최소 방향 일치도. 0이면 진행 방향 기준 좌우 90도까지만 본다.
    // 수색에는 되돌아가기가 없다. 뒤는 이미 지나온 곳이라 거기서 나올 것이 없다.
    private const float MinSearchAlignment = 0f;

    // 방을 고를 때 "가던 방향 쪽"을 거리 몇 m 만큼으로 칠지. 앞쪽 방은 뒤쪽 방보다
    // 이 값의 두 배만큼 가까운 것으로 계산된다.
    private const float RoomDirectionBonus = 20f;

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

    // 흔적 둘레를 도는 각도와 도는 쪽(+1 / -1). 한 수색 동안 방향을 바꾸지 않는다.
    private float _searchAngle;
    private float _searchTurn = 1f;

    // 이번에 고른 방. 확정될 때 뒤진 방 목록에 넣는다.
    private UndergroundModule _nextSearchRoom;

    // 흔적이 끊긴 시점의 진행 방향. 수색 내내 이 방향 앞쪽만 본다.
    private Vector3 _searchForward = Vector3.forward;

    // 보스가 흔적을 향해 걸어간 방향. 흔적이 생길 때 잡아 둔다.
    private Vector3 _traceApproach;

    private float _lastGiveUpTime = float.NegativeInfinity;
    private float _restUntil;
    private float _nextNoiseCheckTime;
    private Vector3 _wanderPoint;
    private bool _hasWanderPoint;

    // 지금 나아가고 있는 방향. 다음 지점을 이 방향 기준으로 골라서 왔다 갔다 하지 않게 한다.
    private Vector3 _heading;

    // 흔적이 끊긴 방향. 포기한 뒤에도 한동안 훑기를 이쪽으로 끌어당긴다.
    private Vector3 _wanderBias;
    private float _wanderBiasUntil;

    // 방 단위로 움직이기 위한 것들. 지도가 없는 곳에서는 null 로 남고 좌표 방식으로 돈다.
    private UndergroundRandomMapGenerator _map;

    // 경로 계산 결과를 담을 그릇. 매번 새로 만들면 프레임마다 쓰레기가 쌓인다.
    // 필드 초기화로는 못 만든다(NavMesh 가 아직 준비되지 않은 시점이다). Awake 에서 만든다.
    private NavMeshPath _path;

    // 이번 흔적에서 이미 뒤진 방들. 표식 반경이 아니라 방 자체로 거른다.
    private readonly List<UndergroundModule> _searchedRooms = new List<UndergroundModule>();

    // 문으로 이어진 방 관계. 지도가 만들어진 뒤 한 번 계산한다.
    private readonly Dictionary<UndergroundModule, List<UndergroundModule>> _roomLinks =
        new Dictionary<UndergroundModule, List<UndergroundModule>>();

    // 이번 수색에서 볼 방들. 흔적이 있던 방에서 문을 타고 뻗어 나간 범위다.
    private readonly List<UndergroundModule> _searchArea = new List<UndergroundModule>();

    // 그 범위의 기준이 된 방과 지금 몇 칸까지 폈는지. 고를 방이 없으면 여기서 한 칸 더 편다.
    private UndergroundModule _searchRoot;
    private int _searchAreaDepth;

    // 수색을 끝낸 자리들. 여기 걸리는 방은 훑고 다닐 때 목적지가 되지 않는다.
    private readonly List<SweptZone> _swept = new List<SweptZone>();

    // 이미 들어가 본 방들. 개수 제한을 두지 않는다. 최근 몇 개만 기억하면 그 밖의 방으로
    // 금방 되돌아가서, 밖에서는 같은 데를 계속 도는 것으로 보인다. 전부 돌면 비우고 다시 센다.
    private readonly List<UndergroundModule> _visitedRooms = new List<UndergroundModule>();

    // 수색을 끝낸 자리와 그 반경. 디버그 표시가 읽는다.
    public IEnumerable<Vector3> SweptPositions
    {
        get
        {
            float now = Time.time;

            foreach (SweptZone zone in _swept)
            {
                if (now < zone.ExpireTime)
                {
                    yield return zone.Position;
                }
            }
        }
    }

    public float SweptRadius => _sweptRadius;

    // 수색을 마친 자리 하나. 시간이 지나면 풀린다.
    private struct SweptZone
    {
        public Vector3 Position;
        public float ExpireTime;
    }

    private void Awake()
    {
        // 눈으로 본 직후 0.5초는 소리가 흔적을 덮지 않는다. 소음은 몇 초 전 자리라,
        // 보이는 동안 끼어들면 흔적이 뒤로 끌려간다.
        _path = new NavMeshPath();

        _lifetime = new TraceLifetime(_noiseTraceInterval, 0.5f);
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

            // 표적이 다운되거나 본부로 빠지면 그 자리로 계속 걸어갈 이유가 없다. 그냥 두면
            // 보스가 시신 자리를 왕복하며 밀어낸다.
            //
            // 단 흔적에 도착해 뒤지기 시작한 뒤에는 보지 않는다. 수색은 애초에 쫓을 표적이
            // 없어서 하는 것이다. 그 사람이 빠졌다는 것이 주변을 그만 볼 이유가 되지 않는다.
            if (!_reachedTrace && _survivor != null && !SurvivorRegistry.IsActive(_survivor))
            {
                GiveUp("표적 이탈");
                return false;
            }

            return true;
        }
    }

    // 포기한 직후 잠깐 서 있는 중인지. 그래프의 휴식 가지가 이 값으로 묶여 있다.
    public bool IsResting => Time.time < _restUntil;

    // 마지막으로 확인된 사람. 흔적을 따라가는 데는 쓰지 않고, 그 사람이 쓰러졌는지만 확인한다.
    public GameObject Survivor => _survivor;

    // 지금 서 있는 공간과 거기서 돌 수 있는 원의 크기. 디버그 표시용.
    public string RoomDescription
    {
        get
        {
            UndergroundModule room = EnsureMap() ? FindRoomAt(transform.position) : null;

            if (room == null || room.Bounds == null)
            {
                return "공간 모름";
            }

            Bounds bounds = room.Bounds.bounds;
            float half = Mathf.Min(bounds.extents.x, bounds.extents.z);

            return $"{room.name} 반지름 {half:0.0}m";
        }
    }

    // 지금 쫓고 있는 흔적의 자리. 디버그 표시가 읽는다.
    public Vector3 Trace => _trace;

    // 이 흔적이 어디서 왔는지. 표시용이고 판정에는 쓰지 않는다.
    public string TraceSource => _lifetime.FromNoise ? "소리" : "시야";

    // 디버그 표시용. 소리 갱신 제한이 얼마나 남았는지.
    public float NoiseGateRemaining => _lifetime.NoiseGateRemaining(Time.time);
    public float RestRemaining => Mathf.Max(0f, _restUntil - Time.time);

    // 왜 수색을 끝냈는지. 포기 경로가 여럿이라 밖에서는 구분이 안 된다. 디버그 표시용.
    public string LastGiveUpReason { get; private set; } = "없음";
    public float GiveUpAge => Time.time - _lastGiveUpTime;

    // 추적이 어디까지 왔는지. 디버그 표시용이라 상태를 바꾸지 않는다.
    public string SearchProgress
        => _reachedTrace
            ? $"주변 수색 {_searchPointCount - _searchPointsLeft}/{_searchPointCount} (후보 방 {_searchArea.Count}개)"
            : "흔적으로 이동 중";

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

        // 목적지로 고른 방만 기록하면, 가는 길에 지나친 방이 안 가본 곳으로 남아 나중에
        // 다시 목적지가 된다. 지금 서 있는 방을 계속 찍어야 실제로 들어가 본 곳이 남는다.
        if (EnsureMap())
        {
            RememberRoom(FindRoomAt(transform.position));
        }

        if (NoiseSystem.TryGetLoudest(transform.position, _hearingFactor, out Vector3 noise))
        {
            RecordNoise(noise);
        }
    }

    private bool EnsureMap()
    {
        if (_map == null)
        {
            _map = FindFirstObjectByType<UndergroundRandomMapGenerator>();
        }

        return _map != null;
    }

    // 소리 난 자리를 흔적으로 남긴다. 흔적이 없으면 새로 만들고, 있으면 옮긴다.
    //
    // 간격 제한이 핵심이다. 이게 없으면 사람이 0.4초마다 내는 소음이 그대로 흔적이 되어
    // 실시간 추적이 된다. 제한을 두면 보스가 향하는 곳은 늘 몇 초 전 자리다.
    private void RecordNoise(Vector3 position)
    {
        // 이미 다 본 곳(표식 안)에서 난 소리는 받지 않는다. 방금 훑고 나온 자리로
        // 되돌아가게 되고, 그게 같은 데를 계속 도는 것으로 보인다.
        //
        // 눈으로 본 것은 이 규칙을 타지 않는다. 표식 안에 서 있는 사람이 보이는데도
        // 무시하면 그건 다 본 것이 아니라 그냥 못 본 것이다.
        if (IsSwept(position))
        {
            return;
        }

        // 흔적으로 걸어가는 중이면 뒤에서 난 소리로는 방향을 틀지 않는다.
        //
        // 소리는 "반경 - 거리"가 가장 큰 것이 뽑힌다. 문 소리는 반경이 커서, 지나온 자리에
        // 남아 있는 문 소리가 앞쪽의 작은 발소리를 이긴다. 그러면 흔적이 뒤로 점프해서
        // 가던 길을 끊고 되돌아가고, 정작 가려던 자리는 확인도 못 한다.
        //
        // 도착해서 뒤지기 시작한 뒤에는 어느 쪽 소리든 받는다. 눈으로 본 것도 항상 받는다.
        if (_hasTrace && !_reachedTrace && IsBehindTrace(position))
        {
            return;
        }

        // 옮겨도 되는지(시야 우선 / 간격 제한)는 전부 TraceLifetime 이 판단한다.
        if (_lifetime.TryRecordNoise(Time.time))
        {
            SetTrace(position);
        }
    }

    // 지금 흔적으로 가는 방향을 기준으로 그 자리가 뒤쪽인지.
    private bool IsBehindTrace(Vector3 point)
    {
        Vector3 position = transform.position;

        Vector3 toTrace = _trace - position;
        Vector3 toPoint = point - position;
        toTrace.y = 0f;
        toPoint.y = 0f;

        if (toTrace.sqrMagnitude <= 0.0001f || toPoint.sqrMagnitude <= 0.0001f)
        {
            return false;
        }

        return Vector3.Dot(toPoint.normalized, toTrace.normalized) < 0f;
    }

    // 단서가 없을 때 보스가 향할 지점. 이게 보스의 기본 이동 방식이다.
    //
    // 정해진 목적지(방 중앙 웨이포인트)를 순서대로 도는 방식을 걷어내고 이걸로 대체했다.
    // 목적지가 미리 정해져 있으면 보스는 그 점까지 한 번에 걸어가 버려서, 가는 길에 아무것도
    // 살피지 않는다. 마침 그 직선이 숨은 사람 쪽이면 증거 없이 정확히 찾아오는 것처럼 보인다.
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
    public void GiveUp(string reason = "봐주기")
    {
        LastGiveUpReason = reason;
        _lastGiveUpTime = Time.time;

        // 버리기 전에 방향만 챙긴다. 포기했다고 방향까지 놓으면 사람이 사라진 쪽을 코앞에
        // 두고 엉뚱한 데를 훑는다. 그쪽으로 밀고 나가도록 한동안 붙잡아 둔다.
        Vector3 lostDirection = _traceDirection;

        ClearDestination();

        _restUntil = Time.time + _restDuration;
        _heading = lostDirection.sqrMagnitude > 0.0001f ? lostDirection : transform.forward;
        _wanderBias = _heading;
        _wanderBiasUntil = Time.time + _wanderBiasDuration;
    }

    // 순간이동 뒤에 부른다. 옮겨오기 전 목적지는 지도 반대편이라, 그대로 두면 도착하자마자
    // 왔던 곳으로 되돌아간다. 방향 편향도 저쪽 지형 기준이라 여기서는 의미가 없다.
    public void ForgetDestination()
    {
        ClearDestination();

        _heading = transform.forward;
        _wanderBias = Vector3.zero;
        _wanderBiasUntil = 0f;
        _visitedRooms.Clear();
        _swept.Clear();
    }

    private void ClearDestination()
    {
        _survivor = null;
        _hasTrace = false;
        _lifetime.Clear();
        _traceDirection = Vector3.zero;
        _traceApproach = Vector3.zero;

        _reachedTrace = false;
        _hasSearchPoint = false;
        _searchPointsLeft = 0;
        _searchedRooms.Clear();
        _searchArea.Clear();

        _hasWanderPoint = false;
    }

    // 만료 시각(_forgetTime)은 Record 가 정한다. 흔적은 "본 자리"이므로 둘 다 눈만 갱신한다.
    private void SetTrace(Vector3 position)
    {
        // 흔적이 옮겨간 방향이 곧 사람이 간 방향이다. 눈으로 따라갈 때든 소리가 이어질 때든 같다.
        //
        // 첫 흔적은 옮겨간 자리가 없으므로 보스에서 흔적을 향하는 방향을 쓴다. 북쪽에서 단서가
        // 잡혔으면 사람은 북쪽에 있는 것이고, 흔적이 끊긴 뒤에도 계속 북쪽을 뒤져야 한다.
        Vector3 delta = _hasTrace ? position - _trace : position - transform.position;
        delta.y = 0f;
        if (delta.sqrMagnitude > MovedThreshold * MovedThreshold)
        {
            _traceDirection = delta.normalized;
        }

        // 보스가 흔적으로 걸어갈 방향. 도착한 뒤 어느 쪽을 앞으로 볼지의 기준이 된다.
        //
        // 흔적끼리의 이동 방향(_traceDirection)만으로는 안 된다. 소리로 생긴 흔적은 이리저리
        // 튀어서 그 방향이 보스가 온 쪽을 가리키기도 하는데, 그러면 "앞쪽"이 실제로는 뒤가
        // 되어 뒤질 방이 하나도 안 남고 수색이 곧바로 끝난다.
        Vector3 approach = position - transform.position;
        approach.y = 0f;
        if (approach.sqrMagnitude > MovedThreshold * MovedThreshold)
        {
            _traceApproach = approach.normalized;
        }

        _trace = position;
        _hasTrace = true;

        // 새 흔적이 생겼으면 이전 수색 진행은 의미가 없다. 처음부터 다시 쫓는다.
        _reachedTrace = false;
        _hasSearchPoint = false;
        _searchPointsLeft = _searchPointCount;
        _searchedRooms.Clear();

        // 쫓을 것이 생겼으니 휴식은 끝난다.
        _restUntil = 0f;
        _hasWanderPoint = false;
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

            // 흔적에 닿았다. 여기서부터가 수색이므로 뒤질 시간을 새로 받는다.
            // 여기 서서 한 번 본 것이므로 표식을 찍는다.
            MarkSwept(_trace);

            // 진행 방향을 사람이 간 쪽으로 맞춘다. 흔적까지 걸어오는 동안에는 이 값이
            // 갱신되지 않아서, 마지막으로 훑고 다닐 때의 방향이 그대로 남아 있다.
            // 그게 반대쪽을 가리키고 있으면 앞쪽 90도 안에 방이 하나도 안 잡혀서,
            // 수색 횟수가 그대로 남은 채 흔적 앞에 서 있게 된다.
            _heading = _traceApproach.sqrMagnitude > 0.0001f
                ? _traceApproach
                : ResolveTraceDirection();
            _searchForward = _heading;

            _reachedTrace = true;
            _hasSearchPoint = false;
            _searchAngle = 0f;
            _searchTurn = Random.value < 0.5f ? -1f : 1f;

            // 흔적이 있던 방은 뒤지지 않는다. 사람이 지나간 자리이지 있는 자리가 아니다.
            // 뒤질 곳은 그 방에서 문으로 이어진 방들이다.
            if (EnsureMap())
            {
                EnsureRoomLinks();

                UndergroundModule traceRoom = FindRoomAt(_trace);
                if (traceRoom != null)
                {
                    _searchedRooms.Add(traceRoom);
                }

                CollectSearchArea(traceRoom, _searchDepth);
            }
        }

        // 2단계. 가는 중이면 목표를 바꾸지 않는다. 매번 바꾸면 방향이 흔들려 제자리를 맴돈다.
        if (_hasSearchPoint && FlatDistance(position, _searchPoint) > _arriveDistance)
        {
            return _searchPoint;
        }

        // 직전 지점에 도착했다는 뜻이다. 선 자리마다 표식을 남긴다. 걸음 수와 표식 수가
        // 맞아야 어디까지 뒤졌는지가 눈에 보인다. 방을 거르는 것은 표식이 아니라
        // 뒤진 방 목록이 하므로, 표식을 다 찍어도 다음 방이 막히지 않는다.
        if (_hasSearchPoint)
        {
            MarkSwept(_searchPoint);
            _hasSearchPoint = false;
        }

        // 정해진 횟수를 다 뒤졌으면 포기한다.
        if (_searchPointsLeft <= 0)
        {
            GiveUp("수색 횟수 소진");
            return PickFallbackPoint();
        }

        _searchPointsLeft--;
        _searchPoint = PickPointAroundTrace();
        _hasSearchPoint = true;
        return _searchPoint;
    }

    // 흔적이 끊긴 뒤 뒤질 방. 흔적이 있던 방(A)이 아니라 그 앞쪽 방들(B, C, D)을 고른다.
    //
    // 흔적 자리는 뒤지지 않는다. 거기는 사람이 지나간 자리이지 있는 자리가 아니다.
    // 흔적 둘레를 돌면 정작 앞쪽 방을 볼 횟수를 그 자리에서 다 써 버린다.
    //
    // 앞뒤는 "흔적에서 봤을 때"로 가른다. 지금 보는 방향으로 가르면, B 를 보고 나와서
    // C 로 가려 할 때 갈림길로 되돌아가는 첫 걸음이 뒤쪽으로 잡혀 C 와 D 가 통째로 빠진다.
    // 갈림길로 돌아 나오는 것은 빠꾸가 아니다. 빠꾸는 흔적 쪽으로 되돌아가는 것이다.
    private Vector3 PickPointAroundTrace()
    {
        if (!EnsureMap())
        {
            return TryPickCirclePoint(out Vector3 circle) ? circle : PickFallbackPoint();
        }

        // 1순위. 흔적 앞쪽의 안 뒤진 방.
        if (TryPickRoom(true, out Vector3 point))
        {
            return point;
        }

        // 2순위. 방향을 가리지 않는다. 막다른 갈림길이면 옆이나 뒤 방도 봐야 한다.
        // 흔적이 있던 방은 어차피 뒤진 목록에 있으므로 거기로 되돌아가지는 않는다.
        if (TryPickRoom(false, out point))
        {
            return point;
        }

        // 3순위. 문 범위를 한 칸 넓혀서 다시 본다.
        if (ExpandSearchArea() && (TryPickRoom(true, out point) || TryPickRoom(false, out point)))
        {
            return point;
        }

        // 4순위. 방을 못 찾아도 서 있지는 않는다.
        return PickFallbackPoint();
    }

    // 뒤질 방 하나. forwardOnly 가 false 면 앞뒤를 가리지 않는다.
    private bool TryPickRoom(bool forwardOnly, out Vector3 point)
    {
        Vector3 position = transform.position;

        point = position;
        float bestScore = float.NegativeInfinity;
        bool found = false;

        foreach (UndergroundModule module in _searchArea)
        {
            // 이미 뒤진 방은 방 자체로 거른다. 표식 반경으로 거르면 방 크기에 따라
            // 걸리기도 하고 안 걸리기도 해서, 같은 방을 다시 고르는 일이 생긴다.
            if (module == null || module.Bounds == null || _searchedRooms.Contains(module) ||
                !TryResolveRoomPoint(module, position, out Vector3 candidate))
            {
                continue;
            }

            Vector3 fromTrace = candidate - _trace;
            fromTrace.y = 0f;

            float spread = fromTrace.magnitude;
            if (spread <= 0.0001f)
            {
                continue;
            }

            // 흔적보다 뒤에 있는 방은 이미 지나온 쪽이다.
            float alignment = Vector3.Dot(fromTrace / spread, _searchForward);
            if (forwardOnly && alignment < MinSearchAlignment)
            {
                continue;
            }

            // 실제로 걸어갈 길을 뽑는다. 못 가는 곳이면 후보가 아니다. 값도 직선이 아니라
            // 길이로 써야, 벽 너머라 가깝게 보이는 방이 먼저 뽑히지 않는다.
            if (!TryMeasurePath(position, candidate, out float distance) ||
                distance < _arriveDistance)
            {
                continue;
            }

            float score = alignment * SearchDirectionBonus - distance;

            if (score > bestScore)
            {
                bestScore = score;
                point = candidate;
                found = true;
                _nextSearchRoom = module;
            }
        }

        if (found && _nextSearchRoom != null)
        {
            _searchedRooms.Add(_nextSearchRoom);
            UpdateHeading(position, point);
        }

        return found;
    }

    // 지도가 없을 때. 흔적 둘레를 한 방향으로 돌면서 지점을 고른다.
    //
    // 각도를 매번 무작위로 고르면 지점끼리는 떨어져 있어도 오가는 길이 흔적 가운데에서
    // 계속 엇갈려서, 같은 자리를 몇 번씩 밟는 뒤엉킨 선이 된다.
    private bool TryPickCirclePoint(out Vector3 point)
    {
        Vector3 position = transform.position;
        Vector3 forward = ResolveTraceDirection();
        float step = 360f / Mathf.Max(2, _searchPointCount);

        for (int attempt = 0; attempt < SamplingAttempts; attempt++)
        {
            // 막힌 각도는 건너뛰되 도는 쪽은 유지한다. 되돌아가는 대신 더 돌아본다.
            _searchAngle += _searchTurn * step;
            Vector3 direction = Quaternion.Euler(0f, _searchAngle, 0f) * forward;

            if (!TryResolveCirclePoint(_trace, FallbackSearchRadius, direction, position, out Vector3 candidate))
            {
                continue;
            }

            UpdateHeading(position, candidate);
            point = candidate;
            return true;
        }

        // 둘레가 다 막혔거나 이미 뒤진 자리뿐이다. 여기서 더 할 것은 없다.
        point = position;
        return false;
    }

    // 중심에서 그 방향으로 반지름만큼 떨어진 점. 벽에 걸리면 조금 안쪽에서 한 번 더 본다.
    private bool TryResolveCirclePoint(
        Vector3 center, float radius, Vector3 direction, Vector3 position, out Vector3 point)
    {
        foreach (float scale in SearchRadiusScales)
        {
            Vector3 candidate = center + direction * (radius * scale);

            if (!NavMesh.SamplePosition(candidate, out NavMeshHit hit, NavSampleRadius, NavMesh.AllAreas))
            {
                continue;
            }

            // 지금 선 자리와 거의 같으면 이동이 0m로 끝나고 훑어보기만 반복된다.
            if (FlatDistance(hit.position, position) < _arriveDistance)
            {
                continue;
            }

            point = hit.position;
            return true;
        }

        point = position;
        return false;
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
    private Vector3 PickWanderPoint()
    {
        // 방이 있으면 방 단위로 움직인다. 한 방에 목적지 하나라, 들어가서 가로질러 문으로
        // 나가는 모양이 된다. 좌표를 찍는 방식은 방 안에서 목적지가 서너 번 잡혀 제자리를
        // 오갔다. 방 하나를 훑는 데 목적지가 여러 개 필요하지 않다.
        if (TryPickRoomPoint(out Vector3 point) || TryPickStepPoint(out point))
        {
            return point;
        }

        return PickFallbackPoint();
    }

    // 갈 곳을 못 정했을 때 마지막으로 쓰는 자리. 어디든 걸어갈 수 있는 곳 하나를 찾는다.
    //
    // 제자리를 목적지로 주면 경로가 생기지 않아서(hasPath=False) 보스가 그대로 굳고,
    // 굳음 감시가 3초마다 행동 트리를 재시작한다. 갈 곳이 마땅치 않다는 것은 서 있을
    // 이유가 되지 못한다. 조건에 맞는 곳이 없으면 조건을 풀어서라도 움직인다.
    private Vector3 PickFallbackPoint()
    {
        Vector3 position = transform.position;
        Vector3 forward = ResolveHeading();

        for (int attempt = 0; attempt < SamplingAttempts; attempt++)
        {
            // 방향은 고루 돌려가며, 거리는 조금씩 늘려가며 본다.
            float angle = 360f / SamplingAttempts * attempt;
            float radius = _wanderStepMin * (1f + attempt * 0.5f);
            Vector3 candidate = position + Quaternion.Euler(0f, angle, 0f) * forward * radius;

            if (!NavMesh.SamplePosition(candidate, out NavMeshHit hit, NavSampleRadius, NavMesh.AllAreas) ||
                !TryMeasurePath(position, hit.position, out float distance) ||
                distance < _arriveDistance)
            {
                continue;
            }

            UpdateHeading(position, hit.position);
            return hit.position;
        }

        return position;
    }

    // 다음에 들어갈 방의 자리. 지도가 없으면 false.
    private bool TryPickRoomPoint(out Vector3 point)
    {
        point = Vector3.zero;

        if (!EnsureMap())
        {
            return false;
        }

        Vector3 position = transform.position;
        UndergroundModule current = FindRoomAt(position);
        RememberRoom(current);

        if (!TryFindNextRoom(position, current, out point))
        {
            // 갈 수 있는 방을 전부 돌았다. 기록을 비우고 처음부터 다시 돈다.
            // 수색 자국도 같이 푼다. 안 그러면 그 방들만 계속 빠져서 갈 곳이 없다.
            _visitedRooms.Clear();
            _swept.Clear();
            RememberRoom(current);

            if (!TryFindNextRoom(position, current, out point))
            {
                return false;
            }
        }

        UpdateHeading(position, point);
        return true;
    }

    // 가깝고, 가던 방향 쪽에 있고, 최근에 들르지 않은 방을 고른다.
    private bool TryFindNextRoom(Vector3 position, UndergroundModule current, out Vector3 point)
    {
        Vector3 forward = ResolveWanderDirection();

        point = Vector3.zero;
        float bestScore = float.NegativeInfinity;
        bool found = false;

        foreach (UndergroundModule module in _map.PlacedModules)
        {
            if (module == null || module.Bounds == null ||
                module == current || _visitedRooms.Contains(module))
            {
                continue;
            }

            if (!TryResolveRoomPoint(module, position, out Vector3 candidate) || IsSwept(candidate))
            {
                continue;
            }

            // 실제로 걸어갈 길로 잰다. 직선으로 재면 못 가는 방이 뽑혀서, 도착도 못 하고
            // 방문 기록도 안 남는다. 그러면 다음에 또 그 방이 뽑힌다.
            Vector3 delta = candidate - position;
            delta.y = 0f;

            if (delta.sqrMagnitude <= 0.0001f ||
                !TryMeasurePath(position, candidate, out float distance) ||
                distance < _arriveDistance)
            {
                continue;
            }

            float score = Vector3.Dot(delta.normalized, forward) * RoomDirectionBonus - distance;

            if (score > bestScore)
            {
                bestScore = score;
                point = candidate;
                found = true;
            }
        }

        return found;
    }

    // 방 안의 한 자리. 한가운데가 아니라 들어오는 쪽에서 건너편으로 물린 곳을 잡는다.
    //
    // 정확히 중앙을 찍으면 방마다 가운데까지 걸어갔다가 거기 서서 방향을 트는, 자로 잰 듯한
    // 길이 나온다. 지나갈 쪽으로 물려 두면 들어와서 가로질러 나가는 모양이 된다.
    //
    // NavMesh 위로 끌어당겨서 기둥 속이나 벽 너머가 되지 않게 한다.
    private bool TryResolveRoomPoint(UndergroundModule module, Vector3 from, out Vector3 point)
    {
        Bounds bounds = module.Bounds.bounds;

        Vector3 center = bounds.center;
        center.y = bounds.min.y;

        Vector3 approach = center - from;
        approach.y = 0f;

        Vector3 crossed = center;

        if (approach.sqrMagnitude > 0.0001f)
        {
            Vector3 extents = new Vector3(bounds.extents.x, 0f, bounds.extents.z);
            crossed += Vector3.Scale(approach.normalized, extents) * RoomCrossFactor;
        }

        // 물린 자리가 벽 속이면 한가운데로 물러난다. 그 한 점이 빗나갔다고 방 전체를
        // 후보에서 빼면, 뒤질 방이 하나도 안 남아 수색이 그냥 끝나 버린다.
        if (NavMesh.SamplePosition(crossed, out NavMeshHit hit, NavSampleRadius, NavMesh.AllAreas) ||
            NavMesh.SamplePosition(center, out hit, NavSampleRadius, NavMesh.AllAreas))
        {
            point = hit.position;
            return true;
        }

        point = center;
        return false;
    }

    // 문으로 이어진 방 관계를 만든다. 같은 문을 양쪽에서 쓰고 있으면 이어진 것이다.
    //
    // 생성기는 소켓에 "이어졌다"는 표시만 남기고 상대가 누구인지는 들고 있지 않다.
    // 그래서 이어진 소켓끼리 같은 자리에 겹쳐 있는지로 짝을 찾는다.
    private void EnsureRoomLinks()
    {
        IReadOnlyList<UndergroundModule> modules = _map.PlacedModules;

        // 라운드가 바뀌면 방이 통째로 갈린다. 개수가 다르면 다시 만든다.
        if (_roomLinks.Count == modules.Count)
        {
            return;
        }

        _roomLinks.Clear();

        foreach (UndergroundModule module in modules)
        {
            if (module != null)
            {
                _roomLinks[module] = new List<UndergroundModule>();
            }
        }

        for (int i = 0; i < modules.Count; i++)
        {
            for (int j = i + 1; j < modules.Count; j++)
            {
                if (modules[i] == null || modules[j] == null || !SharesDoor(modules[i], modules[j]))
                {
                    continue;
                }

                _roomLinks[modules[i]].Add(modules[j]);
                _roomLinks[modules[j]].Add(modules[i]);
            }
        }
    }

    private static bool SharesDoor(UndergroundModule a, UndergroundModule b)
    {
        foreach (DoorSocket first in a.DoorSockets)
        {
            if (first == null || !first.IsConnected)
            {
                continue;
            }

            foreach (DoorSocket second in b.DoorSockets)
            {
                if (second != null && second.IsConnected &&
                    FlatDistance(first.transform.position, second.transform.position) < DoorMatchDistance)
                {
                    return true;
                }
            }
        }

        return false;
    }

    // 흔적이 있던 방에서 문을 타고 정해진 칸수만큼 뻗어 나간 방들.
    //
    // 복도는 칸수로 세지 않고 그냥 통과한다. 뒤질 공간이 아니라 지나가는 길이라서다.
    // 복도를 한 칸으로 세면 "옆방"이 두 칸 밖이 되어, 정작 볼 방이 범위 밖으로 밀린다.
    private void CollectSearchArea(UndergroundModule start, int depthLimit)
    {
        _searchArea.Clear();
        _searchRoot = start;
        _searchAreaDepth = depthLimit;

        if (start == null || !_roomLinks.ContainsKey(start))
        {
            return;
        }

        var visited = new HashSet<UndergroundModule> { start };
        var frontier = new List<UndergroundModule> { start };

        for (int depth = 0; depth < depthLimit && frontier.Count > 0; depth++)
        {
            var rooms = new List<UndergroundModule>();
            var pending = new List<UndergroundModule>(frontier);

            while (pending.Count > 0)
            {
                UndergroundModule current = pending[pending.Count - 1];
                pending.RemoveAt(pending.Count - 1);

                foreach (UndergroundModule neighbour in _roomLinks[current])
                {
                    if (neighbour == null || !visited.Add(neighbour))
                    {
                        continue;
                    }

                    if (IsCorridor(neighbour))
                    {
                        pending.Add(neighbour);
                    }
                    else
                    {
                        rooms.Add(neighbour);
                    }
                }
            }

            _searchArea.AddRange(rooms);
            frontier = rooms;
        }
    }

    // 문 범위를 한 칸 넓힌다. 넓어졌으면 true.
    private bool ExpandSearchArea()
    {
        if (_searchRoot == null)
        {
            return false;
        }

        int before = _searchArea.Count;

        _searchAreaDepth++;
        CollectSearchArea(_searchRoot, _searchAreaDepth);

        return _searchArea.Count > before;
    }

    // 복도인지. 한쪽이 눈에 띄게 짧은 조각이면 복도로 본다.
    private static bool IsCorridor(UndergroundModule module)
    {
        if (module == null || module.Bounds == null)
        {
            // 모르는 조각은 지나가는 길로 친다. 뒤지러 들어갔다가 헛걸음하는 쪽이 더 나쁘다.
            return true;
        }

        Vector3 extents = module.Bounds.bounds.extents;
        float shortSide = Mathf.Min(extents.x, extents.z);
        float longSide = Mathf.Max(extents.x, extents.z);

        return shortSide <= 0.0001f || longSide / shortSide >= CorridorAspect;
    }

    private UndergroundModule FindRoomAt(Vector3 position)
    {
        foreach (UndergroundModule module in _map.PlacedModules)
        {
            if (module == null || module.Bounds == null)
            {
                continue;
            }

            // 높이는 무시한다. 층이 겹치지 않으므로 평면으로만 봐도 방이 갈린다.
            Bounds bounds = module.Bounds.bounds;
            Vector3 flat = new Vector3(position.x, bounds.center.y, position.z);

            if (bounds.Contains(flat))
            {
                return module;
            }
        }

        return null;
    }

    private void MarkSwept(Vector3 position)
    {
        float now = Time.time;

        for (int i = _swept.Count - 1; i >= 0; i--)
        {
            if (now >= _swept[i].ExpireTime)
            {
                _swept.RemoveAt(i);
            }
        }

        _swept.Add(new SweptZone { Position = position, ExpireTime = now + _sweptSeconds });
    }

    // 거기까지 실제로 걸어갈 수 있는지와 그 길의 길이.
    //
    // NavMesh.SamplePosition 은 그 좌표가 바닥 위인지만 보고 길이 있는지는 보지 않는다.
    // 못 가는 지점을 골라 도착을 못 하고 굳던 것도, 벽 너머라 가까워 보이는 방이 먼저
    // 뽑히던 것도 전부 직선으로 쟀기 때문이다.
    private bool TryMeasurePath(Vector3 from, Vector3 to, out float length)
    {
        length = 0f;

        if (!NavMesh.CalculatePath(from, to, NavMesh.AllAreas, _path) ||
            _path.status != NavMeshPathStatus.PathComplete ||
            _path.corners.Length < 2)
        {
            return false;
        }

        Vector3[] corners = _path.corners;

        for (int i = 1; i < corners.Length; i++)
        {
            length += FlatDistance(corners[i - 1], corners[i]);
        }

        return true;
    }

    private bool IsSwept(Vector3 point)
    {
        float now = Time.time;

        foreach (SweptZone zone in _swept)
        {
            if (now < zone.ExpireTime && FlatDistance(zone.Position, point) < _sweptRadius)
            {
                return true;
            }
        }

        return false;
    }

    private void RememberRoom(UndergroundModule room)
    {
        if (room != null && !_visitedRooms.Contains(room))
        {
            _visitedRooms.Add(room);
        }
    }

    // 지도가 없을 때. 정한 방향으로 한 걸음씩 나아간다.
    private bool TryPickStepPoint(out Vector3 point)
    {
        Vector3 position = transform.position;

        // 정한 방향으로 쭉 간다. 걸음마다 방향을 새로 뽑으면, 앞쪽이 막혔을 때 뒤쪽 후보가
        // 뽑혀서 갔던 곳으로 되돌아온다. 그게 앞뒤로 왔다 갔다 하는 것으로 보인다.
        if (TryStep(position, ResolveWanderDirection(), out point))
        {
            return true;
        }

        // 앞이 막혔을 때만 방향을 새로 정한다. 시도마다 각도를 넓혀서, 막다른 곳에서는
        // 결국 돌아 나올 수 있게 한다.
        for (int attempt = 1; attempt < SamplingAttempts; attempt++)
        {
            Vector3 turned = Quaternion.Euler(0f, RandomTurn(attempt), 0f) * ResolveHeading();

            if (TryStep(position, turned, out point))
            {
                return true;
            }
        }

        // 사방이 다 막혔다. 위에서 다른 방법으로 갈 곳을 찾는다.
        point = position;
        return false;
    }

    // 그 방향으로 한 걸음 나갈 수 있으면 true. 성공하면 진행 방향도 거기에 맞춘다.
    //
    // 실제로 걷는 거리는 (뽑힌 거리 - 도착 판정 거리)다. NavMesh 로 끌려오면서 코앞으로
    // 당겨진 후보를 받으면 몇십 cm 걷고 다음 지점을 새로 뽑아 제자리에서 찔끔거린다.
    private bool TryStep(Vector3 from, Vector3 direction, out Vector3 point)
    {
        point = from;

        Vector3 candidate = from + direction.normalized * Random.Range(_wanderStepMin, _wanderStepMax);

        if (!NavMesh.SamplePosition(candidate, out NavMeshHit hit, NavSampleRadius, NavMesh.AllAreas) ||
            FlatDistance(hit.position, from) < _wanderStepMin)
        {
            return false;
        }

        UpdateHeading(from, hit.position);
        point = hit.position;
        return true;
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

    // 훑을 방향. 흔적이 끊긴 쪽으로 매 걸음 조금씩 당긴다.
    //
    // 씨앗만 심어 두면 걸음마다 ±60° 씩 어긋나 몇 걸음 만에 반대쪽을 보고 있다.
    // 매번 당겨야 좌우로 흔들리면서도 사람이 간 쪽으로 나아가는 모양이 나온다.
    private Vector3 ResolveWanderDirection()
    {
        Vector3 heading = ResolveHeading();

        if (Time.time >= _wanderBiasUntil || _wanderBias.sqrMagnitude <= 0.0001f)
        {
            return heading;
        }

        return Vector3.Slerp(heading, _wanderBias, _wanderBiasWeight).normalized;
    }

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
            // 흔적과 지금 향하는 지점.
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(_trace, 0.8f);

            // 사람이 가던 방향. 수색은 이 앞쪽 방들만 본다.
            Gizmos.color = new Color(1f, 0.5f, 0f);
            Gizmos.DrawLine(_trace, _trace + _searchForward * 5f);

            // 이번 수색에서 볼 방들.
            Gizmos.color = Color.cyan;
            foreach (UndergroundModule room in _searchArea)
            {
                if (room != null && room.Bounds != null)
                {
                    Gizmos.DrawWireCube(room.Bounds.bounds.center, room.Bounds.bounds.size);
                }
            }

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
