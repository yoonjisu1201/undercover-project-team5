using Unity.Netcode;
using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public abstract class CartBase : InteractableBase
{
	private const ulong Empty = ulong.MaxValue;

	[SerializeField] private float _holdDistance = 2f;

	[Header("=== 끌림 물리 (관성/스윙 조절) ===")]
	[SerializeField] private float _followStiffness = 12f;   // 목표 지점으로 당기는 세기. 높이면 딱 붙어 따라온다
	[SerializeField] private float _followDamping = 8f;      // 목표 속도에 수렴하는 속도. 낮추면 출렁임이 커진다
	[SerializeField] private float _yawStiffness = 10f;      // 홀더가 바라보는 방향으로 회전하는 세기
	[SerializeField] private float _yawDamping = 6f;

	private Rigidbody _rigidbody;
	private Collider _collider;

	// 현재 이 카트 잡고있는사람의 ID(네트워크 직렬화용 ID)
	private readonly NetworkVariable<ulong> _currentHolderId = new NetworkVariable<ulong>(Empty);
	private Player _currentHolder;

	// 카트이름(추후 추가될까봐.. 고급카트 일반카트 이런거)
	public abstract string CartName { get; }

	[Header("=== 왼쪽, 오른쪽 핸들(IK붙을 위치) ===")]
	[SerializeField] public Transform LeftHandle;
	[SerializeField] public Transform RightHandle;

	// 해당 카트의 소유자가 있는가?
	public bool IsHolderExists => _currentHolderId.Value != Empty;

	protected override void Awake()
	{
		base.Awake();

		_rigidbody = GetComponent<Rigidbody>();
		_collider = GetComponent<Collider>();
	}

	public override void OnNetworkSpawn()
	{
		base.OnNetworkSpawn();

		_currentHolderId.OnValueChanged += HandleHolderIdChanged;

		// 카트를 든 채로 접속이 끊기면 영영 못 쓰게 되므로, 서버에서 홀더 해제를 강제한다
		if (IsServer)
		{
			NetworkManager.OnClientDisconnectCallback += HandleHolderDisconnected;
		}
	}

	public override void OnNetworkDespawn()
	{
		base.OnNetworkDespawn();

		_currentHolderId.OnValueChanged -= HandleHolderIdChanged;

		if (IsServer)
		{
			NetworkManager.OnClientDisconnectCallback -= HandleHolderDisconnected;
		}
	}

	private void HandleHolderDisconnected(ulong clientId)
	{
		if (_currentHolderId.Value == clientId)
		{
			_currentHolderId.Value = Empty;
			NetworkObject.RemoveOwnership();
		}
	}

	public override string InteractionText => $"{CartName}카트 끌기";

	// 일반적으로 카트는 이미 사용중인 사람이 있으면 상호작용 불가능하게 한다
	public override bool CanInteract(GameObject interactor) => !IsHolderExists;

	// 상호작용한 플레이어가 카트 잡기 요청
	public override void Interact(GameObject interactor)
	{
		HoldCartRpc();
	}

	// 카트 내려놓기
	public virtual void ReleaseCart()
	{
		ReleaseCartRpc();
	}

	[Rpc(SendTo.Server)]
	private void HoldCartRpc(RpcParams rpcParams = default)
	{
		// 이미 다른 사람이 점거중이면 안됨
		if (IsHolderExists)
		{
			Debug.LogWarning($"[CartBase] 이미 다른 사람이 사용중인 카트에 접근했습니다.");
			return;
		}

		// 아니라면, currentHolderId를 업데이트
		_currentHolderId.Value = rpcParams.Receive.SenderClientId;

		// 카트를 잡은 클라이언트가 끌림 물리를 직접 계산하도록 소유권을 넘긴다
		NetworkObject.ChangeOwnership(rpcParams.Receive.SenderClientId);
	}

	[Rpc(SendTo.Server)]
	private void ReleaseCartRpc(RpcParams rpcParams = default)
	{
		if (!IsHolderExists)
		{
			Debug.LogWarning($"[CartBase] 소유자가 없는데 카트 소유 해제를 요청했습니다.");
			return;
		}

		if (_currentHolderId.Value != rpcParams.Receive.SenderClientId)
		{
			Debug.LogWarning($"[CartBase] 소유자가 아닌 사람이 카트 소유 해제를 요청했습니다.");
			return;
		}

		// currentHolderId를 빼기
		_currentHolderId.Value = Empty;

		NetworkObject.RemoveOwnership();
	}


	// 홀더를 향해 물리적으로 끌려가게 한다. 카트를 잡은 클라이언트가 소유권을 가지고 직접 시뮬레이션하며,
	// 그 결과는 NetworkTransform이 나머지 클라이언트로 복제한다.
	protected virtual void FixedUpdate()
	{
		if (_currentHolder == null || !IsOwner) { return; }

		Vector3 targetPosition = _currentHolder.transform.position + _currentHolder.transform.forward * _holdDistance;

		// 높이는 중력에 맡기고 수평 성분만 제어한다
		Vector3 toTarget = targetPosition - _rigidbody.position;
		toTarget.y = 0f;

		Vector3 horizontalVelocity = _rigidbody.linearVelocity;
		horizontalVelocity.y = 0f;

		// 위치 오차를 목표 속도로 바꾸고 그 속도에 맞춰가는 형태라, 급정지/코너링에서 관성이 남는다
		Vector3 desiredVelocity = toTarget * _followStiffness;
		_rigidbody.AddForce((desiredVelocity - horizontalVelocity) * _followDamping, ForceMode.Acceleration);

		float yawError = Mathf.DeltaAngle(_rigidbody.rotation.eulerAngles.y, _currentHolder.transform.eulerAngles.y);
		float desiredYawRate = yawError * Mathf.Deg2Rad * _yawStiffness;
		float yawRateError = desiredYawRate - _rigidbody.angularVelocity.y;
		_rigidbody.AddTorque(Vector3.up * (yawRateError * _yawDamping), ForceMode.Acceleration);
	}

	protected virtual void HandleHolderIdChanged(ulong oldId, ulong newId)
	{
		// oldId가 null이라면, 새로운 소유자가 생긴 것. 새로운 소유자를 등록한다.
		if (oldId == Empty)
		{
			_currentHolder = NetworkManager.Singleton.ConnectedClients[newId].PlayerObject.GetComponent<Player>();
			_currentHolder.PlayerInteraction.CarryingCart = this;

			// 카트가 잡은 사람 바로 앞에 배치되기 때문에 서로 계속 부딫혀 못 밀리는 걸 방지.
			Collider holderCollider = _currentHolder.GetComponent<Collider>();
			if (_collider != null && holderCollider != null)
			{
				Physics.IgnoreCollision(_collider, holderCollider, true);
			}
		}

		// newId가 null이라면 소유 해제한 것. 이미 있던 소유자 해제한다
		else if (newId == Empty)
		{
			// 접속 종료 등으로 홀더 오브젝트가 이미 파괴된 상태일 수 있으므로 확인 후 정리한다
			if (_currentHolder != null)
			{
				_currentHolder.PlayerInteraction.CarryingCart = null;

				Collider holderCollider = _currentHolder.GetComponent<Collider>();
				if (_collider != null && holderCollider != null)
				{
					Physics.IgnoreCollision(_collider, holderCollider, false);
				}
			}
			_currentHolder = null;
		}
	}
}