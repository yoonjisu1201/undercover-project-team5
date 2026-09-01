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
	[SerializeField, Min(0f)] private float _minimumRunStamina = 1f;
	[SerializeField, Range(0f, 0.5f)] private float _staminaRunResumeRatio = 0.2f;
	[SerializeField] private float _jumpPower = 10f;

	[Header("중력 관련")]
	[SerializeField] private float _gravityValue = 2.5f;   // 떨어질 때 중력 배수
	[SerializeField] private float _riseMultiplier = 2f;   // 올라갈 때 중력 배수 (클수록 정점에 빨리 도달 = 상승이 빨라짐)
	[SerializeField] private Rigidbody _rigidbody;

	[Header("피격 넉백")]
	[SerializeField] private PlayerKnockback _knockback = new PlayerKnockback();

	// 맵 밖으로 떨어졌을 때의 안전망. 조정할 값이 없어서 인스펙터에 내놓지 않는다.
	private readonly PlayerFallRecovery _fallRecovery = new PlayerFallRecovery();

	[Header("경사 미끄러짐 방지")]
	[SerializeField] private PhysicsMaterial _gripMaterial; // 멈춰 있을 때 경사에 고정 (높은 마찰)
	private CapsuleCollider _bodyCollider;
	private PhysicsMaterial _slideMaterial;                 // 이동 중 사용 (초기 마찰0 머티리얼)

	[Header("카메라 관련")]
	[SerializeField] private PlayerCameraController _playerCameraController;

	[Header("지면 판정")]
	[SerializeField] private Transform _groundCheck;
	[SerializeField, Min(0.01f)] private float _groundCheckRadius = 0.3f;

	[Header("발소리")]
	// 한 걸음 사이의 간격. 달리기는 이동 속도가 _runSpeedMultiplier(1.5)배라 간격도 그만큼 짧다.
	[SerializeField, Min(0.05f)] private float _footstepWalkInterval = 0.45f;
	[SerializeField, Min(0.05f)] private float _footstepRunInterval = 0.3f;

	private readonly FootstepLoop _footsteps = new(SoundKey.Player_FootstepWalk, SoundKey.Player_FootstepRun);
	[SerializeField] private LayerMask _jumpableSurfaceMask;

	private const float MoveInputDeadZone = 0.01f;

	// Update에서 입력을 수집하고 FixedUpdate에서 같은 스냅샷을 소비한다.
	private Vector2 _moveInput;
	private bool _runHeld;
	private bool _jumpRequested;
	private bool _recoveryRequested;
	private Vector3 _recoveryPosition;
	private bool _isGrounded;

	// 만들어 둔 InputActions 파일
	private CustomInputActions _actions;

	private Animator _animator;
	private PlayerHealth _playerHealth;
	private PlayerStamina _playerStamina;
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

	// 착지 판정 시점의 낙하 속도(m/s, 양수). 오너에서만 채워진다.
	private float _landingImpactSpeed;

	// 보스 감지가 착지 소리를 내야 해서 점프 여부를 알아야 한다.
	// _networkIsJumping이 오너가 쓰고 모두가 읽는 값이고, HandleJumpingChanged가 각 피어의
	// _isJumping을 따라 갱신하므로 서버에서도 이 값이 맞다.
	public bool IsJumping => _isJumping;

	// 오너가 착지한 순간. 인자는 내려온 속도(m/s, 양수)다. 연출 세기를 이 값으로 조절해야
	// 제자리 점프와 높은 곳에서의 낙하가 같은 무게로 보이지 않는다.
	//
	// UI 흔들림처럼 화면 쪽 연출만 구독한다. 로컬 연출이고 구독자가 씬 곳곳에 흩어져 있어서
	// static 으로 둔다. 구독자는 OnEnable/OnDisable 짝으로 붙였다 떼야 한다.
	public static event Action<float> OwnerLanded;

	// 오너가 공중에 떠 있는지 바뀐 순간. UI 가 뜬 동안 들려 있다가 내려오는 데 쓴다.
	//
	// 착지 이벤트와 따로 두는 이유: 공중에서 쓰러지면 착지 이벤트는 나지 않는데(넘어지는 연출이
	// 따로 있어서 막아둔다), 그때도 들려 있던 UI 는 제자리로 내려와야 한다.
	public static event Action<bool> OwnerAirborneChanged;

	private bool _isStaminaExhausted;
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
		_playerStamina = GetComponent<PlayerStamina>();
		_playerInteraction = GetComponent<PlayerInteraction>();

		_bodyCollider = GetComponent<CapsuleCollider>();
		if (_bodyCollider != null)
		{
			_slideMaterial = _bodyCollider.sharedMaterial; // 인스펙터에 붙어 있는 마찰0 머티리얼
		}

		_knockback.Initialize(transform, _rigidbody, _bodyCollider);
		_fallRecovery.Initialize(transform, _rigidbody);

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
		bool changed = IsSpawned && IsOwner && _networkIsRunning.Value != value;
		SyncAnimatorBool(IsRunningHash, _networkIsRunning, value);

		// 스태미나 소모/회복은 값이 실제로 바뀌는 시점(달리기 시작/중지)에만 서버에 알린다.
		if (changed)
		{
			if (value) { _playerStamina.StartRunning(); }
			else { _playerStamina.StopRunning(); }
		}
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

	private void HandleJumpingChanged(bool previous, bool value)
	{
		_isJumping = value;
		ApplyAnimatorBool(IsJumpingHash, value);

		if (IsOwner)
		{
			OwnerAirborneChanged?.Invoke(value);
		}

		if (value)
		{
			SoundManager.Instance?.PlayAt(SoundKey.Player_Jump, transform.position);
			return;
		}

		// 떠 있다가 내려온 순간에만 착지 소리를 낸다.
		//
		// 스폰 직후 초기 상태를 적용하는 호출은 previous 가 false 라 여기 들어오지 않는다.
		// 쓰러질 때도 점프 상태를 내리는데(HandleDownedChanged), 그때는 쓰러지는 소리가 따로 나므로
		// 공중에서 당했다고 착지 소리까지 겹쳐 낼 이유가 없다.
		if (!previous || (_playerHealth != null && _playerHealth.IsDowned))
		{
			return;
		}

		SoundManager.Instance?.PlayAt(SoundKey.Player_Land, transform.position);

		// 착지 충격은 내 화면에만 전해진다. 남이 뛰어내렸다고 내 UI 가 흔들릴 이유는 없다.
		if (IsOwner)
		{
			OwnerLanded?.Invoke(_landingImpactSpeed);
		}
	}

	// #392: PlayerHealth.DownedStateChanged -> Animator IsDowned -> Downed/Getting Up 전이 흐름의 연결 지점이다.
	private void HandleDownedStateChanged(bool previousValue, bool value)
	{
		ApplyAnimatorBool(IsDownedHash, value);

		if (value)
		{
			SoundManager.Instance?.PlayAt(SoundKey.Player_Downed, transform.position);

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

		if (_isGettingUp)
		{
			SoundManager.Instance?.PlayAt(SoundKey.Player_Revive, transform.position);
		}
	}

	// 보스와는 물리로 부딪히지 않는다. 밀리는 것은 공격이 넉백으로 직접 준다.
	//
	// 물리에 맡기면 어떻게 해도 벽을 뚫는다. 벽과 보스 사이에 끼면 솔버는 둘 중 하나를
	// 포기해야 하는데, 벽을 포기하는 쪽이 싸서 사람이 벽 밖으로 나간다. 겹치는 문제는
	// BossController 가 물러나는 것으로 푼다.
	//
	// 레이어로 가를 수는 없다. 보스와 사람이 같은 Default 레이어라, 그 조합을 끄면
	// 사람이 벽과 바닥까지 통과한다.
	//
	// 물리 설정은 피어마다 따로라 모든 클라이언트가 각자 불러야 한다. 보스가 나중에
	// 등장하면 보스 쪽에서 같은 짝을 맺는다.
	public void IgnoreBossCollision()
	{
		if (_bodyCollider == null)
		{
			return;
		}

		BossController boss = FindFirstObjectByType<BossController>();
		if (boss == null || !boss.TryGetComponent(out CapsuleCollider bossCollider))
		{
			return;
		}

		Physics.IgnoreCollision(bossCollider, _bodyCollider, true);
	}

	private void UpdateJumpAnimation()
	{
		if (_isJumping && _rigidbody.linearVelocity.y <= 0f && _isGrounded)
		{
			// 낙하 속도는 여기서 잡아둔다. 접지하면 곧바로 0 이 되므로, 착지 이벤트를 낼 때
			// 다시 읽으면 세기를 구할 수 없다.
			_landingImpactSpeed = -_rigidbody.linearVelocity.y;

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

		IgnoreBossCollision();
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
		UpdateFootstep();

		if (!IsOwner)
		{
			return;
		}

		if (_fallRecovery.NeedsRecovery(IsGrounded(), out Vector3 safePosition))
		{
			_recoveryPosition = safePosition;
			_recoveryRequested = true;
		}

		if (IsControlBlocked())
		{
			ClearInputSnapshot();
			return;
		}

		_moveInput = Vector2.ClampMagnitude(_actions.Player.Move.ReadValue<Vector2>(), 1f);
		_runHeld = _actions.Player.Shift.IsPressed();

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

		if (_recoveryRequested)
		{
			_recoveryRequested = false;
			ApplyTeleport(_recoveryPosition, transform.rotation);
		}

		_isGrounded = IsGrounded();
		UpdateJumpAnimation();
		ApplyViewRotation();
		UpdateGettingUpState();

		if (IsControlBlocked())
		{
			StopControlledMovement();
			ApplyAirGravity();
			return;
		}

		if (_knockback.IsActive)
		{
			SetMovementState(false, false);
			UpdateFrictionMaterial(true, _isGrounded);
			_knockback.Tick();
			ApplyAirGravity();
			return;
		}

		ApplyControlledMovement();
		TryJump();
		ApplyAirGravity();
	}

	private bool IsControlBlocked()
	{
		return GameplayUiMode.IsMovementBlocked
			|| _playerHealth.IsDowned
			|| _isGettingUp
			|| _playerCameraController.IsCameraTransitioning;
	}

	private void ClearInputSnapshot()
	{
		_moveInput = Vector2.zero;
		_runHeld = false;
		_jumpRequested = false;
	}

	private void UpdateGettingUpState()
	{
		if (!_isGettingUp
			|| _animator.IsInTransition(0)
			|| !_animator.GetCurrentAnimatorStateInfo(0).IsName("Base Layer.Idle"))
		{
			return;
		}

		_isGettingUp = false;
		GettingUpFinished?.Invoke();
		_playerCameraController.TransitionToFirstPersonView();
	}

	private void StopControlledMovement()
	{
		ClearInputSnapshot();
		SetMovementState(false, false);
		UpdateFrictionMaterial(false, _isGrounded);
		SetHorizontalVelocity(Vector3.zero);
	}

	// 맞은 자리의 반대쪽으로 민다. PlayerHealth 가 오너에게만 보낸다.
	public void ApplyKnockback(Vector3 sourcePosition)
	{
		if (IsOwner)
		{
			_knockback.Push(sourcePosition);
		}
	}

	// 몸체 yaw와 그 yaw를 기준으로 한 이동을 같은 물리 틱에서 처리한다.
	// 렌더 프레임의 즉각적인 카메라 yaw는 PlayerCameraController가 별도로 보정한다.
	private void ApplyViewRotation()
	{
		_rigidbody.MoveRotation(_playerCameraController.ViewYawRotation);
	}

	private void ApplyControlledMovement()
	{
		bool isMoving = _moveInput.sqrMagnitude > MoveInputDeadZone;
		UpdateStaminaExhaustion();
		bool isRunning = CanRun(isMoving);

		SetMovementState(isMoving, isRunning);
		UpdateFrictionMaterial(isMoving, _isGrounded);

		Vector3 localDirection = new Vector3(_moveInput.x, 0f, _moveInput.y);
		Vector3 worldDirection = _playerCameraController.ViewYawRotation * localDirection;
		SetHorizontalVelocity(worldDirection * ResolveMoveSpeed(isRunning));
	}

	private bool CanRun(bool isMoving)
	{
		return isMoving
			&& _runHeld
			&& _playerInteraction.CarryingCart == null
			&& !_isStaminaExhausted
			&& !_playerStamina.IsRedZonePenalized
			&& _playerStamina.CurrentStamina > _minimumRunStamina;
	}

	private float ResolveMoveSpeed(bool isRunning)
	{
		if (_playerInteraction.CarryingCart != null)
		{
			return _moveSpeedWithCart;
		}

		return isRunning ? _moveSpeed * _runSpeedMultiplier : _moveSpeed;
	}

	private void SetMovementState(bool isMoving, bool isRunning)
	{
		SetMovingState(isMoving);
		SetRunningState(isRunning);
	}

	private void SetHorizontalVelocity(Vector3 horizontalVelocity)
	{
		Vector3 velocity = _rigidbody.linearVelocity;
		velocity.x = horizontalVelocity.x;
		velocity.z = horizontalVelocity.z;
		_rigidbody.linearVelocity = velocity;
	}

	private void UpdateStaminaExhaustion()
	{
		float currentStamina = _playerStamina.CurrentStamina;
		if (currentStamina <= _minimumRunStamina)
		{
			_isStaminaExhausted = true;
			return;
		}

		float resumeStamina = Mathf.Max(_minimumRunStamina, _playerStamina.MaxStamina * _staminaRunResumeRatio);
		if (_isStaminaExhausted && currentStamina >= resumeStamina)
		{
			_isStaminaExhausted = false;
		}
	}

	// 이동 중이거나 공중이면 마찰0(벽을 미끄러져 지나감), 지면에 멈춰 있으면 높은 마찰(경사에서 안 미끄러짐)
	private void UpdateFrictionMaterial(bool isMoving, bool grounded)
	{
		if (_bodyCollider == null || _gripMaterial == null)
		{
			return;
		}

		PhysicsMaterial target = (isMoving || !grounded) ? _slideMaterial : _gripMaterial;
		if (_bodyCollider.sharedMaterial != target)
		{
			_bodyCollider.sharedMaterial = target;
		}
	}


	// 점프 예약이 있으면 위 방향 속도를 부여한다 (누른 시간과 무관하게 항상 같은 점프)
	private void TryJump()
	{
		if (!_jumpRequested)
		{
			return;
		}

		_jumpRequested = false;
		if (!_isGrounded)
		{
			return;
		}

		SetJumpingState(true);

		Vector3 velocity = _rigidbody.linearVelocity;
		velocity.y = _jumpPower;
		_rigidbody.linearVelocity = velocity;
		_isGrounded = false;
	}

	// 공중에서 상승/낙하에 추가 중력을 줘서 스냅감을 만든다
	private void ApplyAirGravity()
	{
		if (_isGrounded && _rigidbody.linearVelocity.y <= 0f)
		{
			return;
		}

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

	// 각 클라이언트가 자기 화면의 플레이어마다 이 타이머를 돌린다. 동기화된 이동 상태와 위치를
	// 그대로 쓰므로 발소리 때문에 통신이 발생하지 않는다.
	private void UpdateFootstep()
	{
		if (!_networkIsMoving.Value || _isJumping)
		{
			_footsteps.Stop();
			return;
		}

		bool running = _networkIsRunning.Value;
		if (!_footsteps.Tick(running, _footstepWalkInterval, _footstepRunInterval))
		{
			return;
		}

		// 발이 땅에 없으면 이번 걸음은 넘긴다. 타이머는 위에서 이미 갱신했다.
		if (!IsGrounded())
		{
			return;
		}

		_footsteps.Play(transform.position, running);
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
		_fallRecovery.Forget();

		_playerCameraController.SetYaw(rotation.eulerAngles.y);

		_rigidbody.linearVelocity = Vector3.zero;
		_rigidbody.angularVelocity = Vector3.zero;
		_rigidbody.position = position;
		_rigidbody.rotation = rotation;
	}
}
