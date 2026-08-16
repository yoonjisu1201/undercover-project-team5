using UnityEngine;

public class PlayerItemIK : HandIKBase {
	[Header("=== 왼손 아이템 드는 슬롯 ===")]
	[SerializeField] private Transform _leftHandParent;
	[Header("=== 왼손 목표 위치 ===")]
	[SerializeField] private Transform _leftHandTarget;

	[Header("=== 오른손 아이템 드는 슬롯 ===")]
	[SerializeField] private Transform _rightHandParent;
	[Header("=== 오른손 목표 위치 ===")]
	[SerializeField] private Transform _rightHandTarget;

	[Header("=== 손전등 오브젝트 ===")]
	[SerializeField] private GameObject _flashLightPrefab;

	[Header("=== 손전등 꺼질 때 같이 끌 Light오브젝트 ===")] 
	[SerializeField] private Light _lightInCamera;

	[Header("=== 머리 피벗(참고용) ===")]
	[SerializeField] private GameObject _headPivot;

	private GameObject _itemOnLeftHand;
	private GameObject _itemOnRightHand;

	// 원격 손전등 광원을 네트워크로 동기화된 플레이어 시야 방향에 맞추기 위해 사용한다.
	private PlayerCameraController _playerCameraController;

	// 손전등 손 모델을 처음 만들 때 찾은 자식 광원들과 프리팹에 저장된 각 광원의 기본 활성 상태다.
	// 두 배열은 같은 인덱스를 사용하며, 장식용 광원은 기본 상태를 유지하고 Spot Light는 원격 표현용으로 별도 제어한다.
	private Light[] _flashLightSources;
	private bool[] _flashLightSourceDefaultStates;

	// 장착 여부는 선택 슬롯 상태이고, 점등 여부는 FlashlightItem에서 동기화된 On/Off 상태다.
	// 두 상태를 분리해야 전원이 켜져 있어도 선택 해제나 카트 사용 중에는 표시만 숨길 수 있다.
	private bool _isFlashLightEquipped;
	private bool _isFlashLightOn;

	// 왼손 손전등을 선택했거나 기존 오른손 아이템이 있을 때만 HandIKBase가 IK 적용을 요청한다.
	public override bool IsActive => _isFlashLightEquipped || _itemOnRightHand != null;

	// 인벤토리에서 손전등을 선택하기 전에는 장착 표시가 없으므로 카메라 광원을 꺼 둔다.
	protected override void Awake() {
		base.Awake();

		// 같은 플레이어 오브젝트의 동기화된 시야각을 사용한다.
		_playerCameraController = GetComponent<PlayerCameraController>();

		_lightInCamera.enabled = false;
	}

	// Animator의 손 IK가 장착 모델 회전을 확정한 뒤 원격 Spot Light만 실제 시야 방향으로 다시 맞춘다.
	private void LateUpdate() {
		// 소유자는 카메라 광원을 사용하고, 최초 장착 전에는 보정할 손 모델 광원이 없다.
		if (_lightInCamera.gameObject.activeInHierarchy || _flashLightSources == null) {
			return;
		}

		// 플레이어의 동기화된 시야각과 몸의 Y축 방향으로 원격 광원의 최종 진행 방향을 계산한다.
		Vector3 lightDirection =
			Quaternion.AngleAxis(_playerCameraController.ViewPitch, transform.right) * transform.forward;

		for (int i = 0; i < _flashLightSources.Length; i++) {
			Light lightSource = _flashLightSources[i];

			// 현재 켜진 Spot Light만 보정해 꺼졌거나 장식용인 광원 상태는 변경하지 않는다.
			if (lightSource.type != LightType.Spot || !lightSource.enabled) {
				continue;
			}

			lightSource.transform.rotation = Quaternion.LookRotation(lightDirection, transform.up);
		}
	}

	// FlashlightItem이 선택될 때 호출해 손 모델을 준비하고 동기화된 점등 상태로 장착한다.
	// 손 모델은 최초 장착 때 한 번만 생성하고 이후 선택에서는 같은 인스턴스를 재사용한다.
	public void EquipFlashlight(bool isOn) {
		// 아직 손 모델이 없다는 것은 이 플레이어에서 처음 장착했다는 뜻이다.
		if (_itemOnLeftHand == null) {
			// 네트워크 아이템과 별개인 표현용 모델을 왼손 슬롯 아래에 생성한다.
			_itemOnLeftHand = Instantiate(_flashLightPrefab, _leftHandParent);

			// 비활성 자식까지 포함해야 프리팹에서 꺼 둔 광원의 원래 상태도 기록할 수 있다.
			_flashLightSources = _itemOnLeftHand.GetComponentsInChildren<Light>(true);
			_flashLightSourceDefaultStates = new bool[_flashLightSources.Length];

			for (int i = 0; i < _flashLightSources.Length; i++) {
				// 같은 인덱스에 각 광원의 프리팹 기본 활성 상태를 보관한다.
				_flashLightSourceDefaultStates[i] = _flashLightSources[i].enabled;
			}
		}

		// 선택 슬롯에 손전등이 있음을 먼저 기록해 모델 표시와 왼손 IK를 활성화한다.
		_isFlashLightEquipped = true;

		// 아이템 인스턴스가 보존한 점등 값을 받아 드롭·재획득 뒤에도 같은 상태를 복원한다.
		_isFlashLightOn = isOn;

		// 장착 모델은 보이게 하되 실제 광원은 점등 상태와 함께 계산한다.
		SetFlashlightVisible(true);
	}

