using Unity.Netcode;
using UnityEngine;

public class PlayerCameraController : NetworkBehaviour
{
    private const string MouseSensitivityKey = "MouseSensitivity";

    [Header("카메라 관련")]
    [SerializeField] private GameObject _headPivot;
    [SerializeField] private Camera _camera;
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

    // 손전등 등 손 IK가 따라가는 각도. 헤드 피벗(카메라)보다 좁게 잡아서 팔이 가동 범위를 넘어 꺾이지 않게 한다.
    [Header("팔 IK 따라가기 (헤드 피벗과 별도로 클램프)")]
    [SerializeField] private Transform _armFollowPivot;
    private readonly float _armFollowMinPitch = -40f;
    private readonly float _armFollowMaxPitch = 20f;

    // 헤드램프(Flashlight) 등 카메라와 동일한 시야각을 그대로 따라가야 하는 오브젝트가 붙는 피벗.
    // 헤드 피벗과 달리 오너/논오너 모두 이 시점에 갱신되므로, raycast 없이 파렌팅만으로 시선을 따라간다.
    [Header("카메라 시야각 그대로 따라가기 (오너/논오너 공통)")]
    [SerializeField] private Transform _lightFollowPivot;

    [Header("로컬 카메라 연출")]
    [Tooltip("호흡 오프셋은 여기서 계산하고, 실제 카메라 반영은 이 컨트롤러가 담당한다.")]
    [SerializeField] private ExhaustedBreathCameraEffect _exhaustedBreathEffect;

    private CustomInputActions _actions;
    private PlayerRenderer _playerRenderer;
    private float _yaw;
    private float _pitch;
    private Vector3 _cameraBaseLocalPosition;
    private Quaternion _cameraBaseLocalRotation;
    private Quaternion _headBoneBaseRotation;
    private Vector3 _cameraTransitionStartPosition;
    private Quaternion _cameraTransitionStartRotation;
    private float _cameraTransitionElapsedTime;
    private bool _useDownedCameraView;
    private bool _isCameraTransitioning;

    // 오너가 갱신하는 pitch 값. 다른 클라이언트는 이 값을 읽어 헤드 본을 회전시킨다.
    private readonly NetworkVariable<float> _networkPitch =
        new NetworkVariable<float>(0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

    public GameObject HeadPivot => _headPivot;
    public Transform LightFollowPivot => _lightFollowPivot;

    // 팔 IK와 레이저가 카메라 상하 조준을 따라가도록 소유자는 로컬 값, 다른 클라이언트는 동기화 값을 제공한다.
    public float ViewPitch => IsOwner ? _pitch : _networkPitch.Value;

    // 이동 컴포넌트가 물리 틱에서 몸체 회전과 이동 방향을 같은 yaw로 계산할 때 사용한다.
    public Quaternion ViewYawRotation => Quaternion.Euler(0f, _yaw, 0f);

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
        // Awake 가 돌기 전에 파괴되면 _actions 가 아직 없다.
        _actions?.Disable();
        base.OnDestroy();
    }

    public override void OnNetworkSpawn()
    {
        _camera ??= GetComponentInChildren<Camera>(true);

        // 카메라가 없으면 이 컴포넌트가 할 수 있는 일이 없다. 이후 코드가 널 검사 없이
        // 바로 쓰는 근거라, 여기서 멈추고 한 번만 알린다.
        if (_camera == null)
        {
            Debug.LogError($"[PlayerCameraController] Player 프리팹에 Camera 참조가 없습니다. OwnerClientId={OwnerClientId}", this);
            enabled = false;
            return;
        }

        if (_downedCameraAnchor == null)
        {
            Debug.LogError("[PlayerCameraController] _downedCameraAnchor 참조가 없습니다. 다운 시점 전환이 동작하지 않습니다.", this);
        }

        _cameraBaseLocalPosition = _camera.transform.localPosition;
        _cameraBaseLocalRotation = _camera.transform.localRotation;

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
        // 카메라 참조가 없어 스폰에서 멈춘 경우에도 디스폰은 불린다.
        if (IsOwner && _camera != null)
        {
            ResetExhaustedBreath();
            LocalCameraProvider.Unregister(_camera);
        }
        base.OnNetworkDespawn();
    }

