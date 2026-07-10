namespace Undercover.World
{
    public interface INpcState
    {
        NpcStateId Id { get; }
        void Enter(NpcContext ctx);
        void Tick(NpcContext ctx);
        void Exit(NpcContext ctx);
    }
}
