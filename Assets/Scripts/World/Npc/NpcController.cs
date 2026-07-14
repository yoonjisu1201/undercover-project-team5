using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

[RequireComponent(typeof(NpcStateMachine))]
public sealed class NpcController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private NpcStateMachine _stateMachine;
    [SerializeField] private MapBlockController _blockController;

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
    private NpcDestination _reservedDestination;
    private CancellationTokenSource _activationCancellationSource;

    private bool _hasObservedState;
    private bool _isIdle;
    private int _idleSessionVersion;
    private bool _isMoving;
    private Vector3 _lastProgressPosition;
    private float _lastProgressTime;

    /// <summary>
    /// Inspector에서 사용할 NpcStateMachine 참조를 자동으로 연결합니다.
    /// </summary>
    private void Reset()
    {
        _stateMachine = GetComponent<NpcStateMachine>();
    }

    /// <summary>
    /// 필수 참조를 보완하고 외부 Provider가 없으면 기본 Checkpoint Provider를 생성합니다.
    /// </summary>
    private void Awake()
    {
        if (_stateMachine == null)
        {
            _stateMachine = GetComponent<NpcStateMachine>();
        }

        if (_blockController == null)
        {
            _blockController = FindFirstObjectByType<MapBlockController>();
        }

        if (_destinationProvider == null)
        {
            _destinationProvider = new NpcCheckpointDestinationProvider(
                _blockController,
                _navMeshSampleDistance,
                _maxDestinationAttempts);
        }
    }

    /// <summary>
    /// 상태 변경 이벤트를 구독하고 Pool 재활성화마다 새로운 취소 수명과
    /// Idle 대기 세션을 시작합니다.
    /// </summary>
    private void OnEnable()
    {
        // 이전 활성화에서 이미 Idle을 확인했다면 세션 번호를 올리고 대기를 직접 재시작합니다.
        // FSM은 같은 Idle 요청을 무시하므로 StateChanged가 다시 발생하지 않기 때문입니다.
        CreateActivationCancellationSource();

        if (_stateMachine != null)
        {
            _stateMachine.StateChanged -= HandleStateChanged;
            _stateMachine.StateChanged += HandleStateChanged;
        }

        if (_hasObservedState && _isIdle)
        {
            _idleSessionVersion++;
            StartWaitingForDestination();
        }
    }

    /// <summary>
    /// 이동 상태에서 위치 진행 여부를 가볍게 확인해 정체 복구를 수행합니다.
    /// </summary>
    private void Update()
    {
        UpdateStallRecovery();
    }

    /// <summary>
    /// 이벤트와 활성화 수명을 정리하고 보유한 Checkpoint 예약을 한 번 해제합니다.
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
    /// 파괴 시 남은 이벤트 구독, 비동기 수명과 Checkpoint 예약을 정리합니다.
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

    public void SetDestinationProvider(INpcDestinationProvider destinationProvider)
    {
        _destinationProvider = destinationProvider;
    }

    /// <summary>
    /// Spawner가 이미 예약한 Checkpoint를 중복 예약 없이 현재 목적지로 인계받습니다.
    /// </summary>
    /// <param name="checkpoint">Spawner가 예약한 Checkpoint입니다.</param>
    /// <param name="position">해당 Checkpoint 안에서 배치된 위치입니다.</param>
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
    /// 풀 반환 전에 대기 작업과 이동을 중단하고 보유한 예약을 해제합니다.
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
    /// 새 상태를 기록하고 이전 Idle 대기를 무효화하며, 상태에 맞춰
    /// 다음 목적지 대기 또는 이동 진행 추적을 시작합니다.
    /// </summary>
    /// <param name="stateId">전환이 완료된 현재 상태 식별자입니다.</param>
    private void HandleStateChanged(NpcStateId stateId)
    {
        // - Idle이면 정체 추적을 끄고 다음 목적지 대기를 시작합니다.
        // - Walk 또는 Run이면 현재 위치와 시간을 저장해 이동 진행을 추적합니다.
        _hasObservedState = true;
        _isIdle = stateId == NpcStateId.Idle;
        _idleSessionVersion++;

        if (_isIdle)
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
    /// 활성화 수명과 필수 의존성이 유효할 때 Idle 목적지 대기를 시작합니다.
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
    /// 현재 Idle 세션이 유지되는 동안 무작위 시간만큼 기다린 뒤 목적지를 예약합니다.
    /// 예약 실패 시 기존 예약을 유지하고 재시도하며, 성공 시 예약 교체 후 이동합니다.
    /// 상태 변경과 오브젝트 수명 종료에 따른 취소는 정상 종료로 처리합니다.
    /// </summary>
    /// <param name="cancellationToken">현재 풀 활성화 수명에 연결된 취소 토큰입니다.</param>
    private async UniTaskVoid WaitForDestinationAsync(CancellationToken cancellationToken)
    {
        // 2. 최소·최대 Idle 시간 사이에서 무작위로 기다린 뒤 상태를 다시 확인합니다.
        // 3. 현재 위치와 예약 중인 Checkpoint를 Provider에 전달합니다.
        // 4. 실패하면 기존 예약을 유지하고 다시 기다렸다가 시도합니다.
        // 5. 성공하면 예약을 교체하고 Walk 또는 Run을 요청한 뒤 반복을 끝냅니다.
        // OperationCanceledException은 오브젝트 수명에 따른 정상 취소로 처리합니다.
        int idleSessionVersion = _idleSessionVersion;

        try
        {
            while (!cancellationToken.IsCancellationRequested &&
                _isIdle &&
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

                if (!_isIdle || idleSessionVersion != _idleSessionVersion)
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
    /// 새 목적지 예약이 성공한 뒤에만 이전 Checkpoint 예약을 해제하고 교체합니다.
    /// 같은 Checkpoint이면 예약 수를 바꾸지 않고 위치만 교체합니다.
    /// </summary>
    /// <param name="nextDestination">새로 예약된 목적지입니다.</param>
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
    /// 설정된 달리기 확률을 한 번 판정해 목적지까지 걷거나 달리도록 요청합니다.
    /// </summary>
    /// <param name="destination">이동할 예약 목적지입니다.</param>
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
    /// 최소 이동 거리를 기준으로 진행 시간을 갱신하고, 제한 시간 동안
    /// 진행하지 못하면 예약을 유지한 채 Idle 복귀를 요청합니다.
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

    private void CreateActivationCancellationSource()
    {
        CancelActivationCancellationSource();
        _activationCancellationSource =
            CancellationTokenSource.CreateLinkedTokenSource(destroyCancellationToken);
    }

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
