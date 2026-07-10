using UnityEngine;

namespace Undercover.World
{
    public sealed class NpcContext
    {
        public NpcStateMachine Machine;
        public NpcMovement Movement;
        public MonoBehaviour Owner;
    }
}
