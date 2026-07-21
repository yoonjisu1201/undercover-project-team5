using System.Collections.Generic;
using Unity.Netcode;
using UnityEditor.PackageManager;
using UnityEngine;
using UnityEngine.LightTransport;
using Debug = UnityEngine.Debug;

public sealed class InterferenceEventManager : NetworkBehaviour
{
    [Header("방해 이벤트 시간")]
    [SerializeField, Range(0.1f, 3f)] private float _warningDuration = 1f;
    [SerializeField, Range(5f, 15f)] private float _minimumDuration = 5f;
    [SerializeField, Range(5f, 15f)] private float _maximumDuration = 15f;

    private InterferenceEventId _serverEventId;
    private double _serverEventEndTime;

    private InterferenceEventId _localEventId;
    private double _localActiveStartTime;
    private double _localEventEndTime;
    private bool _isLocalEventActive;

    private readonly Dictionary<InterferenceEventId, IInterferenceEvent>
        _events = new Dictionary<InterferenceEventId, IInterferenceEvent>();

    public static InterferenceEventManager Instance { get; private set; }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            enabled = false;
            return;
        }

        Instance = this;
    }

    private void OnValidate()
    {
        _maximumDuration = Mathf.Max(_minimumDuration, _maximumDuration);
    }

    public override void OnNetworkSpawn()
    {
        if (IsServer && RoundManager.Instance != null)
        {
            RoundManager.Instance.OnRoundStateChanged += HandleRoundStateChanged;
        }
    }

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

    public override void OnNetworkDespawn()
    {
        if (RoundManager.Instance != null)
        {
            RoundManager.Instance.OnRoundStateChanged -= HandleRoundStateChanged;
        }

        EndLocalEvent();
    }

    public override void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }

        base.OnDestroy();
    }

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

    private bool RejectStart(InterferenceEventId eventId, string reason)
    {
        Debug.LogWarning($"[Interference] {eventId} 시작 실패: {reason}", this);

        return false;
    }

    public void RegisterEvent(IInterferenceEvent interferenceEvent)
    {
        if (interferenceEvent.Id == InterferenceEventId.None)
        {
            Debug.LogWarning($"[Interference] {interferenceEvent.Id} == None.",this);
            return;
        }

        _events[interferenceEvent.Id] = interferenceEvent;
    }

    public void UnregisterEvent(IInterferenceEvent interferenceEvent)
    {
        if (_events.TryGetValue(interferenceEvent.Id, out IInterferenceEvent registeredEvent) &&
            ReferenceEquals(registeredEvent, interferenceEvent))
        {
            _events.Remove(interferenceEvent.Id);
        }
    }

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

    [Rpc(SendTo.NotServer)]
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

    [Rpc(SendTo.NotServer)]
    private void EndEventRpc(InterferenceEventId eventId, double eventEndTime)
    {
        if (_localEventId != eventId || _localEventEndTime != eventEndTime)
        {
            return;
        }

        EndLocalEvent();
    }

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
