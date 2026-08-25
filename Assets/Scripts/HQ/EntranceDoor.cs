using Unity.Netcode;
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

		// 문을 쓴 본인은 바로 다음 줄에서 반대편으로 순간이동한다. RPC 가 돌아올 때쯤이면
		// 이미 문에서 멀어져 있어서 위치가 있는 소리로는 아무것도 못 듣는다. 그래서 본인 소리는
		// 위치 없이 즉시 낸다.
		SoundManager.Instance?.Play(SoundKey.Door_Open);

		// 남아 있는 사람들은 문 위치에서 듣는다. 누가 지하로 내려갔는지 소리로 알 수 있어야 한다.
		RequestDoorSoundRpc(transform.position);

		// 플레이어가 상호작용했다면, 이동시켜주면 됨
		player.PlayerMove.TeleportToPosition(_targetPoint.position, _targetPoint.rotation);
	}

	// 문은 서버 소유라 클라이언트가 곧장 모두에게 보낼 수 없다. 서버를 거쳐 퍼뜨린다.
	[Rpc(SendTo.Server)]
	private void RequestDoorSoundRpc(Vector3 position, RpcParams rpcParams = default)
	{
		// 보낸 본인은 위에서 이미 자기 소리를 냈다. 다시 보내면 겹친다.
		PlayDoorSoundRpc(position, RpcTarget.Not(rpcParams.Receive.SenderClientId, RpcTargetUse.Temp));
	}

	[Rpc(SendTo.SpecifiedInParams)]
	private void PlayDoorSoundRpc(Vector3 position, RpcParams rpcParams = default)
	{
		SoundManager.Instance?.PlayAt(SoundKey.Door_Open, position);
	}
}
