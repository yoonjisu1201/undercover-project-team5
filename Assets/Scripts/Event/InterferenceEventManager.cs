using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using Debug = UnityEngine.Debug;


// 서버 권한으로 하나의 방해 효과를 실행하고 활성화와 종료를 클라이언트에 동기화합니다.
public sealed class InterferenceEventManager : NetworkBehaviour
{
    // 서버에서 현재 실행 중인 방해 효과 식별자입니다.
    private InterferenceEffectId _serverEventId;

    // 서버에서 현재 방해 효과를 종료할 네트워크 시간입니다.
    private double _serverEventEndTime;

    // 로컬 클라이언트에서 실행 중인 방해 효과 식별자입니다.
    private InterferenceEffectId _localEventId;

    // 로컬 클라이언트에서 현재 방해 효과를 종료할 네트워크 시간입니다.
    private double _localEventEndTime;

    // 방해 효과 식별자별 구현체를 보관합니다.
    private readonly Dictionary<InterferenceEffectId, InterferenceEffectBase>
        _events = new Dictionary<InterferenceEffectId, InterferenceEffectBase>();

    // 현재 씬의 방해 효과 관리자 인스턴스를 가져옵니다.
    public static InterferenceEventManager Instance { get; private set; }


    // 중복 인스턴스를 제거하고 같은 GameObject의 방해 효과를 등록합니다.
    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        RegisterEvents();
    }


    // 네트워크에 스폰될 때 서버에서 라운드 상태 변경을 구독합니다.
    public override void OnNetworkSpawn()
    {
        if (IsServer && RoundManager.Instance != null)
        {
            RoundManager.Instance.OnRoundStateChanged += HandleRoundStateChanged;
        }
    }


    // 개발용 F2 입력으로 시야 방해 효과를 시작하고 서버에서 종료 시간을 확인합니다.
    private void Update()
    {
        if (!IsSpawned || !IsServer)
        {
            return;
        }

        if (Keyboard.current != null && Keyboard.current.f2Key.wasPressedThisFrame)
        {
            TryStartEvent(InterferenceEffectId.FieldVision);
        }

        if (_serverEventId == InterferenceEffectId.None || NetworkManager.ServerTime.Time < _serverEventEndTime)
        {
            return;
        }

        EndCurrentServerEvent(InterferenceEndReason.Normal);
    }


    // 네트워크에서 디스폰될 때 라운드 상태 구독과 로컬 방해 효과를 정리합니다.
    public override void OnNetworkDespawn()
    {
        if (RoundManager.Instance != null)
        {
            RoundManager.Instance.OnRoundStateChanged -= HandleRoundStateChanged;
        }

        EndLocalEvent(InterferenceEndReason.Despawned);
    }


    // 오브젝트가 제거될 때 정적 인스턴스 참조를 해제합니다.
    public override void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }

        base.OnDestroy();
    }


    // 서버에서 지정한 방해 효과를 즉시 시작합니다.
    // eventId: 시작할 방해 효과 식별자입니다.
    // 반환값: 시작 조건을 만족해 요청을 수락하면 true, 그렇지 않으면 false입니다.
    public bool TryStartEvent(InterferenceEffectId eventId)
    {
        if (!IsSpawned)
        {
            return RejectStart(eventId, "Manager가 아직 Spawn되지 않았습니다.");
        }

        if (!IsServer)
        {
            return RejectStart(eventId, "서버에서만 시작할 수 있습니다.");
        }

        if (eventId == InterferenceEffectId.None)
        {
            return RejectStart(eventId, "None은 시작할 수 없습니다.");
        }

        if (_serverEventId != InterferenceEffectId.None)
        {
            return RejectStart(eventId, $"{_serverEventId} 효과가 이미 실행 중입니다.");
        }

        if (!IsRoundInProgress())
        {
            string roundState = RoundManager.Instance == null ? "RoundManager 없음" : RoundManager.Instance.CurrentState.ToString();

            return RejectStart(eventId, $"현재 라운드 상태에서는 시작할 수 없습니다. 상태: {roundState}");
        }

        if (!_events.TryGetValue(eventId, out InterferenceEffectBase interferenceEffect))
        {
            return RejectStart(eventId, "등록된 방해 효과 구현체가 없습니다.");
        }

        double eventEndTime = NetworkManager.ServerTime.Time + interferenceEffect.Duration;

        _serverEventId = eventId;
        _serverEventEndTime = eventEndTime;

        StartEventRpc(eventId, eventEndTime);

        return true;
    }


    // 이벤트 시작 요청을 거절하고 원인을 로그로 기록합니다.
    private bool RejectStart(InterferenceEffectId eventId, string reason)
    {
        Debug.LogWarning($"[Interference] {eventId} 시작 실패: {reason}", this);

        return false;
    }


    // 클라이언트와 호스트에서 방해 효과를 즉시 활성화합니다.
    // eventId: 시작할 방해 효과 식별자입니다.
    // eventEndTime: 효과를 종료할 네트워크 시간입니다.
    [Rpc(SendTo.ClientsAndHost)]
    private void StartEventRpc(InterferenceEffectId eventId, double eventEndTime)
    {
        if (NetworkManager.ServerTime.Time >= eventEndTime)
        {
            return;
        }

        EndLocalEvent(InterferenceEndReason.Replaced);

        InterferenceEffectBase interferenceEffect = GetEvent(eventId);

        if (interferenceEffect == null)
        {
            return;
        }

        _localEventId = eventId;
        _localEventEndTime = eventEndTime;

        interferenceEffect.Activate();
    }


    // 클라이언트와 호스트에서 지정한 방해 효과를 종료합니다.
    // eventId: 종료할 방해 효과 식별자입니다.
    // eventEndTime: 시작 당시 동기화한 종료 시간입니다.
    // reason: 방해 효과를 종료하는 사유입니다.
    [Rpc(SendTo.ClientsAndHost)]
    private void EndEventRpc(InterferenceEffectId eventId, double eventEndTime, InterferenceEndReason reason)
    {
        if (_localEventId != eventId || _localEventEndTime != eventEndTime)
        {
            return;
        }

        EndLocalEvent(reason);
    }


    // 서버의 현재 방해 효과 상태를 초기화하고 종료 정보를 클라이언트에 전달합니다.
    // reason: 현재 방해 효과를 종료하는 사유입니다.
    private void EndCurrentServerEvent(InterferenceEndReason reason)
    {
        if (_serverEventId == InterferenceEffectId.None)
        {
            return;
        }

        InterferenceEffectId eventId = _serverEventId;
        double eventEndTime = _serverEventEndTime;

        _serverEventId = InterferenceEffectId.None;
        _serverEventEndTime = 0d;

        EndEventRpc(eventId, eventEndTime, reason);
    }


    // 로컬 방해 효과 상태를 초기화하고 구현체에 종료 사유를 전달합니다.
    // reason: 로컬 방해 효과를 종료하는 사유입니다.
    private void EndLocalEvent(InterferenceEndReason reason)
    {
        if (_localEventId == InterferenceEffectId.None)
        {
            return;
        }

        InterferenceEffectBase interferenceEffect = GetEvent(_localEventId);

        _localEventId = InterferenceEffectId.None;
        _localEventEndTime = 0d;

        if (interferenceEffect == null)
        {
            return;
        }

        interferenceEffect.Deactivate(reason);
    }


    // 진행 라운드를 벗어나면 서버에서 현재 방해 효과를 종료합니다.
    // roundState: 변경된 라운드 상태입니다.
    private void HandleRoundStateChanged(RoundState roundState)
    {
        if (roundState == RoundState.Round1 || roundState == RoundState.Round2)
        {
            return;
        }

        EndCurrentServerEvent(InterferenceEndReason.RoundEnded);
    }


    // 현재 상태가 방해 효과를 실행할 수 있는 진행 라운드인지 확인합니다.
    private bool IsRoundInProgress()
    {
        if (RoundManager.Instance == null)
        {
            Debug.LogError("[Interference] RoundManager를 찾을 수 없습니다.", this);

            return false;
        }

        RoundState roundState = RoundManager.Instance.CurrentState;

        return roundState == RoundState.Round1 || roundState == RoundState.Round2;
    }


    // 같은 GameObject에 있는 방해 효과 구현체를 식별자별로 등록합니다.
    private void RegisterEvents()
    {
        foreach (InterferenceEffectBase interferenceEffect in GetComponents<InterferenceEffectBase>())
        {
            if (interferenceEffect.Id == InterferenceEffectId.None)
            {
                Debug.LogWarning("[Interference] None 식별자는 등록할 수 없습니다.", interferenceEffect);
                continue;
            }

            if (_events.ContainsKey(interferenceEffect.Id))
            {
                Debug.LogError($"[Interference] {interferenceEffect.Id} 효과가 중복 등록됐습니다.", interferenceEffect);
                continue;
            }

            _events.Add(interferenceEffect.Id, interferenceEffect);
        }
    }


    // 지정한 식별자에 등록된 방해 효과 구현체를 가져옵니다.
    private InterferenceEffectBase GetEvent(InterferenceEffectId eventId)
    {
        if (_events.TryGetValue(eventId, out InterferenceEffectBase interferenceEffect))
        {
            return interferenceEffect;
        }

        Debug.LogError($"[Interference] {eventId} 효과 구현체가 등록되지 않았습니다.", this);

        return null;
    }
}
