using System;
using UnityEngine;

// 추격 진입 시 기존 경로를 유지하거나 새 목적지로 달리는 행동을 관리합니다.
public sealed class NpcRunState : INpcState
{
    // Animator의 Run 상태 값은 Root Transition의 NpcState 조건과 동일한 2입니다.
    private const int NpcStateValue = 2;

    // Run State에서 NavMeshAgent에 적용할 기존 달리기 속도입니다.
    [Range(4f, 6f)] private float _speed = 4f;

    // Run State가 직접 사용하는 이동과 Animator 의존성만 보관합니다.
    private NpcMovement _movement;
    private Animator _animator;

    // 목적지를 함께 전달받은 Run 요청에서 사용할 위치를 보관합니다.
    private Vector3 _destination;
    // 목적지가 없는 추격 전환과 새 목적지 Run 요청을 구분합니다.
    private bool _hasDestination;

    public void Initialize(NpcMovement movement, Animator animator)
    {
        // 필요한 컴포넌트만 직접 전달받습니다.
        _movement = movement;
        _animator = animator;
    }

    public void Enter()
    {
        // 기존 경로가 일시 정지된 경우 다시 열고 Run 속도를 적용합니다.
        _movement.Resume();
        _movement.SetSpeed(_speed);

        // 기존 Root Transition이 Run State로 이동하도록 상태 값을 설정합니다.
        _animator.SetInteger(AnimatorHashes.NpcState, NpcStateValue);

        // 목적지 없는 추격 요청은 기존 NavMesh 경로를 그대로 이어서 달립니다.
        if (!_hasDestination)
        {
            Debug.Log($"[NpcRunState] '{_movement.name}' NPC가 새 목적지 없이 기존 NavMesh 경로로 Run에 진입합니다.", _movement);
            return;
        }

        // 목적지를 함께 받은 요청은 해당 위치로 새 Run 경로를 설정합니다.
        _movement.MoveTo(_destination);
    }

    public void Execute()
    {
        // Run의 종료 조건은 기존 추격 시스템이 결정하므로 매 프레임 처리하지 않습니다.
    }

    public void Exit()
    {
        // 다음 Run 요청이 이전 목적지를 재사용하지 않도록 진입 준비값을 비웁니다.
        _hasDestination = false;
    }

    public void PrepareWithCurrentPath()
    {
        // 목적지 없는 Run은 현재 NavMesh 경로를 유지한다는 뜻으로 기록합니다.
        _hasDestination = false;
    }

    public void PrepareWithDestination(Vector3 destination)
    {
        // Run 진입 전에 새 목적지를 저장하고 적용 대상으로 표시합니다.
        _destination = destination;
        _hasDestination = true;
    }

    public void UpdateDestination(Vector3 destination)
    {
        // 이미 Run 중인 NPC는 재진입하지 않고 현재 NavMesh 목적지만 갱신합니다.
        _destination = destination;
        _hasDestination = true;
        _movement.MoveTo(destination);
    }
}
