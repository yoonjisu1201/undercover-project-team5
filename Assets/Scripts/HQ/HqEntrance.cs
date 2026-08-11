using UnityEngine;

public class HqEntrance : InteractableBase {
    [Header("=== 본부로 텔레포트할 시 도착할 위치 ===")]
    [SerializeField] private Transform _hqSpawnPoint;

    // 차량 모델의 바퀴+차체 콜라이더를 합친 bounds 중심 하나로 조준 판정을 하다 보니,
    // 오브젝트가 커서 그 중심점을 정확히 조준해야만 상호작용이 잡히는 문제가 있었다.
    // NPC와 마찬가지로 판정 반경을 넉넉하게 잡아준다.
    [SerializeField, Min(1f)] private float _aimRadiusMultiplier = 5f;
    public override float AimRadiusMultiplier => _aimRadiusMultiplier;

    public override string InteractionText => "본부로 이동하기";
    public override bool CanInteract(GameObject interactor) => true;
	
    public override void Interact(GameObject interactor) {
        // 플레이어가 아닌 사람이 상호작용하면 제끼기
        if (!interactor.TryGetComponent<Player>(out var player)) { return; }
		
        // 플레이어가 상호작용했다면, 이동시켜주면 됨
        player.PlayerMove.TeleportToPosition(_hqSpawnPoint.position, _hqSpawnPoint.rotation);
    }
}
