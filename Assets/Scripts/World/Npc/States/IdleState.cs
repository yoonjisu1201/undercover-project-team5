using UnityEngine;

/// <summary>
/// NPC 이동을 정지한 상태를 나타냅니다.
/// </summary>
public sealed class IdleState : INpcState
{
        /// <inheritdoc />
        public NpcStateId Id => NpcStateId.Idle;

        /// <inheritdoc />
        public void Enter(NpcContext ctx)
        {
            ctx.Movement.Stop();

            Debug.Log($"[NPC]{ctx.LifecycleSource.name} Enter Idle ", ctx.LifecycleSource);
        }

        /// <inheritdoc />
        public void Execute(NpcContext ctx)
        {

        }

        /// <inheritdoc />
        public void Exit(NpcContext ctx)
        {
        }
}
