using UnityEngine;

public static class Layers {
	public static void HideLayerFromCamera(Camera camera, int layerIndex) {
		camera.cullingMask &= ~(1 << layerIndex);
	}
	
	public static void ShowLayerToCamera(Camera camera, int layerIndex) {
		camera.cullingMask |= 1 << layerIndex;
	}
	
	public static readonly int MinimapOnly = LayerMask.NameToLayer("MinimapOnly");
}