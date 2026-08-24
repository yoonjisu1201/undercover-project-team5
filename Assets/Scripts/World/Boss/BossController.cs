using System.Collections.Generic;
using Unity.Behavior;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

// 보스의 Behavior 그래프가 돌기 위한 주변 정리를 맡는다. 판단은 전부 그래프가 한다.
//
// 하는 일:
//  - 서버가 아닌 인스턴스에서 NavMeshAgent가 트랜스폼을 건드리지 않게 막는다(NetworkTransform과 충돌).
//  - 매 라운드 새로 생성되는 지하 모듈에서 배회 지점을 만들어 그래프 Blackboard에 넣는다.
//  - 먼 모듈로 순간이동시키고, 위치를 알 수 없는 쿵 소리로 "옮겨왔다"는 것만 알린다.
//    옮긴 뒤에는 그래프를 다시 시작해서 이동 노드가 목적지를 새로 잡게 한다.
//  - 이동 속도를 애니메이터 파라미터로 옮긴다. Behavior의 이동 노드는 float 하나만 쓰는데
//    이 프로젝트 애니메이터는 걷기/달리기 bool 두 개를 쓰기 때문이다. 라운드마다 외형이 바뀌고
//    외형마다 자기 Animator를 들고 있어서 NetworkAnimator로 동기화할 수 없다. 대신 각 클라이언트가
//    동기화된 위치 변화량으로 속도를 계산해 자기 화면의 Animator에 직접 넣는다.
[RequireComponent(typeof(NavMeshAgent))]
[RequireComponent(typeof(BehaviorGraphAgent))]
[RequireComponent(typeof(BossVisual))]
public class BossController : NetworkBehaviour
{
    [Header("배회")]
    [Tooltip("그래프 Blackboard의 배회 지점 변수 이름. 그래프에서 이름을 바꾸면 여기도 바꿔야 한다.")]
    [SerializeField] private string _waypointsVariableName = "Patrol Waypoints";

    [Tooltip("모듈 바닥에서 이만큼 띄운 곳을 배회 지점으로 삼는다. 바닥에 딱 붙이면 NavMesh 샘플링이 실패할 수 있다.")]
    [SerializeField, Min(0f)] private float _waypointHeightOffset = 0.5f;

    [Tooltip("모듈 중심에서 이 거리 안의 NavMesh를 찾는다. 넓은 방은 중심이 통로 밖일 수 있어 넉넉히 둔다.")]
    [SerializeField, Min(0.1f)] private float _waypointSampleRadius = 6f;

    [Header("순간이동")]
    [Tooltip("이 거리 안에 사람이 있는 모듈로는 옮기지 않는다. 눈앞에 나타나면 대응할 여지가 없다.")]
    [SerializeField, Min(0f)] private float _teleportMinPlayerDistance = 25f;

    [Tooltip("옮겨온 직후 들리는 쿵 소리. 방향을 알 수 없게 2D로 재생한다.")]
    [SerializeField] private AudioSource _teleportCueSource;

    [SerializeField] private AudioClip _teleportCueClip;

    [Header("애니메이션")]
    [SerializeField] private string _walkParameter = "IsWalking";
    [SerializeField] private string _runParameter = "IsRunning";

    [Tooltip("이 속도 이상이면 달리는 것으로 본다. 추격 속도(4.5)와 배회 속도(2.2) 사이 값이어야 한다.")]
    [SerializeField, Min(0f)] private float _runSpeedThreshold = 3.2f;

    [SerializeField, Min(0f)] private float _walkSpeedThreshold = 0.2f;

    private NavMeshAgent _agent;
    private BehaviorGraphAgent _brain;
    private BossVisual _visual;
    private Vector3 _lastAnimationPosition;
    private UndergroundRandomMapGenerator _mapGenerator;

    // 순간이동 직후 그래프를 다시 시작해야 하는지. 노드 실행 중에 Restart를 부르면 재진입이
    // 되므로 한 프레임 미뤄서 처리한다.
    private bool _restartGraphPending;
    private Transform _waypointRoot;
    private readonly List<GameObject> _waypoints = new();

    private void Awake()
    {
        _agent = GetComponent<NavMeshAgent>();
        _brain = GetComponent<BehaviorGraphAgent>();
        _visual = GetComponent<BossVisual>();
        _lastAnimationPosition = transform.position;
    }

