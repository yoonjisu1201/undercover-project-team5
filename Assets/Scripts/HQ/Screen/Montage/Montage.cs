using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;

public class Montage : MonoBehaviour {
	private Dictionary<MontageParts, GameObject> _clothByParts;

	[Header("=== 옷을 생성할 부모 Transform ===")]
	[SerializeField] private Transform _clothParent;
	public void Initialize() {
		_clothByParts = new Dictionary<MontageParts, GameObject>();
	}
	
	// 옷입히기
	public void WearCloth(MontageParts part, GameObject cloth) {
		// 이미 존재하면 갈아입혀야 함.
		if (_clothByParts.TryGetValue(part, out _)) { RemoveCloth(part); }
		
		GameObject obj = Instantiate(cloth, transform);
		obj.transform.localPosition = Vector3.zero;
		
		_clothByParts[part] = obj;
	}
	
	// 옷벗기기
	public void RemoveCloth(MontageParts part) {
		if (!_clothByParts.ContainsKey(part)) {
			Debug.LogError($"[Montage] 입고 있지 않은 부위를 탈착하려 함");
		}
		
		Destroy(_clothByParts[part]);
		_clothByParts[part] = null;
	}
}