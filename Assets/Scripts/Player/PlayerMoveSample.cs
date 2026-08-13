using System;
using Unity.Netcode;
using UnityEngine;


/* InputActions를 활용하여 Input을 처리하는 방법 샘플입니다.
 * 현재 /Asset/CustomInputActions 파일 활용하고 있습니다.
 * 추가 액션을 넣고 싶다면 위 경로 파일 내부 내용을 수정하면 됩니다.
 */

public class PlayerMoveSample : NetworkBehaviour
{
	[Header("이동 관련")]
	[SerializeField] private float _moveSpeedWithCart = 3f;
	[SerializeField] private float _moveSpeed = 5f;
	[SerializeField] private float _runSpeedMultiplier = 1.5f;
	[SerializeField] private float _jumpPower = 10f;

	[Header("중력 관련")]
	[SerializeField] private float _gravityValue = 2.5f;   // 떨어질 때 중력 배수
	[SerializeField] private float _riseMultiplier = 2f;   // 올라갈 때 중력 배수 (클수록 정점에 빨리 도달 = 상승이 빨라짐)
	[SerializeField] private Rigidbody _rigidbody;

	[Header("경사 미끄러짐 방지")]
	[SerializeField] private PhysicsMaterial _gripMaterial; // 멈춰 있을 때 경사에 고정 (높은 마찰)
	private CapsuleCollider _bodyCollider;
	private PhysicsMaterial _slideMaterial;                 // 이동 중 사용 (초기 마찰0 머티리얼)

	[Header("카메라 관련")]
	[SerializeField] private PlayerCameraController _playerCameraController;

	[Header("지면 판정")]
	[SerializeField] private Transform _groundCheck;
	[SerializeField, Min(0.01f)] private float _groundCheckRadius = 0.3f;
	[SerializeField] private LayerMask _jumpableSurfaceMask;

	// 점프 입력 예약 (Update에서 감지 → FixedUpdate에서 힘 적용)
	private bool _jumpRequested = false;

	// 만들어 둔 InputActions 파일
	private CustomInputActions _actions;

