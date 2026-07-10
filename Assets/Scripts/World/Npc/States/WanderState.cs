using System;
using UnityEngine;

namespace Undercover.World
{
    [Serializable]
    public sealed class WanderState : INpcState
    {
        [SerializeField] private Vector3 _destination;
        [SerializeField] private NpcLocomotionMode _locomotionMode = NpcLocomotionMode.Walk;
        [NonSerialized] private INpcState _idleState;

        public NpcStateId Id => NpcStateId.Wander;

        public void Configure(INpcState idleState)
        {
            _idleState = idleState;
        }

        public void SetDestination(Vector3 destination)
        {
            _destination = destination;
        }

        public void SetLocomotionMode(NpcLocomotionMode mode)
        {
            _locomotionMode = mode;
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
