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
		_rootParts = new Dictionary<MontageParts, GameObject>();
		
		// 옷을 입히면 사라져야 하는 부위들
		_rootParts.Add(MontageParts.Torso, TorsoRoot);
		_rootParts.Add(MontageParts.Arms, ArmRoot);
		_rootParts.Add(MontageParts.Pants, PantsRoot);
		_rootParts.Add(MontageParts.Shoes, ShoesRoot);
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
		if (!_clothByParts.ContainsKey(part)) {
			Debug.LogError($"[Montage] 입고 있지 않은 부위를 탈착하려 함");
		}
		
		Destroy(_clothByParts[part]);
		_clothByParts[part] = null;
		
		// 옷을 벗으면, 그 부위의 RootObject를 활성화
		if (_rootParts.TryGetValue(part, out GameObject rootObj)) {
			rootObj.gameObject.SetActive(true);
		}
	}
}