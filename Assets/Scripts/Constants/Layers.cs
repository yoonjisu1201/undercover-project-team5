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

	// 1인칭 전용 손·장비 뷰모델. 오버레이 카메라만 이 레이어를 그리고, 본 카메라는 제외한다.
	public static readonly int FirstPersonHands = LayerMask.NameToLayer("FirstPersonHands");
}
