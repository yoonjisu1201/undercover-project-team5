using UnityEngine;

// 미니맵 카메라가 MinimapOnly 레이어(플레이어 마커)를 렌더링하도록 보장
public sealed class MinimapCameraLayerSetup : MonoBehaviour {
	private void Awake() {
		Camera camera = GetComponent<Camera>();
		Layers.ShowLayerToCamera(camera, Layers.MinimapOnly);
		Layers.HideLayerFromCamera(camera, Layers.NotInMinimap);
	}
}
