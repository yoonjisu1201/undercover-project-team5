using System;

namespace Undercover.World
{
    [Serializable]
    public sealed class LookAroundState : INpcState
    {
        public NpcStateId Id => NpcStateId.LookAround;

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
