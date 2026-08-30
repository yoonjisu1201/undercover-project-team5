using Unity.Netcode;
using UnityEngine;

public class PlayerCameraController : NetworkBehaviour
{
    private const string MouseSensitivityKey = "MouseSensitivity";

    [Header("카메라 관련")]
    [SerializeField] private GameObject _headPivot;
    [SerializeField] private Camera _camera;
    [SerializeField] private Transform _cameraPivot;
    [SerializeField] private Transform _headBone;
    [SerializeField] private Transform _downedCameraAnchor;
    [SerializeField, Min(0.01f)] private float _cameraTransitionDuration = 0.35f;

    // 범위는 1~100 이다. 슬라이더만 있으면 대다수가 쓰는 1~12 가 폭의 11% 로
    // 몰려 조절이 어렵지만, 설정창에서 숫자를 직접 입력할 수 있으므로 문제되지 않는다.
    public const float DegreesPerCountPerSensitivity = 0.0066f;
    public const float MinSensitivity = 1f;
    public const float MaxSensitivity = 100f;
    public const float DefaultSensitivity = 5f;

    public static float ToRotateSpeed(float sensitivity)
        => Mathf.Clamp(sensitivity, MinSensitivity, MaxSensitivity) * DegreesPerCountPerSensitivity;

    [SerializeField] private float _rotateSpeed = DefaultSensitivity * DegreesPerCountPerSensitivity;

    // 카메라 상하 시야 각도 제한 (위로 볼 때 최소, 아래로 볼 때 최대)
    // 값이 작을수록(0에 가까울수록) 시야 제한이 커진다
    [SerializeField] private float _minPitch = -50f; // 위쪽으로 볼 수 있는 한계
    [SerializeField] private float _maxPitch = 50f;  // 아래쪽으로 볼 수 있는 한계

    // #803: 착석 중에는 몸을 돌리지 않고 고개만 좌우로 돌리므로 좌석 정면 기준 시야 범위를 제한한다.
    [Header("앉은 상태 카메라")]
    [SerializeField, Range(0f, 180f)] private float _seatedYawLimit = 70f;

    // 손전등 등 손 IK가 따라가는 각도. 헤드 피벗(카메라)보다 좁게 잡아서 팔이 가동 범위를 넘어 꺾이지 않게 한다.
    [Header("팔 IK 따라가기 (헤드 피벗과 별도로 클램프)")]
    [SerializeField] private Transform _armFollowPivot;
    private readonly float _armFollowMinPitch = -40f;
    private readonly float _armFollowMaxPitch = 20f;

    [Header("로컬 카메라 연출")]
    [Tooltip("호흡 오프셋은 여기서 계산하고, 실제 카메라 반영은 이 컨트롤러가 담당한다.")]
    [SerializeField] private ExhaustedBreathCameraEffect _exhaustedBreathEffect;

    private CustomInputActions _actions;
    private PlayerRenderer _playerRenderer;
    private float _yaw;
    // #803: 좌석 정면을 기준으로 누적한 머리의 좌우 회전값이며 플레이어 몸 회전에는 적용하지 않는다.
    private float _seatedYaw;
    private float _pitch;
    private Vector3 _cameraBaseLocalPosition;
    private Quaternion _headBoneBaseRotation;
    private Vector3 _cameraTransitionStartPosition;
    private Quaternion _cameraTransitionStartRotation;
    private float _cameraTransitionElapsedTime;
    private bool _useDownedCameraView;
    // #803: 착석 중 일반 시점 회전 대신 좌석 전용 제한 회전을 사용하기 위한 로컬 카메라 상태다.
    private bool _useSeatedCameraView;
    private bool _isCameraTransitioning;

