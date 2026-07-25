using UnityEngine;

public class PlayerRenderer : MonoBehaviour {
	[Header("=== 비활성화할 머리 오브젝트들 ===")] 
	[SerializeField] private GameObject[] _headObjects;
	
	public void SetHeadObjectsLayer(int LayerMask) {
		foreach (var obj in _headObjects) {
			obj.layer = LayerMask;
		}
	}
}