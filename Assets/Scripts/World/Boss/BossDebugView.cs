using System.Text;
using Unity.Behavior;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;

// 보스가 무엇을 듣고 무엇을 보는지 화면에 그려주는 개발용 표시.
//
// 소음은 좌표와 "들리는 거리"만 있는 눈에 안 보이는 값이라, 왜 보스가 왔는지 왜 안 왔는지
// 플레이만으로는 알 수 없다. 그걸 눈으로 확인하기 위한 것이다.
//
// 판정은 전부 서버에서만 돌기 때문에 의미 있는 값이 나오는 것도 호스트뿐이다.
// 클라이언트에서는 소음 목록이 비어 있다.
[RequireComponent(typeof(BossPerception))]
public class BossDebugView : MonoBehaviour
{
    // F9는 기존 디버그 메뉴가 이미 쓰고 있어서 겹치지 않게 F8로 둔다.
    [Tooltip("이 키로 표시를 켜고 끈다.")]
    [SerializeField] private Key _toggleKey = Key.F8;

    [Tooltip("화면 왼쪽 위 글자 표시. 끄면 씬 기즈모만 그린다.")]
    [SerializeField] private bool _showOverlay = true;

    [Tooltip("보스가 향하는 지점과 경로를 게임 화면에 직접 찍는다. 벽 뒤여도 보인다.")]
    [SerializeField] private bool _showWorldMarkers = true;


    private BehaviorGraphAgent _brain;
    private BossPerception _perception;
    private BossTargetMemory _memory;
    private BossDormancy _dormancy;
    private BossAttack _attack;
    private BossThreatReporter _threat;
    private NavMeshAgent _agent;

    // 직전 갱신 때의 목적지. 매번 바뀌면 사람을 실시간으로 따라가는 중이라는 뜻이다.
    private Vector3 _lastDestination;
    private bool _hasLastDestination;

    // 화면에 찍을 것들. OnGUI 는 한 프레임에 여러 번 불리므로 글자와 같은 주기로만 다시 모은다.
    private Vector3[] _pathCorners = System.Array.Empty<Vector3>();
    private Vector3 _markerDestination;
    private bool _hasMarkerDestination;
    private GUIStyle _markerStyle;

    // 이름표를 바닥 방향으로 밀어내는 거리. 위에서 내려다볼 때 글자가 겹치지 않게 한다.
    private static readonly Vector3 LabelSideDestination = new Vector3(2.5f, 0f, 0f);
    private static readonly Vector3 LabelSideTrace = new Vector3(-2.5f, 0f, 2.5f);

    // 표시 내용을 다시 만드는 간격(초). 매 프레임 만들면 디버그 표시가 오히려 부하가 된다.
    private const float TextRefreshInterval = 0.2f;

    private readonly StringBuilder _text = new();
    private float _nextTextTime;
    private GUIStyle _style;

    private void Awake()
    {
        _brain = GetComponent<BehaviorGraphAgent>();
        _perception = GetComponent<BossPerception>();
        _memory = GetComponent<BossTargetMemory>();
        _dormancy = GetComponent<BossDormancy>();
        _attack = GetComponent<BossAttack>();
        _threat = GetComponent<BossThreatReporter>();
        _agent = GetComponent<NavMeshAgent>();

        // 필드 타입이 바뀌면 예전 직렬화 값이 그대로 남아 Key 범위를 벗어난다. 그 값을 그대로
        // 인덱서에 넣으면 매 프레임 예외가 쏟아지므로, 여기서 한 번 걸러 기본값으로 되돌린다.
        if (!System.Enum.IsDefined(typeof(Key), _toggleKey))
        {
            Debug.LogWarning($"[보스 디버그] 토글 키 값({(int)_toggleKey})이 잘못돼 F8로 되돌립니다.", this);
            _toggleKey = Key.F8;
        }
    }

    private void Update()
    {
        // 이 프로젝트는 Input System 패키지를 쓴다. 구 Input 클래스는 예외를 던진다.
        if (Keyboard.current != null && Keyboard.current[_toggleKey].wasPressedThisFrame)
        {
            // 상태는 DebugOverlayToggle 이 들고 있다. 플레이어 쪽 표시와 함께 움직여야 하는데,
            // 보스와 플레이어 오브젝트의 수명이 달라서 각자 들고 있으면 어긋난다.
            DebugOverlayToggle.RequestToggle();
        }
    }

