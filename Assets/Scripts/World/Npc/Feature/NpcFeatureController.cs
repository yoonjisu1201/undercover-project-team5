using Unity.Netcode;
using UnityEngine;

[RequireComponent(typeof(NpcOutfitController))]
public class NpcFeatureController : NetworkBehaviour {
	private NpcOutfitController _outfitController;
	private readonly NetworkVariable<NpcFeature> _feature = new NetworkVariable<NpcFeature>(
		new NpcFeature(),
		NetworkVariableReadPermission.Everyone,
		NetworkVariableWritePermission.Server
	);

	public NpcFeature NpcFeature => _feature.Value;

	private void Awake() {
		_outfitController = GetComponent<NpcOutfitController>();
	}

	public override void OnNetworkSpawn() {
		// 서버에서만, 각 Npc의 Outfit을 설정해준다.
		if (IsServer) {
			_feature.Value = new NpcFeature {
				Outfit = _outfitController.CreateRandomOutfitFeature()
			};
		}

		// 각 플레이어들은 모두 외형을 적용한다.
		_outfitController.ApplyOutfit(_feature.Value.Outfit);
	}

	public void SendFeatureTo(CriminalNpcManager criminalManager) {
		criminalManager.SetCriminalFeature(_feature.Value);
	}

}
