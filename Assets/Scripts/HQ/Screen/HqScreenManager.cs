using UnityEngine;

public class HqScreenManager : MonoBehaviour {
	[Header("=== Controller 등록 ===")]
	[SerializeField] HqScreenController hqScreenController;
	
	private void Awake() {
		hqScreenController.Initialize();
	}
}