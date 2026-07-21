using System.Collections.Generic;
using Unity.Netcode;
using UnityEditor.PackageManager;
using UnityEngine;
using UnityEngine.LightTransport;
using Debug = UnityEngine.Debug;

/// <summary>
/// 서버 권한으로 하나의 방해 이벤트만 실행하고 경고, 활성화, 종료 시점을 클라이언트에 동기화합니다.
/// </summary>
public sealed class InterferenceEventManager : NetworkBehaviour
{
    /// <summary>이벤트 효과가 활성화되기 전 경고 지속 시간입니다.</summary>
    [Header("방해 이벤트 시간")]
    [SerializeField, Range(0.1f, 3f)] private float _warningDuration = 1f;

    /// <summary>이벤트 효과의 최소 지속 시간입니다.</summary>
    [SerializeField, Range(5f, 15f)] private float _minimumDuration = 5f;

    /// <summary>이벤트 효과의 최대 지속 시간입니다.</summary>
    [SerializeField, Range(5f, 15f)] private float _maximumDuration = 15f;

    /// <summary>서버에서 현재 실행 중인 이벤트 식별자입니다.</summary>
    private InterferenceEventId _serverEventId;

    /// <summary>서버에서 현재 이벤트를 종료할 네트워크 시간입니다.</summary>
    private double _serverEventEndTime;

    /// <summary>로컬 클라이언트에서 처리 중인 이벤트 식별자입니다.</summary>
    private InterferenceEventId _localEventId;

    /// <summary>로컬 클라이언트에서 이벤트 효과를 활성화할 네트워크 시간입니다.</summary>
    private double _localActiveStartTime;

    /// <summary>로컬 클라이언트에서 이벤트 효과를 종료할 네트워크 시간입니다.</summary>
    private double _localEventEndTime;

    /// <summary>로컬 클라이언트에서 이벤트 효과가 활성화되었는지 나타냅니다.</summary>
    private bool _isLocalEventActive;

    /// <summary>이벤트 식별자별 실행 구현체를 보관합니다.</summary>
    private readonly Dictionary<InterferenceEventId, IInterferenceEvent>
        _events = new Dictionary<InterferenceEventId, IInterferenceEvent>();

    /// <summary>현재 씬의 방해 이벤트 관리자 인스턴스를 가져옵니다.</summary>
    public static InterferenceEventManager Instance { get; private set; }

