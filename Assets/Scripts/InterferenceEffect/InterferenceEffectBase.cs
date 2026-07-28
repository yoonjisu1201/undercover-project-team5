using Unity.Netcode;
using UnityEngine;
using UnityEngine.Events;


// 방해 효과 종류를 식별합니다.
public enum InterferenceEffectType
{
    // 실행 중인 방해 효과가 없습니다.
    None,

    // 플레이어 시야를 방해합니다.
    FieldVision,
    
    // 화면 글리치
    Glitch,
}


// 방해 효과가 종료된 원인을 나타냅니다.
public enum InterferenceEndReason
{
    // 예정된 지속 시간이 지나 종료됐습니다.
    Normal,

    // 라운드가 종료되어 강제로 종료됐습니다.
    RoundEnded,

    // 다른 방해 효과로 교체됐습니다.
    Replaced,

    // Manager가 네트워크에서 제거됐습니다.
    Despawned,
}


// 방해 효과의 공통 지속 시간을 관리하고, 대상 클라이언트를 계산해 시작·종료 RPC를 보냅니다.
// 서버에서만 Activate()/Deactivate()를 호출합니다.
// RPC 전송과 이벤트 발생은 base가 전담합니다. 하위 클래스는 OnActivateEffect/OnDeactivateEffect에
// 순수 로컬 효과 로직만 구현하면 됩니다 (RPC나 이벤트를 직접 다루지 않습니다).
public abstract class InterferenceEffectBase : NetworkBehaviour
{
    // 방해 효과가 활성화된 후 유지되는 시간입니다.
    [SerializeField, Range(5f, 15f)] private float _duration = 5f;

    // 방해 효과가 활성화될 때 호출됩니다.
    [SerializeField] private UnityEvent _onInterferenceStarted = new UnityEvent();

    // 방해 효과가 종료될 때 호출됩니다.
    [SerializeField] private UnityEvent _onInterferenceEnded = new UnityEvent();

    // Activate 시점에 계산해 Deactivate까지 그대로 재사용할 대상 클라이언트 목록입니다.
    // Deactivate에서 다시 계산하면 그 사이 대상이 바뀌어(역할 변경, 접속 종료 등)
    // 시작 때와 다른 클라이언트에게 종료 신호가 갈 수 있으므로 반드시 캐시한 값을 재사용합니다.
    private ulong[] _activeTargetClientIds;

    // 방해 효과 식별자입니다.
    public abstract InterferenceEffectType _type { get; }

    // 방해 효과가 활성화된 후 유지되는 시간입니다.
    public float Duration => _duration;


    // 서버에서 호출합니다. 대상 클라이언트를 계산해 캐시하고, 그 대상에게만 시작 RPC를 보냅니다.
    public void Activate()
    {
        _activeTargetClientIds = GetTargetClientsList();

        ActivateRpc(RpcTarget.Group(_activeTargetClientIds, RpcTargetUse.Temp));
    }


    // 서버에서 호출합니다. Activate 시점에 캐시해둔 대상과 동일한 대상에게만 종료 RPC를 보냅니다.
    // reason: 방해 효과를 종료하는 사유입니다.
    public void Deactivate(InterferenceEndReason reason)
    {
        if (_activeTargetClientIds == null)
        {
            return;
        }

        DeactivateRpc(reason, RpcTarget.Group(_activeTargetClientIds, RpcTargetUse.Temp));

        _activeTargetClientIds = null;
    }


    // 이 방해 효과를 받아야 하는 클라이언트 ID 목록을 계산합니다.
    // 예: FieldVision은 현장 역할 클라이언트만 반환하도록 구현합니다. (#293에서 구현)
    public abstract ulong[] GetTargetClientsList();


    // 대상 클라이언트에서 로컬 효과를 켭니다. RPC나 이벤트는 base가 처리하므로 순수 로직만 작성하세요.
    protected abstract void OnActivateEffect();


    // 대상 클라이언트에서 로컬 효과를 끕니다. RPC나 이벤트는 base가 처리하므로 순수 로직만 작성하세요.
    // reason: 방해 효과를 종료하는 사유입니다.
    protected abstract void OnDeactivateEffect(InterferenceEndReason reason);


    // 대상 클라이언트에서 실제로 실행됩니다. override하지 않습니다.
    [Rpc(SendTo.SpecifiedInParams)]
    private void ActivateRpc(RpcParams rpcParams = default)
    {
        OnActivateEffect();

        Debug.Log($"[Interference] {_type} 활성화", this);

        _onInterferenceStarted.Invoke();
    }


    // 대상 클라이언트에서 실제로 실행됩니다. override하지 않습니다.
    // reason: 방해 효과를 종료하는 사유입니다.
    [Rpc(SendTo.SpecifiedInParams)]
    private void DeactivateRpc(InterferenceEndReason reason, RpcParams rpcParams = default)
    {
        ApplyDeactivate(reason);
    }


    // 종료 로직 본체입니다. DeactivateRpc와 OnNetworkDespawn 양쪽에서 재사용합니다.
    // reason: 방해 효과를 종료하는 사유입니다.
    private void ApplyDeactivate(InterferenceEndReason reason)
    {
        OnDeactivateEffect(reason);

        string endState = reason == InterferenceEndReason.Normal ? "종료" : "중단";

        Debug.Log($"[Interference] {_type} {endState}: {reason}", this);

        _onInterferenceEnded.Invoke();
    }


    // Manager가 더 이상 "현재 로컬에 적용된 효과"를 추적하지 않으므로,
    // despawn 시 로컬 효과가 켜진 채로 남지 않도록 base에서 직접 정리합니다.
    // (네트워크 전송 없이 로컬 정리만 하므로 ApplyDeactivate를 직접 호출합니다)
    public override void OnNetworkDespawn()
    {
        ApplyDeactivate(InterferenceEndReason.Despawned);
    }
}