    public override void OnNetworkSpawn()
    {
        // 서버가 아닌 인스턴스는 NavMeshAgent가 스스로 트랜스폼을 갱신하지 않게 해서
        // NetworkTransform이 동기화한 값과 충돌하지 않게 한다.
        if (!IsServer)
        {
            _agent.updatePosition = false;
            _agent.updateRotation = false;

            // 판단은 서버만 한다. BehaviorGraphAgent 의 NetcodeRunOnlyOnOwner 는 소유자 기준이라
            // 소유권이 클라이언트로 재분배되면 그쪽에서 그래프가 돌아버린다. 위치는 서버 권한이므로
            // 그 순간 보스가 얼어붙는다. 그래서 서버 여부로 직접 끈다.
            _brain.enabled = false;
            return;
        }

        _mapGenerator = FindFirstObjectByType<UndergroundRandomMapGenerator>();
        if (_mapGenerator != null)
        {
            _mapGenerator.OnGenerated += RebuildWaypoints;
        }

        RebuildWaypoints();
    }

    public override void OnNetworkDespawn()
    {
        if (_mapGenerator != null)
        {
            _mapGenerator.OnGenerated -= RebuildWaypoints;
            _mapGenerator = null;
        }

        if (IsServer)
        {
            ClearWaypoints();

            // 루트는 보스의 자식이 아니라 씬 루트에 만든 별도 오브젝트다. 여기서 지우지 않으면
            // 보스가 라운드마다 새로 스폰되면서 빈 BossPatrolPoints 가 계속 쌓인다.
            if (_waypointRoot != null)
            {
                Destroy(_waypointRoot.gameObject);
                _waypointRoot = null;
            }
        }
    }

    private void Update()
    {
        // 애니메이션은 각 클라이언트가 자기 화면의 Animator에 직접 넣는다.
        UpdateAnimatorState();

        if (IsServer && _restartGraphPending)
        {
            _restartGraphPending = false;
            _brain.Restart();
        }
    }

    // 서버는 NavMeshAgent 속도를 그대로 쓰고, 클라이언트는 동기화된 위치 변화량으로 속도를 낸다.
    // 두 값이 정확히 같지는 않지만 걷기/달리기를 가르는 데는 충분하다.
    private void UpdateAnimatorState()
    {
        // 위치는 Animator 가 없는 프레임에도 갱신해야 한다. 안 그러면 외형 모델이 붙는 순간
        // 몇 프레임 전 위치와 비교되어 속도가 크게 튀고, 멈춰 있어도 달리기 모션이 한 번 나온다.
        Vector3 position = transform.position;
        float clientSpeed = Vector3.Distance(position, _lastAnimationPosition) / Mathf.Max(Time.deltaTime, 0.0001f);
        _lastAnimationPosition = position;

        Animator animator = _visual != null ? _visual.ActiveAnimator : null;
        if (animator == null)
        {
            return;
        }

        float speed = IsServer ? _agent.velocity.magnitude : clientSpeed;

        animator.SetBool(_walkParameter, speed > _walkSpeedThreshold);
        animator.SetBool(_runParameter, speed >= _runSpeedThreshold);
    }

    #region 순간이동

