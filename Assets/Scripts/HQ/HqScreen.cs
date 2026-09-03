using UnityEngine;

public class HqScreen : InteractableBase {
	[Header("=== 등장할 콘솔창 ===")]
	[SerializeField] private HqScreenController _consoleUi;
	[Header("=== 상호작용 시에 꺼주기 위한 UI들 등록 ===")]
	[SerializeField] private InventoryUI _inventoryUI;
	[SerializeField] private MontageShareUI _montageUI;

	// 역할 체크는 여기서 빼고 점유 여부만 본다.
	// GetInteractionText로 안내 문구를 보여줄 수 있다.
	public override bool CanInteract(GameObject interactor) {
		return _occupiedPlayer == null;
	}

	public override string GetInteractionText(GameObject interactor) {
		return InteractionText;
	}

	private PlayerMoveSample _occupiedPlayer = null;

	// 지금 보고 있는 화면을 관전자에게 알리는 데 쓴다.
	private Player _occupiedPlayerInfo;
	private HqConsoleState _consoleState;
	private CCTVHub _cctvHub;

	public override void Interact(GameObject interactor) {
		if (_occupiedPlayer != null) {
			return;
		}

		// 플레이어의 움직임 막기(콘솔 조작 동안)
		_occupiedPlayer = interactor.GetComponent<PlayerMoveSample>();
		_occupiedPlayerInfo = interactor.GetComponent<Player>();

		_inventoryUI.gameObject.SetActive(false);
		_montageUI.gameObject.SetActive(false);

		GameplayUiMode.Instance.ActivateCursor();

		_consoleUi.gameObject.SetActive(true);

		// 관전 미러가 조작을 막아 두었을 수 있다. 콘솔 UI는 씬에 하나뿐이라 열 때마다 되돌린다.
		_consoleUi.SetInteractable(true);

		_consoleUi.OnScreenClosed += HandleTabClosed;

		SubscribeScreens();
		CaptureConsoleState();
		PushConsoleState();
	}

	// 대상 화면을 따라가려면 탭뿐 아니라 탭 안쪽 상태까지 알아야 한다.
	// CCTV 카메라는 콘솔이 gitignore 대상 프리팹에 있어 인스펙터 참조를 쓸 수 없어 씬에서 찾는다.
	private void SubscribeScreens() {
		_consoleUi.TabChanged += HandleTabChanged;

		_cctvHub = FindFirstObjectByType<CCTVHub>();
		if (_cctvHub != null) {
			_cctvHub.OnCctvNumberChanged += HandleCctvNumberChanged;
		}

		if (_consoleUi.Minimap != null) {
			_consoleUi.Minimap.ModeChanged += HandleMinimapModeChanged;
		}

		if (_consoleUi.Montage != null) {
			_consoleUi.Montage.ActivePartChanged += HandleMontagePartChanged;
		}
	}

	private void UnsubscribeScreens() {
		_consoleUi.TabChanged -= HandleTabChanged;

		if (_cctvHub != null) {
			_cctvHub.OnCctvNumberChanged -= HandleCctvNumberChanged;
			_cctvHub = null;
		}

		if (_consoleUi.Minimap != null) {
			_consoleUi.Minimap.ModeChanged -= HandleMinimapModeChanged;
		}

		if (_consoleUi.Montage != null) {
			_consoleUi.Montage.ActivePartChanged -= HandleMontagePartChanged;
		}
	}

	// 열자마자 지금 상태를 한 번 담는다. 이후로는 바뀐 항목만 고쳐 올린다.
	private void CaptureConsoleState() {
		_consoleState.Tab = (sbyte)_consoleUi.CurrentTab;
		_consoleState.CctvCamera = _cctvHub != null ? (byte)_cctvHub.UsingCctvNumber : (byte)0;
		_consoleState.MinimapMode = _consoleUi.Minimap != null ? (byte)_consoleUi.Minimap.Mode : (byte)0;
		_consoleState.MontagePart = _consoleUi.Montage != null ? (byte)_consoleUi.Montage.ActivePart : (byte)0;
	}

	private void PushConsoleState() {
		_occupiedPlayerInfo?.SetHqConsole(_consoleState);
	}

	private void HandleTabChanged(HqScreenController.ConsoleTab tab) {
		_consoleState.Tab = (sbyte)tab;
		PushConsoleState();
	}

	private void HandleCctvNumberChanged(int cameraIndex) {
		_consoleState.CctvCamera = (byte)cameraIndex;
		PushConsoleState();
	}

	private void HandleMinimapModeChanged(MinimapScreenController.MinimapMode mode) {
		_consoleState.MinimapMode = (byte)mode;
		PushConsoleState();
	}

	private void HandleMontagePartChanged(ClothPart part) {
		_consoleState.MontagePart = (byte)part;
		PushConsoleState();
	}

	private void HandleTabClosed() {
		_consoleUi.OnScreenClosed -= HandleTabClosed;
		UnsubscribeScreens();

		_consoleState = HqConsoleState.Closed;
		PushConsoleState();
		_occupiedPlayerInfo = null;

		// 콘솔 조작 끝나면 다시 열어주기
		_inventoryUI.gameObject.SetActive(true);
		_montageUI.gameObject.SetActive(true);

		GameplayUiMode.Instance.DeactivateCursor();
		_occupiedPlayer = null;
	}
}
