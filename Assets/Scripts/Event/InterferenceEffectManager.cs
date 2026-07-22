using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using Debug = UnityEngine.Debug;


// 서버 권한으로 하나의 방해 효과를 실행하고 활성화와 종료를 클라이언트에 동기화합니다.
public sealed class InterferenceEffectManager : NetworkBehaviour
{
    // 서버에서 현재 실행 중인 방해 효과 식별자입니다.
    private InterferenceEffectId _serverEffectId;

    // 서버에서 현재 방해 효과를 종료할 네트워크 시간입니다.
    private double _serverEffectEndTime;

    // 로컬 클라이언트에서 실행 중인 방해 효과 식별자입니다.
    private InterferenceEffectId _localEffectId;

    // 로컬 클라이언트에서 현재 방해 효과를 종료할 네트워크 시간입니다.
    private double _localEffectEndTime;

    // 방해 효과 식별자별 구현체를 보관합니다.
    private readonly Dictionary<InterferenceEffectId, InterferenceEffectBase>
        _effects = new Dictionary<InterferenceEffectId, InterferenceEffectBase>();

    // 현재 씬의 방해 효과 관리자 인스턴스를 가져옵니다.
    public static InterferenceEffectManager Instance { get; private set; }


    // 중복 인스턴스를 제거하고 같은 GameObject의 방해 효과를 등록합니다.
    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        RegisterEffects();
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
            TryStartEffect(InterferenceEffectId.FieldVision);
        }

        if (_serverEffectId == InterferenceEffectId.None || NetworkManager.ServerTime.Time < _serverEffectEndTime)
        {
            return;
        }

        EndCurrentServerEffect(InterferenceEndReason.Normal);
    }


    // 네트워크에서 디스폰될 때 라운드 상태 구독과 로컬 방해 효과를 정리합니다.
    public override void OnNetworkDespawn()
    {
        if (RoundManager.Instance != null)
        {
            RoundManager.Instance.OnRoundStateChanged -= HandleRoundStateChanged;
        }

        EndLocalEffect(InterferenceEndReason.Despawned);
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
    public bool TryStartEffect(InterferenceEffectId effectId)
    {
        if (!IsSpawned)
        {
            return RejectStart(effectId, "Manager가 아직 Spawn되지 않았습니다.");
        }

        if (!IsServer)
        {
            return RejectStart(effectId, "서버에서만 시작할 수 있습니다.");
        }

        if (effectId == InterferenceEffectId.None)
        {
            return RejectStart(effectId, "None은 시작할 수 없습니다.");
        }

        if (_serverEffectId != InterferenceEffectId.None)
        {
            return RejectStart(effectId, $"{_serverEffectId} 효과가 이미 실행 중입니다.");
        }

        if (!IsRoundInProgress())
        {
            string roundState = RoundManager.Instance == null ? "RoundManager 없음" : RoundManager.Instance.CurrentState.ToString();

            return RejectStart(effectId, $"현재 라운드 상태에서는 시작할 수 없습니다. 상태: {roundState}");
        }

        if (!_effects.TryGetValue(effectId, out InterferenceEffectBase interferenceEffect))
        {
            return RejectStart(effectId, "등록된 방해 효과 구현체가 없습니다.");
        }

        double effectEndTime = NetworkManager.ServerTime.Time + interferenceEffect.Duration;

        _serverEffectId = effectId;
        _serverEffectEndTime = effectEndTime;

        StartEffectRpc(effectId, effectEndTime);

        return true;
    }


    // 이벤트 시작 요청을 거절하고 원인을 로그로 기록합니다.
    private bool RejectStart(InterferenceEffectId effectId, string reason)
    {
        Debug.LogWarning($"[Interference] {effectId} 시작 실패: {reason}", this);

        return false;
    }


    // 클라이언트와 호스트에서 방해 효과를 즉시 활성화합니다.
    // eventId: 시작할 방해 효과 식별자입니다.
    // eventEndTime: 효과를 종료할 네트워크 시간입니다.
    [Rpc(SendTo.ClientsAndHost)]
    private void StartEffectRpc(InterferenceEffectId effectId, double effectEndTime)
    {
        if (NetworkManager.ServerTime.Time >= effectEndTime)
        {
            return;
        }

        EndLocalEffect(InterferenceEndReason.Replaced);

        InterferenceEffectBase interferenceEffect = GetEffect(effectId);

        if (interferenceEffect == null)
        {
            return;
        }

        _localEffectId = effectId;
        _localEffectEndTime = effectEndTime;

        interferenceEffect.Activate();
    }


    // 클라이언트와 호스트에서 지정한 방해 효과를 종료합니다.
    // eventId: 종료할 방해 효과 식별자입니다.
    // eventEndTime: 시작 당시 동기화한 종료 시간입니다.
    // reason: 방해 효과를 종료하는 사유입니다.
    [Rpc(SendTo.ClientsAndHost)]
    private void EndEffectRpc(InterferenceEffectId effectId, double effectEndTime, InterferenceEndReason reason)
    {
        if (_localEffectId != effectId || _localEffectEndTime != effectEndTime)
        {
            return;
        }

        EndLocalEffect(reason);
    }


    // 서버의 현재 방해 효과 상태를 초기화하고 종료 정보를 클라이언트에 전달합니다.
    // reason: 현재 방해 효과를 종료하는 사유입니다.
    private void EndCurrentServerEffect(InterferenceEndReason reason)
    {
        if (_serverEffectId == InterferenceEffectId.None)
        {
            return;
        }

        InterferenceEffectId effectId = _serverEffectId;
        double effectEndTime = _serverEffectEndTime;

        _serverEffectId = InterferenceEffectId.None;
        _serverEffectEndTime = 0d;

        EndEffectRpc(effectId, effectEndTime, reason);
    }


    // 로컬 방해 효과 상태를 초기화하고 구현체에 종료 사유를 전달합니다.
    // reason: 로컬 방해 효과를 종료하는 사유입니다.
    private void EndLocalEffect(InterferenceEndReason reason)
    {
        if (_localEffectId == InterferenceEffectId.None)
        {
            return;
        }

        InterferenceEffectBase interferenceEffect = GetEffect(_localEffectId);

        _localEffectId = InterferenceEffectId.None;
        _localEffectEndTime = 0d;

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

        EndCurrentServerEffect(InterferenceEndReason.RoundEnded);
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
    private void RegisterEffects()
    {
        foreach (InterferenceEffectBase interferenceEffect in GetComponents<InterferenceEffectBase>())
        {
            if (interferenceEffect.Id == InterferenceEffectId.None)
            {
                Debug.LogWarning("[Interference] None 식별자는 등록할 수 없습니다.", interferenceEffect);
                continue;
            }

            if (_effects.ContainsKey(interferenceEffect.Id))
            {
                Debug.LogError($"[Interference] {interferenceEffect.Id} 효과가 중복 등록됐습니다.", interferenceEffect);
                continue;
            }

            _effects.Add(interferenceEffect.Id, interferenceEffect);
        }
    }


    // 지정한 식별자에 등록된 방해 효과 구현체를 가져옵니다.
    private InterferenceEffectBase GetEffect(InterferenceEffectId effectId)
    {
        if (_effects.TryGetValue(effectId, out InterferenceEffectBase interferenceEffect))
        {
            return interferenceEffect;
        }

        Debug.LogError($"[Interference] {effectId} 효과 구현체가 등록되지 않았습니다.", this);

        return null;
    }
}