    private void OnGUI()
    {
        if (!DebugOverlayToggle.Shown || !_showOverlay)
        {
            return;
        }

        // OnGUI 는 한 프레임에 여러 번(Layout/Repaint) 불린다. 글자를 만들 때 시야 판정과
        // 레이캐스트가 돌기 때문에, 그릴 때만 그리고 내용은 간격을 두고 다시 만든다.
        if (Event.current.type != EventType.Repaint)
        {
            return;
        }

        _style ??= new GUIStyle(GUI.skin.label)
        {
            fontSize = 13,
            richText = false,
            normal = { textColor = Color.white }
        };

        if (Time.unscaledTime >= _nextTextTime)
        {
            _nextTextTime = Time.unscaledTime + TextRefreshInterval;
            _text.Clear();
            CollectWorldMarkers();
            AppendState();
            AppendChanceState();
            AppendDestination();
            AppendHeartbeatThreat();
            AppendGraphState();
            AppendNoises();
        }

        var area = new Rect(12f, 12f, 460f, Screen.height - 24f);
        GUI.color = new Color(0f, 0f, 0f, 0.55f);
        GUI.DrawTexture(area, Texture2D.whiteTexture);
        GUI.color = Color.white;
        GUI.Label(new Rect(area.x + 8f, area.y + 6f, area.width - 16f, area.height - 12f), _text.ToString(), _style);

        DrawWorldMarkers();
    }

    // 보스가 어디로 가는지를 게임 화면에 직접 찍는다.
    //
    // 씬 뷰 기즈모는 플레이 중에 볼 수 없고, 글자로 나온 좌표만으로는 그게 어디인지 감이 안 온다.
    // "왜 이쪽으로 오지"를 눈으로 확인하려면 목적지와 경로가 화면에 보여야 한다.
    //
    // 판정이 서버에만 있으므로 호스트에서만 의미가 있다. 벽 뒤도 그대로 비친다(가림 처리 안 함).
    private void DrawWorldMarkers()
    {
        if (!_showWorldMarkers || !_hasMarkerDestination)
        {
            return;
        }

        Camera camera = Camera.main;
        if (camera == null)
        {
            return;
        }

        _markerStyle ??= new GUIStyle(GUI.skin.label) { fontSize = 12, alignment = TextAnchor.MiddleLeft };

        // 지나온 경로부터 그려서 목적지 표시가 위에 오게 한다.
        for (int i = 0; i + 1 < _pathCorners.Length; i++)
        {
            DrawWorldLine(camera, _pathCorners[i], _pathCorners[i + 1], new Color(1f, 0.6f, 0.1f, 0.9f));
        }

        DrawMarker(camera, transform.position + Vector3.up * 2.4f, Color.magenta, "보스 눈");
        DrawMarker(camera, _markerDestination, Color.red, "목적지");

        if (_memory != null && _memory.HasMemory)
        {
            DrawMarker(camera, _memory.Trace, Color.yellow, "흔적");
        }

        foreach (NoiseSystem.Noise noise in NoiseSystem.ActiveNoises)
        {
            if (noise.ExpireTime <= Time.time)
            {
                continue;
            }

            DrawMarker(camera, noise.Position, Color.cyan, noise.Kind);
        }
    }

    // 월드 좌표 한 점을 화면에 네모와 이름으로 찍는다. 카메라 뒤면 그리지 않는다.
    private void DrawMarker(Camera camera, Vector3 world, Color color, string label)
    {
        if (!TryProject(camera, world, out Vector2 point))
        {
            return;
        }

        GUI.color = color;
        GUI.DrawTexture(new Rect(point.x - 5f, point.y - 5f, 10f, 10f), Texture2D.whiteTexture);
        GUI.Label(new Rect(point.x + 9f, point.y - 9f, 220f, 18f), label, _markerStyle);
        GUI.color = Color.white;
    }

