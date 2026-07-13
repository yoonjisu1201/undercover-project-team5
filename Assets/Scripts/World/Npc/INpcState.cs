/// <summary>
/// NPC 상태가 따라야 하는 진입, 실행, 종료 수명 주기를 정의합니다.
/// </summary>
public interface INpcState
{
    /// <summary>상태를 식별하는 값을 가져옵니다.</summary>
    NpcStateId Id { get; }

    /// <summary>상태로 전환될 때 한 번 실행됩니다.</summary>
    /// <param name="ctx">NPC 상태 실행에 필요한 공용 참조입니다.</param>
    void Enter(NpcContext ctx);

    /// <summary>현재 상태가 유지되는 동안 매 프레임 실행됩니다.</summary>
    /// <param name="ctx">NPC 상태 실행에 필요한 공용 참조입니다.</param>
    void Execute(NpcContext ctx);

    /// <summary>다른 상태로 전환되거나 상태 머신이 종료될 때 한 번 실행됩니다.</summary>
    /// <param name="ctx">NPC 상태 실행에 필요한 공용 참조입니다.</param>
    void Exit(NpcContext ctx);
}