    // 사람들에게서 가장 먼 모듈로 옮긴다. 성공하면 위치를 알 수 없는 쿵 소리를 모두에게 들려준다.
    // 그래프의 순간이동 노드가 호출한다.
    public bool TeleportToDistantModule()
    {
        if (!IsServer || _mapGenerator == null)
        {
            return false;
        }

        List<Vector3> playerPositions = CollectPlayerPositions();

        Vector3 bestPosition = Vector3.zero;
        float bestDistance = _teleportMinPlayerDistance;
        bool found = false;

        foreach (UndergroundModule module in _mapGenerator.PlacedModules)
        {
            if (module == null || module.Bounds == null)
            {
                continue;
            }

            Vector3 candidate = module.Bounds.bounds.center;
            candidate.y = module.Bounds.bounds.min.y + _waypointHeightOffset;

            // 가장 가까운 사람과의 거리로 평가한다. 한 명에게서 멀어도 다른 사람 옆이면 의미가 없다.
            float nearest = NearestDistance(candidate, playerPositions);
            if (nearest < bestDistance)
            {
                continue;
            }

            if (!NavMesh.SamplePosition(candidate, out NavMeshHit hit, 3f, NavMesh.AllAreas))
            {
                continue;
            }

            bestDistance = nearest;
            bestPosition = hit.position;
            found = true;
        }

        if (!found)
        {
            return false;
        }

        // Warp 는 경로를 버리고 위치만 옮긴다. 이동 중이던 경로가 남으면 새 자리에서 되돌아가려 한다.
        _agent.Warp(bestPosition);

        if (_agent.isOnNavMesh)
        {
            _agent.ResetPath();
        }

        // 이동 노드는 "표적 위치가 바뀔 때만" 목적지를 다시 잡는다. Warp 로 경로가 사라져도
        // 표적은 그대로이므로 목적지를 다시 잡지 않고, 결과적으로 새 자리에서 멈춰 선다.
        // 그래프를 다시 시작해서 이동 노드가 목적지를 새로 계산하게 만든다.
        _restartGraphPending = true;

        PlayTeleportCueRpc();
        return true;
    }

    private List<Vector3> CollectPlayerPositions()
    {
        var positions = new List<Vector3>();

        foreach (PlayerHealth survivor in SurvivorRegistry.Active())
        {
            positions.Add(survivor.transform.position);
        }

        return positions;
    }

    // 사람이 아무도 없으면 어디로 가도 되므로 무한대를 돌려준다.
    private static float NearestDistance(Vector3 point, List<Vector3> positions)
    {
        float nearest = float.MaxValue;
        foreach (Vector3 position in positions)
        {
            nearest = Mathf.Min(nearest, Vector3.Distance(point, position));
        }

        return nearest;
    }

    // 방향을 알 수 없어야 한다. 어디서 났는지 알면 "저쪽에 있다"가 되어 오히려 안심하게 된다.
    [Rpc(SendTo.Everyone)]
    private void PlayTeleportCueRpc()
    {
        if (_teleportCueSource == null || _teleportCueClip == null)
        {
            return;
        }

        _teleportCueSource.PlayOneShot(_teleportCueClip);
    }

    #endregion

    #region 배회 지점

    // 배회 지점은 모듈 하나당 하나. 방마다 들르게 해야 보스가 한쪽 구석만 맴돌지 않는다.
    private void RebuildWaypoints()
    {
        ClearWaypoints();

        if (_mapGenerator == null)
        {
            return;
        }

        if (_waypointRoot == null)
        {
            _waypointRoot = new GameObject("BossPatrolPoints").transform;
        }

        foreach (UndergroundModule module in _mapGenerator.PlacedModules)
        {
            if (module == null || module.Bounds == null)
            {
                continue;
            }

            Vector3 center = module.Bounds.bounds.center;
            center.y = module.Bounds.bounds.min.y + _waypointHeightOffset;

            // 모듈 중심이 벽 안이나 통로 밖일 수 있다. NavMesh 위로 당겨두지 않으면
            // Patrol 노드가 그 지점을 못 찾아 실패하고, 보스가 가만히 서 있게 된다.
            if (!NavMesh.SamplePosition(center, out NavMeshHit navHit, _waypointSampleRadius, NavMesh.AllAreas))
            {
                continue;
            }

            center = navHit.position;

            var point = new GameObject($"PatrolPoint_{_waypoints.Count}");
            point.transform.SetParent(_waypointRoot, worldPositionStays: true);
            point.transform.position = center;
            _waypoints.Add(point);
        }

        // 목록을 그때그때 새로 만들어 넘긴다. Blackboard가 들고 있는 List를 직접 고치면
        // 그래프가 이미 순회 중인 인덱스와 어긋난다.
        if (!_brain.SetVariableValue(_waypointsVariableName, new List<GameObject>(_waypoints)))
        {
            Debug.LogWarning(
                $"[보스 배회] 그래프에 '{_waypointsVariableName}' 변수가 없어 배회 지점을 넘기지 못했습니다.", this);
        }
    }

    private void ClearWaypoints()
    {
        foreach (GameObject point in _waypoints)
        {
            if (point != null)
            {
                Destroy(point);
            }
        }

        _waypoints.Clear();
    }

    #endregion
}