    private void SetCameraActive(bool active)
    {
        // HeadAnchorPosition과 시점 추종 오브젝트가 카메라의 자식이므로
        // 원격 플레이어에서도 계층 전체는 활성 상태를 유지한다.
        if (!_camera.gameObject.activeSelf)
        {
            _camera.gameObject.SetActive(true);
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

        Vector2 mouseDelta = _actions.Player.Mouse.ReadValue<Vector2>();

        _yaw += mouseDelta.x * _rotateSpeed;
        _pitch -= mouseDelta.y * _rotateSpeed;
        _pitch = Mathf.Clamp(_pitch, _minPitch, _maxPitch);

        // 흔들림은 각자 화면의 연출이라 pitch 동기화 값에는 섞지 않는다.
        // 섞으면 남의 캐릭터 머리가 같이 떨린다.
        _networkPitch.Value = _pitch;
    }

    // 카메라 Transform 을 만지는 곳은 여기 하나다. Update 에서도 손대면 같은 프레임에
    // 두 번 쓰게 되고, 나중에 쓴 쪽이 이기는 순서 문제가 조용히 생긴다.
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

        if (IsOwner)
        {
            // 호흡 컴포넌트는 오프셋 계산만 하고, Transform 반영은 이 컨트롤러가 맡는다.
            float breathBob = 0f;
            float breathPitch = 0f;
            _exhaustedBreathEffect?.Evaluate(Time.deltaTime, out breathBob, out breathPitch);
            ApplyFirstPersonCameraPose(breathBob, breathPitch);
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
            _armFollowPivot.rotation = ViewYawRotation * Quaternion.Euler(armPitch, 0f, 0f);
        }

        // 카메라와 동일한 시야각을 그대로 따라가야 하는 피벗(헤드램프 등). 클램프 없이 pitch 전체를 적용한다.
        if (_lightFollowPivot != null)
        {
            _lightFollowPivot.rotation = ViewYawRotation * Quaternion.Euler(pitch, 0f, 0f);
        }
    }

    // 카메라는 본/헤드 피벗을 따라가지 않고 플레이어 루트 아래에서 직접 시점을 만든다.
    // Rigidbody 보간으로 늦게 반영되는 루트 yaw도 렌더 프레임의 최신 입력 기준으로 보정한다.
    //
    // 지금 시점의 목표 포즈만 계산한다. 전환 중에는 이 값이 보간의 도착점으로도 쓰인다.
    private void ResolveFirstPersonPose(float breathBob, float breathPitch, out Vector3 position, out Quaternion rotation)
    {
        Quaternion viewYawRotation = ViewYawRotation;
        position = transform.position
            + viewYawRotation * _cameraBaseLocalPosition
            + Vector3.up * breathBob;
        rotation = viewYawRotation
            * _cameraBaseLocalRotation
            * Quaternion.Euler(_pitch + breathPitch, 0f, 0f);
    }

    private void ApplyFirstPersonCameraPose(float breathBob, float breathPitch)
    {
        ResolveFirstPersonPose(breathBob, breathPitch, out Vector3 position, out Quaternion rotation);
        _camera.transform.SetPositionAndRotation(position, rotation);
    }

    private void ResetExhaustedBreath()
    {
        _exhaustedBreathEffect?.ResetEffect();

        // 내부 계산값만 지우면 마지막 프레임의 Transform 오프셋은 그대로 남는다.
        // 컨트롤러가 보관한 기준값으로 함께 복구해야 다음 카메라 상태가 어긋나지 않는다.
        ApplyFirstPersonCameraPose(0f, 0f);
    }

    public void SetMouseSensitivity(float sensitivity)
    {
        _rotateSpeed = ToRotateSpeed(sensitivity);
    }

    public void SetYaw(float yaw)
    {
        _yaw = yaw;
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
        ResolveFirstPersonPose(0f, 0f, out Vector3 firstPersonPosition, out Quaternion firstPersonRotation);

        Vector3 targetPosition = _useDownedCameraView ? _downedCameraAnchor.position : firstPersonPosition;
        Quaternion targetRotation = _useDownedCameraView ? _downedCameraAnchor.rotation : firstPersonRotation;

        _cameraTransitionElapsedTime += Time.deltaTime;
        float transitionProgress = Mathf.Clamp01(_cameraTransitionElapsedTime / _cameraTransitionDuration);

        // 위치와 회전이 같은 곡선을 타야 한다. 한쪽만 선형이면 전환 중간에 시선이 목표보다
        // 앞서거나 뒤처져서 화면이 한 번 흔들린 것처럼 보인다.
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
