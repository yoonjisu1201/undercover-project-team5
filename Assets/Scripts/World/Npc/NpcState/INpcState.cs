// NpcStateMachine이 실행할 상태의 공통 수명 주기입니다.
public interface INpcState
{
    void Enter();
    void Execute();
    void Exit();
}
