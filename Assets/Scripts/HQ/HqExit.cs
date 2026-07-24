using UnityEngine;

public class HqExit : InteractableBase {
	[Header("=== 현장으로 텔레포트할 시 도착할 위치 ===")]
	[SerializeField] private Transform _fieldSpawnPoint;
	
	public override string InteractionText => "현장으로 이동하기";
	public override bool CanInteract(GameObject interactor) => true;
	
	public override void Interact(GameObject interactor) {
		// 플레이어가 아닌 사람이 상호작용하면 제끼기
		if (!interactor.TryGetComponent<Player>(out var player)) { return; }
		
		// 플레이어가 상호작용했다면, 이동시켜주면 됨
		player.PlayerMove.TeleportToPosition(_fieldSpawnPoint.position, _fieldSpawnPoint.rotation);
	}
}
