using System;
using UnityEngine;

/// <summary>
/// NPC를 정지시키고 보관된 다음 상태 전환 요청을 실행합니다.
/// </summary>
[Serializable]
public sealed class IdleState : INpcState
{
        private INpcState _requestedState;
        private bool _transitionRequested;

        /// <inheritdoc />
        public NpcStateId Id => NpcStateId.Idle;

        /// <summary>
        /// 다음에 전환할 상태를 보관합니다. 실제 전환은 <see cref="Execute"/>에서 수행됩니다.
        /// </summary>
        /// <param name="nextState">다음에 진입할 상태입니다.</param>
        public void RequestTransition(INpcState nextState)
        {
            _requestedState = nextState;
            _transitionRequested = (nextState != null);
        }

        /// <inheritdoc />
        public void Enter(NpcContext ctx)
        {
            ctx.Movement.Stop();
            Debug.Log($"[NPC]{ctx.LifecycleSource.name} Enter Idle ", ctx.LifecycleSource);
        }

        /// <inheritdoc />
        public void Execute(NpcContext ctx)
        {
            if (!_transitionRequested || _requestedState == null)
                return;

            INpcState nextState = _requestedState;
            _transitionRequested = false;
            _requestedState = null;
            ctx.StateMachine.ChangeState(nextState);
        }

        /// <inheritdoc />
        public void Exit(NpcContext ctx)
        {
        }
}