	// 선택 슬롯에서 벗어나면 장착 상태를 해제하고 손 모델과 광원을 숨긴다.
	// 모델은 파괴하지 않아 같은 플레이어가 다시 선택할 때 재사용한다.
	public void UnequipFlashlight() {
		_isFlashLightEquipped = false;
		SetFlashlightVisible(false);
	}

	// FlashlightItem의 동기화된 On/Off 값이 바뀌면 보존하고 현재 보이는 광원에 반영한다.
	public void SetFlashlightOn(bool isOn) {
		// 모델이 임시로 숨겨진 동안에도 값을 저장해야 다시 표시할 때 최신 상태가 복원된다.
		_isFlashLightOn = isOn;

		// 카트 사용 등으로 모델이 임시 비활성화된 동안에는 점등 상태여도 실제 빛을 켜지 않는다.
		bool isVisible = _isFlashLightEquipped && _itemOnLeftHand != null && _itemOnLeftHand.activeSelf;

		SetFlashlightLights(isVisible && _isFlashLightOn);
	}

	// 다른 걸 잡을 때(카트 잡을 때 등)에는 손에 있는 오브젝트 비활성화한다.
	public void DisableItems() {
		// 장착 여부와 점등 상태는 유지하고 모델과 광원만 임시로 숨겨 이후 IK 적용 때 복원할 수 있게 한다.
		SetFlashlightVisible(false);
		_itemOnRightHand?.SetActive(false);
	}

	public override void ApplyIK(int layerIndex) {
		// 잡을 때 손에 있는 오브젝트 활성화
		// 손전등이 선택된 경우에만 모델을 복원하며 광원은 저장된 점등 상태까지 함께 확인한다.
		SetFlashlightVisible(_isFlashLightEquipped);
		_itemOnRightHand?.SetActive(true);

		// 왼손에 아이템 있으면, 왼손 위치 옮기기
		// 표현용 모델이 생성돼 있어도 현재 슬롯에서 손전등을 선택한 경우에만 왼손 IK를 적용한다.
		if (_isFlashLightEquipped && _itemOnLeftHand != null) {
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

	// 손전등 모델 표시와 실제 광원을 한 경로에서 갱신해 서로 다른 상태로 남지 않게 한다.
	private void SetFlashlightVisible(bool isVisible) {
		// 아직 최초 장착 전이면 모델이 없으므로 조건부로 활성 상태만 변경한다.
		_itemOnLeftHand?.SetActive(isVisible);

		// 모델이 보이고 아이템의 점등 상태도 On일 때만 빛을 활성화한다.
		SetFlashlightLights(isVisible && _isFlashLightOn);
	}

	// 소유자는 1인칭 카메라 광원을 사용하고, 원격 표현은 손 모델의 Spot Light로 같은 점등 상태를 표시한다.
	private void SetFlashlightLights(bool isEnabled) {
		// 소유자의 활성 카메라에서는 시선 방향을 비추는 카메라 광원이 실제 빛을 담당한다.
		_lightInCamera.enabled = isEnabled;

		// 최초 장착 전에는 자식 광원 배열이 없으므로 카메라 광원 처리만 하고 끝낸다.
		if (_flashLightSources == null) {
			return;
		}

		// 원격 플레이어의 카메라 오브젝트는 비활성화되므로 손 모델의 Spot Light가 실제 빛을 담당한다.
		bool useHandSpotlight = !_lightInCamera.gameObject.activeInHierarchy;

		for (int i = 0; i < _flashLightSources.Length; i++) {
			Light lightSource = _flashLightSources[i];
			bool isHandSpotlight = lightSource.type == LightType.Spot;

			// Spot Light는 원격 표현에서만 켜 중복 조명을 막고, 장식용 광원은 프리팹 기본 상태를 유지한다.
			bool shouldEnableSource = isHandSpotlight
				? useHandSpotlight
				: _flashLightSourceDefaultStates[i];

			lightSource.enabled = isEnabled && shouldEnableSource;
		}
	}
}
