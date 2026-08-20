using UnityEngine;

public static class Layers {
	// 카메라에서 특정 레이어 보이지 않도록 처리
	public static void HideLayerFromCamera(Camera camera, int layerIndex) {
		if (camera == null || layerIndex < 0) {
			return;
		}

		camera.cullingMask &= ~(1 << layerIndex);
	}

	// 카메라에서 특정 레이어 보이도록 처리
	public static void ShowLayerToCamera(Camera camera, int layerIndex) {
		if (camera == null || layerIndex < 0) {
			return;
		}

		camera.cullingMask |= 1 << layerIndex;
	}
	
	public static readonly int MinimapOnly = LayerMask.NameToLayer("MinimapOnly");
	public static readonly int Item = LayerMask.NameToLayer("Item");
	public static readonly int LocalPlayerHead = LayerMask.NameToLayer("LocalPlayerHead");
	public static readonly int LocalCameraOnly =  LayerMask.NameToLayer("LocalCameraOnly");
	public static readonly int CCTVPostProcessing = LayerMask.NameToLayer("CCTVPostProcessing");
	public static readonly int NotInMinimap = LayerMask.NameToLayer("NotInMinimap");
	public static readonly int NavMeshOnly = LayerMask.NameToLayer("NavMeshOnly");
}