    // 두 점 사이를 점을 찍어 잇는다. OnGUI 에는 선을 긋는 기능이 없어서 나눠 찍는다.
    private void DrawWorldLine(Camera camera, Vector3 from, Vector3 to, Color color)
    {
        const int Steps = 12;

        GUI.color = color;
        for (int i = 0; i <= Steps; i++)
        {
            Vector3 world = Vector3.Lerp(from, to, i / (float)Steps);
            if (TryProject(camera, world, out Vector2 point))
            {
                GUI.DrawTexture(new Rect(point.x - 2f, point.y - 2f, 4f, 4f), Texture2D.whiteTexture);
            }
        }

        GUI.color = Color.white;
    }

    // 월드 좌표를 GUI 좌표로. GUI 는 y 가 위에서부터라 화면 높이에서 빼야 한다.
    private static bool TryProject(Camera camera, Vector3 world, out Vector2 point)
    {
        Vector3 screen = camera.WorldToScreenPoint(world);
        point = new Vector2(screen.x, Screen.height - screen.y);
        return screen.z > 0f;
    }

    // 보스가 지금 실제로 걸어갈 길. NavMeshAgent 가 계산한 경로를 그대로 그린다.
    //
    // 보스가 움직이면 경로도 매 프레임 새로 계산되므로 이 선은 보스를 따라 줄어들고, 목적지가
    // 바뀌면 통째로 다시 그려진다. 지나온 자리를 남기지 않는 이유가 이것이다. 남은 자취는
    // 보스가 멈춰 있어도 그대로 떠 있어서, 지금 어디로 가는지와 아무 상관이 없다.
    //
    // 선을 보스의 현재 위치에서 시작한다. 경로의 첫 꼭짓점은 계산 시점의 자리라 보스가
    // 걸어가는 동안 몸에서 떨어져 보인다.
    private void DrawPathGizmos()
    {
        if (!_agent.hasPath && !_agent.pathPending)
        {
            return;
        }

        Vector3[] corners = _agent.path.corners;
        if (corners.Length == 0)
        {
            return;
        }

        const float Lift = 0.2f;
        Gizmos.color = new Color(1f, 0.6f, 0.1f);

        Vector3 previous = transform.position + Vector3.up * Lift;

        for (int i = 0; i < corners.Length; i++)
        {
            Vector3 corner = corners[i] + Vector3.up * Lift;
            Gizmos.DrawLine(previous, corner);
            previous = corner;

            // 꺾이는 지점. 위에서 내려다볼 때 어디서 도는지가 보인다. 마지막 점은 목적지라
            // 따로 크게 그려지므로 여기서는 뺀다.
            if (i + 1 < corners.Length)
            {
                DrawFlatCircle(corner, 0.4f);
            }
        }
    }

    // 바닥과 평행한 원. 위에서 내려다볼 때 지점이 보이게 하려는 것이다.
    // Gizmos.DrawWireSphere 는 위에서 보면 원으로 보이지만 작을 때 뭉개져서 따로 그린다.
    private static void DrawFlatCircle(Vector3 center, float radius)
    {
        // 반경이 클수록 조각을 늘린다. 소음 반경(수십 m)을 12조각으로 그리면 각진 다각형이 된다.
        int segments = Mathf.Clamp(Mathf.CeilToInt(radius * 4f), 12, 48);
        Vector3 previous = center + new Vector3(radius, 0f, 0f);

        for (int i = 1; i <= segments; i++)
        {
            float angle = Mathf.PI * 2f * i / segments;
            Vector3 next = center + new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
            Gizmos.DrawLine(previous, next);
            previous = next;
        }
    }

    // 지점 위로 기둥을 세우고 이름을 붙인다. 위에서 내려다보는 뷰에서 지점을 찾기 위한 것이다.
    // 지점 위로 기둥을 세우고 이름을 붙인다.
    //
    // 이름표를 바닥과 나란한 방향으로도 밀어낸다. 높이만 다르게 두면 위에서 똑바로
    // 내려다볼 때 전부 한 점으로 뭉쳐서 글자가 겹쳐 읽을 수 없다.
    private static void DrawPost(Vector3 position, float height, Vector3 labelSide, string label)
    {
        Gizmos.DrawLine(position, position + Vector3.up * height);
#if UNITY_EDITOR
        UnityEditor.Handles.color = Gizmos.color;
        UnityEditor.Handles.Label(position + Vector3.up * (height + 0.4f) + labelSide, label);
#endif
    }

