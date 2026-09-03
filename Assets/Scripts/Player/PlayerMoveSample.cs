using System;
using Unity.Netcode;
using Unity.Netcode.Components;
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
	// 보스 몸 안으로 이 거리까지만 들어갈 수 있다. 보스의 _personalSpace(1.1)보다 좁게 둬야,
	// 그 사이 구간에서 사람이 밀고 들어가며 보스를 조금씩 밀어낼 수 있다.
	// 같거나 넓으면 애초에 닿지를 못해 미는 느낌이 아예 사라진다.
	[Tooltip("보스에게 이 거리까지만 다가갈 수 있다(m). 보스의 Personal Space 보다 좁게 둔다")]
	[SerializeField, Min(0f)] private float _bossBlockRadius = 0.9f;

	private BossController _boss;
	private CapsuleCollider _bodyCollider;
	private PhysicsMaterial _slideMaterial;                 // 이동 중 사용 (초기 마찰0 머티리얼)

	[Header("카메라 관련")]
	[SerializeField] private PlayerCameraController _playerCameraController;

	// 판정 범위를 코드 안의 숫자로 두면 캡슐 바닥과 바닥면 사이 어디에 걸쳐 있는지 눈으로
	// 확인할 수 없다. 발밑에 트리거 콜라이더를 두고 거기에 닿는지로 본다.
	[Header("지면 판정")]
	[SerializeField] private GroundCheckTrigger _groundCheck;

	// 발이 닿는 순간과 몸이 완전히 내려앉는 순간은 다르다. 닿자마자 점프를 받으면
	// 아직 내려앉는 중에 다시 떠서, 공중에서 점프한 것처럼 보인다.
	[Tooltip("발이 닿고 이 시간이 지나야 다시 점프할 수 있다(초). 내려앉는 동안을 덮는다")]
	[SerializeField, Min(0f)] private float _jumpGroundedDelay = 0.12f;

	// 발이 닿은 뒤에 착지 자세를 잡으면, 닿는 동작과 자세가 정리되는 동작이 따로 보인다.
	// 남의 화면은 보간 때문에 더 늦게 그려져서 그 간격이 더 벌어진다.
	// 내려오는 동안 미리 잡아 두면 닿는 순간에는 이미 끝나 있어 한 동작으로 보인다.
	[Tooltip("착지 몇 초 전에 착지 자세를 미리 잡을지(초). 0 이면 닿은 뒤에 잡는다")]
	[SerializeField, Min(0f)] private float _landAnticipationSeconds = 0.12f;

	// 접지가 시작된 시각. 공중에 뜨면 초기화한다.
	private float _groundedSince = float.NegativeInfinity;

	[Header("발소리")]
	// 한 걸음 사이의 간격. 달리기는 이동 속도가 _runSpeedMultiplier(1.5)배라 간격도 그만큼 짧다.
	[SerializeField, Min(0.05f)] private float _footstepWalkInterval = 0.45f;
	[SerializeField, Min(0.05f)] private float _footstepRunInterval = 0.3f;

	private readonly FootstepLoop _footsteps = new(SoundKey.Player_FootstepWalk, SoundKey.Player_FootstepRun);

	private const float MoveInputDeadZone = 0.01f;

	// Update에서 입력을 수집하고 FixedUpdate에서 같은 스냅샷을 소비한다.
	private Vector2 _moveInput;
	private bool _runHeld;
	private bool _jumpRequested;
	private bool _recoveryRequested;
	private Vector3 _recoveryPosition;

	// 접지 판정은 물리 쿼리라 Update 에서 프레임당 한 번만 갱신하고 모두가 이 값을 읽는다.
	private bool _isGrounded;

	// 만들어 둔 InputActions 파일
	private CustomInputActions _actions;

	private Animator _animator;
	private PlayerHealth _playerHealth;
	private PlayerStamina _playerStamina;
	private PlayerInteraction _playerInteraction;
	private NetworkTransform _networkTransform;
	private static readonly int IsMovingHash = Animator.StringToHash("IsMoving");
	private static readonly int IsRunningHash = Animator.StringToHash("IsRunning");
	private static readonly int IsJumpingHash = Animator.StringToHash("IsJumping");
	// #392: PlayerHealth의 동기화된 다운 상태를 Animator의 Downed/Getting Up 전이에 연결한다.
	private static readonly int IsDownedHash = Animator.StringToHash("IsDowned");
	private readonly NetworkVariable<bool> _networkIsMoving = new NetworkVariable<bool>(
		false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
	private readonly NetworkVariable<bool> _networkIsRunning = new NetworkVariable<bool>(
		false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
	private readonly NetworkVariable<bool> _networkIsJumping = new NetworkVariable<bool>(
		false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

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
		_networkTransform = GetComponent<NetworkTransform>();

		_bodyCollider = GetComponent<CapsuleCollider>();
		if (_bodyCollider != null)
		{
			_slideMaterial = _bodyCollider.sharedMaterial; // 인스펙터에 붙어 있는 마찰0 머티리얼
		}

		ValidateRequiredReferences();

		_knockback.Initialize(transform, _rigidbody, _bodyCollider);
		_fallRecovery.Initialize(transform, _rigidbody);
	}

	// 없으면 동작할 수 없는 참조는 여기서 한 번만 검사한다.
	// 이후 코드가 널 검사 없이 바로 쓰는 근거이자, 프리팹 연결이 빠졌을 때의 유일한 신호다.
	private void ValidateRequiredReferences()
	{
		LogIfMissing(_rigidbody, nameof(_rigidbody));
		LogIfMissing(_playerCameraController, nameof(_playerCameraController));
		LogIfMissing(_animator, nameof(_animator));
		LogIfMissing(_playerHealth, nameof(_playerHealth));
		LogIfMissing(_playerStamina, nameof(_playerStamina));
		LogIfMissing(_playerInteraction, nameof(_playerInteraction));
		LogIfMissing(_groundCheck, nameof(_groundCheck));
	}

	private void LogIfMissing(UnityEngine.Object reference, string fieldName)
	{
		if (reference == null)
		{
			Debug.LogError($"[PlayerMoveSample] {fieldName} 참조가 없습니다. Player 프리팹 연결을 확인하세요.", this);
		}
	}

	public override void OnDestroy()
	{
		// Awake 가 돌기 전에 파괴되면 _actions 가 아직 없다.
		_actions?.Disable();
		base.OnDestroy();
	}

	private void ApplyAnimatorBool(int hash, bool value)
	{
		_animator.SetBool(hash, value);
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

			ClearInputSnapshot();

			SetLocomotionState(false, false);
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

		_boss = boss;
		Physics.IgnoreCollision(bossCollider, _bodyCollider, true);
	}

	// 물리 충돌을 껐으니 보스 몸 안으로 그냥 걸어 들어갈 수 있다. 밀어내서 막으면 벽에
	// 끼었을 때 벽 밖으로 나가므로(위 주석 참고), 밀지 않고 보스 쪽으로 향하는 속도 성분만
	// 깎는다. 이동을 줄이기만 하니 어떤 경우에도 사람을 벽 밖으로 내보내지 않는다.
	//
	// 옆으로 향하는 성분은 남겨서, 막힌 채로 보스 주위를 미끄러지듯 돌 수 있다.
	private Vector3 BlockMovementIntoBoss(Vector3 horizontalVelocity)
	{
		if (_bossBlockRadius <= 0f || _boss == null || _boss.IsHidden)
		{
			return horizontalVelocity;
		}

		Vector3 away = transform.position - _boss.transform.position;
		away.y = 0f;

		float distance = away.magnitude;
		if (distance >= _bossBlockRadius || distance <= 0.0001f)
		{
			return horizontalVelocity;
		}

		Vector3 towardBoss = -away / distance;
		float speedIntoBoss = Vector3.Dot(horizontalVelocity, towardBoss);

		// 이미 멀어지는 중이면 건드리지 않는다. 겹친 상태에서 빠져나오는 것까지 막으면 갇힌다.
		if (speedIntoBoss <= 0f)
		{
			return horizontalVelocity;
		}

		return horizontalVelocity - towardBoss * speedIntoBoss;
	}

	private void UpdateJumpAnimation()
	{
		if (!_isJumping)
		{
			return;
		}

		float verticalSpeed = _rigidbody.linearVelocity.y;

		if (verticalSpeed <= 0f && _isGrounded)
		{
			// 낙하 속도는 여기서 잡아둔다. 접지하면 곧바로 0 이 되므로, 착지 이벤트를 낼 때
			// 다시 읽으면 세기를 구할 수 없다.
			_landingImpactSpeed = -verticalSpeed;

			SetJumpingState(false);
			return;
		}

		// 아직 공중이어도 곧 닿을 것이 보이면 미리 끝낸다.
		// 점프 자체는 여전히 막힌다. 점프 조건에 _isGrounded 가 들어 있어서 공중에서는 못 뛴다.
		if (verticalSpeed >= 0f || _landAnticipationSeconds <= 0f || _groundCheck == null)
		{
			return;
		}

		float fallSpeed = -verticalSpeed;
		float lookAhead = fallSpeed * _landAnticipationSeconds;

		// 트리거는 무시한다. 맵 구역 볼륨 같은 것에 걸리면 공중에서 착지 자세가 나온다.
		if (!Physics.Raycast(
				_groundCheck.transform.position,
				Vector3.down,
				lookAhead,
				_groundCheck.JumpableSurfaceMask,
				QueryTriggerInteraction.Ignore))
		{
			return;
		}

		_landingImpactSpeed = fallSpeed;
		SetJumpingState(false);
	}

	// 스폰될 때마다(내 캐릭터든 다른 사람 캐릭터든) 호출된다.
	public override void OnNetworkSpawn()
	{
		Debug.Log($"[PlayerMoveNetworkTest] OwnerClientId = {OwnerClientId}, IsOwner = {IsOwner}");

		if (IsOwner && gameObject.scene.name == "WaitingRoom")
		{
			SynchronizeSpawnRigidbody(_rigidbody, transform.position, transform.rotation);
		}

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

	private static void SynchronizeSpawnRigidbody(Rigidbody rigidbody, Vector3 position, Quaternion rotation)
	{
		rigidbody.linearVelocity = Vector3.zero;
		rigidbody.angularVelocity = Vector3.zero;
		rigidbody.position = position;
		rigidbody.rotation = rotation;
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
		// 접지 판정은 물리 쿼리다. 발소리·점프 입력·착지 애니메이션·추락 감지가 모두 필요로 하므로
		// 여기서 한 번만 구해 돌려 쓴다. 원격 플레이어도 발소리를 내야 해서 IsOwner 가드보다 앞이다.
		bool wasGrounded = _isGrounded;
		_isGrounded = IsGrounded();

		// 닿은 순간을 기록해 둔다. 떠 있는 동안은 비워서 다음 착지 때 다시 세게 한다.
		if (_isGrounded)
		{
			if (!wasGrounded)
			{
				_groundedSince = Time.time;
			}
		}
		else
		{
			_groundedSince = float.NegativeInfinity;
		}

		UpdateFootstep();

		if (!IsOwner)
		{
			return;
		}

		// 조작이 막혀 있어도 떨어지는 것은 막아야 하므로 UI 가드보다 앞에 둔다.
		if (_fallRecovery.NeedsRecovery(_isGrounded, out Vector3 safePosition))
		{
			_recoveryPosition = safePosition;
			_recoveryRequested = true;
		}

		// 기상 판정은 스스로 IsControlBlocked 의 조건(_isGettingUp)이다.
		// 막힘 검사 뒤에 두면 한 번 쓰러진 사람이 영영 일어나지 못한다.
		UpdateGettingUpState();

		if (IsControlBlocked())
		{
			ClearInputSnapshot();
			return;
		}

		_moveInput = Vector2.ClampMagnitude(_actions.Player.Move.ReadValue<Vector2>(), 1f);
		_runHeld = _actions.Player.Shift.IsPressed();

		// 세 가지를 모두 만족해야 점프한다.
		//  - 접지: 발이 땅에 닿아 있다
		//  - !_isJumping: 이전 점프가 끝났다. 판정이 한 프레임 흔들려도 공중에서 다시 뜨지 않는다
		//  - 안착 대기: 닿은 뒤 _jumpGroundedDelay 만큼 지났다. 내려앉는 중에는 받지 않는다
		if (_actions.Player.Jump.WasPressedThisFrame()
			&& _isGrounded
			&& !_isJumping
			&& Time.time - _groundedSince >= _jumpGroundedDelay)
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

		// 낙하 속도를 Rigidbody 에서 직접 읽어야 해서 물리 틱에 남겨 둔다.
		UpdateJumpAnimation();
		ApplyViewRotation();

		// 넉백을 조작 차단보다 먼저 본다. 쓰러뜨리는 타격도 넉백을 함께 보내는데,
		// 차단 분기가 먼저 수평 속도를 지워 버리면 맞고도 제자리에 서 있는 것처럼 보인다.
		//
		// 마찰은 미끄러지는 쪽으로 둔다. 서 있을 때 쓰는 접지 머티리얼은 마찰이 높아서,
		// 밀어 준 속도를 바닥이 한두 프레임 만에 잡아먹는다.
		if (_knockback.IsActive)
		{
			// 밀리는 동안 예약된 점프는 버린다. 넉백이 끝난 뒤 공중에서 튀어 오르지 않게 한다.
			_jumpRequested = false;

			SetLocomotionState(false, false);
			UpdateFrictionMaterial(true, _isGrounded);
			_knockback.Tick();
			ApplyAirGravity();
			return;
		}

		if (IsControlBlocked())
		{
			StopControlledMovement();
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
		SetLocomotionState(false, false);
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

		SetLocomotionState(isMoving, isRunning);
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

	private void SetLocomotionState(bool isMoving, bool isRunning)
	{
		SetMovingState(isMoving);
		SetRunningState(isRunning);
	}

	private void SetHorizontalVelocity(Vector3 horizontalVelocity)
	{
		horizontalVelocity = BlockMovementIntoBoss(horizontalVelocity);

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

		// 접지 검사는 입력을 받은 Update 에서 이미 끝났다. 여기서 다시 보면
		// 난간 끝에서 누른 점프가 판정만 먹고 사라진다.
		_jumpRequested = false;
		SetJumpingState(true);

		Vector3 velocity = _rigidbody.linearVelocity;
		velocity.y = _jumpPower;
		_rigidbody.linearVelocity = velocity;
	}

	// 공중에서 상승/낙하에 추가 중력을 줘서 스냅감을 만든다
	private void ApplyAirGravity()
	{
		// 땅을 딛고 내려가는 중이면 추가 중력을 얹지 않는다.
		// 얹으면 경사에서 몸이 계속 아래로 눌려 미끄러진다.
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
		if (!_isGrounded)
		{
			return;
		}

		_footsteps.Play(transform.position, running);
	}

	// 긴급 탈출 컴포넌트도 이동 코드와 같은 지면 판정을 재사용한다.
	public bool IsGrounded()
	{
		return _groundCheck != null && _groundCheck.IsGrounded;
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

		// 다른 클라이언트에서는 이 순간이동도 NetworkTransform 보간을 타서, 지상(0)·본부(-300)·
		// 지하(-500) 사이를 훑으며 넘어간다. 관전 중이면 그 구간이 그대로 화면에 보인다.
		// 텔레포트 플래그를 실어 보내면 받는 쪽이 보간 없이 바로 옮겨 간다.
		_networkTransform.Teleport(position, rotation, transform.localScale);
	}
}
