using Unity.Netcode;
using UnityEngine;


/* InputActions를 활용하여 Input을 처리하는 방법 샘플입니다.
 * 현재 /Asset/CustomInputActions 파일 활용하고 있습니다.
 * 추가 액션을 넣고 싶다면 위 경로 파일 내부 내용을 수정하면 됩니다.
 */

public class PlayerMoveSample : NetworkBehaviour
{
	[Header("이동 관련")]
	[SerializeField] private float _moveSpeed = 5f;
	[SerializeField] private float _rotateSpeed = 0.5f;
	[SerializeField] private float _runMultiplier = 1.8f;
	[SerializeField] private float _jumpPower = 10f;

	[Header("중력 관련")]
	// 땅 판정: pivot의 y가 이 범위 안이면 땅에 있다고 본다 (평지 y=0 기준)
	[SerializeField] private float _groundMinY = -0.2f;
	[SerializeField] private float _groundMaxY = 0f;
	[SerializeField] private float _gravityValue = 2.5f;   // 떨어질 때 중력 배수
	[SerializeField] private float _riseMultiplier = 2f;   // 올라갈 때 중력 배수 (클수록 정점에 빨리 도달 = 상승이 빨라짐)
	[SerializeField] private Rigidbody _rigidbody;

	[Header("카메라 관련")]
	[SerializeField] private GameObject _headPivot;
	[SerializeField] private Camera _camera;
	[SerializeField] private Transform _headBone;

	[Header("지면 판정")]
	[SerializeField] private Transform _groundCheck;
	[SerializeField, Min(0.01f)] private float _groundCheckRadius = 0.3f;
	[SerializeField] private LayerMask _jumpableSurfaceMask;

	// 카메라 상하 시야 각도 제한 (위로 볼 때 최소, 아래로 볼 때 최대)
	// 값이 작을수록(0에 가까울수록) 시야 제한이 커진다
	[SerializeField] private float _minPitch = -50f; // 위쪽으로 볼 수 있는 한계
	[SerializeField] private float _maxPitch = 50f;  // 아래쪽으로 볼 수 있는 한계

	private float _yaw = 0f;
	private float _pitch = 0f;
	private Quaternion _headBoneBaseRotation;

