using System;
using TMPro;
using UnityEngine;

public class PlayerNameTag : MonoBehaviour {
	[Header("=== 이름표 텍스트 등록 ===")]
	[SerializeField] private TMP_Text _nameText;

	// LocalCamera에서만 보이도록 함. 미니맵, CCTV에서는 닉네임 확인 안되게
	private void Awake() {
		_nameText.gameObject.layer = Layers.LocalCameraOnly;
	}

	public void SetText(string text) {
		_nameText.text = text;
	}

	private void LateUpdate() {
		Camera mainCamera = LocalCameraProvider.MainCamera;
		
		// 메인카메라 지정되기 전에는 안함
		if (mainCamera == null) { return; }
		
		// x회전은 적용 안시킴. 적용시키면 하늘 봤을 때 이름표도 같이 하늘 봄..
		Quaternion newRotation = mainCamera.transform.rotation;
		newRotation.x = 0f;
		transform.rotation = newRotation;
	}
}