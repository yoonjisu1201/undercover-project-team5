using System.Text;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

// 심장 박동이 지금 어느 단계로 나고 있는지 화면에 그려주는 개발용 표시.
//
// 박동은 클립을 바꿔 끼우는 방식이라, 소리만 듣고는 "지금 90인지 120인지"를 구분하기 어렵다.
// 게다가 클립이 비어 있으면 호출부가 조용히 넘어가서 아무 소리도 안 나는데, 코드 쪽에는
// 이상이 없어 보인다. 그 두 가지를 눈으로 확인하기 위한 것이다.
//
// 자기 심장 소리라서 오너에게만 의미가 있다.
[RequireComponent(typeof(PlayerHeartbeat))]
public class HeartbeatDebugView : NetworkBehaviour
{
    // 보스 디버그와 같은 F8을 쓴다. 두 표시가 한 번에 켜지고 꺼지도록 맞춘 것이다.
    // 서로 다른 오브젝트에 붙어 있어서(보스 / 플레이어) 각자 키보드를 읽는다. 묶을 필요가 없다.
    [Tooltip("이 키로 표시를 켜고 끈다. 보스 디버그와 같은 키라 둘이 같이 켜진다.")]
    [SerializeField] private Key _toggleKey = Key.F8;

    [Tooltip("표시가 켜져 있을 때 이 키로 단계를 강제로 돌려 본다. 보스 없이도 180을 들어보려면 필요하다.")]
    [SerializeField] private Key _forceKey = Key.Backslash;

    // 단계를 강제로 돌릴 순서. 마지막 None 은 "강제 해제"다.
    private static readonly SoundKey[] ForceCycle =
    {
        SoundKey.None,
        SoundKey.Player_HeartBeat_Tired,
        SoundKey.Player_HeartBeat_Exhausted,
        SoundKey.Player_HeartBeat_Hiding,
        SoundKey.Player_HeartBeat_Spotted,
    };

    // 표시 내용을 다시 만드는 간격(초). 매 프레임 만들면 디버그 표시가 오히려 부하가 된다.
    private const float TextRefreshInterval = 0.1f;

    private PlayerHeartbeat _heartbeat;
    private readonly StringBuilder _text = new();
    private float _nextTextTime;
    private int _forceIndex;
    private GUIStyle _style;

    private void Awake()
    {
        _heartbeat = GetComponent<PlayerHeartbeat>();

        // 필드 타입이 바뀌면 예전 직렬화 값이 그대로 남아 Key 범위를 벗어난다. 그 값을 인덱서에
        // 넣으면 매 프레임 예외가 쏟아지므로 여기서 한 번 걸러 기본값으로 되돌린다.
        if (!System.Enum.IsDefined(typeof(Key), _toggleKey)) _toggleKey = Key.F8;
        if (!System.Enum.IsDefined(typeof(Key), _forceKey)) _forceKey = Key.Backslash;
    }

    private void Update()
    {
        if (!IsOwner || Keyboard.current == null) return;

        if (Keyboard.current[_toggleKey].wasPressedThisFrame)
        {
            DebugOverlayToggle.RequestToggle();
        }

        // 표시가 꺼져 있으면 강제 지정도 풀어 둔다. 강제가 걸린 채로 표시만 꺼지면
        // 왜 소리가 계속 나는지 알 수 없게 된다. 씬을 옮겨 꺼진 경우도 여기서 함께 정리된다.
        if (!DebugOverlayToggle.Shown && _forceIndex != 0)
        {
            _forceIndex = 0;
            _heartbeat.DebugForcedKey = SoundKey.None;
        }

        if (DebugOverlayToggle.Shown && Keyboard.current[_forceKey].wasPressedThisFrame)
        {
            _forceIndex = (_forceIndex + 1) % ForceCycle.Length;
            _heartbeat.DebugForcedKey = ForceCycle[_forceIndex];
        }
    }

    private void OnGUI()
    {
        if (!DebugOverlayToggle.Shown || !IsOwner) return;

        // OnGUI 는 한 프레임에 여러 번(Layout/Repaint) 불린다. 그릴 때만 그린다.
        if (Event.current.type != EventType.Repaint) return;

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
            AppendChannels();
        }

