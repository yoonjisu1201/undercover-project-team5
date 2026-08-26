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

    // 손전등 등 손 IK가 따라가는 각도. 헤드 피벗(카메라)보다 좁게 잡아서 팔이 가동 범위를 넘어 꺾이지 않게 한다.
    [Header("팔 IK 따라가기 (헤드 피벗과 별도로 클램프)")]
    [SerializeField] private Transform _armFollowPivot;
    private readonly float _armFollowMinPitch = -40f;
    private readonly float _armFollowMaxPitch = 20f;

    // 스태미나가 바닥나 심장 소리가 날 때 카메라를 조금 흔들어 헐떡이는 느낌을 준다.
    // 소리만 나고 화면은 멀쩡하면 어색해서 넣는 연출이다.
    [Header("탈진 호흡 흔들림")]
    [Tooltip("헐떡임 1회 주기. 심장 박동(90 BPM)보다 느려야 호흡으로 읽힌다.")]
    [SerializeField, Min(0.01f)] private float _breathsPerSecond = 1.1f;

    [Tooltip("위아래로 흔들리는 거리(m). 크게 주면 멀미가 난다.")]
    [SerializeField, Min(0f)] private float _breathBobDistance = 0.02f;

    [Tooltip("고개를 끄덕이는 각도.")]
    [SerializeField, Min(0f)] private float _breathPitchDegrees = 0.5f;

    [Tooltip("좌우로 기우는 각도. 끄덕임의 절반 주기로 흔들려 기계적으로 보이지 않게 한다.")]
    [SerializeField, Min(0f)] private float _breathRollDegrees = 0.35f;

    [Tooltip("흔들림이 올라오는 시간(초).")]
    [SerializeField, Min(0.01f)] private float _breathFadeIn = 0.4f;

    [Tooltip("잦아드는 시간(초). 올라올 때보다 길어야 회복이 천천히 느껴진다.")]
    [SerializeField, Min(0.01f)] private float _breathFadeOut = 1.2f;

    private CustomInputActions _actions;
    private float _yaw;
    private float _pitch;
    private PlayerHeartbeat _heartbeat;
    private Vector3 _cameraBaseLocalPosition;
    private float _breathPhase;   // 0~1 로 감아서 쓴다. 계속 더하면 정밀도가 떨어진다.
    private float _breathWeight;  // 0~1. 흔들림 세기.
    private float _breathPitch;
    private float _breathRoll;
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

    // 팔 IK와 레이저가 카메라 상하 조준을 따라가도록 소유자는 로컬 값, 다른 클라이언트는 동기화 값을 제공한다.
    public float ViewPitch => IsOwner ? _pitch : _networkPitch.Value;

    public bool IsCameraTransitioning => _isCameraTransitioning;

    private void Awake()
    {
        _rotateSpeed = ToRotateSpeed(PlayerPrefs.GetFloat(MouseSensitivityKey, DefaultSensitivity));
        _actions = new CustomInputActions();
        _actions.Enable();
        _heartbeat = GetComponent<PlayerHeartbeat>();

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
        if (!IsOwner ||
            GameplayUiMode.IsActive ||
            _useDownedCameraView ||
            _isCameraTransitioning)
        {
            return;
        }

        Vector2 mouseDelta = _actions.Player.Mouse.ReadValue<Vector2>();

        _yaw += mouseDelta.x * _rotateSpeed;
        _pitch -= mouseDelta.y * _rotateSpeed;
        _pitch = Mathf.Clamp(_pitch, _minPitch, _maxPitch);

        transform.rotation = Quaternion.Euler(0f, _yaw, 0f);

        UpdateExhaustedBreath();
        _headPivot.transform.localRotation = Quaternion.Euler(_pitch + _breathPitch, 0f, _breathRoll);

        // 흔들림은 각자 화면의 연출이라 pitch 동기화 값에는 섞지 않는다.
        // 섞으면 남의 캐릭터 머리가 같이 떨린다.
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

    // 탈진 심장 박동이 나는 동안만 호흡 흔들림을 채운다. 세기는 서서히 오르고 내려서
    // 소리가 켜지는 순간에 화면이 튀지 않게 한다.
    private void UpdateExhaustedBreath()
    {
        bool isExhausted = _heartbeat != null
            && _heartbeat.CurrentKey == SoundKey.Player_HeartBeat_Exhausted;

        float target = isExhausted ? 1f : 0f;
        float fadeDuration = target > _breathWeight ? _breathFadeIn : _breathFadeOut;
        _breathWeight = Mathf.MoveTowards(_breathWeight, target, Time.deltaTime / fadeDuration);

        // 완전히 잦아들었으면 흔들림이 이미 0으로 적용된 상태라 매 프레임 계산할 필요가 없다.
        if (_breathWeight <= 0f)
        {
            _breathPitch = 0f;
            _breathRoll = 0f;
            return;
        }

        _breathPhase = Mathf.Repeat(_breathPhase + Time.deltaTime * _breathsPerSecond, 1f);

        float wave = Mathf.Sin(_breathPhase * Mathf.PI * 2f);
        float swayWave = Mathf.Sin(_breathPhase * Mathf.PI); // 절반 주기

        _breathPitch = wave * _breathPitchDegrees * _breathWeight;
        _breathRoll = swayWave * _breathRollDegrees * _breathWeight;

        _camera.transform.localPosition =
            _cameraBaseLocalPosition + Vector3.up * (wave * _breathBobDistance * _breathWeight);
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
        _useDownedCameraView = true;
        Layers.ShowLayerToCamera(_camera, Layers.LocalPlayerHead);
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
        }
    }
}
