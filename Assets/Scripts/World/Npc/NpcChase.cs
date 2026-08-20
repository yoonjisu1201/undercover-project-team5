using Unity.Netcode;
using UnityEngine;

// 검거 판정 후 도망가는 NPC의 목적지를 계속 이어줍니다.
//
// 배회와 추격은 목적지를 고르는 규칙이 다릅니다. 배회는 완전히 도착한 뒤 다음 목적지를 고르지만,
// 추격은 멈추지 않고 달려야 하므로 도착 전에 미리 다음 목적지를 잡습니다.
// 한 클래스에 섞여 있으면 "Run 상태는 목적지를 가진다"는 조건을 누가 지키는지 불분명해지므로 분리했습니다.
[RequireComponent(typeof(NpcMovement))]
[RequireComponent(typeof(NpcStateMachine))]
[RequireComponent(typeof(NpcRandomWander))]
public sealed class NpcChase : MonoBehaviour
{
    // 이만큼 남았을 때 다음 목적지를 잡아, 멈추지 않고 이어 달리게 합니다.
    [SerializeField, Min(0.5f)] private float _repathDistance = 3f;

    private NpcMovement _movement;
    private NpcStateMachine _stateMachine;
    private NpcRandomWander _wander;

    // 이 NPC가 지금 추격당하는 대상인지 여부입니다. 배회 로직이 이 값을 보고 물러납니다.
    public bool IsChasing
    {
        get
        {
            ArrestChaseManager chaseManager = ArrestChaseManager.Instance;

            return chaseManager != null &&
                   chaseManager.CurrentState == ArrestChaseState.Chasing &&
                   chaseManager.Target != null &&
                   chaseManager.Target.gameObject == gameObject;
        }
    }

    private void Awake()
    {
        _movement = GetComponent<NpcMovement>();
        _stateMachine = GetComponent<NpcStateMachine>();
        _wander = GetComponent<NpcRandomWander>();
    }

    private void Update()
    {
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer)
        {
            return;
        }

        if (!IsChasing || _movement.IsHeldExternally)
        {
            return;
        }

        // 추격 시작은 목적지 없이 Run으로 전환되므로, 향할 곳을 여기서 채워 준다.
        // 경로가 아예 없을 때도 IsNearDestination이 true를 돌려주기 때문에 이 한 줄로 두 경우가 함께 처리된다.
        // (경로 없음 = 기다릴 것이 없음 = 지금 목적지를 골라야 함)
        if (!_movement.IsNearDestination(_repathDistance))
        {
            return;
        }

        // 목적지를 못 찾으면 다음 프레임에 다시 시도한다. 추격은 Idle로 돌아가지 않는다.
        if (_wander.TryGetDestination(out Vector3 destination))
        {
            _stateMachine.ChangeToRun(destination);
        }
    }
}
