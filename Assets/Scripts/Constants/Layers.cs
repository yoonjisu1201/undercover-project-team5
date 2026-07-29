using UnityEngine;

public static class Layers {
	// 카메라에서 특정 레이어 보이지 않도록 처리
	public static void HideLayerFromCamera(Camera camera, int layerIndex) {
		camera.cullingMask &= ~(1 << layerIndex);
	}

	// 카메라에서 특정 레이어 보이도록 처리
	public static void ShowLayerToCamera(Camera camera, int layerIndex) {
		camera.cullingMask |= 1 << layerIndex;
	}
	
	public static readonly int MinimapOnly = LayerMask.NameToLayer("MinimapOnly");
	public static readonly int LocalPlayerHead = LayerMask.NameToLayer("LocalPlayerHead");
	public static readonly int LocalCameraOnly =  LayerMask.NameToLayer("LocalCameraOnly");
}
