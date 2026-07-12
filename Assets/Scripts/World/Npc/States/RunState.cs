using System;
using UnityEngine;

/// <summary>
/// NPC를 달리기 속도로 목적지까지 이동시키고 도착하면 Idle 상태로 전환합니다.
/// </summary>
[Serializable]
public sealed class RunState : INpcState
{
        private Vector3 _destination;
        [SerializeField, Min(0f)] private float _speed = 4f;
        private INpcState _idleState;

        /// <inheritdoc />
        public NpcStateId Id => NpcStateId.Run;

        /// <summary>도착 후 전환할 Idle 상태를 연결합니다.</summary>
        /// <param name="idleState">도착 후 진입할 Idle 상태입니다.</param>
        public void Configure(INpcState idleState)
        {
            _idleState = idleState;
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
            ctx.Movement.SetSpeed(_speed);
            ctx.Movement.MoveTo(_destination);
            Debug.Log($"[NPC]{ctx.LifecycleSource.name} Enter Run {_destination}", ctx.LifecycleSource);
        }

        /// <inheritdoc />
        public void Execute(NpcContext ctx)
        {
            if (ctx.Movement.HasArrived && _idleState != null)
            {
                ctx.StateMachine.ChangeState(_idleState);
            }
                
        }

        /// <inheritdoc />
        public void Exit(NpcContext ctx)
        {
        }
}
