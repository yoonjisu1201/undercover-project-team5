using System.Collections.Generic;
using UnityEngine;

public class Montage : MonoBehaviour {
	private Dictionary<MontageParts, GameObject> _clothByParts;
	private Dictionary<MontageParts, GameObject> _rootParts;

	[Header("=== 각 위치의 루트 아이템들 ===")]
	[SerializeField] private GameObject HeadRoot;
	[SerializeField] private GameObject TorsoRoot;
	[SerializeField] private GameObject ArmRoot;
	[SerializeField] private GameObject PantsRoot;
	[SerializeField] private GameObject ShoesRoot;

	[Header("=== 옷을 생성할 부모 Transform ===")]
	[SerializeField] private Transform _clothParent;
	
	public void Initialize() {
		_clothByParts = new Dictionary<MontageParts, GameObject>();
		
		// 옷을 입히면 사라져야 하는 부위들 (루트파츠)
		// 이게 사라지지 않으면, 두 개의 파츠가 겹치면서 노란 반점 생김
		_rootParts = new Dictionary<MontageParts, GameObject> {
			{ MontageParts.Torso, TorsoRoot },
			{ MontageParts.Arms, ArmRoot },
			{ MontageParts.Pants, PantsRoot },
			{ MontageParts.Shoes, ShoesRoot }
		};
	}
	
	// 옷입히기
	public void WearCloth(MontageParts part, GameObject cloth) {
		// 이미 존재하면 갈아입혀야 함.
		if (_clothByParts.TryGetValue(part, out _)) { RemoveCloth(part); }
		
		GameObject obj = Instantiate(cloth, transform);
		obj.transform.localPosition = Vector3.zero;
		
		// 옷을 입으면, 그 부위의 RootObject를 비활성화
		if (_rootParts.TryGetValue(part, out GameObject rootObj)) {
			rootObj.gameObject.SetActive(false);
		}
		
		_clothByParts[part] = obj;
	}
	
	// 옷벗기기
	public void RemoveCloth(MontageParts part) {
		if (!_clothByParts.TryGetValue(part, out GameObject cloth)) {
			return;
		}

		Destroy(cloth);

		// 키를 남겨두면 같은 부위를 다시 입힐 때 WearCloth가 입고 있다고 착각한다
		_clothByParts.Remove(part);

		// 옷을 벗으면, 그 부위의 RootObject를 활성화
		if (_rootParts.TryGetValue(part, out GameObject rootObj)) {
			rootObj.gameObject.SetActive(true);
		}
	}

	public void RemoveAllClothes() {
		foreach (MontageParts part in new List<MontageParts>(_clothByParts.Keys)) {
			RemoveCloth(part);
		}
	}

	public void SetRootPartsVisible(bool visible) {
		SetActiveIfNotNull(HeadRoot, visible);
		SetActiveIfNotNull(TorsoRoot, visible);
		SetActiveIfNotNull(ArmRoot, visible);
		SetActiveIfNotNull(PantsRoot, visible);
		SetActiveIfNotNull(ShoesRoot, visible);
	}

	public bool TryGetClothBounds(MontageParts part, out Bounds bounds) {
		bounds = default;

		if (!_clothByParts.TryGetValue(part, out GameObject cloth) || cloth == null) {
			return false;
		}

		Renderer[] renderers = cloth.GetComponentsInChildren<Renderer>(true);
		bool hasBounds = false;

		foreach (Renderer renderer in renderers) {
			if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy) {
				continue;
			}

			if (!hasBounds) {
				bounds = renderer.bounds;
				hasBounds = true;
				continue;
			}

			bounds.Encapsulate(renderer.bounds);
		}

		return hasBounds;
	}

	private static void SetActiveIfNotNull(GameObject target, bool active) {
		if (target != null) {
			target.SetActive(active);
		}
	}
}
