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

	// 넉백이 끝나는 시각과 지금 남아 있는 밀림 속도.
	private float _knockbackUntil;
	private Vector3 _knockbackVelocity;

	// 마지막으로 멀쩡히 서 있던 자리. 맵 밖으로 떨어졌을 때 여기로 되돌린다.
	private Vector3 _lastSafePosition;
	private bool _hasSafePosition;
	private float _nextFallCheckTime;
	private float _fallingSeconds;

	// 겹침 해소로 밀려나는 속도의 상한(m/s). 기본값 10 은 벽을 넘길 만큼 세다.
	private const float MaxDepenetrationVelocity = 1f;

	[Header("피격 넉백")]
	[Tooltip("맞은 순간의 속도(m/s). 곧바로 줄어들기 시작하므로 실제로 밀리는 거리는 이보다 짧다.")]
	[SerializeField, Min(0f)] private float _knockbackSpeed = 8f;

	[Tooltip("밀리는 동안 조작이 속도를 덮지 않는 시간(초). 길면 조작을 뺏긴 느낌이 난다.")]
	[SerializeField, Min(0f)] private float _knockbackSeconds = 0.25f;

	[Tooltip("초당 감쇠율. 클수록 처음만 세게 밀리고 금방 멎는다. "
		+ "0이면 감쇠 없이 등속으로 미끄러져서 맞았다기보다 밀려나는 것처럼 보인다.")]
	[SerializeField, Min(0f)] private float _knockbackDamping = 12f;

	// 밀리는 길을 확인할 때 한 스텝 거리에 더해 두는 여유(m). 벽에 닿기 직전에 멎게 한다.
	private const float KnockbackSkin = 0.05f;

	// 맵 밖으로 떨어졌는지 확인하는 간격(초)과, 마지막 안전 지점보다 이만큼 아래면 떨어진 것으로 본다.
	private const float FallCheckInterval = 0.5f;
	private const float FallRecoverDepth = 5f;

	// 이 시간 동안 계속 떨어지고 있어야 되돌린다. 엘리베이터나 계단 낙차와 구분하기 위한 것이다.
	private const float FallRecoverSeconds = 1.5f;

	// 안전 지점을 찾을 때 NavMesh 위로 끌어당기는 거리(m).
	private const float SafeSampleRadius = 1.5f;

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

		// 콜라이더가 겹쳤을 때 밀어내는 속도의 상한. 기본값 10m/s 는 한 스텝에 20cm 를 민다.
		//
		// 보스는 Rigidbody 없이 콜라이더만 들고 transform 으로 움직인다. 물리 엔진은 그것을
		// 정적 지형으로 보기 때문에 접촉으로 풀지 못하고 겹침 해소로 밀어내는데, 그 세기가
		// 그대로 나오면 플레이어가 벽 너머로 튕겨 나간다. 인스펙터에 없는 값이라 여기서 준다.
		_rigidbody.maxDepenetrationVelocity = MaxDepenetrationVelocity;
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

		SetBossCollisionIgnored(value);

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

	// 쓰러진 동안에는 보스가 몸을 통과하게 한다.
	//
	// 보스는 Rigidbody 없이 콜라이더만 들고 transform 으로 움직인다. 그러면 겹침이 질량 없이
	// 밀어내기로만 해소돼서, 지나갈 때마다 쓰러진 몸이 떠밀린다. 다운 중에는 입력이 막혀
	// 스스로 되돌아올 수도 없고, NetworkTransform 이 소유자 권한이라 밀려난 위치를 본인이
	// 그대로 확정해 전원에게 퍼뜨린다. 서버가 교정해 주지 않으므로 접촉 자체를 없앤다.
	//
	// 몸을 고정하는 방법도 있지만 그러면 보스가 시신에 막히거나 타고 올라간다.
	// 레이어로 가르는 것도 안 된다. 보스와 플레이어가 같은 Default 레이어라, 그 조합을 끄면
	// 시신이 벽과 바닥까지 통과한다.
	//
	// 이 호출은 각 피어에서 자기 물리 씬에만 적용되므로 모든 클라이언트가 각자 호출해야 한다.
	// 다운 상태는 NetworkVariable 이라 이 콜백이 전원에게서 돌아간다.
	private void SetBossCollisionIgnored(bool ignored)
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

		Physics.IgnoreCollision(bossCollider, _bodyCollider, ignored);
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
	// 예전에는 보스 콜라이더와 겹치면서 물리가 밀어내는 것이 넉백처럼 보였다. 그건 이동이
	// 아니라 위치를 직접 보정하는 것이라 벽을 그대로 통과했다. 속도로 밀면 이동 경로 검사를
	// 타므로 벽에서 멈춘다.
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
	// 속도를 한 번 주고 놔두면 마찰이 0이라 끝까지 같은 빠르기로 미끄러진다. 맞아서 튕긴
	// 것이 아니라 밀려나는 것처럼 보인다. 처음이 가장 세고 빠르게 죽어야 타격으로 읽힌다.
	private void ApplyKnockbackVelocity()
	{
		_knockbackVelocity *= Mathf.Exp(-_knockbackDamping * Time.fixedDeltaTime);

		// 이번 스텝에 나아갈 만큼 미리 쓸어 보고, 막혀 있으면 그 자리에서 멎는다.
		//
		// 겹침 해소 세기를 낮춰 뒀기 때문에 물리에 맡길 수 없다. 얇은 문이나 벽에 세게
		// 밀어붙이면, 솔버가 밀어내기 전에 몸의 중심이 반대편으로 넘어가 버린다. 그다음에는
		// 넘어간 쪽으로 밀어내는 것이 가까우니 그대로 통과한다.
		float step = _knockbackVelocity.magnitude * Time.fixedDeltaTime;

		if (step > 0f && _rigidbody.SweepTest(
			_knockbackVelocity.normalized, out _, step + KnockbackSkin, QueryTriggerInteraction.Ignore))
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
		if (Time.time < _nextFallCheckTime)
		{
			return;
		}

		float elapsed = Time.time - _nextFallCheckTime + FallCheckInterval;
		_nextFallCheckTime = Time.time + FallCheckInterval;

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