        // 보스 디버그가 왼쪽 위를 쓰고 있어서 오른쪽 위에 그린다. 같은 키로 같이 켜져도 안 겹친다.
        var area = new Rect(Screen.width - 452f, 12f, 440f, 300f);
        GUI.color = new Color(0f, 0f, 0f, 0.55f);
        GUI.DrawTexture(area, Texture2D.whiteTexture);
        GUI.color = Color.white;
        GUI.Label(new Rect(area.x + 8f, area.y + 6f, area.width - 16f, area.height - 12f), _text.ToString(), _style);
    }

    private void AppendState()
    {
        _text.AppendLine($"=== 심장 박동 ({_toggleKey} 로 끄기) ===");

        SoundKey forced = _heartbeat.DebugForcedKey;
        if (forced != SoundKey.None)
        {
            _text.AppendLine($"★ 강제: {Describe(forced)}  ({_forceKey} 로 다음 단계)");
        }
        else
        {
            _text.AppendLine($"강제: 없음 (실제 판정)  ({_forceKey} 로 강제 시작)");
        }

        _text.AppendLine($"스태미나: {_heartbeat.StaminaRatio * 100f:0}%" +
            $"   박동 켜짐: {(_heartbeat.StaminaLatched ? "예" : "아니오")}" +
            $"   세기: {_heartbeat.VolumeScale:0.00}");
        _text.AppendLine($"보스 위협: {DescribeThreat(_heartbeat.Threat)}");
        _text.AppendLine($"발각 이후: {DescribeSinceSpotted()}");
        _text.AppendLine();
        _text.AppendLine($"지금 나는 단계: {Describe(_heartbeat.CurrentKey)}");
        _text.AppendLine();
    }

    // 네 단계 전부의 볼륨을 나란히 보여준다. 단계가 바뀌는 순간에는 두 줄이 동시에 0보다 커지는데,
    // 그게 크로스페이드가 도는 중이라는 뜻이다.
    private void AppendChannels()
    {
        _text.AppendLine("=== 단계별 상태 (볼륨 / 등록 / 클립) ===");

        SoundManager sound = SoundManager.Instance;
        if (sound == null)
        {
            _text.AppendLine("SoundManager 가 없습니다.");
            return;
        }

        bool anyMissing = false;

        for (int i = 1; i < ForceCycle.Length; i++)
        {
            SoundKey key = ForceCycle[i];
            float volume = sound.GetLoopVolume(key);
            bool registered = sound.IsRegistered(key);
            bool hasClip = sound.HasClip(key);
            bool isLoop = sound.IsLoopSound(key);
            anyMissing |= !registered || !hasClip || !isLoop;

            string bar = new string('|', Mathf.RoundToInt(volume * 20f));

            _text.AppendLine(
                $"{(volume > 0f ? "●" : "○")} {Describe(key),-22} {volume:0.00} {bar}" +
                $"  {(registered ? "" : "★미등록")}{(registered && !hasClip ? "★클립없음" : "")}" +
                $"{(registered && hasClip && !isLoop ? "★루프아님" : "")}");
        }

        if (anyMissing)
        {
            _text.AppendLine();
            _text.AppendLine("★ 표시된 단계는 소리가 나지 않습니다.");
            _text.AppendLine("   미등록 = SoundManager 프리팹의 Sounds 목록에 SoundData 가 없음");
            _text.AppendLine("   클립없음 = SoundData 의 Clips 가 비어 있음");
            _text.AppendLine("   루프아님 = SoundData 의 Is Loop 가 꺼져 있음");
        }
    }

    // 발각 이후 흐른 시간과, 지금 어느 구간에 있는지. 이 구간에서는 스태미나를 보지 않으므로
    // "스태미나가 찼는데 왜 180 이 나느냐"를 여기서 가려야 한다.
    private string DescribeSinceSpotted()
    {
        float since = _heartbeat.SinceSpotted;
        if (float.IsInfinity(since)) return "없음 (발각된 적 없음)";

        float hold = _heartbeat.SpottedHold;
        float tail = hold + _heartbeat.SearchTail;

        if (since < hold) return $"{since:0.0}초 — 180 유지 구간 (~{hold:0}초, 스태미나 무시)";
        if (since < tail) return $"{since:0.0}초 — 120 유지 구간 (~{tail:0}초, 스태미나 무시)";

        return $"{since:0.0}초 — 구간 끝 (스태미나 판정)";
    }

    private static string Describe(SoundKey key)
    {
        switch (key)
        {
            case SoundKey.Player_HeartBeat_Tired: return "70 BPM (지침)";
            case SoundKey.Player_HeartBeat_Exhausted: return "90 BPM (거의 바닥)";
            case SoundKey.Player_HeartBeat_Hiding: return "120 BPM (수색 중)";
            case SoundKey.Player_HeartBeat_Spotted: return "180 BPM (발각)";
            default: return "없음 (조용함)";
        }
    }

    private static string DescribeThreat(BossThreat threat)
    {
        switch (threat)
        {
            case BossThreat.Spotted: return "발각 — 내가 외계인을 보고 있음";
            case BossThreat.Searching: return "수색 중 — 근처에 있지만 나를 못 봄";
            default: return "없음";
        }
    }
}
