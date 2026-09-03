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

	// 아이템은 _itemData 의 오프셋을 손 로컬 기준으로 쓰므로, 뷰모델 손 본을 그대로 넘기면
	// 캐릭터 손에 들렸을 때와 같은 자리에 붙는다. 따로 맞출 값이 없다.
	// 손 본과 같은 기준점이어야 한다. 아이템마다 쥐는 자리가 달라서 ItemData 의 오프셋을
	// 그대로 얹어 쓰고, 이 앵커는 손 전체를 한 번에 밀고 당기는 용도다.
	[Tooltip("뷰모델 오른손 아래의 FpItemAnchor. 손 본과 같은 자세로 두고, 아이템별 위치는 ItemData 에서 잡는다")]
	[SerializeField] private Transform _firstPersonRightHandAnchor;


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

	// 파괴된 UnityEngine.Object 는 C# 기준으로 null 이 아니라 ?. 를 그대로 통과한다.
	// 그대로 메서드를 부르면 transform 접근에서 MissingReferenceException 이 나는데,
	// 이게 NetworkList.ReadDelta 안에서 터지면 그 메시지의 남은 슬롯 변경이 통째로 버려진다.
	// 라운드가 끝나 아이템이 파괴된 뒤 재입장하면 인벤토리가 갱신되지 않던 원인이다.
	// 유니티가 오버로드한 == 로 한 번 걸러 낸 참조만 쓴다.
	private ItemBase AliveRightHandItem => _itemOnRightHand != null ? _itemOnRightHand : null;
	private Flashlight AliveFlashlight => _flashlight != null ? _flashlight : null;

	// 1인칭 뷰모델이 오른손을 그릴지 정할 때 쓴다. 빈손이면 화면만 가린다.
	public bool HasRightHandItem => _itemOnRightHand != null;

	// 지금 오른손에 들린 아이템.
	public ItemBase RightHandItem => _itemOnRightHand;

	// 제압기를 들어올릴 때 총만 크게 보이게 한다. 손 크기는 그대로 둔다.
	public void SetRightHandItemScale(float multiplier) {
		AliveRightHandItem?.SetHeldScaleMultiplier(multiplier);
	}

	private void Awake() {
		_animator = GetComponent<Animator>();
		_playerCameraController ??= GetComponent<PlayerCameraController>();
		_inventory = GetComponent<PlayerInventory>();
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
			AliveFlashlight?.ToggleOnOff();
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

	// 제압기를 쓰는 동안에도 손전등은 뷰모델 왼손에 그대로 둔다. 1인칭에서는 진짜 손이
	// 레이어 컬링으로 안 보이므로, 옮기면 손전등만 허공에 떠 보이고 옮기는 순간 툭 튄다.
	private bool UseFirstPersonFlashlight =>
		_useFirstPersonHands && IsOwner && _firstPersonLeftHandAnchor != null;

	private bool UseFirstPersonRightHand =>
		_useFirstPersonHands && IsOwner && _firstPersonRightHandAnchor != null;

	private void ApplyRightHandItem() {
		if (_itemOnRightHand == null) {
			return;
		}

		bool firstPerson = UseFirstPersonRightHand;
		_itemOnRightHand.SetEquipped(firstPerson ? _firstPersonRightHandAnchor : _rightHandParent);
		_itemOnRightHand.SetFirstPersonRendering(firstPerson);
	}

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
		ApplyRightHandItem();
	}

	// 인벤토리 선택이 바뀔 때마다(전 클라이언트) 호출된다. 오른손에 들린 아이템을 현재 선택된 슬롯의
	// 아이템으로 맞춘다 - 새로 스폰하지 않고, 이미 존재하는 같은 인스턴스를 손으로 옮기기만 한다.
	private void RefreshRightHandItem() {
		_inventory.TryGetSelectedItemBase(out ItemBase item);

		if (item == _itemOnRightHand) {
			return;
		}

		AliveRightHandItem?.SetEquipped(null);
		AliveRightHandItem?.SetFirstPersonRendering(false);
		_itemOnRightHand = item;
		ApplyRightHandItem();
	}

	// 숨겨 뒀던 것을 다시 보이게 한다. 조준 자세가 풀리는 동안 손이 비지 않게 하려고 쓴다.
	public void EnableItems() {
		AliveFlashlight?.SetVisible(true);
		AliveRightHandItem?.SetHandVisible(true);
	}

	// 다른 걸 잡을 때(카트 잡을 때 등)에는 손에 있는 오브젝트 비활성화한다.
	public void DisableItems() {
		DisableItems(hideFlashlight: true);
	}

	// 카트나 다운처럼 양손을 다 쓰는 상황에서는 손전등까지 내린다.
	// 제압기는 왼손이 손전등을 그대로 들고 있으므로 오른손 아이템만 내린다.
	public void DisableItems(bool hideFlashlight) {
		if (hideFlashlight) {
			AliveFlashlight?.SetVisible(false);
		}

		// 1인칭에서는 손에 든 아이템이 곧 화면에 보이는 총이다. 여기서 내리면 화면이 비고,
		// 진짜 총으로 갈아 끼우면 자리가 달라 툭 튄다. 내 화면에서는 그대로 들고 있는다.
		if (UseFirstPersonRightHand) {
			return;
		}

		AliveRightHandItem?.SetHandVisible(false);
	}

	public void ApplyIK(int layerIndex) {
		// 잡을 때 손에 있는 오브젝트 활성화
		AliveFlashlight?.SetVisible(true);
		AliveRightHandItem?.SetHandVisible(true);

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
