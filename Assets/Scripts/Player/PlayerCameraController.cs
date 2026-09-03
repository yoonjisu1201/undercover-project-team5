using Unity.Netcode;
using UnityEngine;

public class PlayerCameraController : NetworkBehaviour
{
    private const string MouseSensitivityKey = "MouseSensitivity";

    [Header("카메라 관련")]
    [SerializeField] private GameObject _headPivot;
    [SerializeField] private Camera _camera;

    [Tooltip("1인칭 전용 레이어만 그리는 오버레이 카메라. 벽에 붙어도 잘리지 않도록 근평면을 짧게 잡는다.")]
    [SerializeField] private Camera _firstPersonHandsCamera;
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
    // 카메라 상한(-50)까지 그대로 따라가게 둔다. -40 에서 잘리면 끝까지 올려다봤을 때
    // 손만 멈춰 있어서 시선과 팔이 어긋난다.
    private readonly float _armFollowMinPitch = -50f;
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

    // 관전 중 시야를 빌려오는 팀원. null이면 내 시점이다.
    private PlayerCameraController _spectateTarget;

    // 오너가 갱신하는 pitch 값. 다른 클라이언트는 이 값을 읽어 헤드 본을 회전시킨다.
    private readonly NetworkVariable<float> _networkPitch =
        new NetworkVariable<float>(0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

    // 오버레이가 켜지고 꺼질 때 알린다. 손에 든 물건을 뷰모델 손과 캐릭터 손 사이에서
    // 옮겨야 하는 쪽(PlayerItemIK)이 듣는다.
    public event System.Action<bool> FirstPersonHandsVisibilityChanged;

    // 이벤트는 스폰 때 한 번만 울려서, 늦게 구독한 쪽은 첫 상태를 놓친다.
    // 구독자가 스폰 시점에 지금 상태를 직접 물어볼 수 있어야 한다.
    public bool IsFirstPersonHandsActive => _firstPersonHandsCamera != null && _firstPersonHandsCamera.enabled;

    public GameObject HeadPivot => _headPivot;
    public Transform LightFollowPivot => _lightFollowPivot;

    // 팔 IK와 레이저가 카메라 상하 조준을 따라가도록 소유자는 로컬 값, 다른 클라이언트는 동기화 값을 제공한다.
    public float ViewPitch => IsOwner ? _pitch : _networkPitch.Value;

    // 이동 컴포넌트가 물리 틱에서 몸체 회전과 이동 방향을 같은 yaw로 계산할 때 사용한다.
    // _yaw 는 오너의 Update 에서만 갱신된다. 다른 클라이언트에서는 스폰 당시 값에 멈춰 있어서
    // 그대로 쓰면 시야 방향이 몸과 따로 논다 - 팔 IK 목표와 손전등 피벗이 월드 회전으로
    // 잡히기 때문에, 상대방 화면에서 왼팔이 엉뚱한 곳을 쫓아가며 뒤틀린다.
    // 몸통은 오너가 MoveRotation(ViewYawRotation) 으로 돌리고 NetworkTransform 이 회전을
    // 동기화하므로, 논오너에게는 몸통 회전이 곧 시야 yaw 다.
    public Quaternion ViewYawRotation => IsOwner
        ? Quaternion.Euler(0f, _yaw, 0f)
        : Quaternion.Euler(0f, transform.eulerAngles.y, 0f);

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

        // 1인칭 전용 오버레이는 내 화면에만 필요하다. 원격 플레이어 쪽에서는 그릴 것이 없다.
        SetFirstPersonOverlayEnabled(active);
    }

