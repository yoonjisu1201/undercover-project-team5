using UnityEngine;
using UnityEngine.Rendering;

public class PlayerRenderer : MonoBehaviour {
	[Header("=== 비활성화할 머리 오브젝트들 ===")]
	[SerializeField] private GameObject[] _headObjects;

	private GameObject[] _headShadowCasters;

	// 머리는 레이어 컬링으로 숨겨서 그림자까지 사라진다. 같은 메시를 Shadows Only로
	// 그리는 사본을 골격에 붙여 카메라에는 안 잡히고 그림자만 남게 한다.
	private void Awake() {
		_headShadowCasters = new GameObject[_headObjects.Length];
		for (int i = 0; i < _headObjects.Length; i++) {
			_headShadowCasters[i] = CreateShadowOnlyCopy(_headObjects[i]);
		}
	}

	public void SetHeadObjectsLayer(int LayerMask) {
		foreach (var obj in _headObjects) {
			obj.layer = LayerMask;
		}
	}

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
