using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

// NPC 주변 Sphere Collider 반경 안에서 임시 목적지를 생성해 반복해서 배회시킵니다.
[RequireComponent(typeof(NpcMovement))]
[RequireComponent(typeof(NpcStateMachine))]
public sealed class NpcRandomWander : MonoBehaviour
{
    [Header("Wander Area")]
    [SerializeField] private SphereCollider _wanderArea;
    [SerializeField] private LayerMask _groundLayer;

    [Header("Destination Settings")]
    [SerializeField, Min(1)] private int _maxAttempts = 20;
    [SerializeField, Min(0f)] private float _raycastHeight = 10f;
    [SerializeField, Min(0f)] private float _raycastDistance = 30f;
    [SerializeField, Min(0.01f)] private float _navMeshSampleDistance = 1f;
    [SerializeField, Min(0f)] private float _minimumMoveDistance = 2f;

    [Header("Idle Settings")]
    [SerializeField, Min(0f)] private float _minimumIdleSeconds = 1f;
    [SerializeField, Min(0f)] private float _maximumIdleSeconds = 3f;

    private NpcMovement _movement;
    private NpcStateMachine _stateMachine;
    private MapRegion _spawnRegion;
    private float _nextMoveTime;
    private bool _hasRequestedMove;

    // NPC가 생성된 구역을 배회 가능 범위로 설정합니다.
    public void Initialize(MapRegion spawnRegion)
    {
        _spawnRegion = spawnRegion;
    }

    private void Reset()
    {
        FindWanderArea();
    }

    private void Awake()
    {
        _movement = GetComponent<NpcMovement>();
        _stateMachine = GetComponent<NpcStateMachine>();

        if (_wanderArea == null)
        {
            FindWanderArea();
        }

        if (_wanderArea != null)
        {
            _wanderArea.isTrigger = true;
        }
    }

    private void Start()
    {
        ScheduleNextMove();
    }

    private void Update()
    {
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer ||
            _movement == null || _stateMachine == null)
        {
            return;
        }

        if (_hasRequestedMove)
        {
            if (!_movement.HasArrived)
            {
                return;
            }

            _hasRequestedMove = false;
            ScheduleNextMove();
            return;
        }

        if (Time.time < _nextMoveTime)
        {
            return;
        }

        if (TryGetRandomDestination(out Vector3 destination))
        {
            _stateMachine.RequestWalk(destination);
            _hasRequestedMove = true;
            return;
        }

        ScheduleNextMove();
    }

    private bool TryGetRandomDestination(out Vector3 destination)
    {
        destination = default;

        if (_wanderArea == null || _spawnRegion == null || _groundLayer.value == 0)
        {
            return false;
        }

        Vector3 center = _wanderArea.transform.TransformPoint(_wanderArea.center);
        Vector3 scale = _wanderArea.transform.lossyScale;
        float radius = _wanderArea.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z));
        float minimumDistanceSquared = _minimumMoveDistance * _minimumMoveDistance;

        for (int attempt = 0; attempt < _maxAttempts; attempt++)
        {
            Vector2 offset = Random.insideUnitCircle * radius;
            Vector3 rayOrigin = new Vector3(center.x + offset.x, center.y + _raycastHeight, center.z + offset.y);

            if (!Physics.Raycast(
                    rayOrigin,
                    Vector3.down,
                    out RaycastHit groundHit,
                    _raycastDistance,
                    _groundLayer,
                    QueryTriggerInteraction.Ignore) ||
                !NavMesh.SamplePosition(
                    groundHit.point,
                    out NavMeshHit navMeshHit,
                    _navMeshSampleDistance,
                    NavMesh.AllAreas))
            {
                continue;
            }

            Vector3 sphereClosestPoint = _wanderArea.ClosestPoint(navMeshHit.position);
            if ((sphereClosestPoint - navMeshHit.position).sqrMagnitude > Mathf.Epsilon ||
                !_spawnRegion.Contains(navMeshHit.position) ||
                !_spawnRegion.SpawnArea.IsNearGroundSurface(navMeshHit.position) ||
                (navMeshHit.position - transform.position).sqrMagnitude < minimumDistanceSquared)
            {
                continue;
            }

            destination = navMeshHit.position;
            return true;
        }

        return false;
    }

    private void ScheduleNextMove()
    {
        float minimum = Mathf.Min(_minimumIdleSeconds, _maximumIdleSeconds);
        float maximum = Mathf.Max(_minimumIdleSeconds, _maximumIdleSeconds);
        _nextMoveTime = Time.time + Random.Range(minimum, maximum);
    }

    private void FindWanderArea()
    {
        if (TryGetComponent(out SphereCollider rootSphereCollider))
        {
            _wanderArea = rootSphereCollider;
            _wanderArea.isTrigger = true;
            return;
        }

        foreach (SphereCollider sphereCollider in GetComponentsInChildren<SphereCollider>(true))
        {
            if (sphereCollider.gameObject.name == "WanderArea")
            {
                _wanderArea = sphereCollider;
                _wanderArea.isTrigger = true;
                return;
            }
        }
    }
}
