using UnityEngine;

public class HqScreen : InteractableBase {
	[Header("=== 등장할 콘솔창 ===")]
	[SerializeField] private MonitorController _consoleUi;
	[Header("=== 상호작용 시에 꺼주기 위한 UI들 등록 ===")]
	[SerializeField] private InventoryUI _inventoryUI;
	[SerializeField] private MontageShareUI _montageUI;
	
	public override string InteractionText => "관제 콘솔 사용하기";

	// 역할 체크는 여기서 빼고 점유 여부만 본다. 본부요원이 아니어도 조준/상호작용 시도는 가능해야
	// GetInteractionText로 안내 문구를 보여줄 수 있다.
	public override bool CanInteract(GameObject interactor) {
		return _occupiedPlayer == null;
	}

	// 본부요원이 아니면 평소 문구 대신 역할 제한 안내를 보여준다.
	public override string GetInteractionText(GameObject interactor) {
		return IsHeadquarter(interactor) ? InteractionText : "본부요원만 사용할 수 있습니다";
	}

	// 본부요원이 아닐 때는 눌러도 콘솔이 열리지 않으므로 " : E" 키 힌트를 숨긴다.
	public override bool ShowInteractionKeyHint(GameObject interactor) {
		return IsHeadquarter(interactor);
	}

	private static bool IsHeadquarter(GameObject interactor) {
		return interactor.TryGetComponent<Player>(out var player) && player.PlayerRole == Role.Headquarter;
	}

	private PlayerMoveSample _occupiedPlayer = null;

	public override void Interact(GameObject interactor) {
		if (_occupiedPlayer != null || !IsHeadquarter(interactor)) {
			return;
		}

		// 플레이어의 움직임 막기(콘솔 조작 동안)
		_occupiedPlayer = interactor.GetComponent<PlayerMoveSample>();
		
		_inventoryUI.gameObject.SetActive(false);
		_montageUI.gameObject.SetActive(false);
		
		GameplayUiMode.Instance.ActivateCursor();
		
		_consoleUi.gameObject.SetActive(true);
		_consoleUi.OnScreenClosed += HandleTabClosed;
	}

	private void HandleTabClosed() {
		_consoleUi.OnScreenClosed -= HandleTabClosed;
		
		// 콘솔 조작 끝나면 다시 열어주기
		_inventoryUI.gameObject.SetActive(true);
		_montageUI.gameObject.SetActive(true);
		
		GameplayUiMode.Instance.DeactivateCursor();
		_occupiedPlayer = null;
	}
}