    // 지금 어느 가지가 목적지를 찍었는지.
    //
    // NavMeshAgent 에는 훑고 다니는 중에도 목적지가 늘 박혀 있다. 그래서 "목적지가 어디인가"만으로는
    // 아무것도 가릴 수 없다. 그 목적지를 누가 찍었는지가 유일하게 의미 있는 정보다.
    //
    // 실행 중인 노드 종류로 판별한다. 이동 노드는 둘뿐이고 성격이 정반대다.
    //  - Navigate To Target   : 사람 오브젝트를 매 프레임 다시 읽는다. 실시간 추적
    //  - Navigate To Location : 고정 좌표로 간다. 소리·흔적·훑기가 모두 이쪽
    // 후자는 좌표만 봐서는 구분이 안 되므로 기억 상태로 마저 가른다.
    private string DescribeBehaviour()
    {
        bool chasing = false;
        bool attacking = false;
        bool movingToPoint = false;

        foreach (string node in EnumerateActiveNodes())
        {
            if (node.StartsWith("NavigateToTargetAction")) chasing = true;
            else if (node.StartsWith("AttackSurvivorAction")) attacking = true;
            else if (node.StartsWith("NavigateToLocationAction")) movingToPoint = true;
        }

        if (attacking) return "공격";
        if (chasing) return "추격(사람 실시간 추적)";

        if (_memory != null && _memory.IsResting) return "수색 - 포기 직후 한 박자 정지";

        if (movingToPoint)
        {
            if (_memory != null && _memory.HasMemory) return "수색 - 흔적 주위";
            return NoiseSystem.TryGetLoudest(transform.position, 1f, out _)
                ? "수색 - 소리 난 쪽"
                : "수색 - 단서 없음(기본 상태)";
        }

        return "대기";
    }

    // 목적지가 사람을 겨냥한 것인지 한 줄로. 글자 패널의 판단과 같은 기준이다.
    private string DescribeDestination(Vector3 destination)
    {
        float nearest = float.MaxValue;
        foreach (PlayerHealth survivor in SurvivorRegistry.Active())
        {
            nearest = Mathf.Min(nearest, Vector3.Distance(destination, survivor.transform.position));
        }

        string behaviour = DescribeBehaviour();

        return nearest == float.MaxValue
            ? $"목적지 [{behaviour}]"
            : $"목적지 [{behaviour}] - 사람까지 {nearest:0.0}m";
    }

    // 화면에 찍을 목적지와 경로를 모은다. 판정은 서버에만 있어서 클라이언트에서는 비워 둔다.
    private void CollectWorldMarkers()
    {
        _hasMarkerDestination = false;
        _pathCorners = System.Array.Empty<Vector3>();

        bool isServer = NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer;
        if (!isServer || _agent == null || !_agent.isOnNavMesh)
        {
            return;
        }

        _markerDestination = _agent.destination;
        _hasMarkerDestination = true;

        if (_agent.hasPath)
        {
            _pathCorners = _agent.path.corners;
        }
    }

    private void AppendState()
    {
        bool isServer = NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer;

        _text.AppendLine($"=== 보스 감지 ({_toggleKey} 로 끄기) ===");
        if (!isServer)
        {
            _text.AppendLine("클라이언트에서는 판정이 돌지 않아 값이 비어 있습니다. 호스트에서 보세요.");
            return;
        }

        if (_dormancy != null && _dormancy.IsDormant)
        {
            _text.AppendLine("상태: 잠복 (가까이 오면 깨어남)");
        }
        else
        {
            _text.AppendLine($"상태: {DescribeBehaviour()}");
        }

        _text.AppendLine($"시야: {(_perception.TryGetVisibleSurvivor(out GameObject seen) ? seen.name : "없음")}");
        _text.AppendLine($"  판정: {_perception.DescribeSightCheck()}");
        _text.AppendLine($"근접 감지: {(_perception.TryGetNearbySurvivor(out GameObject near) ? near.name : "없음")}");

        if (_memory != null)
        {
            // GetSearchPoint 를 부르면 안 된다. 수색 횟수를 깎고 포기까지 시켜서,
            // 디버그 표시를 켜 놓은 것만으로 보스 행동이 달라진다. 상태만 읽는다.
            _text.AppendLine(_memory.HasMemory
                ? $"흔적: {(_memory.Survivor == null ? "?" : _memory.Survivor.name)} @ {_memory.Trace} "
                    + $"({_memory.SearchProgress})"
                : _memory.IsResting ? "흔적: 없음 (포기 직후 정지)"
                : "흔적: 없음");
        }

        if (_attack != null)
        {
            _text.AppendLine($"공격 중: {_attack.IsAttacking}");
        }

        _text.AppendLine();
    }

