using Unity.Netcode;
using UnityEngine;

public class PlayerItemIK : NetworkBehaviour, IHandIK {
	[Header("=== 왼손 아이템 드는 슬롯 ===")]
	[SerializeField] private Transform _leftHandParent;
	[Header("=== 왼손 목표 위치 ===")]
	[SerializeField] private Transform _leftHandTarget;

	[Header("=== 오른손 아이템 드는 슬롯 ===")]
	[SerializeField] private Transform _rightHandParent;
	[Header("=== 오른손 목표 위치 ===")]
	[SerializeField] private Transform _rightHandTarget;

	[Header("=== 손전등 오브젝트 ===")]
	[SerializeField] private Flashlight _flashLightPrefab;

	[Header("=== 머리 피벗(참고용) ===")]
	[SerializeField] private GameObject _headPivot;

	[SerializeField] private PlayerCameraController _playerCameraController;

	// 왼손에 실제로 스폰된 네트워크 아이템(손전등) 참조. 서버만 쓰고 전원이 읽는다.
	private readonly NetworkVariable<NetworkObjectReference> _leftHandItemRef =
		new(default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

	private Animator _animator;
	private CustomInputActions _actions;
	private GameObject _itemOnRightHand;
	private Flashlight _flashlight;

	public bool IsActive => true;

	private void Awake() {
		_animator = GetComponent<Animator>();
		_playerCameraController ??= GetComponent<PlayerCameraController>();
	}

	private void OnEnable() {
		_actions ??= new CustomInputActions();
		_actions.Enable();
	}

	private void OnDisable() {
		_actions?.Disable();
	}

	public override void OnNetworkSpawn() {
		_leftHandItemRef.OnValueChanged += HandleLeftHandItemChanged;

		// 스폰 시점에 이미 값이 채워져 있는 경우(뒤늦게 관전하는 클라이언트 등)를 대비해 한 번 직접 반영한다.
		ResolveLeftHand(_leftHandItemRef.Value);

		// 왼손에는 항상 플래시라이트 있어야 함. 서버만 스폰한다.
		if (IsServer) {
			SpawnFlashlight();
		}
	}

	public override void OnNetworkDespawn() {
		_leftHandItemRef.OnValueChanged -= HandleLeftHandItemChanged;

		if (IsServer && _leftHandItemRef.Value.TryGet(out NetworkObject networkObject)) {
			networkObject.Despawn(true);
		}
	}

	private void Update() {
		// 손전등 NetworkObject가 PlayerItemIK보다 늦게 동기화되는 클라이언트가 있어서(도착 순서 경쟁),
		// 한 번 실패해도 resolve될 때까지 계속 재시도한다.
		if (_flashlight == null) {
			ResolveLeftHand(_leftHandItemRef.Value);
		}

		if (!IsOwner) {
			return;
		}

		if (_actions.Player.Flashlight.WasPressedThisFrame()) {
			_flashlight?.ToggleOnOff();
		}
	}

	private void SpawnFlashlight() {
		Flashlight instance = Instantiate(_flashLightPrefab, _leftHandParent.position, _leftHandParent.rotation);
		NetworkObject networkObject = instance.GetComponent<NetworkObject>();
		networkObject.SpawnWithOwnership(OwnerClientId, destroyWithScene: true); 
		// 플레이어 자신의 NetworkObject로 파렌팅하고, 정확한 위치는 Flashlight가 매 프레임 로컬로 따라간다.
		networkObject.TrySetParent(NetworkObject, worldPositionStays: true);

		_leftHandItemRef.Value = networkObject;
		
		// OnValueChanged 콜백에 암묵적으로 기대지 않고, 스폰한 직후 바로 명시적으로 반영한다.
		ResolveLeftHand(networkObject);
	}

	private void HandleLeftHandItemChanged(NetworkObjectReference previousValue, NetworkObjectReference newValue) {
		ResolveLeftHand(newValue);
	}

	private void ResolveLeftHand(NetworkObjectReference reference) {
		if (reference.TryGet(out NetworkObject networkObject) && networkObject.TryGetComponent(out _flashlight)) {
			_flashlight.Initialize(_playerCameraController, _leftHandParent);
		}
	}

	// 다른 걸 잡을 때(카트 잡을 때 등)에는 손에 있는 오브젝트 비활성화한다.
	public void DisableItems() {
		_flashlight?.SetVisible(false);
		_itemOnRightHand?.SetActive(false);
	}

	public void ApplyIK(int layerIndex) {
		// 잡을 때 손에 있는 오브젝트 활성화
		_flashlight?.SetVisible(true);
		_itemOnRightHand?.SetActive(true);

		// 왼손에 아이템 있으면, 왼손 위치 옮기기
		if (_leftHandItemRef.Value.TryGet(out NetworkObject _)) {
			_animator.SetIKPositionWeight(AvatarIKGoal.LeftHand, 1f);
			_animator.SetIKRotationWeight(AvatarIKGoal.LeftHand, 1f);
			_animator.SetIKPosition(AvatarIKGoal.LeftHand, _leftHandTarget.position);
			_animator.SetIKRotation(AvatarIKGoal.LeftHand, _leftHandTarget.rotation);
		}
		// 왼손 아이템 없으면, 왼손 IK 해제
		else {
			_animator.SetIKPositionWeight(AvatarIKGoal.LeftHand, 0f);
			_animator.SetIKRotationWeight(AvatarIKGoal.LeftHand, 0f);
		}

		// 오른손에 아이템 있으면, 오른손 위치 옮기기
		if (_itemOnRightHand != null) {
			_animator.SetIKPositionWeight(AvatarIKGoal.RightHand, 1f);
			_animator.SetIKRotationWeight(AvatarIKGoal.RightHand, 1f);
			_animator.SetIKPosition(AvatarIKGoal.RightHand, _rightHandTarget.position);
			_animator.SetIKRotation(AvatarIKGoal.RightHand, _rightHandTarget.rotation);
		}
		// 오른손 아이템 없으면, 오른손 IK 해제
		else {
			_animator.SetIKPositionWeight(AvatarIKGoal.RightHand, 0f);
			_animator.SetIKRotationWeight(AvatarIKGoal.RightHand, 0f);
		}
	}
}
