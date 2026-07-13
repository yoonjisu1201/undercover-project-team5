using UnityEngine;

/// <summary>
/// NPC를 걷기 속도로 목적지까지 이동시키고 도착하면 Idle 상태로 전환합니다.
/// </summary>
public sealed class WalkState : INpcState
{
    private readonly float _speed;
    
    private Vector3? _destination;
    
    /// <inheritdoc />
    public NpcStateId Id => NpcStateId.Walk;
    
    /// <summary>
    /// 걷기 상태를 생성합니다.
    /// </summary>
    /// <param name="speed">걷기 이동 속도입니다.</param>
    public WalkState(float speed)
    {
        _speed = Mathf.Max(0f, speed);
    }
    
    
    /// <summary>다음 진입 시 사용할 목적지를 저장합니다.</summary>
    /// <param name="destination">이동할 월드 좌표입니다.</param>
    public void SetDestination(Vector3 destination)
    {
        _destination = destination;
    }
    
    /// <inheritdoc />
    public void Enter(NpcContext ctx)
    {
        if (!_destination.HasValue)
        {
            Debug.LogError($"[NPC]{ctx.LifecycleSource.name} Walk destination is not set.",
                ctx.LifecycleSource);
            return;
        }
    
        ctx.Movement.SetSpeed(_speed);
        ctx.Movement.MoveTo(_destination.Value);
        Debug.Log($"[NPC]{ctx.LifecycleSource.name} Enter Walk {_destination}", ctx.LifecycleSource);
    }
    
    /// <inheritdoc />
    public void Execute(NpcContext ctx)
    {
        if (ctx.Movement.HasArrived)
        {
            ctx.StateMachine.RequestIdle();
        } 
    }
    
    /// <inheritdoc />
    public void Exit(NpcContext ctx)
    {
        _destination = null;
    }
}