    // 확률과 시간으로 도는 규칙들의 지금 상태.
    //
    // 표적 전환(35%)과 봐주기(15%)는 재현이 안 돼서, 표시가 없으면 "안 되는 것 같은데"를
    // 확인할 방법이 없다. 흔적 수명과 소리 갱신 제한도 초 단위로 도는 값이라 마찬가지다.
    // 값이 흐르는 것을 눈으로 봐야 규칙이 도는지 멈춰 있는지 알 수 있다.
    private void AppendChanceState()
    {
        _text.AppendLine("=== 시간·확률 규칙 ===");

        if (_memory != null)
        {
            _text.AppendLine(_memory.HasMemory
                ? $"흔적 수명: {_memory.TraceRemaining:0.0}초 ({_memory.TraceSource})   "
                    + $"소리 갱신까지: {_memory.NoiseGateRemaining:0.0}초"
                : $"흔적 없음   휴식 남음: {_memory.RestRemaining:0.0}초");
        }

        if (_perception != null)
        {
            _text.AppendLine($"표적 유지: {_perception.TargetHeldSeconds:0.0}초   "
                + $"다음 전환 주사위: {_perception.SwitchRollRemaining:0.0}초");

            _text.AppendLine(_perception.IsMercyActive
                ? "봐주는 중 (감각 둔해짐)"
                : $"봐주기 쿨다운: {_perception.MercyLockRemaining:0.0}초");
        }

        _text.AppendLine();
    }

    // 보스가 "지금 어디로 가고 있는지"와 "그게 사람을 겨냥한 것인지".
    //
    // 보스를 움직이는 노드는 둘뿐이다 — 추격(Navigate To Target, 사람을 실시간으로 따라감)과
    // 그 밖의 전부(Navigate To Location, 고정 좌표). 증거 없이 따라오는 것처럼 보일 때
    // 둘 중 무엇인지 가리려면 목적지 자체를 봐야 한다.
    //
    // 판단 기준:
    //  - 목적지가 사람 바로 위(0~2m)이고 매 갱신마다 바뀐다  -> 사람을 직접 따라가는 중
    //  - 목적지가 사람 근처지만 고정돼 있다                  -> 마지막으로 본 자리나 소리 난 자리
    //  - 목적지가 사람과 멀다                                -> 수색 중. 우연히 지나가는 것
    private void AppendDestination()
    {
        if (_agent == null || !_agent.isOnNavMesh)
        {
            return;
        }

        Vector3 destination = _agent.destination;

        _text.AppendLine("=== 지금 어디로 가는가 ===");
        _text.AppendLine($"경로: {_agent.pathStatus}   남은 거리: {_agent.remainingDistance:0.0}m   "
            + $"멈춤: {_agent.isStopped}");

        bool moved = _hasLastDestination && (destination - _lastDestination).sqrMagnitude > 0.25f;
        _lastDestination = destination;
        _hasLastDestination = true;

        float nearest = float.MaxValue;
        string nearestName = "없음";
        foreach (PlayerHealth survivor in SurvivorRegistry.Active())
        {
            float distance = Vector3.Distance(destination, survivor.transform.position);
            if (distance < nearest)
            {
                nearest = distance;
                nearestName = survivor.gameObject.name;
            }
        }

        if (nearest == float.MaxValue)
        {
            _text.AppendLine("목적지 ↔ 사람: 대상 없음");
        }
        else
        {
            _text.AppendLine($"목적지 ↔ {nearestName}: {nearest:0.0}m   "
                + $"목적지 갱신: {(moved ? "예" : "아니오")}");
            _text.AppendLine($"  찍은 가지: {DescribeBehaviour()}");
        }

        _text.AppendLine();
    }

