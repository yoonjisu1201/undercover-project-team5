using System;
using System.Collections.Generic;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;


/* InputActions를 활용하여 Input을 처리하는 방법 샘플입니다.
 * 현재 /Asset/CustomInputActions 파일 활용하고 있습니다.
 * 추가 액션을 넣고 싶다면 위 경로 파일 내부 내용을 수정하면 됩니다.
 */

public class PlayerMoveSample : NetworkBehaviour
{
	// #803: 좌석을 점유하지 않은 상태와 실제 좌석 ID를 충돌 없이 구분하기 위한 예약값이다.
	private const int NoSeat = -1;
	// #803: 앉기·기상 애니메이션이 안정 상태에 도착했는지 확인한 뒤 서버 단계를 완료하기 위해 사용한다.
	private const string IdleStateName = "Base Layer.Idle";
	private const string SittingIdleStateName = "Base Layer.Sitting Idle";

	// #803: 착석 전환은 이동 잠금과 Animator 완료 판정을 함께 제어하므로
	// #803: 서기, 앉는 중, 착석 완료, 일어나는 중의 4단계로 관리한다.
	// #803: 앉기 완료 전 기상을 막고, 기상 완료 전 이동·좌석 점유가 해제되지 않도록 단계를 구분한다.
	private enum SeatingPhase : byte
	{
		Standing,
		SittingDown,
		Seated,
		StandingUp
	}

	// #803: 각 플레이어의 동기화된 좌석 ID를 모아 서버와 클라이언트가 동일한 점유 여부를 계산한다.
	private static readonly List<PlayerMoveSample> _spawnedPlayers = new();

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
	// #803: 좌석 포즈로 이동한 오너의 위치와 회전을 다른 피어에 즉시 전달한다.
	private NetworkTransform _networkTransform;

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
	// #803: 4단계 SeatingPhase를 Animator가 사용하는 이진 착석 값으로 변환해 적용한다.
	private static readonly int IsSittingHash = Animator.StringToHash("IsSitting");
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
	// #803: 서버가 착석·기상의 4단계 진행 상태를 관리하고 모든 피어가 이를 공유한다.
	private readonly NetworkVariable<SeatingPhase> _networkSeatingPhase =
		new NetworkVariable<SeatingPhase>(
			SeatingPhase.Standing,
			NetworkVariableReadPermission.Everyone,
			NetworkVariableWritePermission.Server);
	// #803: 좌석별 점유 판정을 위해 현재 플레이어가 사용하는 좌석 ID를 동기화한다.
	private readonly NetworkVariable<int> _networkCurrentSeatId =
		new NetworkVariable<int>(
			NoSeat,
			NetworkVariableReadPermission.Everyone,
			NetworkVariableWritePermission.Server);

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
	// #803: 앉기 시작부터 기상 완료까지 입력과 Rigidbody 이동을 차단하는 오너 로컬 상태다.
	private bool _isSeatMovementBlocked;
	// #803: 같은 애니메이션 완료를 FixedUpdate마다 서버에 반복 보고하지 않도록 현재 단계의 요청 여부를 기록한다.
	private bool _seatTransitionCompletionRequested;

	// #803: 완전히 서 있고 점프 중이 아닐 때만 착석을 허용해 전환 중 중복 요청과 점프 도중 좌석 이동을 막는다.
	public bool CanSit => _networkSeatingPhase.Value == SeatingPhase.Standing && !_isJumping;
	// #803: 앉는 중과 일어나는 중에도 일반 상호작용·이동 차단이 유지되도록 Standing만 false로 본다.
	public bool IsSitting => _networkSeatingPhase.Value != SeatingPhase.Standing;
	// #803: 앉기 애니메이션이 끝난 안정 단계에서만 기상 입력을 허용한다.
	public bool CanStand => _networkSeatingPhase.Value == SeatingPhase.Seated;

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

