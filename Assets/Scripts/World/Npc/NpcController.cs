using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

/// <summary>
/// FSM 상태를 기준으로 Idle 대기, 목적지 예약 교체, 이동 요청과 정체 복구를 조정합니다.
/// Scene 의존성은 Spawner에서 주입받고 비동기 작업과 예약은 활성화 수명에 맞춰 정리합니다.
/// </summary>
[RequireComponent(typeof(NpcStateMachine))]
public sealed class NpcController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private NpcStateMachine _stateMachine;

    [Header("Destination Selection")]
    [SerializeField, Min(0f)] private float _navMeshSampleDistance = 2f;
    [SerializeField, Min(1)] private int _maxDestinationAttempts = 8;

    [Header("Idle")]
    [SerializeField, Min(0f)] private float _minimumIdleSeconds = 1f;
    [SerializeField, Min(0f)] private float _maximumIdleSeconds = 3f;
    [SerializeField, Range(0f, 1f)] private float _runProbability = 0.25f;

    [Header("Stall Recovery")]
    [SerializeField, Min(0f)] private float _stallTimeoutSeconds = 5f;
    [SerializeField, Min(0f)] private float _minimumProgressDistance = 0.1f;

    private INpcDestinationProvider _destinationProvider;
    private MapBlockController _blockController;
    private NpcDestination _reservedDestination;
    private CancellationTokenSource _activationCancellationSource;

    private int _idleSessionVersion;
    private bool _isMoving;
    private Vector3 _lastProgressPosition;
    private float _lastProgressTime;

    /// <summary>
    /// Inspector용 StateMachine 참조를 같은 오브젝트에서 자동 연결합니다.
    /// </summary>
    private void Reset()
    {
        _stateMachine = GetComponent<NpcStateMachine>();
    }

    /// <summary>
    /// 비어 있는 StateMachine 참조만 복구하며 Scene 의존성은 Configure로 받습니다.
    /// </summary>
    private void Awake()
    {
        if (_stateMachine == null)
        {
            _stateMachine = GetComponent<NpcStateMachine>();
        }
    }

    /// <summary>
    /// 상태 구독과 활성화 수명을 새로 연결하고 재대여된 Idle NPC의 대기를 재개합니다.
    /// </summary>
    private void OnEnable()
    {
        CreateActivationCancellationSource();

        if (_stateMachine != null)
        {
            _stateMachine.StateChanged -= HandleStateChanged;
            _stateMachine.StateChanged += HandleStateChanged;
        }

        if (_stateMachine != null &&
            _stateMachine.CurrentStateId == NpcStateId.Idle)
        {
            _idleSessionVersion++;
            StartWaitingForDestination();
        }
    }

    /// <summary>
    /// 이동 진전만 확인해 정체를 복구하며 경로는 다시 계산하지 않습니다.
    /// </summary>
    private void Update()
    {
        UpdateStallRecovery();
    }

    /// <summary>
    /// 활성화 수명·상태 구독·예약을 정리해 비활성 NPC의 유령 작업을 막습니다.
    /// </summary>
    private void OnDisable()
    {
        CancelActivationCancellationSource();

        if (_stateMachine != null)
        {
            _stateMachine.RequestIdle();
            _stateMachine.StateChanged -= HandleStateChanged;
        }

        ReleaseReservedDestination();
        _isMoving = false;
    }

    /// <summary>
    /// 파괴 시 남은 상태 구독·대기·예약을 최종 정리합니다.
    /// </summary>
    private void OnDestroy()
    {
        if (_stateMachine != null)
        {
            _stateMachine.StateChanged -= HandleStateChanged;
        }

        CancelActivationCancellationSource();
        ReleaseReservedDestination();
    }

    /// <summary>
    /// 외부 Provider를 주입해 기본 Checkpoint Provider보다 우선 사용합니다.
    /// </summary>
    public void SetDestinationProvider(INpcDestinationProvider destinationProvider)
    {
        _destinationProvider = destinationProvider;
    }

    /// <summary>
    /// Spawner의 Scene BlockController를 주입하고 기본 Provider를 구성합니다.
    /// 이미 외부 Provider가 있으면 덮어쓰지 않습니다.
    /// </summary>
    public void Configure(MapBlockController blockController)
    {
        _blockController = blockController;

        if (_destinationProvider != null || _blockController == null)
        {
            return;
        }

        _destinationProvider = new NpcCheckpointDestinationProvider(
            _blockController,
            _navMeshSampleDistance,
            _maxDestinationAttempts);
    }

    /// <summary>
    /// Spawner가 확보한 Checkpoint 예약을 늘리지 않고 NPC 소유로 인계합니다.
    /// </summary>
    public void AssignSpawnCheckpoint(NpcCheckpoint checkpoint, Vector3 position)
    {
        if (checkpoint == null)
        {
            return;
        }

        ReleaseReservedDestination();
        _reservedDestination = new NpcDestination(position, checkpoint);
    }

    /// <summary>
    /// Pool 반환 전에 대기와 이동을 멈추고 보유 예약을 한 번 해제합니다.
    /// </summary>
    public void PrepareForPoolReturn()
    {
        CancelActivationCancellationSource();

        if (_stateMachine != null)
        {
            _stateMachine.RequestIdle();
        }

        ReleaseReservedDestination();
        _isMoving = false;
    }

    /// <summary>
    /// 이전 Idle 대기를 무효화하고 새 상태에 맞춰 대기 또는 정체 추적을 시작합니다.
    /// </summary>
    private void HandleStateChanged(NpcStateId stateId)
    {
        _idleSessionVersion++;

        if (stateId == NpcStateId.Idle)
        {
            _isMoving = false;
            StartWaitingForDestination();
            return;
        }

        if (stateId == NpcStateId.Walk || stateId == NpcStateId.Run)
        {
            _isMoving = true;
            _lastProgressPosition = transform.position;
            _lastProgressTime = Time.time;
            return;
        }

        _isMoving = false;
    }

    /// <summary>
    /// 필수 의존성과 활성화 토큰이 유효할 때 Idle 목적지 탐색을 시작합니다.
    /// </summary>
    private void StartWaitingForDestination()
    {
        if (_activationCancellationSource == null ||
            _activationCancellationSource.IsCancellationRequested ||
            _destinationProvider == null ||
            _stateMachine == null)
        {
            return;
        }

        WaitForDestinationAsync(_activationCancellationSource.Token).Forget();
    }

    /// <summary>
    /// 같은 Idle 세션에서 목적지를 재시도하고 성공 시 예약 교체 후 이동을 요청합니다.
    /// 실패에는 기존 예약을 유지하며 상태 변경이나 비활성화 시 정상 종료합니다.
    /// </summary>
    private async UniTaskVoid WaitForDestinationAsync(CancellationToken cancellationToken)
    {
        int idleSessionVersion = _idleSessionVersion;

        try
        {
            while (!cancellationToken.IsCancellationRequested &&
                _stateMachine.CurrentStateId == NpcStateId.Idle &&
                idleSessionVersion == _idleSessionVersion)
            {
                float minimumSeconds = Mathf.Max(0f, _minimumIdleSeconds);
                float maximumSeconds = Mathf.Max(minimumSeconds, _maximumIdleSeconds);
                float delaySeconds = UnityEngine.Random.Range(
                    minimumSeconds,
                    maximumSeconds);

                await UniTask.Delay(
                    TimeSpan.FromSeconds(delaySeconds),
                    cancellationToken: cancellationToken);

                if (_stateMachine.CurrentStateId != NpcStateId.Idle ||
                    idleSessionVersion != _idleSessionVersion)
                {
                    return;
                }

                NpcCheckpoint currentCheckpoint = _reservedDestination?.Checkpoint;

                if (!_destinationProvider.TryReserveDestination(
                        transform.position,
                        currentCheckpoint,
                        out NpcDestination nextDestination))
                {
                    continue;
                }

                SwapReservation(nextDestination);
                RequestMovement(nextDestination);
                return;
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    /// <summary>
    /// 새 예약 성공 후에만 기존 예약을 해제하며 같은 Checkpoint면 위치만 교체합니다.
    /// </summary>
    private void SwapReservation(NpcDestination nextDestination)
    {
        if (nextDestination == null)
        {
            return;
        }

        if (_reservedDestination != null &&
            _reservedDestination.Checkpoint != nextDestination.Checkpoint)
        {
            _destinationProvider.ReleaseDestination(_reservedDestination);
        }

        _reservedDestination = nextDestination;
    }

    /// <summary>
    /// 설정 확률을 한 번 판정해 예약 목적지까지 Walk 또는 Run을 요청합니다.
    /// </summary>
    private void RequestMovement(NpcDestination destination)
    {
        if (UnityEngine.Random.value < Mathf.Clamp01(_runProbability))
        {
            _stateMachine.RequestRun(destination.Position);
        }
        else
        {
            _stateMachine.RequestWalk(destination.Position);
        }
    }

    /// <summary>
    /// 제한 시간 동안 이동 진전이 없으면 예약을 유지한 채 Idle로 복귀합니다.
    /// </summary>
    private void UpdateStallRecovery()
    {
        if (!_isMoving || _stateMachine == null)
        {
            return;
        }

        float minimumProgressDistance = Mathf.Max(0.0001f, _minimumProgressDistance);
        float minimumProgressDistanceSquared =
            minimumProgressDistance * minimumProgressDistance;

        if ((transform.position - _lastProgressPosition).sqrMagnitude >=
            minimumProgressDistanceSquared)
        {
            _lastProgressPosition = transform.position;
            _lastProgressTime = Time.time;
            return;
        }

        if (Time.time - _lastProgressTime < Mathf.Max(0f, _stallTimeoutSeconds))
        {
            return;
        }

        _isMoving = false;
        _stateMachine.RequestIdle();
    }

    /// <summary>
    /// 현재 예약을 Provider를 통해 한 번 반환하고 참조를 비웁니다.
    /// </summary>
    private void ReleaseReservedDestination()
    {
        if (_reservedDestination == null)
        {
            return;
        }

        if (_destinationProvider != null)
        {
            _destinationProvider.ReleaseDestination(_reservedDestination);
        }
        else if (_reservedDestination.Checkpoint != null)
        {
            _reservedDestination.Checkpoint.ReleaseReservation();
        }

        _reservedDestination = null;
    }

    /// <summary>
    /// 이전 활성화 작업을 취소하고 현재 대기에 연결할 토큰을 만듭니다.
    /// </summary>
    private void CreateActivationCancellationSource()
    {
        CancelActivationCancellationSource();
        _activationCancellationSource =
            CancellationTokenSource.CreateLinkedTokenSource(destroyCancellationToken);
    }

    /// <summary>
    /// 활성화별 대기를 취소·해제해 비활성 NPC의 후속 작업을 막습니다.
    /// </summary>
    private void CancelActivationCancellationSource()
    {
        if (_activationCancellationSource == null)
        {
            return;
        }

        _activationCancellationSource.Cancel();
        _activationCancellationSource.Dispose();
        _activationCancellationSource = null;
    }
}
