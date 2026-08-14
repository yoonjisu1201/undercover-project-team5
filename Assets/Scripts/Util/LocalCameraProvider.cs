using UnityEngine;

// 플레이어(나)의 LocalCamera를 등록하고 사용할 수 있게 해주는 클래스
public static class LocalCameraProvider
{
	public static Camera MainCamera { get; private set; }

	private static Camera s_fallbackCamera;

	[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
	private static void BootstrapFallbackCamera()
	{
		if (s_fallbackCamera != null) return;

		var fallbackObject = new GameObject("FallbackCamera");
		Object.DontDestroyOnLoad(fallbackObject);

		s_fallbackCamera = fallbackObject.AddComponent<Camera>();
		s_fallbackCamera.clearFlags = CameraClearFlags.SolidColor;
		s_fallbackCamera.backgroundColor = Color.black;
		s_fallbackCamera.cullingMask = 0;
		s_fallbackCamera.depth = -100f;
		s_fallbackCamera.enabled = false;

		fallbackObject.AddComponent<FallbackCameraActivator>();
	}

	public static void Register(Camera camera)
	{
		MainCamera = camera;
		SetFallbackEnabled(false);
	}

	public static void Unregister(Camera camera)
	{
		if (MainCamera == camera)
		{
			MainCamera = null;
		}
	}

	private static void SetFallbackEnabled(bool enabled)
	{
		if (s_fallbackCamera != null && s_fallbackCamera.enabled != enabled)
		{
			s_fallbackCamera.enabled = enabled;
		}
	}

	private static bool HasEnabledGameplayCamera()
	{
		if (MainCamera != null && MainCamera.isActiveAndEnabled) return true;

		foreach (Camera camera in Object.FindObjectsByType<Camera>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
		{
			if (camera != s_fallbackCamera && camera.isActiveAndEnabled)
			{
				return true;
			}
		}

		return false;
	}

	private sealed class FallbackCameraActivator : MonoBehaviour
	{
		private void LateUpdate()
		{
			SetFallbackEnabled(!HasEnabledGameplayCamera());
		}
	}
}
