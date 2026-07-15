# NPC Idle/Walk Animation Design

## Goal

`AnimTest` 씬의 `Modular_Character_08`에서 Humanoid Idle/Walk 애니메이션을 확인하고, 이후 `NPC_SimpleWander`가 기존 FSM 상태에 맞춰 같은 애니메이션을 재생할 수 있게 한다.

## Scope

- `X Bot@Breathing Idle.fbx`와 `X Bot@Strut Walking.fbx`를 Humanoid 반복 애니메이션으로 사용한다.
- Idle과 Walk 상태만 포함하는 Animator Controller를 만든다.
- `Modular_Character_08`의 Animator에 Controller를 연결하고 Root Motion을 끈다.
- `NpcAnimator` 컴포넌트 하나를 추가해 기존 `NpcStateMachine.StateChanged` 이벤트를 구독한다.
- `NpcStateId.Idle`이면 Animator의 `Idle`, `NpcStateId.Walk`이면 `Walk`로 짧게 CrossFade한다.
- 기존 `NpcStateMachine`, `IdleState`, `WalkState`, `NpcMovement`는 수정하지 않는다.
- Run 애니메이션, Blend Tree, 속도 파라미터, 네트워크 동기화는 이번 범위에 포함하지 않는다.

## Structure

### Animator Controller

- 상태: `Idle`, `Walk`
- 기본 상태: `Idle`
- 파라미터와 Animator Transition 조건은 만들지 않는다.
- 상태 전환은 `NpcAnimator`가 상태 이름 해시를 사용해 `Animator.CrossFade`로 요청한다.

### NpcAnimator

- 같은 NPC의 `NpcStateMachine`과 캐릭터 Visual의 `Animator`를 직렬화 참조로 받는다.
- 활성화 시 `StateChanged`를 구독하고 비활성화 시 해제한다.
- Idle과 Walk만 처리하며 다른 상태에는 새 동작을 추가하지 않는다.
- 애니메이션 에셋을 런타임에 로드하지 않는다. Controller에 연결된 상태만 재생한다.

## Runtime Flow

```text
NpcStateMachine.RequestIdle
-> StateChanged(Idle)
-> NpcAnimator
-> Animator.CrossFade(Idle)

NpcStateMachine.RequestWalk
-> StateChanged(Walk)
-> NpcAnimator
-> Animator.CrossFade(Walk)
```

NPC 위치 이동과 회전은 계속 `NavMeshAgent`가 담당한다. Animator의 Apply Root Motion은 꺼서 애니메이션이 실제 이동을 중복 적용하지 않게 한다.

## AnimTest Verification

- `Modular_Character_08`의 Avatar가 유효하고 Controller가 연결돼 있어야 한다.
- Play Mode 시작 시 Idle이 재생돼야 한다.
- FSM이 Walk로 전환되면 Walk가 재생되고, 목적지 도착 후 Idle로 복귀해야 한다.
- Console에 Animator, Avatar, Missing Script 오류가 없어야 한다.

## Reuse

검증이 끝나면 같은 Controller와 `NpcAnimator` 구성을 `NPC_SimpleWander`의 Visual 캐릭터에 적용한다. 기존 큐브 Visual 교체와 150 NPC 적용은 별도 요청 범위로 남긴다.
