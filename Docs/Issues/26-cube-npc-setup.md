# Issue 26 Cube NPC Setup

## 완료 조건

- 임시 평면의 NavMesh 위에서 큐브 한 개가 고정 좌표와 클릭 지점으로 이동한다.
- 상태 로그가 `Idle -> Wander -> Idle` 순서로 출력된다.
- Idle에서는 Stand, Wander에서는 Walk 또는 Run 속도가 적용된다.
- `SetDestination`은 상태 진입 또는 새 목적지 요청 때만 호출된다.
- Unity Console에 컴파일 오류와 실행 오류가 없다.

## TODO 작업 순서

1. `NpcMovement.Awake`, `NpcStateMachine.Awake`, `TransitionTo`를 채워 컴파일한다.
2. `NpcMovement.SetLocomotionMode`와 `WanderState.Enter`를 채워 Walk/Run 속도 차이를 확인한다.
3. `NpcMovement.MoveTo`를 채워 고정 좌표 이동을 확인한다.
4. `NpcMovement.HasArrived`와 Idle/Wander의 `Tick`을 채워 왕복 상태 전환을 확인한다.
5. `NpcClickMoveInput.Update`와 `RequestMove`를 채워 클릭 지점 이동을 확인한다.
6. `OnDestroy`의 상태 종료와 이벤트 정리 자리를 확인한다.

## Unity 수동 구성

테스트 Scene은 `Assets/Scenes/Works/NpcBehaviorTest.unity`에 구성되어 있다.

- `Environment/Ground`: 40x40 Plane, Static
- `Environment/Navigation`: `NavMeshSurface`, Bake 완료
- `NPC_Test_Cube`: Cube + `NavMeshAgent` + `NpcMovement` + `NpcStateMachine`
- `NPC_Test_Input`: Main Camera와 NPC StateMachine 참조 연결
- `Waypoints/Waypoint_01~06`: #27에서 사용할 빈 Transform

FSM TODO를 채운 뒤 이 Scene을 열어 Play Mode로 검증한다. 프리팹이 필요해지는 시점에 `NPC_Test_Cube`를 `Assets/Prefabs/Npc/` 아래에 수동 저장한다.

씬과 프리팹은 Unity Editor에서 직접 생성한다. YAML 수동 편집이나 자동 생성 스크립트는 사용하지 않는다.

## 확인 포인트

- Agent가 Bake된 NavMesh 위에 놓였는지 확인한다.
- `remainingDistance`는 `pathPending`이 끝난 뒤 읽는다.
- 도착 판정은 `stoppingDistance`와 작은 허용값을 함께 사용한다.
- 새 상태는 `INpcState` 구현 클래스 하나를 추가하고 StateMachine 연결만 최소 수정한다.
- #27 전에는 UniTask, 랜덤 웨이포인트, NpcSpawner를 추가하지 않는다.
- #27에서는 배회 중 Stand/Walk/Run 선택을 추가하고, 수상 행동은 후속 일정에 별도 State로 추가한다.
