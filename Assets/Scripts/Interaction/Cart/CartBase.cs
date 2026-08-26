using Unity.Netcode;
using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public abstract class CartBase : InteractableBase
{
	private const ulong Empty = ulong.MaxValue;

	[SerializeField] private float _holdDistance = 2f;
	[SerializeField] private float _wallCheckPadding = 0.05f; // 벽 앞에서 멈출 때 남겨두는 여유 거리
	[SerializeField] private float _maxStepHeight = 0.3f;      // 이 높이 이하 턱은 벽이 아니라 단차로 취급해 타고 올라간다
	[SerializeField] private float _groundClearance = 0.1f;    // 카트 바닥 판정을 띄워서 바닥이 단차로 취급되는 문제 수정

	private Rigidbody _rigidbody;
	private Collider _collider;

	// 라운드 전환 시 이 위치(StartPoint 기준 로컬 오프셋)로 카트를 되돌린다.
	private Vector3 _spawnLocalPosition;
	private Quaternion _spawnLocalRotation;

	// 현재 이 카트 잡고있는사람의 ID(네트워크 직렬화용 ID)
	private readonly NetworkVariable<ulong> _currentHolderId = new NetworkVariable<ulong>(Empty);
	private Player _currentHolder;
	private Rigidbody _holderRigidbody; // 스윕 검사에서 홀더 자신을 걸러내기 위한 캐시

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

		_spawnLocalPosition = transform.localPosition;
		_spawnLocalRotation = transform.localRotation;
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

	public override string InteractionText => LocalizeInteractionText("interact_pull_cart");

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

	// 서버가 라운드 전환 시점에 카트 홀더를 강제로 해제하고 스폰 위치로 되돌린다.
	// NpcTracker.ResetForNewRound()와 같은 목적, 같은 호출 시점(RoundManager)을 따른다.
	// 그렇지 않으면 이전 라운드에서 끌려다닌 로컬 오프셋이 그대로 남아, StartPoint가 새 구역으로
	// 옮겨갈 때 그 오프셋만큼 엉뚱한 위치로 나타나게 된다.
	public virtual void ResetForNewRound()
	{
		if (!IsServer) { return; }

		if (IsHolderExists)
		{
			_currentHolderId.Value = Empty;
			NetworkObject.RemoveOwnership();
		}

		transform.SetLocalPositionAndRotation(_spawnLocalPosition, _spawnLocalRotation);
		_rigidbody.position = transform.position;
		_rigidbody.rotation = transform.rotation;
		_rigidbody.linearVelocity = Vector3.zero;
		_rigidbody.angularVelocity = Vector3.zero;
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

		// 카트를 잡은 클라이언트가 위치 계산 책임자가 되도록 소유권을 넘긴다.
		// 이 클라이언트의 FixedUpdate 계산 결과가 NetworkTransform으로 나머지에게 복제된다.
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

	
	// 카트를 잡은 클라이언트(소유권을 가진 쪽)만 계산하고, 그 결과는 NetworkTransform이 나머지 클라이언트에게 동기화한다.
	protected virtual void FixedUpdate()
	{
		if (_currentHolder == null || !IsOwner) { return; }

		// 이번 업데이트에서 도달해야 할 포지션
		Vector3 targetPosition = _currentHolder.transform.position + _currentHolder.transform.forward * _holdDistance;
		
		// y좌표는 자체적으로 가지게 하기
		targetPosition.y = transform.position.y;
		
		// 현재 내 위치와 포지션까지의 거리 계산
		Vector3 movement = targetPosition - transform.position;
		float distance = movement.magnitude;
		float stepUp = 0f;

		if (distance > 0.0001f)
		{
			Vector3 direction = movement / distance;

			// 어떤 방향으로 이번에 가려고 하는 만큼(distance) 갈 때 벽에 막히는지 확인하기
			if (TryGetWallDistance(direction, distance, out float wallDistance))
			{
				// 발밑 높이에서는 막혔더라도, _maxStepHeight만큼 위에서는 안 막힌다면
				// 벽이 아니라 오를 수 있는 낮은 단차로 보고 수평 이동을 그대로 허용한다.
				if (IsStepClimbable(direction, distance)) {
					stepUp = _maxStepHeight;
				}
				else {
					distance = Mathf.Max(0f, wallDistance - _wallCheckPadding);
				}
			}
			movement = direction * distance;
		}

		Quaternion targetRotation = Quaternion.LookRotation(_currentHolder.transform.forward, Vector3.up);

		Vector3 nextPosition = transform.position + movement;
		// 단차를 타고 오르는 경우, 콜라이더 바닥이 턱 위로 올라서도록 살짝 들어 올린다.
		// 실제 표면 높이는 이후 중력으로 자연스럽게 맞춰진다.
		nextPosition.y += stepUp;

		_rigidbody.MovePosition(nextPosition);
		_rigidbody.MoveRotation(targetRotation);
	}

	// 스윕에 쓸 half-extents와 원점(둘 다 월드 기준)을 계산한다.
	// 박스 바닥을 바닥면보다 _groundClearance만큼 띄운다.
	// 이래야 바닥 자체가 벽으로 걸리는 걸 방지할 수 있음(안 그러면 계속 걸렸다 풀렸다 하면서단차 로직이 오작동해 카트가 들썩거리게 된다)
	private bool TryGetSweepBox(out Vector3 halfExtents, out Vector3 origin)
	{
		halfExtents = Vector3.zero;
		origin = Vector3.zero;

		if (_collider is not BoxCollider box) { return false; }

		// box.size/center는 로컬 좌표 기준이라, 월드 스케일/위치로 변환해줘야 실제 크기의 박스로 검사된다.
		halfExtents = Vector3.Scale(box.size, transform.lossyScale) * 0.5f;

		float clearance = Mathf.Min(_groundClearance, halfExtents.y * 0.9f);
		halfExtents.y -= clearance;
		origin = transform.TransformPoint(box.center) + Vector3.up * clearance;
		return true;
	}

	// hit이 홀더 자신(또는 홀더 몸에 붙은 콜라이더)인지 확인한다.
	// 홀더는 카트 바로 앞에 붙어 있는 상태라, 이걸 벽으로 오인하면 카트가 홀더에 막혀서 못 움직이게 된다.
	private bool IsHolderHit(RaycastHit hit)
	{
		return _holderRigidbody != null && hit.rigidbody == _holderRigidbody;
	}

	// hit이 이 카트 자신에게 붙은 콜라이더인지 확인한다(루트 콜라이더뿐 아니라 ItemLandingBlocker 같은
	// 자식 콜라이더도 포함). 그렇지 않으면 카트 자신의 자식 콜라이더를 벽으로 오인해 못 움직이게 된다.
	private bool IsOwnCollider(Collider collider)
	{
		return collider.GetComponentInParent<CartBase>() == this;
	}

	// direction으로 distance만큼 이동할 때, 발밑 높이가 아니라 _maxStepHeight만큼 위에서 스윕해도
	// 여전히 막히는지 확인한다. 위쪽이 뚫려 있으면 발밑에 걸린 건 벽이 아니라 낮은 단차라는 뜻이다.
	private bool IsStepClimbable(Vector3 direction, float distance)
	{
		if (!TryGetSweepBox(out Vector3 halfExtents, out Vector3 baseOrigin)) { return false; }

		Vector3 origin = baseOrigin + Vector3.up * _maxStepHeight;

		RaycastHit[] hits = Physics.BoxCastAll(
			origin, halfExtents, direction, transform.rotation, distance,
			~0, QueryTriggerInteraction.Ignore);

		foreach (RaycastHit hit in hits)
		{
			if (IsOwnCollider(hit.collider)) { continue; }
			if (IsHolderHit(hit)) { continue; }

			// 위쪽에서도 뭔가에 걸리면, 진짜 벽(또는 너무 높은 턱)이라 오를 수 없다.
			return false;
		}

		return true;
	}

	// 카트 콜라이더 모양으로 이동 경로를 스윕 검사해서, 벽 등에 막히면 그 지점까지의 거리를 반환한다.
	// 홀더 자신의 콜라이더는 무시한다(항상 근접해 있으므로).
	private bool TryGetWallDistance(Vector3 direction, float maxDistance, out float distance)
	{
		// 아무것도 안 막으면 원래 가려던 거리(maxDistance) 그대로 이동한다.
		distance = maxDistance;

		if (!TryGetSweepBox(out Vector3 halfExtents, out Vector3 origin)) { return false; }

		// 카트의 박스를 현재 회전 그대로 유지한 채, direction 방향으로 maxDistance만큼 밀어보며
		// 경로 위에 걸리는 모든 콜라이더를 가져온다. Ray가 아니라 박스 전체로 훑기 때문에
		// 카트 모서리가 벽에 스치는 것도 잡아낼 수 있다.
		// QueryTriggerInteraction.Ignore로 트리거 콜라이더(체력 회복 영역 등)는 애초에 제외한다.
		RaycastHit[] hits = Physics.BoxCastAll(
			origin, halfExtents, direction, transform.rotation, maxDistance,
			~0, QueryTriggerInteraction.Ignore);

		// 여러 개가 걸릴 수 있으므로, 그중 가장 가까운(=가장 먼저 막히는) 지점을 찾는다.
		bool found = false;
		foreach (RaycastHit hit in hits)
		{
			// 카트 자기 자신의 콜라이더(ItemLandingBlocker 등 자식 콜라이더 포함)는 당연히 스윕 결과에 걸리므로 제외한다.
			if (IsOwnCollider(hit.collider)) { continue; }
			if (IsHolderHit(hit)) { continue; }

			// 지금까지 찾은 것보다 더 가까이서 막혔다면, 그 지점을 새로운 정지 지점으로 갱신한다.
			if (hit.distance < distance)
			{
				distance = hit.distance;
				found = true;
			}
		}

		// found가 false면 막힌 게 없다는 뜻이라, 호출한 쪽에서 원래 거리(maxDistance) 그대로 써도 된다.
		return found;
	}

	protected virtual void HandleHolderIdChanged(ulong oldId, ulong newId)
	{
		// oldId가 null이라면, 새로운 소유자가 생긴 것. 새로운 소유자를 등록한다.
		if (oldId == Empty)
		{
			_currentHolder = NetworkManager.Singleton.ConnectedClients[newId].PlayerObject.GetComponent<Player>();
			_currentHolder.PlayerInteraction.CarryingCart = this;
			_currentHolder.PlayerInventory.SetCartCarrying(true);
			_holderRigidbody = _currentHolder.GetComponent<Rigidbody>();

			// 카트가 잡은 사람 바로 앞에 배치되기 때문에 서로 계속 부딫혀 못 밀리는 걸 방지.
			Collider holderCollider = _currentHolder.GetComponent<Collider>();
			if (_collider != null && holderCollider != null)
			{
				Physics.IgnoreCollision(_collider, holderCollider, true);
			}

			// 잡았을 때 Kinematic 꺼주기. 중력/충돌로 Y가 단차·경사에 맞춰지게 하기 위함.
			_rigidbody.isKinematic = false;
		}

		// newId가 null이라면 소유 해제한 것. 이미 있던 소유자 해제한다
		else if (newId == Empty)
		{
			// 접속 종료 등으로 홀더 오브젝트가 이미 파괴된 상태일 수 있으므로 확인 후 정리한다
			if (_currentHolder != null)
			{
				_currentHolder.PlayerInteraction.CarryingCart = null;
				_currentHolder.PlayerInventory.SetCartCarrying(false);

				Collider holderCollider = _currentHolder.GetComponent<Collider>();
				if (_collider != null && holderCollider != null)
				{
					Physics.IgnoreCollision(_collider, holderCollider, false);
				}
			}
			_currentHolder = null;
			_holderRigidbody = null;

			// 놓았을 때 Kinematic 다시 켜주기.
			_rigidbody.isKinematic = true;
		}
	}
}