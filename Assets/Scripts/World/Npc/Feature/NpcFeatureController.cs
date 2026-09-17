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
	private OutfitFeature _outfit;

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

	// CCTV 호버 표시처럼 이 NPC의 착용 의상을 읽어야 하는 쪽에 공개한다.
	public OutfitFeature Outfit => _outfit;

	public override void OnNetworkSpawn() {
		if (RoundManager.Instance == null) {
			Debug.LogError("[NPC] RoundManager가 없어 공통 시드 기반 의상을 생성할 수 없습니다.", this);
			return;
		}

		// 세션 시드와 Netcode가 모두에게 동일하게 부여한 NetworkObjectId만 사용한다.
		// NPC별 의상 값 또는 시드는 네트워크로 전송하지 않는다.
		int seed = NpcOutfitSeed.Create(
			RoundManager.Instance.ClothPoolSessionSeed,
			NetworkObject.NetworkObjectId);
		_outfit = _outfitController.CreateDeterministicOutfit(seed);
		_outfitController.ApplyOutfit(_outfit);

		// 의상 파츠가 만들어진 뒤에 CCTV 외곽선을 만들어야 옷까지 포함된다.
		GetComponent<NpcCctvHighlight>()?.BuildOutline();
	}

}
