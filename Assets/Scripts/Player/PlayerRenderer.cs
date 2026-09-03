using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Serialization;

public class PlayerRenderer : MonoBehaviour {
	// 1인칭 화면에서 감출 자기 몸. 머리뿐 아니라 몸통·손·다리까지 모두 넣는다.
	// 손과 장비는 1인칭 전용 뷰모델이 따로 그리므로 여기서는 실제 캐릭터를 통째로 뺀다.
	//
	// 레이어는 클라이언트마다 따로다. 내 화면에서 내 몸만 옮기는 것이라 남의 화면에는 그대로 보인다.
	[Header("=== 1인칭 화면에서 감출 내 몸 ===")]
	[FormerlySerializedAs("_headObjects")]
	[SerializeField] private GameObject[] _localBodyObjects;

	// 관전은 몸 전체가 아니라 머리만 치운다. 보고 있는 팀원의 머리가 카메라 앞을 가리기
	// 때문인데, 몸까지 끄면 관전 화면에서 대상이 통째로 사라진다. 그래서 배열을 따로 둔다.
	[Header("=== 관전 중 감출 머리 ===")]
	[SerializeField] private GameObject[] _headObjects;

	private GameObject[] _bodyShadowCasters;
	private GameObject[] _headShadowCasters;

	// 몸은 레이어 컬링으로 숨겨서 그림자까지 사라진다. 같은 메시를 Shadows Only로
	// 그리는 사본을 골격에 붙여 카메라에는 안 잡히고 그림자만 남게 한다.
	// 손도 오버레이로 넘어가면 본 카메라에서 빠지므로 똑같이 사본을 만든다.
	private void Awake() {
		_bodyShadowCasters = new GameObject[_localBodyObjects.Length];
		for (int i = 0; i < _localBodyObjects.Length; i++) {
			_bodyShadowCasters[i] = CreateShadowOnlyCopy(_localBodyObjects[i]);
		}

		_headShadowCasters = new GameObject[_headObjects.Length];
		for (int i = 0; i < _headObjects.Length; i++) {
			_headShadowCasters[i] = CreateShadowOnlyCopy(_headObjects[i]);
		}
	}

	public void SetLocalBodyLayer(int layer) {
		foreach (var obj in _localBodyObjects) {
			if (obj != null) {
				obj.layer = layer;
			}
		}
	}


	public void SetBodyShadowCastersActive(bool active) {
		if (_bodyShadowCasters == null) {
			return;
		}

		foreach (var obj in _bodyShadowCasters) {
			if (obj != null) {
				obj.SetActive(active);
			}
		}
	}

	// 관전 중에는 보고 있는 팀원의 머리가 카메라 앞으로 들어와 시야를 가린다. 머리 레이어는
	// 각자 자기 머리만 숨기는 용도라 대상만 골라 컬링할 수 없어 오브젝트를 끈다.
	public void SetHeadObjectsActive(bool active) {
		foreach (var obj in _headObjects) {
			if (obj != null) {
				obj.SetActive(active);
			}
		}
	}

	// 머리를 끄면 그림자도 같이 사라진다. 몸 쪽과 같은 방식으로 사본을 대신 켠다.
	public void SetHeadShadowCastersActive(bool active) {
		if (_headShadowCasters == null) {
			return;
		}

		foreach (var obj in _headShadowCasters) {
			if (obj != null) {
				obj.SetActive(active);
			}
		}
	}

	private static GameObject CreateShadowOnlyCopy(GameObject source) {
		if (source == null || !source.TryGetComponent(out SkinnedMeshRenderer sourceRenderer)) {
			return null;
		}

		GameObject copy = new GameObject(source.name + "_ShadowOnly");
		copy.transform.SetParent(source.transform.parent, false);
		copy.layer = source.layer;

		SkinnedMeshRenderer copyRenderer = copy.AddComponent<SkinnedMeshRenderer>();
		copyRenderer.sharedMesh = sourceRenderer.sharedMesh;
		copyRenderer.bones = sourceRenderer.bones;
		copyRenderer.rootBone = sourceRenderer.rootBone;
		copyRenderer.localBounds = sourceRenderer.localBounds;
		copyRenderer.sharedMaterials = sourceRenderer.sharedMaterials;
		copyRenderer.shadowCastingMode = ShadowCastingMode.ShadowsOnly;
		copyRenderer.receiveShadows = false;
		copy.SetActive(false);

		return copy;
	}
}
