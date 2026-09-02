using Unity.Netcode;
using UnityEngine;

public class PlayerItemIK : NetworkBehaviour, IHandIK {
	[Header("=== 왼손 아이템 드는 슬롯 ===")]
	[SerializeField] private Transform _leftHandParent;
	[Header("=== 왼손 목표 위치 ===")]
	[SerializeField] private Transform _leftHandTarget;
	[SerializeField, Range(0f, 1f)] private float _leftHandPositionWeight = 1f;
	[SerializeField, Range(0f, 1f)] private float _leftHandRotationWeight = 0.25f;

	[Header("=== 오른손 아이템 드는 슬롯 ===")]
	[SerializeField] private Transform _rightHandParent;
	[Header("=== 오른손 목표 위치 ===")]
	[SerializeField] private Transform _rightHandTarget;
	[SerializeField, Range(0f, 1f)] private float _rightHandPositionWeight = 1f;
	[SerializeField, Range(0f, 1f)] private float _rightHandRotationWeight = 1f;

	[Header("=== 손전등 오브젝트 ===")]
	[SerializeField] private Flashlight _flashLightPrefab;

	// 내 화면 전용. 뷰모델 왼손의 엄지-검지 사이를 가리키는 앵커다.
	[Header("=== 1인칭 손전등 그립 (뷰모델 왼손) ===")]
	[SerializeField] private Transform _firstPersonLeftHandAnchor;

	// 앵커 위치를 에디터에서 눈으로 잡기 위한 껍데기. 실제 손전등은 런타임에 스폰되므로 꺼 둔다.
	[Tooltip("앵커 자리를 눈으로 확인하기 위한 미리보기. 플레이하면 자동으로 꺼진다.")]
	[SerializeField] private GameObject _firstPersonFlashlightPreview;

	[Header("=== 머리 피벗(참고용) ===")]
	[SerializeField] private GameObject _headPivot;

	[SerializeField] private PlayerCameraController _playerCameraController;

	// 왼손에 실제로 스폰된 네트워크 아이템(손전등) 참조. 서버만 쓰고 전원이 읽는다.
	private readonly NetworkVariable<NetworkObjectReference> _leftHandItemRef =
		new(default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

	private Animator _animator;
	private CustomInputActions _actions;
	private PlayerInventory _inventory;
	private ItemBase _itemOnRightHand;
	private Flashlight _flashlight;
	private bool _useFirstPersonHands;

	public bool IsActive => true;

	private void Awake() {
		_animator = GetComponent<Animator>();
		_playerCameraController ??= GetComponent<PlayerCameraController>();
		_inventory = GetComponent<PlayerInventory>();

		if (_firstPersonFlashlightPreview != null) {
			_firstPersonFlashlightPreview.SetActive(false);
		}
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
		_inventory.OnInventoryChanged += RefreshRightHandItem;

		// 오버레이가 켜져 있는 동안만 뷰모델 손에 붙인다. 다운되어 3인칭으로 물러나면
		// 오버레이가 꺼지므로 캐릭터 손으로 돌려보내야 한다.
		//
		// 카메라 쪽은 자기 OnNetworkSpawn에서 이벤트를 한 번 쏘고 끝이라, 이 컴포넌트가 늦게
		// 스폰되면 그 한 번을 놓친다. 구독만 하지 말고 현재 상태를 직접 읽어 와야 한다.
		if (IsOwner && _playerCameraController != null) {
			_playerCameraController.FirstPersonHandsVisibilityChanged += HandleFirstPersonHandsChanged;
			_useFirstPersonHands = _playerCameraController.IsFirstPersonHandsActive;
		}

		// 스폰 시점에 이미 값이 채워져 있는 경우(뒤늦게 관전하는 클라이언트 등)를 대비해 한 번 직접 반영한다.
		ResolveLeftHand(_leftHandItemRef.Value);
		RefreshRightHandItem();

		// 왼손에는 항상 플래시라이트 있어야 함. 서버만 스폰한다.
		if (IsServer) {
			SpawnFlashlight();
		}
	}

	public override void OnNetworkDespawn() {
		_leftHandItemRef.OnValueChanged -= HandleLeftHandItemChanged;
		_inventory.OnInventoryChanged -= RefreshRightHandItem;

		if (_playerCameraController != null) {
			_playerCameraController.FirstPersonHandsVisibilityChanged -= HandleFirstPersonHandsChanged;
		}

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
			ApplyFlashlightHand();
		}
	}

	private bool UseFirstPersonFlashlight =>
		_useFirstPersonHands && IsOwner && _firstPersonLeftHandAnchor != null;

	private void ApplyFlashlightHand() {
		if (_flashlight == null) {
			return;
		}

		bool firstPerson = UseFirstPersonFlashlight;
		_flashlight.SetHandAnchor(firstPerson ? _firstPersonLeftHandAnchor : _leftHandParent, firstPerson);
		_flashlight.SetFirstPersonRendering(firstPerson);
	}

	private void HandleFirstPersonHandsChanged(bool visible) {
		_useFirstPersonHands = visible;
		ApplyFlashlightHand();
	}

	// 인벤토리 선택이 바뀔 때마다(전 클라이언트) 호출된다. 오른손에 들린 아이템을 현재 선택된 슬롯의
	// 아이템으로 맞춘다 - 새로 스폰하지 않고, 이미 존재하는 같은 인스턴스를 손으로 옮기기만 한다.
	private void RefreshRightHandItem() {
		_inventory.TryGetSelectedItemBase(out ItemBase item);

		if (item == _itemOnRightHand) {
			return;
		}

		_itemOnRightHand?.SetEquipped(null);
		_itemOnRightHand = item;
		_itemOnRightHand?.SetEquipped(_rightHandParent);
	}

	// 다른 걸 잡을 때(카트 잡을 때 등)에는 손에 있는 오브젝트 비활성화한다.
	public void DisableItems() {
		_flashlight?.SetVisible(false);
		_itemOnRightHand?.SetHandVisible(false);
	}

	public void ApplyIK(int layerIndex) {
		// 잡을 때 손에 있는 오브젝트 활성화
		_flashlight?.SetVisible(true);
		_itemOnRightHand?.SetHandVisible(true);

		// 왼손에 아이템 있으면, 왼손 위치 옮기기
		if (_leftHandItemRef.Value.TryGet(out NetworkObject _)) {
			_animator.SetIKPositionWeight(AvatarIKGoal.LeftHand, _leftHandPositionWeight);
			_animator.SetIKRotationWeight(AvatarIKGoal.LeftHand, _leftHandRotationWeight);
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
			_animator.SetIKPositionWeight(AvatarIKGoal.RightHand, _rightHandPositionWeight);
			_animator.SetIKRotationWeight(AvatarIKGoal.RightHand, _rightHandRotationWeight);
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
