using UnityEngine;
using UnityEngine.Events;


// 방해 효과 종류를 식별합니다.
public enum InterferenceEffectId
{
    // 실행 중인 방해 효과가 없습니다.
    None,

    // 플레이어 시야를 방해합니다.
    FieldVision,
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


// 방해 효과의 공통 지속 시간과 활성화, 종료 신호를 관리합니다.
public abstract class InterferenceEffectBase : MonoBehaviour
{
    // 방해 효과가 활성화된 후 유지되는 시간입니다.
    [SerializeField, Range(5f, 15f)] private float _duration = 5f;

    // 방해 효과가 활성화될 때 호출됩니다.
    [SerializeField] private UnityEvent _onInterferenceStarted = new UnityEvent();

    // 방해 효과가 종료될 때 호출됩니다.
    [SerializeField] private UnityEvent _onInterferenceEnded = new UnityEvent();

    // 방해 효과 식별자입니다.
    public abstract InterferenceEffectId Id { get; }

    // 방해 효과가 활성화된 후 유지되는 시간입니다.
    public float Duration => _duration;


    // 활성화 신호를 구독 중인 Unity 이벤트에 전달합니다.
    public virtual void Activate()
    {
        Debug.Log($"[Interference] {Id} 활성화", this);

        _onInterferenceStarted.Invoke();
    }


    // 종료 신호를 구독 중인 Unity 이벤트에 전달합니다.
    // reason: 방해 효과를 종료하는 사유입니다.
    public virtual void Deactivate(InterferenceEndReason reason)
    {
        string endState = reason == InterferenceEndReason.Normal ? "종료" : "중단";

        Debug.Log($"[Interference] {Id} {endState}: {reason}", this);

        _onInterferenceEnded.Invoke();
    }
}