	// #803: 다른 Set*State 메서드와 호출 형식을 맞춰 Animator의 착석 여부를 적용한다.
	// #803: 착석은 서버 권한의 4단계 SeatingPhase로 동기화하므로 이 메서드는 NetworkVariable을 변경하지 않는다.
	private void SetSeatState(bool value)
	{
		ApplyAnimatorBool(IsSittingHash, value);
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

	// #803: 동기화된 단계로 모든 피어의 Animator를 맞추고, 오너의 카메라·이동 잠금 수명 주기를 함께 제어한다.
	private void HandleSeatingPhaseChanged(SeatingPhase previousPhase, SeatingPhase currentPhase)
	{
		// #803: StandingUp 진입 시 착석 bool을 내려 기상 애니메이션을 시작하고, 그 전까지는 착석 자세를 유지한다.
		bool isSitting = currentPhase == SeatingPhase.SittingDown || currentPhase == SeatingPhase.Seated;
		SetSeatState(isSitting);
		// #803: 새 단계로 바뀌면 해당 단계의 애니메이션 완료를 한 번 다시 보고할 수 있어야 한다.
		_seatTransitionCompletionRequested = false;

		// #803: 원격 플레이어는 Animator만 반영하고 입력·카메라·Rigidbody는 실제 오너만 제어한다.
		if (!IsOwner)
		{
			return;
		}

		// #803: 기상 애니메이션 완료가 서버에서 Standing으로 확정된 뒤에만 카메라와 이동을 복구한다.
		if (currentPhase == SeatingPhase.Standing)
		{
			_playerCameraController.ExitSeatedView();
			SetSeatMovementBlocked(false);
			return;
		}

		// #803: SittingDown·Seated·StandingUp 전체에서 이동 잠금과 좌석 위치를 유지한다.
		SetSeatMovementBlocked(true);

		// #803: SittingDown에서 Seated로 바뀔 때 카메라 기준이 다시 초기화되지 않도록 최초 진입만 처리한다.
		if (previousPhase == SeatingPhase.Standing)
		{
			_playerCameraController.EnterSeatedView(transform.eulerAngles.y);
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
			// #803: 다운과 착석 Animator bool이 동시에 켜지지 않도록 착석 표현만 내리고 서버 단계·점유는 유지한다.
			SetSeatState(false);

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
		// #803: 각 플레이어의 서버 관리·동기화된 좌석 ID로 점유 여부를 확인하기 위해
		// #803: 현재 네트워크에 스폰된 플레이어를 점유 검사 목록에 등록한다.
		_spawnedPlayers.Add(this);

		_networkIsMoving.OnValueChanged += HandleMovingChanged;
		_networkIsRunning.OnValueChanged += HandleRunningChanged;
		_networkIsJumping.OnValueChanged += HandleJumpingChanged;
		_networkSeatingPhase.OnValueChanged += HandleSeatingPhaseChanged;
		// #392: PlayerHealth의 NetworkVariable 변경 알림을 모든 클라이언트의 Animator에 반영한다.
		_playerHealth.DownedStateChanged += HandleDownedStateChanged;

		HandleMovingChanged(false, _networkIsMoving.Value);
		HandleRunningChanged(false, _networkIsRunning.Value);
		HandleJumpingChanged(false, _networkIsJumping.Value);
		// #803: OnValueChanged는 현재 값을 재생하지 않으므로 늦게 스폰된 피어에도 현재 착석 단계를 즉시 적용한다.
		HandleSeatingPhaseChanged(SeatingPhase.Standing, _networkSeatingPhase.Value);
		// #392: 기존 상태 초기화와 형식을 맞추되, false를 이전 값으로 넘겨 최초 스폰을 소생으로 판정하지 않는다.
		HandleDownedStateChanged(false, _playerHealth.IsDowned);
	}

	public override void OnNetworkDespawn()
	{
		// #803: 정상 퇴장, 연결 끊김 또는 강제 종료로 Despawn된 플레이어의
		// #803: 마지막 좌석 ID가 점유 검사에 남지 않도록 목록에서 제거한다.
		_spawnedPlayers.Remove(this);
		_networkIsMoving.OnValueChanged -= HandleMovingChanged;
		_networkIsRunning.OnValueChanged -= HandleRunningChanged;
		_networkIsJumping.OnValueChanged -= HandleJumpingChanged;
		_networkSeatingPhase.OnValueChanged -= HandleSeatingPhaseChanged;
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

		// #803: 착석 흐름 중에는 점프 입력을 새로 예약하지 않아 기상 직후 점프가 실행되지 않게 한다.
		if (_isSeatMovementBlocked)
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

		// #803: 이동은 잠겨 있어도 앉기·기상 애니메이션 완료는 계속 감지해야 다음 단계로 진행할 수 있다.
		UpdateSeatAnimationState();

		// #803: 애니메이션 완료 확인 이후 실제 물리 이동과 점프 계산만 중단한다.
		if (_isSeatMovementBlocked)
		{
			return;
		}

		UpdateJumpAnimation();

		// #392: Getting Up -> Idle 전환과 블렌딩이 모두 끝난 뒤에만 이동 잠금을 해제한다.
		if (_isGettingUp &&
			!_animator.IsInTransition(0) &&
			_animator.GetCurrentAnimatorStateInfo(0).IsName(IdleStateName))
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
	public bool IsGrounded()
	{
		return _groundCheck != null && Physics.CheckSphere(
			_groundCheck.position,
			_groundCheckRadius,
			_jumpableSurfaceMask,
			QueryTriggerInteraction.Ignore);
	}

	// #803: 로컬 오너가 완전히 서 있는 경우에만 서버에 해당 좌석의 착석 검증을 요청한다.
	public void RequestSit(int seatId)
	{
		if (IsOwner && CanSit)
		{
			RequestSitRpc(seatId);
		}
	}

	// #803: 앉기 애니메이션이 완료된 오너만 서버에 기상 단계 전환을 요청한다.
	public void RequestStand()
	{
		if (IsOwner && CanStand)
		{
			RequestStandRpc();
		}
	}

	// #803: 좌석이 자신의 현재 이용자인지 판단해 점유 검사와 별도로 기상 상호작용을 허용한다.
	public bool IsCurrentSeat(int seatId)
	{
		return IsSitting && _networkCurrentSeatId.Value == seatId;
	}

	// #803: 착석 중 입력도 일반 Interactable 경로로 처리할 수 있도록 현재 좌석을 반환한다.
	public bool TryGetCurrentSeatInteractable(out InteractableBase currentSeat)
	{
		if (!IsSitting ||
			!WaitingRoomBenchSeatInteractable.TryGetSeat(
				_networkCurrentSeatId.Value,
				out WaitingRoomBenchSeatInteractable seat))
		{
			currentSeat = null;
			return false;
		}

		currentSeat = seat;
		return true;
	}

	// #803: 스폰된 플레이어들의 동기화된 좌석 ID를 비교해 같은 좌석의 중복 착석을 막는다.
	public static bool IsSeatOccupied(int seatId)
	{
		if (seatId == NoSeat)
		{
			return false;
		}

		foreach (PlayerMoveSample player in _spawnedPlayers)
		{
			if (player._networkCurrentSeatId.Value == seatId)
			{
				return true;
			}
		}

		return false;
	}

	// #803: 좌석 오브젝트가 비활성화될 때 서버에 남은 점유 ID를 해제해 다시 사용할 수 없는 좌석을 참조하지 않게 한다.
	public static void ForceReleaseSeatOnServer(int seatId)
	{
		if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer)
		{
			return;
		}

		foreach (PlayerMoveSample player in _spawnedPlayers)
		{
			if (player._networkCurrentSeatId.Value == seatId)
			{
				player.ReleaseSeatOnServer();
			}
		}
	}

	// #803: 오너 요청을 신뢰하지 않고 서버에서 단계·점유·좌석 존재·플레이어 위치를 모두 다시 검증한다.
	[Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
	private void RequestSitRpc(int seatId, RpcParams rpcParams = default)
	{
		if (!CanSit
			|| IsSeatOccupied(seatId)
			|| !WaitingRoomBenchSeatInteractable.TryGetSeat(seatId, out WaitingRoomBenchSeatInteractable seat)
			|| !NpcInteractionValidation.TryGetInteractionCollider(
				NetworkManager,
				rpcParams.Receive.SenderClientId,
				out SphereCollider interactionCollider)
			|| !seat.IsWithinInteractionRange(interactionCollider))
		{
			return;
		}

		seat.GetSeatPose(out Vector3 position, out Quaternion rotation);
		// #803: 같은 프레임의 다른 요청도 점유를 확인할 수 있도록 좌석 ID를 먼저 예약한 뒤 착석 단계를 시작한다.
		_networkCurrentSeatId.Value = seatId;
		_networkSeatingPhase.Value = SeatingPhase.SittingDown;
		ApplySeatPoseRpc(position, rotation);
	}

	// #803: 기존 물리가 좌석 포즈를 밀지 않도록 먼저 이동을 잠그고, Rigidbody와 NetworkTransform을 같은 포즈로 옮겨 즉시 동기화한다.
	[Rpc(SendTo.Owner)]
	private void ApplySeatPoseRpc(Vector3 position, Quaternion rotation)
	{
		SetSeatMovementBlocked(true);
		ApplyTeleport(position, rotation);
		_networkTransform.Teleport(position, rotation, transform.localScale);
		_playerCameraController.EnterSeatedView(rotation.eulerAngles.y);
	}

	// #803: SittingDown이나 StandingUp 중복 입력을 막기 위해 완전히 착석한 단계에서만 기상을 시작한다.
	[Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
	private void RequestStandRpc()
	{
		if (_networkSeatingPhase.Value == SeatingPhase.Seated)
		{
			_networkSeatingPhase.Value = SeatingPhase.StandingUp;
		}
	}

	// #803: 오너 Animator가 목표 안정 상태에 도착했을 때만 서버에 현재 전환 완료를 보고한다.
	private void UpdateSeatAnimationState()
	{
		// #803: 블렌딩 중이거나 이미 보고한 단계는 완료로 판정하지 않아 중복·조기 RPC를 막는다.
		if (_seatTransitionCompletionRequested || _animator.IsInTransition(0))
		{
			return;
		}

		AnimatorStateInfo animatorState = _animator.GetCurrentAnimatorStateInfo(0);
		SeatingPhase currentPhase = _networkSeatingPhase.Value;
		// #803: 앉기는 Sitting Idle, 기상은 Idle 도착을 각각 완료 기준으로 사용한다.
		bool completed =
			currentPhase == SeatingPhase.SittingDown && animatorState.IsName(SittingIdleStateName) || 
			currentPhase == SeatingPhase.StandingUp && animatorState.IsName(IdleStateName);

		if (!completed)
		{
			return;
		}

		_seatTransitionCompletionRequested = true;
		CompleteSeatTransitionRpc(currentPhase);
	}

	// #803: 늦게 도착한 이전 단계 완료 보고를 거부하고 서버가 현재 단계의 다음 상태만 확정한다.
	[Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
	private void CompleteSeatTransitionRpc(SeatingPhase completedPhase)
	{
		if (_networkSeatingPhase.Value != completedPhase)
		{
			return;
		}

		// #803: 앉기 완료 뒤에만 기상 입력을 받을 수 있는 Seated 안정 단계로 전환한다.
		if (completedPhase == SeatingPhase.SittingDown)
		{
			_networkSeatingPhase.Value = SeatingPhase.Seated;
			return;
		}

		// #803: 기상 애니메이션이 끝난 뒤에만 점유를 해제하고 이동을 복구할 수 있도록 한다.
		if (completedPhase == SeatingPhase.StandingUp)
		{
			ReleaseSeatOnServer();
		}
	}

	// #803: 서버에서 좌석 ID를 비운 뒤 Standing을 전파해 점유 해제와 오너 이동 복구를 완료한다.
	private void ReleaseSeatOnServer()
	{
		if (!IsServer || _networkSeatingPhase.Value == SeatingPhase.Standing)
		{
			return;
		}

		_networkCurrentSeatId.Value = NoSeat;
		_networkSeatingPhase.Value = SeatingPhase.Standing;
	}

	// #803: 착석 전환 중 위치가 밀리거나 입력이 누적되지 않도록 이동 상태와 Rigidbody를 함께 고정한다.
	private void SetSeatMovementBlocked(bool blocked)
	{
		// #803: 좌석 포즈 RPC와 단계 콜백이 같은 잠금을 요청하므로 중복 호출에서는 kinematic Rigidbody를 다시 초기화하지 않는다.
		if (_isSeatMovementBlocked == blocked)
		{
			return;
		}

		_isSeatMovementBlocked = blocked;

		// #803: 기상 완료 뒤 물리 이동을 다시 사용할 수 있도록 kinematic만 해제한다.
		if (!blocked)
		{
			_rigidbody.isKinematic = false;
			return;
		}

		// #803: 착석 직전의 입력·속도를 모두 지운 뒤 kinematic으로 바꿔 좌석 포즈를 유지한다.
		_jumpRequested = false;
		SetMovingState(false);
		SetRunningState(false);
		SetJumpingState(false);
		_rigidbody.linearVelocity = Vector3.zero;
		_rigidbody.angularVelocity = Vector3.zero;
		_rigidbody.isKinematic = true;
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

		// #803: 착석 잠금으로 이미 kinematic인 Rigidbody에는 속도를 쓰지 않고 좌석 포즈만 적용한다.
		if (!_rigidbody.isKinematic)
		{
			_rigidbody.linearVelocity = Vector3.zero;
			_rigidbody.angularVelocity = Vector3.zero;
		}
		_rigidbody.position = position;
		_rigidbody.rotation = rotation;
	}
}
