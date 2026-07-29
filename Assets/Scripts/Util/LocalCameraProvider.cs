using UnityEngine;

// 플레이어(나)의 LocalCamera를 등록하고 사용할 수 있게 해주는 클래스
public static class LocalCameraProvider {
	public static Camera MainCamera { get; private set; }

	public static void Register(Camera camera) {
		MainCamera = camera;
	}

	public static void Unregister(Camera camera)
	{
		if (MainCamera == camera)
			MainCamera = null;
	}
}