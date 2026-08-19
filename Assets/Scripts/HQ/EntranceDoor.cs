using UnityEngine;
using UnityEngine.Serialization;

public abstract class EntranceDoor : InteractableBase {
	[Header("=== 텔레포트할 시 도착할 위치 ===")]
	// HqEntrance/HqExit이 각자 들고 있던 필드를 통합하면서, 이미 씬/프리팹에 저장된 참조가
	// 안 끊기도록 예전 이름들을 그대로 매핑해둔다.
	[FormerlySerializedAs("_hqSpawnPoint")]
	[FormerlySerializedAs("_fieldSpawnPoint")]
	[SerializeField] private Transform _targetPoint;

	public Transform TargetPoint => _targetPoint;

	public override bool CanInteract(GameObject interactor) => true;

	public override void Interact(GameObject interactor) {
		// 플레이어가 아닌 사람이 상호작용하면 제끼기
		if (!interactor.TryGetComponent<Player>(out var player)) { return; }

		// 플레이어가 상호작용했다면, 이동시켜주면 됨
		player.PlayerMove.TeleportToPosition(_targetPoint.position, _targetPoint.rotation);
	}
}
