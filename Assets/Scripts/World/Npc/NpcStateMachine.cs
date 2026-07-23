using Unity.Netcode;
using UnityEngine;

// NPC 상태를 생성하고 현재 상태의 수명 주기와 상태 전환을 관리합니다.
[RequireComponent(typeof(NpcMovement))]
public sealed class NpcStateMachine : MonoBehaviour
{
    private static readonly int IsMovingHash = Animator.StringToHash("IsMoving");

    [Header("Movement Settings")]
    [SerializeField, Min(0f)] private float _walkSpeed = 2f;

    [Header("긴 시간 이동시 휴식 설정")]
    [SerializeField, Min(0f)] private float _longTravelDistanceThreshold = 20f; // 20M 이상 이동할 때 휴식이 발생합니다.
    [SerializeField, Min(0.1f)] private float _restIntervalDistance = 10f;  // 10M 이동할 때마다 휴식이 발생합니다.
    [SerializeField, Min(0f)] private float _minimumRestSeconds = 1f; // 휴식의 최소 지속 시간
    [SerializeField, Min(0f)] private float _maximumRestSeconds = 3f; // 휴식의 최대 지속 시간

    private NpcMovement _movement;
    private Animator _animator;

    private Vector3 _lastTravelSamplePosition;
    private float _distanceSinceRest;
    private float _restEndTime;
    private bool _shouldRestDuringTravel;
    private bool _isResting;
    private bool _isWalking;


    private void Awake()
    {
        _movement = GetComponent<NpcMovement>();
        _animator = GetComponentInChildren<Animator>(true);
    }

    private void Start()
    {
        RequestIdle();
    }

    private void Update()
    {
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer)
        {
            return;
        }

        if (_movement.IsHeldExternally) return; // 외부에서 붙잡아 둔 동안은 휴식 타이머 등 내부 상태 갱신도 멈춘다

        if (_isResting)
        {
            UpdateRest();
            return;
        }

        if (_isWalking && _movement.HasArrived)
        {
            RequestIdle();
            return;
        }

        UpdateTravelRest();
    }

    // NPC를 Idle 상태로 전환하도록 요청합니다.
    public void RequestIdle()
    {
        ResetTravelRest();
        _isWalking = false;
        _movement.Stop();
        SetMovingAnimation(false);
    }

    // 지정한 위치로 걷도록 요청합니다.
    public void RequestWalk(Vector3 worldPos)
    {
        BeginTravel(worldPos);
        _isWalking = true;
        _movement.SetSpeed(_walkSpeed);
        _movement.MoveTo(worldPos);
        SetMovingAnimation(true);
    }

    private void BeginTravel(Vector3 destination)
    {
        _isResting = false;
        _movement.Resume();
        _lastTravelSamplePosition = transform.position;
        _distanceSinceRest = 0f;
        _shouldRestDuringTravel =
            Vector3.Distance(transform.position, destination) >= _longTravelDistanceThreshold;
    }

    private void UpdateTravelRest()
    {
        if (!_shouldRestDuringTravel ||
            !_isWalking ||
            _movement.HasArrived)
        {
            return;
        }

        Vector3 currentPosition = transform.position;
        _distanceSinceRest += Vector3.Distance(_lastTravelSamplePosition, currentPosition);
        _lastTravelSamplePosition = currentPosition;

        if (_distanceSinceRest < _restIntervalDistance)
        {
            return;
        }

        _distanceSinceRest = 0f;
        _isResting = true;
        _movement.Pause();

        float minimum = Mathf.Min(_minimumRestSeconds, _maximumRestSeconds);
        float maximum = Mathf.Max(_minimumRestSeconds, _maximumRestSeconds);
        _restEndTime = Time.time + UnityEngine.Random.Range(minimum, maximum);

        SetMovingAnimation(false);
    }

    private void UpdateRest()
    {
        if (Time.time < _restEndTime)
        {
            return;
        }

        _isResting = false;
        _lastTravelSamplePosition = transform.position;
        _movement.Resume();

        SetMovingAnimation(_isWalking);
    }

    private void ResetTravelRest()
    {
        _isResting = false;
        _shouldRestDuringTravel = false;
        _distanceSinceRest = 0f;
        _movement.Resume();
    }

    private void SetMovingAnimation(bool isMoving)
    {
        if (_animator != null)
        {
            _animator.SetBool(IsMovingHash, isMoving);
        }
    }

}
