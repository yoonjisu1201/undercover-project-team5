using UnityEngine;

public abstract class ScreenBase : MonoBehaviour {
	// 특정 스크린 활성화, 비활성화 시에 사용할 함수. 필요 시에는 Override해서 사용
	public virtual void DisableScreen() {
		gameObject.SetActive(false);
	}
	
	// 특정 스크린 활성화, 비활성화 시에 사용할 함수. 필요 시에는 Override해서 사용
	public virtual void ActivateScreen() {
		gameObject.SetActive(true);
	}
}