using System;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;


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
	[Tooltip("맞은 순간의 속도(m/s). 곧바로 줄어들므로 실제로 밀리는 거리는 이보다 짧다.")]
	[SerializeField, Min(0f)] private float _knockbackSpeed = 8f;

	[Tooltip("밀리는 동안 조작이 속도를 덮지 않는 시간(초). 길면 조작을 뺏긴 느낌이 난다.")]
	[SerializeField, Min(0f)] private float _knockbackSeconds = 0.25f;

	[Tooltip("초당 감쇠율. 클수록 처음만 세게 밀리고 금방 멎는다. 0이면 등속으로 미끄러진다.")]
	[SerializeField, Min(0f)] private float _knockbackDamping = 12f;

	// 밀려갈 자리를 볼 때 한 스텝 거리에 더하는 여유(m).
	private const float KnockbackSkin = 0.05f;

	// 그 자리에 몸을 놓아 볼 때 반경을 줄이는 비율. 스치는 접촉까지 막힘으로 치지 않는다.
	private const float BlockCheckShrink = 0.9f;

	// 밀려갈 자리 아래로 바닥을 찾아보는 거리(m). 계단·비탈은 넘어가고 낭떠러지만 걸러낸다.
	private const float GroundProbeDistance = 1.5f;

	// 맵 밖으로 떨어졌는지 확인하는 간격(초).
	private const float FallCheckInterval = 0.5f;

	// 되돌리는 조건. 이 시간 동안 계속 떨어지고, 안전 지점보다 이만큼 아래여야 한다.
	private const float FallRecoverSeconds = 1.5f;
	private const float FallRecoverDepth = 5f;

	// 안전 지점을 찾을 때 NavMesh 위로 끌어당기는 거리(m).
	private const float SafeSampleRadius = 1.5f;

	private float _knockbackUntil;
	private Vector3 _knockbackVelocity;

	// 마지막으로 멀쩡히 서 있던 자리. 맵 밖으로 떨어졌을 때 여기로 되돌린다.
	private Vector3 _lastSafePosition;
	private bool _hasSafePosition;
	private float _lastFallCheckTime;
	private float _fallingSeconds;

	// 밀려갈 자리를 확인할 때 쓰는 버퍼. 인스턴스마다 따로 들고 있어야 서로 덮어쓰지 않는다.
	private readonly Collider[] _blockBuffer = new Collider[8];

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

	// 점프 입력 예약 (Update에서 감지 → FixedUpdate에서 힘 적용)
	private bool _jumpRequested = false;

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
		if (_isJumping && _rigidbody.linearVelocity.y <= 0f && IsGrounded())
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
		// 원격 플레이어 인스턴스에서도 돌아야 하므로 IsOwner 가드보다 앞에 둔다.
		UpdateFootstep();

		if (!IsOwner)
		{
			return;
		}

		// 조작이 막혀 있어도 떨어지는 것은 막아야 하므로 UI 가드보다 앞에 둔다.
		UpdateFallRecovery();

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

		// 넉백 중에는 입력이 속도를 덮지 않는다. 덮으면 밀린 속도가 다음 물리 스텝에
		// 그대로 지워져서, 맞아도 제자리에 서 있는 것처럼 보인다.
		//
		// 마찰은 미끄러지는 쪽으로 둔다. 서 있을 때 쓰는 접지 머티리얼은 마찰이 높아서,
		// 밀어 준 속도를 바닥이 한두 프레임 만에 잡아먹는다.
		if (Time.time < _knockbackUntil)
		{
			SetMovingState(false);
			SetRunningState(false);
			UpdateFrictionMaterial(true);
			ApplyKnockbackVelocity();
			ApplyAirGravity();
			return;
		}

		HandleMovement();
		HandleJump();
		ApplyAirGravity();
	}

	// 맞은 자리의 반대쪽으로 민다. 오너에서만 부른다.
	//
	// 물리에 맡기지 않는다. 겹침 해소는 이동이 아니라 위치를 직접 보정하는 것이라
	// 연속 충돌 판정을 켜 두어도 벽을 그대로 지나간다.
	public void ApplyKnockback(Vector3 sourcePosition)
	{
		if (!IsOwner || _knockbackSpeed <= 0f)
		{
			return;
		}

		Vector3 away = transform.position - sourcePosition;
		away.y = 0f;

		if (away.sqrMagnitude <= 0.0001f)
		{
			return;
		}

		_knockbackVelocity = away.normalized * _knockbackSpeed;
		_knockbackUntil = Time.time + _knockbackSeconds;
	}

	// 밀림을 물리 스텝마다 줄여가며 넣는다.
	//
	// 속도를 한 번 주고 놔두면 넉백 중에는 마찰이 0이라 끝까지 같은 빠르기로 미끄러진다.
	// 맞아서 튕긴 것이 아니라 밀려나는 것으로 보인다. 처음이 세고 빨리 죽어야 타격으로 읽힌다.
	private void ApplyKnockbackVelocity()
	{
		_knockbackVelocity *= Mathf.Exp(-_knockbackDamping * Time.fixedDeltaTime);

		float step = _knockbackVelocity.magnitude * Time.fixedDeltaTime;

		if (step > 0f && !CanPushTo(_knockbackVelocity.normalized, step + KnockbackSkin))
		{
			_knockbackVelocity = Vector3.zero;
		}

		// 세로 속도는 건드리지 않는다. 여기서 덮으면 공중에서 맞았을 때 낙하가 끊긴다.
		_rigidbody.linearVelocity = new Vector3(
			_knockbackVelocity.x, _rigidbody.linearVelocity.y, _knockbackVelocity.z);
	}

	// 입력 방향(바라보는 방향 기준)으로 Rigidbody를 물리적으로 이동시킨다
	private void HandleMovement()
	{
		Vector2 move = _actions.Player.Move.ReadValue<Vector2>();

		bool isMoving = move.sqrMagnitude > 0.01f;
		UpdateStaminaExhaustion();

		bool isRunning =
			isMoving
			&& _actions.Player.Shift.IsPressed()
			&& _playerInteraction.CarryingCart == null // 카트 끄는 중에는 달릴 수 없다.
			&& !_isStaminaExhausted
			&& !_playerStamina.IsRedZonePenalized // 빨간 구간 패널티 중에는 회복도 사용도 막힌다.
			&& _playerStamina.CurrentStamina > _minimumRunStamina; // 스태미나가 없으면 달릴 수 없다.

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
	// 맵 밖으로 떨어졌으면 마지막으로 멀쩡히 서 있던 자리로 되돌린다.
	//
	// 밀려나서 벽을 통과하면 스스로 돌아올 방법이 없다. 조작으로 올라올 수 없고,
	// NetworkTransform 이 소유자 권한이라 서버가 교정해 주지도 않는다. 낙사하거나
	// 라운드가 끝날 때까지 갇힌다.
	//
	// 통과 자체를 막는 것은 물리 쪽에서 하고, 여기는 그래도 뚫렸을 때의 안전망이다.
	// 안전망이 정상 이동을 되돌리는 일이 있어서는 안 되므로, 조건은 셋을 모두 만족할 때만이다.
	// 공중에 떠 있고, 계속 아래로 떨어지는 중이고, 기억해 둔 자리보다 한참 아래여야 한다.
	private void UpdateFallRecovery()
	{
		float elapsed = Time.time - _lastFallCheckTime;
		if (elapsed < FallCheckInterval)
		{
			return;
		}

		_lastFallCheckTime = Time.time;

		Vector3 position = transform.position;

		// 바닥을 딛고 NavMesh 위에 있으면 그 자리를 기억해 둔다.
		if (IsGrounded() &&
			NavMesh.SamplePosition(position, out NavMeshHit hit, SafeSampleRadius, NavMesh.AllAreas))
		{
			_lastSafePosition = hit.position;
			_hasSafePosition = true;
			_fallingSeconds = 0f;
			return;
		}

		// 아래로 떨어지는 중일 때만 센다. 점프해서 올라가는 중이거나 떠 있기만 하면 아니다.
		if (_rigidbody.linearVelocity.y >= 0f)
		{
			_fallingSeconds = 0f;
			return;
		}

		_fallingSeconds += elapsed;

		if (_hasSafePosition &&
			_fallingSeconds >= FallRecoverSeconds &&
			position.y < _lastSafePosition.y - FallRecoverDepth)
		{
			ApplyTeleport(_lastSafePosition, transform.rotation);
		}
	}

	// 그 방향으로 그만큼 밀어도 되는 자리인지. 벽이 없고 발 디딜 곳이 있어야 한다.
	private bool CanPushTo(Vector3 direction, float distance)
	{
		if (_bodyCollider == null)
		{
			return true;
		}

		GetBodyCapsule(direction * distance, out Vector3 bottom, out Vector3 top, out float radius);

		return HasGroundUnder(bottom, radius) && !HasWallAt(bottom, top, radius);
	}

	// 몸을 offset 만큼 옮겼을 때 캡슐이 놓일 자리. 캡슐 축은 y 로 서 있다고 본다.
	private void GetBodyCapsule(Vector3 offset, out Vector3 bottom, out Vector3 top, out float radius)
	{
		float sideScale = Mathf.Max(transform.lossyScale.x, transform.lossyScale.z);
		radius = _bodyCollider.radius * sideScale * BlockCheckShrink;

		float half = Mathf.Max(0f, _bodyCollider.height * 0.5f * transform.lossyScale.y - radius);

		Vector3 center = transform.TransformPoint(_bodyCollider.center) + offset;
		bottom = center - Vector3.up * half;
		top = center + Vector3.up * half;
	}

	// 벽을 뚫는 것만 막아서는 소용이 없다. 난간이나 통로 끝에서 맞으면 앞이 비어 있어서
	// 검사를 그냥 통과하고, 그대로 밀려 떨어진다. 그쪽이 벽을 뚫는 것보다 자주 죽는다.
	private static bool HasGroundUnder(Vector3 bottom, float radius)
	{
		return Physics.Raycast(
			bottom + Vector3.up * radius, Vector3.down,
			radius + GroundProbeDistance, ~0, QueryTriggerInteraction.Ignore);
	}

	// 도착할 자리에 몸을 놓아 보고 겹치는지 센다.
	//
	// 쓸기 검사(SweepTest, CapsuleCast)로는 안 된다. 물리 엔진의 쓸기는 출발 시점에 이미
	// 겹쳐 있는 것을 무시한다. 벽에 등을 붙이고 맞는 상황이 정확히 그 경우라, 바로 앞의
	// 벽을 못 보고 통과했다.
	private bool HasWallAt(Vector3 bottom, Vector3 top, float radius)
	{
		int count = Physics.OverlapCapsuleNonAlloc(
			bottom, top, radius, _blockBuffer, ~0, QueryTriggerInteraction.Ignore);

		for (int i = 0; i < count; i++)
		{
			Collider other = _blockBuffer[i];

			if (other == null || other.transform.IsChildOf(transform))
			{
				continue;
			}

			// 밀리는 물체는 막힘이 아니다. 벽·문처럼 꿈쩍 않는 것만 센다.
			Rigidbody body = other.attachedRigidbody;
			if (body != null && !body.isKinematic)
			{
				continue;
			}

			return true;
		}

		return false;
	}

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
		// 옮겨간 곳은 다른 구역이라 이전 안전 지점이 의미가 없다. 그대로 두면 지하에서
		// 기억한 자리가 지상까지 따라와서, 정상적으로 나간 사람을 도로 지하로 끌어내린다.
		_hasSafePosition = false;
		_fallingSeconds = 0f;

		_playerCameraController.SetYaw(rotation.eulerAngles.y);

		_rigidbody.linearVelocity = Vector3.zero;
		_rigidbody.angularVelocity = Vector3.zero;
		_rigidbody.position = position;
		_rigidbody.rotation = rotation;
	}
}