    // 누구에게 어떤 심장 박동 단계를 내려보내고 있는지, 그리고 왜 그렇게 됐는지.
    //
    // "마주쳤는데 180 이 안 난다"를 가리려면 거리·각도·가린 물체를 봐야 한다. 판정은 서버에서만
    // 돌기 때문에 플레이어 쪽 표시(F8 오른쪽)에서는 결과만 보이고 이유가 안 보인다.
    private void AppendHeartbeatThreat()
    {
        if (_threat == null) return;

        _text.AppendLine("=== 심장 박동 위협도 ===");
        _text.Append(_threat.Diagnosis);
        _text.AppendLine();
    }

    // 그래프가 지금 어느 가지를 돌고 있고, Blackboard에 무엇이 들어 있는지.
    //
    // "감지는 되는데 보스가 안 움직인다"를 가릴 때 필요하다. BossPerception에 직접 물어본 값과
    // 그래프가 조건 노드를 통해 본 값이 다를 수 있어서(예: Self 가 안 묶임), 둘을 나란히 봐야 한다.
    private void AppendGraphState()
    {
        if (_brain == null)
        {
            return;
        }

        _text.AppendLine("=== 그래프 ===");
        _text.AppendLine($"실행 중: {(_brain.Graph != null && _brain.Graph.IsRunning ? "예" : "아니오")}");

        // Self 가 이 보스로 묶여 있어야 조건 노드가 BossPerception 을 찾을 수 있다.
        _text.AppendLine($"Self: {DescribeObjectVariable("Self")}");
        _text.AppendLine($"Target Survivor: {DescribeObjectVariable("Target Survivor")}");

        _text.AppendLine("실행 중인 노드:");
        foreach (string node in EnumerateActiveNodes())
        {
            _text.AppendLine("   " + node);
        }

        _text.AppendLine();
    }

    private string DescribeObjectVariable(string variableName)
    {
        if (!_brain.GetVariable(variableName, out BlackboardVariable<GameObject> variable))
        {
            return "★변수 없음";
        }

        if (variable.Value == null)
        {
            return "비어 있음";
        }

        return variable.Value == gameObject ? "이 보스" : variable.Value.name;
    }

    // 활성 노드 목록은 패키지 내부 값이라 리플렉션으로 읽는다. 개발용 표시라 실패하면 조용히 넘긴다.
    private System.Collections.Generic.IEnumerable<string> EnumerateActiveNodes()
    {
        const System.Reflection.BindingFlags Flags =
            System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.Public |
            System.Reflection.BindingFlags.NonPublic;

        object graph = _brain.Graph;
        if (graph == null)
        {
            yield return "(그래프 없음)";
            yield break;
        }

        object rootGraph = graph.GetType().GetProperty("RootGraph", Flags)?.GetValue(graph);
        if (rootGraph == null)
        {
            yield return "(RootGraph 없음)";
            yield break;
        }

        if (rootGraph.GetType().GetField("m_ActiveNodes", Flags)?.GetValue(rootGraph)
            is not System.Collections.IEnumerable activeNodes)
        {
            yield return "(활성 노드를 읽지 못함)";
            yield break;
        }

        int count = 0;
        foreach (object node in activeNodes)
        {
            if (node == null)
            {
                continue;
            }

            string status = node is Unity.Behavior.Node typed ? typed.CurrentStatus.ToString() : "?";
            yield return $"{node.GetType().Name} : {status}";
            count++;
        }

        if (count == 0)
        {
            yield return "(없음 — 그래프가 멈췄습니다)";
        }
    }

    private void AppendNoises()
    {
        _text.AppendLine("=== 들리는 소음 ===");
        _text.AppendLine("(종류 / 거리 / 들리는거리 / 여유 / 남은시간)");

        int shown = 0;
        foreach (NoiseSystem.Noise noise in NoiseSystem.ActiveNoises)
        {
            float remaining = noise.ExpireTime - Time.time;
            if (remaining <= 0f)
            {
                continue;
            }

            float distance = Vector3.Distance(transform.position, noise.Position);

            // 보스가 실제로 쓰는 판정과 같은 식이다. 여유가 양수면 들린다.
            float margin = noise.Radius - distance;

            _text.AppendLine(
                $"{(margin > 0f ? "●" : "○")} {noise.Kind}  {distance:0.0}m / {noise.Radius:0}m" +
                $"  여유 {margin:+0.0;-0.0}  {remaining:0.0}초");
            shown++;
        }

        if (shown == 0)
        {
            _text.AppendLine("없음");
        }

        _text.AppendLine();
        _text.AppendLine("● = 들린다  ○ = 너무 멀어 안 들린다");
        _text.AppendLine("여유가 가장 큰 소음 하나만 쫓아간다.");
    }

