using UnityEngine;

public class Chair : InteractableBase {
	[Header("=== 의자에 앉았을 때 등장할 콘솔창 ===")]
	[SerializeField] private MonitorController _consoleUi;
	[Header("=== 의자에 앉았을 때 UI 꺼주기 위함 ===")]
	[SerializeField] private InventoryUI _inventoryUI;
	public override string InteractionText => "앉기";

	public override bool CanInteract => _occupiedPlayer == null;
	private PlayerMoveSample _occupiedPlayer = null;

	public override void Interact(GameObject interactor) {
		if (_occupiedPlayer != null) {
			return;
		}

		// 플레이어의 움직임 막기(콘솔 조작 동안)
		_occupiedPlayer = interactor.GetComponent<PlayerMoveSample>();
		
		GameplayUiMode.Instance.ActivateCursor();
		_inventoryUI.gameObject.SetActive(false);
		_consoleUi.gameObject.SetActive(true);
		_consoleUi.OnScreenClosed += UnOccupiedPlayer;
	}

	public void UnOccupiedPlayer() {
		_consoleUi.OnScreenClosed -= UnOccupiedPlayer;
		
		// 콘솔 조작 끝나면 다시 열어주기
		GameplayUiMode.Instance.DeactivateCursor();
		_inventoryUI.gameObject.SetActive(true);
		_occupiedPlayer = null;
	}
}
