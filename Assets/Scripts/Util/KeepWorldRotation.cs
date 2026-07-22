using UnityEngine;

// 미니맵 마커가 플레이어와 함께 회전하지 않도록 하기 위해 추가한 클래스
// 이외에도 회전 고정을 원하는 오브젝트가 있다면 추가하면 됨.
public sealed class KeepWorldRotation : MonoBehaviour {
	private Quaternion _initialWorldRotation;

	private void Awake()
	{
		_initialWorldRotation = transform.rotation;
	}

	private void LateUpdate()
	{
		transform.rotation = _initialWorldRotation;
	}
}