    // 다운처럼 카메라가 3인칭으로 물러나면 오버레이도 꺼야 한다.
    // 켜 둔 채로 두면 화면 앞에 1인칭 전용 오브젝트만 떠 있는 꼴이 된다.
    private void SetFirstPersonOverlayEnabled(bool enabledNow)
    {
        if (_firstPersonHandsCamera != null)
        {
            // 카메라 컴포넌트만 끄면 손 오브젝트는 씬에 그대로 남는다. 오버레이 카메라는 레이어만
            // 보고 그리므로, 남의 플레이어 손까지 내 화면에 같이 딸려 나온다.
            // 손이 이 카메라의 자식이라 오브젝트째 꺼야 남의 뷰모델이 사라진다.
            _firstPersonHandsCamera.gameObject.SetActive(enabledNow);
            _firstPersonHandsCamera.enabled = enabledNow;
        }

        FirstPersonHandsVisibilityChanged?.Invoke(enabledNow);
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

        if (IsOwner && _spectateTarget != null)
        {
            _spectateTarget.ResolveSpectatePose(out Vector3 spectatePosition, out Quaternion spectateRotation);
            _camera.transform.SetPositionAndRotation(spectatePosition, spectateRotation);
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
        // 몸이 다시 보이므로 전용 그림자 캐스터는 꺼서 그림자가 겹치지 않게 한다
        _playerRenderer?.SetBodyShadowCastersActive(false);
        SetFirstPersonOverlayEnabled(false);
        BeginCameraTransition();
    }

    public void TransitionToFirstPersonView()
    {
        _useDownedCameraView = false;
        BeginCameraTransition();
    }

    // 다운된 동안 팀원의 시야를 빌린다. 대상의 카메라를 켜는 대신 내 카메라를 대상의 눈 위치로
    // 옮기므로, LocalCameraProvider에 등록된 카메라와 AudioListener가 그대로 유지된다.
    public void BeginSpectate(PlayerCameraController target)
    {
        if (target == null || target == this)
        {
            return;
        }

        // 다른 팀원을 보고 있었다면 그 사람 머리부터 되돌린다.
        ClearSpectateTarget();
        _spectateTarget = target;
        _spectateTarget.SetHeadVisibleToSpectator(false);

        // 다운 전환 보간이 아직 돌고 있으면 그쪽이 카메라를 계속 잡는다. 관전 대상은 맵 반대편에
        // 있을 수도 있어서 그 거리를 보간으로 훑으면 화면이 크게 쓸린다. 끊고 바로 넘긴다.
        _isCameraTransitioning = false;
    }

    public void EndSpectate()
    {
        if (_spectateTarget == null)
        {
            return;
        }

        ClearSpectateTarget();

        // 카메라가 아직 팀원 눈 위치에 있다. 여기서 내 몸으로 되돌려 두지 않으면
        // 뒤따르는 기상 전환이 맵 반대편에서 내 몸까지 훑는 보간이 된다.
        _camera.transform.SetPositionAndRotation(
            _downedCameraAnchor.position,
            _downedCameraAnchor.rotation);
    }

    private void ClearSpectateTarget()
    {
        if (_spectateTarget == null)
        {
            return;
        }

        _spectateTarget.SetHeadVisibleToSpectator(true);
        _spectateTarget = null;
    }

    // 관전자 화면에서만 도는 로컬 처리다. 머리를 끄면 그림자도 같이 사라지므로
    // 1인칭 시점과 같은 방식으로 그림자 전용 사본을 대신 켠다.
    private void SetHeadVisibleToSpectator(bool visible)
    {
        if (_playerRenderer == null)
        {
            return;
        }

        _playerRenderer.SetHeadObjectsActive(visible);
        _playerRenderer.SetHeadShadowCastersActive(!visible);
    }

    // 다른 클라이언트에서 이 플레이어의 1인칭 시점을 재현한다. 오너의 _yaw/_pitch는 로컬 값이라
    // 쓸 수 없지만, 몸 회전(NetworkTransform)과 _networkPitch가 같은 시선을 이미 동기화하고 있다.
    // 몸통은 yaw만 회전하므로 transform.rotation을 그대로 시점 yaw로 쓴다.
    public void ResolveSpectatePose(out Vector3 position, out Quaternion rotation)
    {
        Quaternion yawRotation = transform.rotation;
        position = transform.position + yawRotation * _cameraBaseLocalPosition;
        rotation = yawRotation * _cameraBaseLocalRotation * Quaternion.Euler(ViewPitch, 0f, 0f);
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
            _playerRenderer?.SetBodyShadowCastersActive(true);
            SetFirstPersonOverlayEnabled(true);
        }
    }
}
