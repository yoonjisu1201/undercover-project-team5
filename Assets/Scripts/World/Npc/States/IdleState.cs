using System;
using UnityEngine;

namespace Undercover.World
{
    [Serializable]
    public sealed class IdleState : INpcState
    {
        [NonSerialized] private INpcState _moveState;
        [NonSerialized] private bool _moveRequested;

        public NpcStateId Id => NpcStateId.Idle;

        public void Configure(INpcState moveState)
        {
            _moveState = moveState;
        }

        public void RequestMove()
        {
            _moveRequested = true;
        }

        public void Enter(NpcContext ctx)
        {
        }

        public void Tick(NpcContext ctx)
        {
        }

        public void Exit(NpcContext ctx)
        {
        }
    }
}
