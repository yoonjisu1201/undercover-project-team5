using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using Debug = UnityEngine.Debug;


// 서버 권한으로 하나의 방해 효과를 실행하고 활성화와 종료를 클라이언트에 동기화합니다.
public sealed class InterferenceEffectManager : NetworkBehaviour
{
    // 서버에서 현재 실행 중인 방해 효과 식별자입니다.
    private InterferenceEffectType _serverRunningEffectType;

    // 서버에서 현재 방해 효과를 종료할 네트워크 시간입니다.
    private double _serverEffectDeadline;

    // 방해 효과 식별자별 구현체를 보관합니다.
    private readonly Dictionary<InterferenceEffectType, InterferenceEffectBase>
        _effects = new Dictionary<InterferenceEffectType, InterferenceEffectBase>();

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
            TryStartEffect(InterferenceEffectType.FieldVision);
        }

        if (Keyboard.current != null && Keyboard.current.f3Key.wasPressedThisFrame)
        {
            TryStartEffect(InterferenceEffectType.Glitch);
        }

        if (_serverRunningEffectType == InterferenceEffectType.None || NetworkManager.ServerTime.Time < _serverEffectDeadline)
        {
            return;
        }

        EndCurrentServerEffect(InterferenceEndReason.Normal);
    }


    // 네트워크에서 디스폰될 때 라운드 상태 구독을 정리합니다.
    public override void OnNetworkDespawn()
    {
        if (RoundManager.Instance != null)
        {
            RoundManager.Instance.OnRoundStateChanged -= HandleRoundStateChanged;
        }
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
    // effectId: 시작할 방해 효과 식별자입니다.
    // 반환값: 시작 조건을 만족해 요청을 수락하면 true, 그렇지 않으면 false입니다.
    public bool TryStartEffect(InterferenceEffectType effectType)
    {
        if (!IsSpawned)
        {
            return RejectStart(effectType, "Manager가 아직 Spawn되지 않았습니다.");
        }

        if (!IsServer)
        {
            return RejectStart(effectType, "서버에서만 시작할 수 있습니다.");
        }

        if (effectType == InterferenceEffectType.None)
        {
            return RejectStart(effectType, "None은 시작할 수 없습니다.");
        }

        if (_serverRunningEffectType != InterferenceEffectType.None)
        {
            return RejectStart(effectType, $"{_serverRunningEffectType} 효과가 이미 실행 중입니다.");
        }

        if (!IsRoundInProgress())
        {
            string roundState = RoundManager.Instance == null ? "RoundManager 없음" : RoundManager.Instance.CurrentState.ToString();

            return RejectStart(effectType, $"현재 라운드 상태에서는 시작할 수 없습니다. 상태: {roundState}");
        }

        if (!_effects.TryGetValue(effectType, out InterferenceEffectBase interferenceEffect))
        {
            return RejectStart(effectType, "등록된 방해 효과 구현체가 없습니다.");
        }

        double effectDeadline = NetworkManager.ServerTime.Time + interferenceEffect.Duration;

        _serverRunningEffectType = effectType;
        _serverEffectDeadline = effectDeadline;

        interferenceEffect.Activate();

        return true;
    }


    // 이벤트 시작 요청을 거절하고 원인을 로그로 기록합니다.
    private bool RejectStart(InterferenceEffectType effectType, string reason)
    {
        Debug.LogWarning($"[Interference] {effectType} 시작 실패: {reason}", this);

        return false;
    }


    // 서버의 현재 방해 효과 상태를 초기화하고 종료 정보를 클라이언트에 전달합니다.
    // reason: 현재 방해 효과를 종료하는 사유입니다.
    private void EndCurrentServerEffect(InterferenceEndReason reason)
    {
        if (_serverRunningEffectType == InterferenceEffectType.None)
        {
            return;
        }

        InterferenceEffectType effectType = _serverRunningEffectType;

        _serverRunningEffectType = InterferenceEffectType.None;
        _serverEffectDeadline = 0d;

        GetEffect(effectType)?.Deactivate(reason);
    }


    // 진행 라운드를 벗어나면 서버에서 현재 방해 효과를 종료합니다.
    // roundState: 변경된 라운드 상태입니다.
    private void HandleRoundStateChanged(RoundState roundState)
    {
        if (roundState == RoundState.InRound)
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

        return roundState == RoundState.InRound;
    }


    // 같은 GameObject에 있는 방해 효과 구현체를 식별자별로 등록합니다.
    private void RegisterEffects()
    {
        foreach (InterferenceEffectBase interferenceEffect in GetComponents<InterferenceEffectBase>())
        {
            if (interferenceEffect._type == InterferenceEffectType.None)
            {
                Debug.LogWarning("[Interference] None 식별자는 등록할 수 없습니다.", interferenceEffect);
                continue;
            }

            if (_effects.ContainsKey(interferenceEffect._type))
            {
                Debug.LogError($"[Interference] {interferenceEffect._type} 효과가 중복 등록됐습니다.", interferenceEffect);
                continue;
            }

            _effects.Add(interferenceEffect._type, interferenceEffect);
        }
    }


    // 지정한 식별자에 등록된 방해 효과 구현체를 가져옵니다.
    private InterferenceEffectBase GetEffect(InterferenceEffectType effectType)
    {
        if (_effects.TryGetValue(effectType, out InterferenceEffectBase interferenceEffect))
        {
            return interferenceEffect;
        }

        Debug.LogError($"[Interference] {effectType} 효과 구현체가 등록되지 않았습니다.", this);

        return null;
    }
}
