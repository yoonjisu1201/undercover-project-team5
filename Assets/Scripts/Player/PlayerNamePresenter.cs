using TMPro;
using Unity.Collections;
using UnityEngine;
using UnityEngine.UI;

public class PlayerNamePresenter : MonoBehaviour {
	[Header("=== 미니맵에 표시될 플레이어 이름 ===")]
	[SerializeField] private TMP_Text _minimapNameText;

	[Header("=== 미니맵 마커 ===")] 
	[SerializeField] private Image _minimapMarker;

	// 플레이어 카메라에선 안 보이게 만들어주기
	private void Awake() {
		_minimapNameText.gameObject.layer = Layers.MinimapOnly;
		_minimapMarker.gameObject.layer = Layers.MinimapOnly; 
	}

	public void HandlePlayerNameChanged(FixedString32Bytes oldName, FixedString32Bytes newName) {
		_minimapNameText.text = newName.ToString();
	}
}