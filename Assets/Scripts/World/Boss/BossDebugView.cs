using System.Text;
using Unity.Behavior;
using Unity.Netcode;
using UnityEngine;
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
    [Tooltip("끄면 아무것도 그리지 않는다. 빌드에 실수로 남아도 조용하도록 기본은 꺼둔다.")]
    [SerializeField] private bool _enabled;

    // F9는 기존 디버그 메뉴가 이미 쓰고 있어서 겹치지 않게 F8로 둔다.
    [Tooltip("이 키로 표시를 켜고 끈다.")]
    [SerializeField] private Key _toggleKey = Key.F8;

    [Tooltip("화면 왼쪽 위 글자 표시. 끄면 씬 기즈모만 그린다.")]
    [SerializeField] private bool _showOverlay = true;

    private BehaviorGraphAgent _brain;
    private BossPerception _perception;
    private BossTargetMemory _memory;
    private BossDormancy _dormancy;
    private BossAttack _attack;
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
            _enabled = !_enabled;
        }
    }

    private void OnGUI()
    {
        if (!_enabled || !_showOverlay)
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
            AppendState();
            AppendGraphState();
            AppendNoises();
        }

        var area = new Rect(12f, 12f, 460f, Screen.height - 24f);
        GUI.color = new Color(0f, 0f, 0f, 0.55f);
        GUI.DrawTexture(area, Texture2D.whiteTexture);
        GUI.color = Color.white;
        GUI.Label(new Rect(area.x + 8f, area.y + 6f, area.width - 16f, area.height - 12f), _text.ToString(), _style);
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

        if (_dormancy != null)
        {
            _text.AppendLine(_dormancy.IsDormant ? "상태: 잠복 (가까이 오면 깨어남)" : "상태: 활동");
        }

        _text.AppendLine($"시야: {(_perception.TryGetVisibleSurvivor(out GameObject seen) ? seen.name : "없음")}");
        _text.AppendLine($"  판정: {_perception.DescribeSightCheck()}");
        _text.AppendLine($"근접 감지: {(_perception.TryGetNearbySurvivor(out GameObject near) ? near.name : "없음")}");

        if (_memory != null)
        {
            _text.AppendLine(_memory.HasMemory
                ? $"기억: {(_memory.Survivor == null ? "?" : _memory.Survivor.name)} → 수색 지점 {_memory.GetSearchPoint()}"
                : "기억: 없음");
        }

        if (_attack != null)
        {
            _text.AppendLine($"공격 중: {_attack.IsAttacking}");
        }

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
        if (!_enabled || !Application.isPlaying)
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

            Gizmos.DrawWireSphere(noise.Position, noise.Radius);

            if (audible)
            {
                Gizmos.DrawLine(transform.position + Vector3.up, noise.Position);
            }
        }
    }
}
