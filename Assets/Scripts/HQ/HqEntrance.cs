using UnityEngine;

public class HqEntrance : InteractableBase {
    [Header("=== 본부로 텔레포트할 시 도착할 위치 ===")]
    [SerializeField] private Transform _hqSpawnPoint;
	
    public override string InteractionText => "본부로 이동하기";
    public override bool CanInteract(GameObject interactor) => true;
	
    public override void Interact(GameObject interactor) {
        // 플레이어가 아닌 사람이 상호작용하면 제끼기
        if (!interactor.TryGetComponent<Player>(out var player)) { return; }
		
        // 플레이어가 상호작용했다면, 이동시켜주면 됨
        player.PlayerMove.TeleportToPosition(_hqSpawnPoint.position, _hqSpawnPoint.rotation);
    }
}
