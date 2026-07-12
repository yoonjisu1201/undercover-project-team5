using UnityEngine;

/// <summary>
/// NPC 상태가 상태 머신과 이동 기능에 접근할 수 있도록 공용 참조를 제공합니다.
/// </summary>
public sealed class NpcContext
{
    /// <summary>상태 전환을 중재하는 상태 머신입니다.</summary>
    public NpcStateMachine StateMachine;

    /// <summary>NavMeshAgent 이동을 담당하는 구성 요소입니다.</summary>
    public NpcMovement Movement;

    /// <summary>로그 문맥과 Unity 수명 주기를 제공하는 구성 요소입니다.</summary>
    public MonoBehaviour LifecycleSource;
}
