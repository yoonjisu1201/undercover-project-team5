using Unity.Netcode;
using UnityEngine;

[RequireComponent(typeof(NpcOutfitController))]
public class NpcFeatureController : NetworkBehaviour {
	private const string IdentificationFillLightName = "Identification Fill Light";
	private const string IdentificationRimLightName = "Identification Rim Light";
	private const float IdentificationFillIntensity = 0.12f;
	private const float IdentificationFillRange = 1.25f;
	private const float IdentificationRimIntensity = 0.04f;
	private const float IdentificationRimRange = 0.9f;

	private NpcOutfitController _outfitController;
	private readonly NetworkVariable<NpcFeature> _feature = new NetworkVariable<NpcFeature>(
		new NpcFeature(),
		NetworkVariableReadPermission.Everyone,
		NetworkVariableWritePermission.Server
	);

	private void Awake() {
		_outfitController = GetComponent<NpcOutfitController>();
		EnsureIdentificationLights();
	}

	private void EnsureIdentificationLights() {
		CreateIdentificationLight(
			IdentificationFillLightName,
			new Vector3(0f, 1.35f, 0.9f),
			new Vector3(0f, 0.95f, 0f),
			new Color(1f, 0.95f, 0.88f),
			IdentificationFillIntensity,
			IdentificationFillRange,
			75f);

		CreateIdentificationLight(
			IdentificationRimLightName,
			new Vector3(0.55f, 1.4f, -0.55f),
			new Vector3(0f, 1f, 0f),
			new Color(0.75f, 0.82f, 0.9f),
			IdentificationRimIntensity,
			IdentificationRimRange,
			45f);
	}

	private void CreateIdentificationLight(string lightName, Vector3 localPosition, Vector3 localTarget, Color color, float intensity, float range, float spotAngle) {
		if (transform.Find(lightName) != null) {
			return;
		}

		GameObject lightObject = new(lightName);
		lightObject.transform.SetParent(transform, false);
		lightObject.transform.localPosition = localPosition;
		lightObject.transform.LookAt(transform.TransformPoint(localTarget));

		Light light = lightObject.AddComponent<Light>();
		light.type = LightType.Spot;
		light.color = color;
		light.intensity = intensity;
		light.range = range;
		light.spotAngle = spotAngle;
		light.cullingMask = 1 << gameObject.layer;
		light.shadows = LightShadows.None;
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