	private Animator _animator;
	private PlayerHealth _playerHealth;
	private PlayerInteraction _playerInteraction;
	private static readonly int IsMovingHash = Animator.StringToHash("IsMoving");
	private static readonly int IsRunningHash = Animator.StringToHash("IsRunning");
	private static readonly int IsJumpingHash = Animator.StringToHash("IsJumping");
	// #392: PlayerHealth의 동기화된 다운 상태를 Animator의 Downed/Getting Up 전이에 연결한다.
	private static readonly int IsDownedHash = Animator.StringToHash("IsDowned");
	private readonly NetworkVariable<bool> _networkIsMoving = new NetworkVariable<bool>(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
	private readonly NetworkVariable<bool> _networkIsRunning = new NetworkVariable<bool>(
			false,
			NetworkVariableReadPermission.Everyone,
			NetworkVariableWritePermission.Owner);
	private readonly NetworkVariable<bool> _networkIsJumping =
		new NetworkVariable<bool>(
			false,
			NetworkVariableReadPermission.Everyone,
			NetworkVariableWritePermission.Owner);

	private bool _isJumping;
	// #392: 실제 소생 후 Getting Up에서 Idle로 돌아갈 때까지 이동을 차단한다.
	private bool _isGettingUp;

	// Getting Up 애니메이션 + 블렌딩이 완전히 끝나는 시점(FixedUpdate에서 감지)에 발동한다.
	public event Action GettingUpFinished;

	private void Awake()
	{
		_actions = new CustomInputActions();
		_actions.Enable();

		_animator = GetComponent<Animator>();
		_playerHealth = GetComponent<PlayerHealth>();
		_playerInteraction = GetComponent<PlayerInteraction>();

		_bodyCollider = GetComponent<CapsuleCollider>();
		if (_bodyCollider != null)
		{
			_slideMaterial = _bodyCollider.sharedMaterial; // 인스펙터에 붙어 있는 마찰0 머티리얼
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

	private void SyncAnimatorBool(int hash, NetworkVariable<bool> networkState, bool value)
	{
		ApplyAnimatorBool(hash, value);

		if (IsSpawned && IsOwner && networkState.Value != value)
		{
			networkState.Value = value;
		}
	}

	private void SetMovingState(bool value)
	{
		SyncAnimatorBool(IsMovingHash, _networkIsMoving, value);
	}

	private void SetRunningState(bool value)
	{
		SyncAnimatorBool(IsRunningHash, _networkIsRunning, value);
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

	private void HandleRunningChanged(bool _, bool value)
	{
		ApplyAnimatorBool(IsRunningHash, value);
	}

	private void HandleJumpingChanged(bool _, bool value)
	{
		_isJumping = value;
		ApplyAnimatorBool(IsJumpingHash, value);
	}

	// #392: PlayerHealth.DownedStateChanged -> Animator IsDowned -> Downed/Getting Up 전이 흐름의 연결 지점이다.
	private void HandleDownedStateChanged(bool previousValue, bool value)
	{
		ApplyAnimatorBool(IsDownedHash, value);

		if (value)
		{
			_jumpRequested = false;

			SetMovingState(false);
			SetRunningState(false);
			SetJumpingState(false);

			if (IsOwner)
			{
				_playerCameraController.TransitionToDownedView();
			}
		}		

		_isGettingUp = previousValue && !value;
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
		_networkIsRunning.OnValueChanged += HandleRunningChanged;
		_networkIsJumping.OnValueChanged += HandleJumpingChanged;
		// #392: PlayerHealth의 NetworkVariable 변경 알림을 모든 클라이언트의 Animator에 반영한다.
		_playerHealth.DownedStateChanged += HandleDownedStateChanged;

		HandleMovingChanged(false, _networkIsMoving.Value);
		HandleRunningChanged(false, _networkIsRunning.Value);
		HandleJumpingChanged(false, _networkIsJumping.Value);
		// #392: 기존 상태 초기화와 형식을 맞추되, false를 이전 값으로 넘겨 최초 스폰을 소생으로 판정하지 않는다.
		HandleDownedStateChanged(false, _playerHealth.IsDowned);
	}

	public override void OnNetworkDespawn()
	{
		_networkIsMoving.OnValueChanged -= HandleMovingChanged;
		_networkIsRunning.OnValueChanged -= HandleRunningChanged;
		_networkIsJumping.OnValueChanged -= HandleJumpingChanged;
		// #392: OnNetworkSpawn에서 등록한 다운 상태 구독을 네트워크 수명 종료 시 해제한다.
		_playerHealth.DownedStateChanged -= HandleDownedStateChanged;
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
			SetRunningState(false);
			return;
		}

		if (_playerHealth.IsDowned ||
			_isGettingUp ||
			_playerCameraController.IsCameraTransitioning)
		{
			return;
		}

		/// 버튼 입력 방식 적용하기
		// Player - Interact라는 행동이 이번 프레임에 눌렸는지 확인한다.
		// Keyboard.current.eKey.wasPressedThisFrame와 비슷하게 동작함
		// 점프 입력은 Update에서 감지(입력 놓침 방지)하고, 실제 힘은 FixedUpdate에서 적용
		if (_actions.Player.Jump.WasPressedThisFrame() && IsGrounded())
		{
			_jumpRequested = true;
		}
	}

	private void FixedUpdate()
	{
		if (!IsOwner)
		{
			return;
		}

		UpdateJumpAnimation();

		// #392: Getting Up -> Idle 전환과 블렌딩이 모두 끝난 뒤에만 이동 잠금을 해제한다.
		if (_isGettingUp &&
			!_animator.IsInTransition(0) &&
			_animator.GetCurrentAnimatorStateInfo(0).IsName("Base Layer.Idle"))
		{
			_isGettingUp = false;
			GettingUpFinished?.Invoke();
			_playerCameraController.TransitionToFirstPersonView();
		}

		// #392: 다운 중에는 PlayerHealth, 소생 후 기상 중에는 _isGettingUp으로 이동을 차단한다.
		if (GameplayUiMode.IsMovementBlocked ||
			_playerHealth.IsDowned ||
			_isGettingUp ||
			_playerCameraController.IsCameraTransitioning)
		{
			_jumpRequested = false;
			SetMovingState(false);
			SetRunningState(false);
			UpdateFrictionMaterial(false);
			ApplyAirGravity();
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
		bool isRunning = 
			isMoving 
			&& _actions.Player.Shift.IsPressed()
			&& _playerInteraction.CarryingCart == null; // 카트 끄는 중에는 달릴 수 없다.

		SetMovingState(isMoving);
		SetRunningState(isRunning);

		UpdateFrictionMaterial(isMoving);

		// forward/right에서 y를 제거해 수평 이동만 남긴다
		Vector3 forward = transform.forward;
		Vector3 right = transform.right;
		forward.y = 0;
		right.y = 0;
		forward.Normalize();
		right.Normalize();
		
		float speed =
			// 카트 끄는 중이면, 카트 속도 적용
			_playerInteraction.CarryingCart != null 
				? _moveSpeedWithCart 
				// 카트 끄는 중 아니라면, isRunning여부 체크해서 알맞은 속도 적용
				: isRunning 
					? _moveSpeed * _runSpeedMultiplier 
					: _moveSpeed;

		// MovePosition은 메서드 → 목표 위치를 계산해서 넘긴다 (fixedDeltaTime 사용)
		Vector3 delta = (forward * move.y + right * move.x) * speed;

		// 점프했을 때의 속도 없애면 안되므로, 이건 직접 적용
		// 벽 뚫리지 않게 하기 위해 MovePosition -> linearVelocity로 수정
		delta.y = _rigidbody.linearVelocity.y;
		_rigidbody.linearVelocity = delta;
	}

	// 이동 중이거나 공중이면 마찰0(벽을 미끄러져 지나감), 지면에 멈춰 있으면 높은 마찰(경사에서 안 미끄러짐)
	private void UpdateFrictionMaterial(bool isMoving)
	{
		if (_bodyCollider == null || _gripMaterial == null)
		{
			return;
		}

		PhysicsMaterial target = (isMoving || !IsGrounded()) ? _slideMaterial : _gripMaterial;
		if (_bodyCollider.sharedMaterial != target)
		{
			_bodyCollider.sharedMaterial = target;
		}
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
		_playerCameraController.SetYaw(rotation.eulerAngles.y);

		_rigidbody.linearVelocity = Vector3.zero;
		_rigidbody.angularVelocity = Vector3.zero;
		_rigidbody.position = position;
		_rigidbody.rotation = rotation;
	}
}