	// 오너가 갱신하는 pitch 값. 다른 클라이언트는 이 값을 읽어 헤드 본을 회전시킨다.
	private readonly NetworkVariable<float> _networkPitch =
		new NetworkVariable<float>(0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

	// 점프 입력 예약 (Update에서 감지 → FixedUpdate에서 힘 적용)
	private bool _jumpRequested = false;

	// 만들어 둔 InputActions 파일
	private CustomInputActions _actions;

	private Animator _animator;
	private static readonly int IsMovingHash = Animator.StringToHash("IsMoving");
	private static readonly int IsJumpingHash = Animator.StringToHash("IsJumping");
	private readonly NetworkVariable<bool> _networkIsMoving =
		new NetworkVariable<bool>(
			false,
			NetworkVariableReadPermission.Everyone,
			NetworkVariableWritePermission.Owner);
	private readonly NetworkVariable<bool> _networkIsJumping =
		new NetworkVariable<bool>(
			false,
			NetworkVariableReadPermission.Everyone,
			NetworkVariableWritePermission.Owner);

	private bool _isJumping;
<<<<<<< HEAD

=======
    
>>>>>>> ae6222e (feat(#222) : 긴급 탈출 로직 분리 및 버튼 복구)
	private void Awake()
	{
		_actions = new CustomInputActions();
		_actions.Enable();

		_animator = GetComponent<Animator>();

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

	private void ApplyAnimatorBool(int hash, bool value)
	{
		_animator?.SetBool(hash, value);
	}

	private void SyncAnimatorBool(
		int hash,
		NetworkVariable<bool> networkState,
		bool value)
	{
		ApplyAnimatorBool(hash, value);

		if (IsSpawned &&
			IsOwner &&
			networkState.Value != value)
		{
			networkState.Value = value;
		}
	}

	private void SetMovingState(bool value)
	{
		SyncAnimatorBool(IsMovingHash, _networkIsMoving, value);
	}

	private void SetJumpingState(bool value)
	{
		_isJumping = value;
		SyncAnimatorBool(IsJumpingHash, _networkIsJumping, value);
	}

	private void HandleMovingChanged(bool _, bool value)
	{
		ApplyAnimatorBool(IsMovingHash, value);
	}

	private void HandleJumpingChanged(bool _, bool value)
	{
		_isJumping = value;
		ApplyAnimatorBool(IsJumpingHash, value);
	}

	private void UpdateJumpAnimation()
	{
		if (_isJumping && _rigidbody.linearVelocity.y <= 0f && IsGrounded())
		{
			SetJumpingState(false);
		}
	}

	// 스폰될 때마다(내 캐릭터든 다른 사람 캐릭터든) 호출된다.
	public override void OnNetworkSpawn()
	{
		Debug.Log($"[PlayerMoveNetworkTest] OwnerClientId = {OwnerClientId}, IsOwner = {IsOwner}");

		_networkIsMoving.OnValueChanged += HandleMovingChanged;
		_networkIsJumping.OnValueChanged += HandleJumpingChanged;

		HandleMovingChanged(false, _networkIsMoving.Value);
		HandleJumpingChanged(false, _networkIsJumping.Value);

		if (!IsOwner)
		{
			_camera.enabled = false; // 내 캐릭터가 아니면 카메라 끄기
			_camera.GetComponent<AudioListener>().enabled = false; //오디오 끄기
			return;
		}

	}

	public override void OnNetworkDespawn()
	{
		_networkIsMoving.OnValueChanged -= HandleMovingChanged;
		_networkIsJumping.OnValueChanged -= HandleJumpingChanged;
		base.OnNetworkDespawn();
	}

	private void Update()
	{
		if (!IsOwner)
		{
			return;
		}

		if (GameplayUiMode.IsActive)
		{
			_jumpRequested = false;
			SetMovingState(false);
			return;
		}

		/// 마우스 관련 이동 적용하기
		Vector2 mouseDelta = _actions.Player.Mouse.ReadValue<Vector2>();

		// 현재 yaw, pitch에 값 적용
		_yaw += mouseDelta.x * _rotateSpeed;
		_pitch -= mouseDelta.y * _rotateSpeed;
		// 위로 쭉 민다고 시야 뒤로 넘어가지 않게 min ~ max 사이 값으로 유지
		// 인스펙터에서 _minPitch(위쪽 한계), _maxPitch(아래쪽 한계) 조정 가능
		_pitch = Mathf.Clamp(_pitch, _minPitch, _maxPitch);

		// 회전 적용
		transform.rotation = Quaternion.Euler(0, _yaw, 0f);
		_headPivot.transform.localRotation = Quaternion.Euler(_pitch, 0f, 0f);
		_networkPitch.Value = _pitch;

		/// 버튼 입력 방식 적용하기
		// Player - Interact라는 행동이 이번 프레임에 눌렸는지 확인한다.
		// Keyboard.current.eKey.wasPressedThisFrame와 비슷하게 동작함
		// 점프 입력은 Update에서 감지(입력 놓침 방지)하고, 실제 힘은 FixedUpdate에서 적용
		if (_actions.Player.Jump.WasPressedThisFrame() && IsGrounded())
		{
			_jumpRequested = true;
		}
	}

	private void LateUpdate()
	{
		if (_headBone == null)
		{
			return;
		}

		// 오너는 로컬 _pitch(지연 없음)를, 다른 클라이언트는 동기화된 값을 사용한다.
		float pitch = IsOwner ? _pitch : _networkPitch.Value;

		// 기준 회전에서 현재 시야각을 계산해 매 프레임 회전이 누적되지 않게 한다.
		_headBone.localRotation = _headBoneBaseRotation * Quaternion.Euler(pitch, 0f, 0f);
	}

	private void FixedUpdate()
	{
<<<<<<< HEAD
		if (!IsOwner)
=======
        if (!IsOwner)
>>>>>>> ae6222e (feat(#222) : 긴급 탈출 로직 분리 및 버튼 복구)
		{
			return;
		}

		UpdateJumpAnimation();

		if (GameplayUiMode.IsMovementBlocked)    // UI 조작 중에는 이동을 받지 않음
		{
			_jumpRequested = false;
			SetMovingState(false);
			return;
		}

		HandleMovement();
		HandleJump();
		ApplyAirGravity();
	}

	// 입력 방향(바라보는 방향 기준)으로 Rigidbody를 물리적으로 이동시킨다
	private void HandleMovement()
	{
		Vector2 move = _actions.Player.Move.ReadValue<Vector2>();

		bool isMoving = move.sqrMagnitude > 0.01f;

		SetMovingState(isMoving);

		// forward/right에서 y를 제거해 수평 이동만 남긴다
		Vector3 forward = transform.forward;
		Vector3 right = transform.right;
		forward.y = 0;
		right.y = 0;
		forward.Normalize();
		right.Normalize();

		// Shift를 "누르고 있는 동안" 달리기 속도 적용
		float speed = _actions.Player.Shift.IsPressed() ? _moveSpeed * _runMultiplier : _moveSpeed;

		// MovePosition은 메서드 → 목표 위치를 계산해서 넘긴다 (fixedDeltaTime 사용)
		Vector3 delta = (forward * move.y + right * move.x) * speed * Time.fixedDeltaTime;
		_rigidbody.MovePosition(_rigidbody.position + delta);
	}


	// 점프 예약이 있으면 위 방향 속도를 부여한다 (누른 시간과 무관하게 항상 같은 점프)
	private void HandleJump()
	{
		if (!_jumpRequested) return;

		SetJumpingState(true);

		Vector3 v = _rigidbody.linearVelocity;
		v.y = _jumpPower;   // _jumpPower가 곧 상승 속도(m/s)
		_rigidbody.linearVelocity = v;
		_jumpRequested = false;
	}

	// 공중에서 상승/낙하에 추가 중력을 줘서 스냅감을 만든다
	private void ApplyAirGravity()
	{
		float multiplier = 0f;
		if (_rigidbody.linearVelocity.y < 0)
		{
			multiplier = _gravityValue;    // 낙하 → 빠르게 떨어짐
		}
		else if (_rigidbody.linearVelocity.y > 0)
		{
			multiplier = _riseMultiplier;  // 상승 → 정점에 빨리 도달
		}
		_rigidbody.linearVelocity += Vector3.up * Physics.gravity.y * multiplier * Time.fixedDeltaTime;
	}

	// 긴급 탈출 컴포넌트도 이동 코드와 같은 지면 판정을 재사용한다.
	public bool IsGrounded()
	{
		return _groundCheck != null && Physics.CheckSphere(
			_groundCheck.position,
			_groundCheckRadius,
			_jumpableSurfaceMask,
			QueryTriggerInteraction.Ignore);
	}

	// 서버에서 지정한 스폰 위치로 이동한다.
	public void TeleportToPosition(Vector3 position, Quaternion rotation)
	{
		// 호스트는 서버와 오너가 같은 인스턴스이므로 RPC를 거치지 않고 즉시 적용한다.
		if (IsOwner)
		{
			ApplyTeleport(position, rotation);
			return;
		}

		TeleportToPositionRpc(position, rotation);
	}

	[Rpc(SendTo.Owner)]
	private void TeleportToPositionRpc(Vector3 position, Quaternion rotation)
	{
		ApplyTeleport(position, rotation);
	}

	private void ApplyTeleport(Vector3 position, Quaternion rotation)
	{
		_rigidbody.linearVelocity = Vector3.zero;
		_rigidbody.angularVelocity = Vector3.zero;
		_rigidbody.position = position;
		_rigidbody.rotation = rotation;
	}
}