    // 오너가 갱신하는 pitch 값. 다른 클라이언트는 이 값을 읽어 헤드 본을 회전시킨다.
    private readonly NetworkVariable<float> _networkPitch =
        new NetworkVariable<float>(0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

    public GameObject HeadPivot => _headPivot;

    // 팔 IK와 레이저가 카메라 상하 조준을 따라가도록 소유자는 로컬 값, 다른 클라이언트는 동기화 값을 제공한다.
    public float ViewPitch => IsOwner ? _pitch : _networkPitch.Value;

    public bool IsCameraTransitioning => _isCameraTransitioning;

    private void Awake()
    {
        _rotateSpeed = ToRotateSpeed(PlayerPrefs.GetFloat(MouseSensitivityKey, DefaultSensitivity));
        _actions = new CustomInputActions();
        _actions.Enable();
        _exhaustedBreathEffect ??= GetComponent<ExhaustedBreathCameraEffect>();
        _playerRenderer = GetComponentInParent<PlayerRenderer>();

        if (_headBone != null)
        {
            _headBoneBaseRotation = _headBone.localRotation;
        }
    }

    public override void OnDestroy()
    {
        _actions.Disable();
        base.OnDestroy();
    }

    public override void OnNetworkSpawn()
    {
        _camera ??= GetComponentInChildren<Camera>(true);
        _cameraBaseLocalPosition = _camera.transform.localPosition;

        if (!IsOwner)
        {
            SetCameraActive(false);
            return;
        }

        SetCameraActive(true);
        LocalCameraProvider.Register(_camera);
    }

    public override void OnNetworkDespawn()
    {
        if (IsOwner)
        {
            ResetExhaustedBreath();
            LocalCameraProvider.Unregister(_camera);
        }
        base.OnNetworkDespawn();
    }

    private void SetCameraActive(bool active)
    {
        if (_camera == null)
        {
            Debug.LogError($"[PlayerCameraController] Player prefab에 Camera 참조가 없습니다. OwnerClientId={OwnerClientId}, IsOwner={IsOwner}", this);
            return;
        }

        if (_camera.gameObject.activeSelf != active)
        {
            _camera.gameObject.SetActive(active);
        }
        _camera.enabled = active;
        if (_camera.TryGetComponent(out AudioListener listener))
        {
            listener.enabled = active;
        }
    }

    private void Update()
    {
        if (!IsOwner)
        {
            return;
        }

        if (GameplayUiMode.IsActive)
        {
            ResetExhaustedBreath();
            return;
        }

        if (_useDownedCameraView || _isCameraTransitioning)
        {
            return;
        }

        // #803: UI·다운·카메라 전환을 먼저 처리한 뒤, 착석 중에는 몸 방향을 고정하고 머리 시점만 갱신한다.
        if (_useSeatedCameraView)
        {
            UpdateSeatedView();
            return;
        }

        Vector2 mouseDelta = _actions.Player.Mouse.ReadValue<Vector2>();

        _yaw += mouseDelta.x * _rotateSpeed;
        _pitch -= mouseDelta.y * _rotateSpeed;
        _pitch = Mathf.Clamp(_pitch, _minPitch, _maxPitch);

        transform.rotation = Quaternion.Euler(0f, _yaw, 0f);

        // 호흡 컴포넌트는 계산만 담당한다. 최종 Transform 적용을 이곳에 모아
        // 마우스 시점 회전 및 다른 카메라 전환과 값이 서로 덮어쓰이지 않게 한다.
        float breathBob = 0f;
        float breathPitch = 0f;
        _exhaustedBreathEffect?.Evaluate(Time.deltaTime, out breathBob, out breathPitch);

        _headPivot.transform.localRotation = Quaternion.Euler(_pitch + breathPitch, 0f, 0f);
        _camera.transform.localPosition = _cameraBaseLocalPosition + Vector3.up * breathBob;

        // 흔들림은 각자 화면의 연출이라 pitch 동기화 값에는 섞지 않는다.
        // 섞으면 남의 캐릭터 머리가 같이 떨린다.
        _networkPitch.Value = _pitch;
    }

    // #803: 좌석 정면 기준 yaw만 제한하고 기존 pitch·호흡·원격 머리 pitch 동기화는 그대로 유지한다.
    private void UpdateSeatedView()
    {
        Vector2 mouseDelta = _actions.Player.Mouse.ReadValue<Vector2>();

        _seatedYaw = Mathf.Clamp(
            _seatedYaw + mouseDelta.x * _rotateSpeed,
            -_seatedYawLimit,
            _seatedYawLimit);

        _pitch -= mouseDelta.y * _rotateSpeed;
        _pitch = Mathf.Clamp(_pitch, _minPitch, _maxPitch);

        float breathBob = 0f;
        float breathPitch = 0f;
        _exhaustedBreathEffect?.Evaluate(Time.deltaTime, out breathBob, out breathPitch);

        // #803: 좌우 회전을 몸 Transform이 아닌 헤드 피벗에 적용해 착석 위치와 몸 방향이 변하지 않게 한다.
        _headPivot.transform.localRotation = Quaternion.Euler(0f, _seatedYaw, 0f) * Quaternion.Euler(_pitch + breathPitch, 0f, 0f);
        _camera.transform.localPosition = _cameraBaseLocalPosition + Vector3.up * breathBob;

        _networkPitch.Value = _pitch;
    }

    private void LateUpdate()
    {
        if (IsOwner && _isCameraTransitioning)
        {
            UpdateCameraTransition();
            return;
        }

        if (IsOwner && _useDownedCameraView)
        {
            _camera.transform.SetPositionAndRotation(
                _downedCameraAnchor.position,
                _downedCameraAnchor.rotation);
            return;
        }

        if (_headBone == null)
        {
            return;
        }

        // 오너는 로컬 _pitch(지연 없음)를, 다른 클라이언트는 동기화된 값을 사용한다.
        float pitch = IsOwner ? _pitch : _networkPitch.Value;

        // 기준 회전에서 현재 시야각을 계산해 매 프레임 회전이 누적되지 않게 한다.
        _headBone.localRotation = _headBoneBaseRotation * Quaternion.Euler(pitch, 0f, 0f);

        // 손 IK 타겟은 헤드 본보다 좁은 범위 안에서만 따라가게 별도 피벗에 클램프된 값을 적용한다.
        // 다른 클라이언트에서도 보여야 하므로 헤드 본과 동일하게 이 시점에 갱신한다.
        if (_armFollowPivot != null)
        {
            float armPitch = Mathf.Clamp(pitch, _armFollowMinPitch, _armFollowMaxPitch);
            _armFollowPivot.localRotation = Quaternion.Euler(armPitch, 0f, 0f);
        }
    }

    private void ResetExhaustedBreath()
    {
        _exhaustedBreathEffect?.ResetEffect();

        // 내부 계산값만 지우면 마지막 프레임의 Transform 오프셋은 그대로 남는다.
        // 컨트롤러가 보관한 기준값으로 함께 복구해야 다음 카메라 상태가 어긋나지 않는다.
        if (_camera != null)
        {
            _camera.transform.localPosition = _cameraBaseLocalPosition;
        }

        if (_headPivot != null)
        {
            _headPivot.transform.localRotation = Quaternion.Euler(_pitch, 0f, 0f);
        }
    }

    public void SetMouseSensitivity(float sensitivity)
    {
        _rotateSpeed = ToRotateSpeed(sensitivity);
    }

    public void SetYaw(float yaw)
    {
        _yaw = yaw;

        // #803: 착석 중 외부 위치 보정이 몸 방향을 바꾸면 이전 좌석 기준 yaw가 남지 않도록 초기화한다.
        if (_useSeatedCameraView)
        {
            _seatedYaw = 0f;
            ApplyHeadPivotRotation();
        }
    }

    // #803: 좌석 회전을 새로운 시점 기준으로 사용하고, 이후 좌우 입력은 몸이 아닌 _seatedYaw에 누적한다.
    public void EnterSeatedView(float bodyYaw)
    {
        // #803: 좌석 포즈 RPC와 SeatingPhase 콜백이 모두 진입을 요청할 수 있어 중복 초기화를 막는다.
        if (_useSeatedCameraView)
        {
            return;
        }

        _yaw = bodyYaw;
        _seatedYaw = 0f;
        _useSeatedCameraView = true;
        ApplyHeadPivotRotation();
    }

    // #803: 기상 후 착석 중 바라보던 방향을 일반 몸 회전에 합쳐 시점이 좌석 정면으로 튀지 않게 한다.
    public void ExitSeatedView()
    {
        // #803: 스폰 시 Standing 초기 상태 적용에서도 호출되므로 실제 착석 중일 때만 시점을 복원한다.
        if (!_useSeatedCameraView)
        {
            return;
        }

        _yaw = transform.eulerAngles.y + _seatedYaw;
        _seatedYaw = 0f;
        _useSeatedCameraView = false;
        transform.rotation = Quaternion.Euler(0f, _yaw, 0f);
        ApplyHeadPivotRotation();
    }

    // #803: 착석 진입·기상·외부 yaw 보정이 동일한 좌우·상하 회전 조합을 사용하도록 한 곳에서 적용한다.
    private void ApplyHeadPivotRotation()
    {
        _headPivot.transform.localRotation = Quaternion.Euler(0f, _seatedYaw, 0f) * Quaternion.Euler(_pitch, 0f, 0f);
    }

    public void TransitionToDownedView()
    {
        ResetExhaustedBreath();
        _useDownedCameraView = true;
        Layers.ShowLayerToCamera(_camera, Layers.LocalPlayerHead);
        // 머리가 다시 보이므로 전용 그림자 캐스터는 꺼서 그림자가 겹치지 않게 한다
        _playerRenderer?.SetHeadShadowCastersActive(false);
        BeginCameraTransition();
    }

    public void TransitionToFirstPersonView()
    {
        _useDownedCameraView = false;
        BeginCameraTransition();
    }

    private void BeginCameraTransition()
    {
        _cameraTransitionStartPosition = _camera.transform.position;
        _cameraTransitionStartRotation = _camera.transform.rotation;
        _cameraTransitionElapsedTime = 0f;
        _isCameraTransitioning = true;
    }

    private void UpdateCameraTransition()
    {
        Vector3 targetPosition = _useDownedCameraView
            ? _downedCameraAnchor.position
            : _cameraPivot.position;

        Quaternion targetRotation = _useDownedCameraView
            ? _downedCameraAnchor.rotation
            : _cameraPivot.rotation * Quaternion.Euler(_pitch, 0f, 0f);

        _cameraTransitionElapsedTime += Time.deltaTime;
        float transitionProgress = Mathf.Clamp01(_cameraTransitionElapsedTime / _cameraTransitionDuration);
        float smoothedProgress = Mathf.SmoothStep(0f, 1f, transitionProgress);

        _camera.transform.SetPositionAndRotation(
            Vector3.Lerp(_cameraTransitionStartPosition, targetPosition, smoothedProgress),
            Quaternion.Slerp(_cameraTransitionStartRotation, targetRotation, smoothedProgress));

        _isCameraTransitioning = transitionProgress < 1f;

        if (!_isCameraTransitioning && !_useDownedCameraView)
        {
            Layers.HideLayerFromCamera(_camera, Layers.LocalPlayerHead);
            _playerRenderer?.SetHeadShadowCastersActive(true);
        }
    }
}