    /// <summary>
    /// 중복 인스턴스를 비활성화하고 현재 인스턴스를 등록합니다.
    /// </summary>
    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            enabled = false;
            return;
        }

        Instance = this;
    }

    /// <summary>
    /// 최대 지속 시간이 최소 지속 시간보다 작아지지 않도록 보정합니다.
    /// </summary>
    private void OnValidate()
    {
        _maximumDuration = Mathf.Max(_minimumDuration, _maximumDuration);
    }

    /// <summary>
    /// 네트워크에 스폰될 때 서버에서 라운드 상태 변경을 구독합니다.
    /// </summary>
    public override void OnNetworkSpawn()
    {
        if (IsServer && RoundManager.Instance != null)
        {
            RoundManager.Instance.OnRoundStateChanged += HandleRoundStateChanged;
        }
    }

    /// <summary>
    /// 서버와 로컬 클라이언트의 이벤트 진행 시간을 갱신합니다.
    /// </summary>
    private void Update()
    {
        if (!IsSpawned)
        {
            Debug.LogError($"[Interference] IsSpawned == {IsSpawned} Manager가 아직 Spawn되지 않았습니다.", this);
            return;
        }

        double serverTime = NetworkManager.ServerTime.Time;

        UpdateServerEvent(serverTime);
        UpdateLocalEvent(serverTime);
    }

    /// <summary>
    /// 네트워크에서 디스폰될 때 라운드 상태 구독과 로컬 이벤트를 정리합니다.
    /// </summary>
    public override void OnNetworkDespawn()
    {
        if (RoundManager.Instance != null)
        {
            RoundManager.Instance.OnRoundStateChanged -= HandleRoundStateChanged;
        }

        EndLocalEvent();
    }

    /// <summary>
    /// 오브젝트가 제거될 때 정적 인스턴스 참조를 해제합니다.
    /// </summary>
    public override void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }

        base.OnDestroy();
    }

    /// <summary>
    /// 서버에서 지정한 방해 이벤트의 경고 및 실행을 시작합니다.
    /// </summary>
    /// <param name="eventId">시작할 방해 이벤트 식별자입니다.</param>
    /// <returns>시작 조건을 만족해 요청을 수락하면 <see langword="true"/>, 그렇지 않으면 <see langword="false"/>입니다.</returns>
    public bool TryStartEvent(InterferenceEventId eventId)
    {
        if (!IsSpawned)
        {
            return RejectStart(eventId, "Manager가 아직 Spawn되지 않았습니다.");
        }

        if (!IsServer)
        {
            return RejectStart(eventId, "서버에서만 시작할 수 있습니다.");
        }

        if (eventId == InterferenceEventId.None)
        {
            return RejectStart(eventId, "None은 시작할 수 없습니다.");
        }

        if (_serverEventId != InterferenceEventId.None)
        {
            return RejectStart(eventId, $"{_serverEventId} 이벤트가 이미 실행 중입니다.");
        }

        if (!IsRoundInProgress())
        {
            string roundState = RoundManager.Instance == null ? "RoundManager 없음": RoundManager.Instance.CurrentState.ToString();

            return RejectStart(eventId, $"현재 라운드 상태에서는 시작할 수 없습니다. 상태: {roundState}");
        }

        float duration = UnityEngine.Random.Range(_minimumDuration, _maximumDuration);

        double activeStartTime = NetworkManager.ServerTime.Time + _warningDuration;

        double eventEndTime = activeStartTime + duration;

        _serverEventId = eventId;
        _serverEventEndTime = eventEndTime;

        StartEventRpc(eventId, activeStartTime, eventEndTime);

        return true;
    }

    /// <summary>
    /// 이벤트 시작 요청을 거절하고 원인을 로그로 기록합니다.
    /// </summary>
    /// <param name="eventId">시작을 요청한 이벤트 식별자입니다.</param>
    /// <param name="reason">요청을 거절한 원인입니다.</param>
    /// <returns>항상 <see langword="false"/>입니다.</returns>
    private bool RejectStart(InterferenceEventId eventId, string reason)
    {
        Debug.LogWarning($"[Interference] {eventId} 시작 실패: {reason}", this);

        return false;
    }

    /// <summary>
    /// 이벤트 식별자에 해당하는 실행 구현체를 등록합니다.
    /// </summary>
    /// <param name="interferenceEvent">등록할 방해 이벤트 구현체입니다.</param>
    public void RegisterEvent(IInterferenceEvent interferenceEvent)
    {
        if (interferenceEvent.Id == InterferenceEventId.None)
        {
            Debug.LogWarning($"[Interference] {interferenceEvent.Id} == None.",this);
            return;
        }

        _events[interferenceEvent.Id] = interferenceEvent;
    }

    /// <summary>
    /// 현재 등록된 구현체와 일치하는 방해 이벤트를 등록 해제합니다.
    /// </summary>
    /// <param name="interferenceEvent">등록 해제할 방해 이벤트 구현체입니다.</param>
    public void UnregisterEvent(IInterferenceEvent interferenceEvent)
    {
        if (_events.TryGetValue(interferenceEvent.Id, out IInterferenceEvent registeredEvent) &&
            ReferenceEquals(registeredEvent, interferenceEvent))
        {
            _events.Remove(interferenceEvent.Id);
        }
    }

    /// <summary>
    /// 서버에서 현재 실행 중인 이벤트를 종료하고 종료 시점을 클라이언트에 전달합니다.
    /// </summary>
    public void StopCurrentEvent()
    {
        if (_serverEventId == InterferenceEventId.None)
        {
            return;
        }

        InterferenceEventId eventId = _serverEventId;
        double eventEndTime = _serverEventEndTime;

        _serverEventId = InterferenceEventId.None;
        _serverEventEndTime = 0d;

        EndEventRpc(eventId, eventEndTime);
    }

    /// <summary>
    /// 클라이언트와 호스트에 이벤트 경고, 활성화, 종료 시점을 설정합니다.
    /// </summary>
    /// <param name="eventId">시작할 이벤트 식별자입니다.</param>
    /// <param name="activeStartTime">효과를 활성화할 네트워크 시간입니다.</param>
    /// <param name="eventEndTime">효과를 종료할 네트워크 시간입니다.</param>
    [Rpc(SendTo.ClientsAndHost)]
    private void StartEventRpc(InterferenceEventId eventId, double activeStartTime, double eventEndTime)
    {
        double serverTime = NetworkManager.ServerTime.Time;

        if (serverTime >= eventEndTime)
        {
            return;
        }

        EndLocalEvent();

        _localEventId = eventId;
        _localActiveStartTime = activeStartTime;
        _localEventEndTime = eventEndTime;
        _isLocalEventActive = false;

        if (serverTime < activeStartTime)
        {
            GetEvent(eventId)?.ShowWarning();
        }

        UpdateLocalEvent(serverTime);
    }

    /// <summary>
    /// 클라이언트와 호스트에서 지정한 이벤트를 종료합니다.
    /// </summary>
    /// <param name="eventId">종료할 이벤트 식별자입니다.</param>
    /// <param name="eventEndTime">시작 당시 동기화한 이벤트 종료 시간입니다.</param>
    [Rpc(SendTo.ClientsAndHost)]
    private void EndEventRpc(InterferenceEventId eventId, double eventEndTime)
    {
        if (_localEventId != eventId || _localEventEndTime != eventEndTime)
        {
            return;
        }

        EndLocalEvent();
    }

    /// <summary>
    /// 서버 시간이 종료 시점에 도달하면 현재 이벤트를 종료합니다.
    /// </summary>
    /// <param name="serverTime">현재 네트워크 서버 시간입니다.</param>
    private void UpdateServerEvent(double serverTime)
    {
        if (!IsServer)
        {
            return;
        }

        if (_serverEventId == InterferenceEventId.None)
        {
            return;
        }

        if (serverTime < _serverEventEndTime)
        {
            return;
        }

        StopCurrentEvent();
    }

    /// <summary>
    /// 로컬 이벤트의 활성화 및 종료 시점을 현재 서버 시간에 맞춰 처리합니다.
    /// </summary>
    /// <param name="serverTime">현재 네트워크 서버 시간입니다.</param>
    private void UpdateLocalEvent(double serverTime)
    {
        if (_localEventId == InterferenceEventId.None)
        {
            return;
        }

        if (serverTime >= _localEventEndTime)
        {
            EndLocalEvent();
            return;
        }

        if (_isLocalEventActive || serverTime < _localActiveStartTime)
        {
            return;
        }

        _isLocalEventActive = true;
        GetEvent(_localEventId)?.Activate();
    }

    /// <summary>
    /// 로컬 이벤트 상태를 초기화하고 등록된 구현체에 종료를 알립니다.
    /// </summary>
    private void EndLocalEvent()
    {
        if (_localEventId == InterferenceEventId.None)
        {
            return;
        }

        InterferenceEventId eventId = _localEventId;

        _localEventId = InterferenceEventId.None;
        _localActiveStartTime = 0d;
        _localEventEndTime = 0d;
        _isLocalEventActive = false;

        GetEvent(eventId)?.Deactivate();
    }

    /// <summary>
    /// 진행 라운드를 벗어나면 서버에서 현재 이벤트를 종료합니다.
    /// </summary>
    /// <param name="roundState">변경된 라운드 상태입니다.</param>
    private void HandleRoundStateChanged(RoundState roundState)
    {
        if (!IsServer)
        {
            return;
        }

        if (roundState == RoundState.Round1 || roundState == RoundState.Round2)
        {
            return;
        }

        StopCurrentEvent();
    }

    /// <summary>
    /// 현재 상태가 방해 이벤트를 실행할 수 있는 진행 라운드인지 확인합니다.
    /// </summary>
    /// <returns>Round1 또는 Round2이면 <see langword="true"/>, 그렇지 않으면 <see langword="false"/>입니다.</returns>
    private bool IsRoundInProgress()
    {
        if (RoundManager.Instance == null)
        {
            Debug.LogError($"[Interference] {RoundManager.Instance} NULL", this);

            return false;
        }

        RoundState roundState = RoundManager.Instance.CurrentState;

        return roundState == RoundState.Round1 || roundState == RoundState.Round2;
    }

    /// <summary>
    /// 지정한 식별자에 등록된 이벤트 실행 구현체를 가져옵니다.
    /// </summary>
    /// <param name="eventId">조회할 방해 이벤트 식별자입니다.</param>
    /// <returns>등록된 구현체이며, 찾지 못하면 <see langword="null"/>입니다.</returns>
    private IInterferenceEvent GetEvent(InterferenceEventId eventId)
    {
        if (_events.TryGetValue(eventId,out IInterferenceEvent interferenceEvent))
        {
            return interferenceEvent;
        }

        Debug.LogError($"[Interference] {eventId} 이벤트 구현체가 등록되지 않았습니다.", this);

        return null;
    }
}