    // 씬/게임 뷰에서 Gizmos를 켜면 보인다. 위에서 내려다보면 소음 반경이 한눈에 들어온다.
    private void OnDrawGizmos()
    {
        if (!DebugOverlayToggle.Shown || !Application.isPlaying)
        {
            return;
        }

        foreach (NoiseSystem.Noise noise in NoiseSystem.ActiveNoises)
        {
            float remaining = noise.ExpireTime - Time.time;
            if (remaining <= 0f)
            {
                continue;
            }

            bool audible = Vector3.Distance(transform.position, noise.Position) <= noise.Radius;

            // 들리는 소음은 빨강, 안 들리는 소음은 회색. 사라질수록 옅어진다.
            Gizmos.color = audible
                ? new Color(1f, 0.2f, 0.2f, Mathf.Clamp01(remaining))
                : new Color(0.6f, 0.6f, 0.6f, Mathf.Clamp01(remaining) * 0.5f);

            // 반경은 바닥에 눕힌 원으로 그린다. 위에서 내려다볼 때 이 소리가 보스에게 닿는지
            // 한눈에 보려면 세로 구체가 아니라 바닥 원이어야 한다.
            DrawFlatCircle(noise.Position + Vector3.up * 0.1f, noise.Radius);
            DrawGroundMarker(noise.Position, 0.6f);

            if (audible)
            {
                Gizmos.DrawLine(
                    transform.position + Vector3.up * 0.2f,
                    noise.Position + Vector3.up * 0.2f);
            }
        }

        DrawDestinationGizmos();
    }

    // 보스가 지금 어디로 가고 있는지. 위에서 내려다보는 씬 뷰에서 "왜 이쪽으로 오는지"를
    // 읽으려면 목적지와 경로가 보여야 한다.
    //
    // 바닥에 붙은 원은 위에서 보면 잘 안 보인다. 그래서 지점마다 위로 기둥을 세우고 이름을 붙인다.
    private void DrawDestinationGizmos()
    {
        if (_agent == null || !_agent.isOnNavMesh)
        {
            return;
        }

        DrawPathGizmos();

        // 흔적. 마지막으로 '본' 자리.
        if (_memory != null && _memory.HasMemory)
        {
            Gizmos.color = Color.yellow;
            DrawGroundMarker(_memory.Trace, 1.2f);
            DrawPost(_memory.Trace, 3f, LabelSideTrace, $"흔적 ({_memory.SearchProgress})");
        }

        // 목적지. 가장 굵게 그린다.
        Vector3 destination = _agent.destination;
        Gizmos.color = Color.red;
        DrawGroundMarker(destination, 2f);
        DrawPost(destination, 5f, LabelSideDestination, DescribeDestination(destination));

        // 보스 자신. 위에서 내려다볼 때 경로의 어느 쪽 끝이 보스인지 알아야 한다.
        Gizmos.color = Color.magenta;
        DrawGroundMarker(transform.position, 1f);
        Gizmos.DrawLine(
            transform.position + Vector3.up * 0.2f,
            transform.position + transform.forward * 2f + Vector3.up * 0.2f);
    }

    // 바닥에 눕혀 그리는 표식. 원 + 십자.
    //
    // 위에서 똑바로 내려다보면 세로로 세운 것은 전부 한 점으로 뭉쳐서 보이지 않는다.
    // 기둥도, DrawWireSphere 의 세로 원들도 마찬가지다. 그래서 바닥과 평행한 도형으로 따로 그린다.
    private static void DrawGroundMarker(Vector3 position, float radius)
    {
        Vector3 center = position + Vector3.up * 0.15f;

        DrawFlatCircle(center, radius);

        // 십자는 원보다 크게 빼서, 멀리서 내려다볼 때도 위치가 잡힌다.
        float arm = radius * 1.8f;
        Gizmos.DrawLine(center + Vector3.left * arm, center + Vector3.right * arm);
        Gizmos.DrawLine(center + Vector3.back * arm, center + Vector3.forward * arm);
    }
}
