using Unity.VisualScripting;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;


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
    private NpcStateMachine _currentState;
    private static readonly int IsMovingHash = Animator.StringToHash("IsMoving");

    public void SetActionEnableState(bool state)
	{
		if (state)
		{
			_actions.Enable();
		}
		else
		{
			_actions.Disable();
		}
	}

	private void Awake()
	{
		// Awake에서 새로 생성
		_actions = new CustomInputActions();
		_actions.Enable();

		if (_headBone != null)
		{
			_headBoneBaseRotation = _headBone.localRotation;
		}
	}

	public override void OnDestroy()
	{
		_actions.Disable();
        SceneManager.sceneLoaded -= HandleSceneLoaded;
        base.OnDestroy();
	}

	// 스폰될 때마다(내 캐릭터든 다른 사람 캐릭터든) 호출된다.
	public override void OnNetworkSpawn()
	{
		Debug.Log($"[PlayerMoveNetworkTest] OwnerClientId = {OwnerClientId}, IsOwner = {IsOwner}");

		if (!IsOwner)
		{
			_camera.enabled = false; // 내 캐릭터가 아니면 카메라 끄기
			_camera.GetComponent<AudioListener>().enabled = false; //오디오 끄기
            return;
        }

        // DisableOtherCameras();
        SceneManager.sceneLoaded += HandleSceneLoaded;
    }
    private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        // DisableOtherCameras();
    }
    private void DisableOtherCameras()
    {
        foreach (var camera in Camera.allCameras)
        {
            // 다른 플레이어 카메라만 끄고 CCTV와 미니맵 카메라는 유지한다.
            if (camera == _camera || camera.GetComponentInParent<PlayerMoveSample>() == null) continue;

            camera.enabled = false;
            if (camera.TryGetComponent(out AudioListener listener))
            {
                listener.enabled = false;
            }
        }
    }

	private void Update()
	{
		if (!IsOwner)
		{
			return;
		}

		if (GameplayUiMode.IsActive)    // UI 조작 중에는 이동, 점프, 시점 입력을 받지 않음
		{
			_jumpRequested = false;
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
		if (_actions.Player.Interact.WasPressedThisFrame())
		{
			Debug.Log($"상호작용 키 눌림!");
		}

		// 점프 입력은 Update에서 감지(입력 놓침 방지)하고, 실제 힘은 FixedUpdate에서 적용
		if (_actions.Player.Jump.WasPressedThisFrame() && IsGrounded())
		{
			Debug.Log($"점프 키 눌림!");
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
		if (!IsOwner)
		{
			return;
		}

		if (GameplayUiMode.IsActive)    // UI 조작 중에는 이동, 점프, 시점 입력을 받지 않음
		{
			_jumpRequested = false;
			return;
		}

		HandleMovement();
		HandleJump();
		ApplyAirGravity();
	}

	// 입력 방향(바라보는 방향 기준)으로 Rigidbody를 물리적으로 이동시킨다
	private void HandleMovement()
	{
        if (_animator != null)
        {
            _animator.SetBool(
                IsMovingHash,
                false);
        }
        Vector2 move = _actions.Player.Move.ReadValue<Vector2>();

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

	private bool IsGrounded()
	{
		return _groundCheck != null && Physics.CheckSphere(
			_groundCheck.position,
			_groundCheckRadius,
			_jumpableSurfaceMask,
			QueryTriggerInteraction.Ignore);
	}

	/// <summary>
	/// 플레이어 소유 클라이언트에서 Rigidbody 위치와 회전을 스폰 포인트로 이동한다.
	/// </summary>
	[Rpc(SendTo.Owner)]
	public void TeleportToPositionRpc(Vector3 position, Quaternion rotation)
	{
		_rigidbody.linearVelocity = Vector3.zero;
		_rigidbody.angularVelocity = Vector3.zero;
		_rigidbody.position = position;
		_rigidbody.rotation = rotation;
	}
}
