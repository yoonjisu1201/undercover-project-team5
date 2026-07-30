using UnityEngine;

public class SubwayMapObject : InteractableBase {

	[Header("=== 지하철 패널과 상호작용했을 떄 숨길 UI들 ===")]
	[SerializeField] private GameObject[] _UIsToHide;

	[Header("=== 실제로 보일 지하철 패널 ===")]
	[SerializeField] private SubwayMapUI _subwayInfoPanel;
	
	public override string InteractionText => $"지하철 노선도 확인";
	
	public override bool CanInteract(GameObject interactor) {
		return interactor.TryGetComponent(out Player player) && player.PlayerRole == Role.Headquarter;
	}
	
	public override void Interact(GameObject interactor) {
		// 숨겨야 할 UI들 모두 숨기기
		foreach (var obj in _UIsToHide) { obj.SetActive(false);}
		// 커서 활성화
		GameplayUiMode.Instance.ActivateCursor();
		
		// 지하철 패널 띄우기
		_subwayInfoPanel.gameObject.SetActive(true);
		// 추후 지하철 패널 꺼질 때 필요한 동작 수행하도록 추가
		_subwayInfoPanel.OnWindowClosed += HandleWindowClosed;
	}

	private void HandleWindowClosed() {
		_subwayInfoPanel.OnWindowClosed -= HandleWindowClosed;
		
		// 숨긴 UI들 다시 보이기
		foreach (var obj in _UIsToHide) { obj.SetActive(true);}
		// 커서 비활성화
		GameplayUiMode.Instance.DeactivateCursor();
	}
}