using UnityEngine;

public class HqScreen : InteractableBase {
	[Header("=== 등장할 콘솔창 ===")]
	[SerializeField] private MonitorController _consoleUi;
	[Header("=== 상호작용 시에 꺼주기 위한 UI들 등록 ===")]
	[SerializeField] private InventoryUI _inventoryUI;
	[SerializeField] private MontageShareUI _montageUI;
	
	public override string InteractionText => "관제 콘솔 사용하기";

	public override bool CanInteract(GameObject interactor) {
		return _occupiedPlayer == null // 1. 이미 상호작용중인 사람이 있는가?
		       && interactor.TryGetComponent<Player>(out var player) // 2. 플레이어가 상호작용중인가? 
		       && player.PlayerRole == Role.Headquarter; // 3. 상호작용하려는 사람이 본부요원인가?
	}

	private PlayerMoveSample _occupiedPlayer = null;

	public override void Interact(GameObject interactor) {
		if (_occupiedPlayer != null) {
			return;
		}

		// 플레이어의 움직임 막기(콘솔 조작 동안)
		_occupiedPlayer = interactor.GetComponent<PlayerMoveSample>();
		
		_inventoryUI.gameObject.SetActive(false);
		_montageUI.gameObject.SetActive(false);
		
		GameplayUiMode.Instance.ActivateCursor();
		
		_consoleUi.gameObject.SetActive(true);
		_consoleUi.OnScreenClosed += UnOccupiedPlayer;
	}

	private void UnOccupiedPlayer() {
		_consoleUi.OnScreenClosed -= UnOccupiedPlayer;
		
		// 콘솔 조작 끝나면 다시 열어주기
		_inventoryUI.gameObject.SetActive(true);
		_montageUI.gameObject.SetActive(true);
		
		GameplayUiMode.Instance.DeactivateCursor();
		_occupiedPlayer = null;
	}
}
