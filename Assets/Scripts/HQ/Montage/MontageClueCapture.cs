using System;
using UnityEngine;

public sealed class MontageClueCapture {
	private const float MinCameraDistance = 0.08f;
	private const float FallbackFieldOfView = 24f;
	private const float CaptureFieldOfView = 4f;
	private const float LightIntensityMultiplier = 2.2f;
	private const float FillLightIntensity = 2.0f;

	private readonly Montage _montage;
	private readonly Camera _camera;
	private readonly Action<MontageState> _applyPreviewState;

	public MontageClueCapture(Montage montage, Camera camera, Action<MontageState> applyPreviewState) {
		_montage = montage;
		_camera = camera;
		_applyPreviewState = applyPreviewState;
	}

	public Texture2D Capture(MontageState state, MontageParts focusPart, MontageState previousState) {
		CameraClearFlags previousClearFlags = _camera.clearFlags;
		Color previousBackgroundColor = _camera.backgroundColor;
		Vector3 previousPosition = _camera.transform.position;
		Quaternion previousRotation = _camera.transform.rotation;
		float previousFieldOfView = _camera.fieldOfView;
		float previousOrthographicSize = _camera.orthographicSize;
		LightState[] previousLightStates = CaptureLightStates();
		GameObject clueFillLight = null;

		try {
			_applyPreviewState(state);
			_montage.SetRootPartsVisible(false);
			_camera.clearFlags = CameraClearFlags.SolidColor;
			_camera.backgroundColor = Color.clear;
			_camera.fieldOfView = CaptureFieldOfView;
			ApplyClueLightBoost(focusPart);
			ApplyFocusFraming(focusPart);
			clueFillLight = CreateClueFillLight(focusPart);
			return CopyCameraTexture();
		}
		finally {
			if (clueFillLight != null) {
				UnityEngine.Object.Destroy(clueFillLight);
			}

			RestoreLightStates(previousLightStates);
			_camera.clearFlags = previousClearFlags;
			_camera.backgroundColor = previousBackgroundColor;
			_camera.transform.SetPositionAndRotation(previousPosition, previousRotation);
			_camera.fieldOfView = previousFieldOfView;
			_camera.orthographicSize = previousOrthographicSize;
			_applyPreviewState(previousState);
			_camera.Render();
		}
	}

	private void ApplyFocusFraming(MontageParts focusPart) {
		if (!_montage.TryGetClothBounds(focusPart, out Bounds bounds) && !TryGetMontageBounds(out bounds)) {
			Vector3 fallbackTarget = _montage.transform.position;
			Vector3 fallbackViewDirection = GetCameraViewDirection(fallbackTarget);
			_camera.transform.position = fallbackTarget + fallbackViewDirection * 2.2f;
			_camera.transform.LookAt(fallbackTarget);
			_camera.fieldOfView = FallbackFieldOfView;
			return;
		}

		bounds = AdjustFocusBounds(bounds, focusPart);
		Vector3 target = bounds.center;
		Vector3 viewDirection = GetCameraViewDirection(target);
		float margin = GetFramingMargin(focusPart);

		if (_camera.orthographic) {
			float verticalSize = bounds.extents.y;
			float horizontalSize = bounds.extents.x / _camera.aspect;
			_camera.orthographicSize = Mathf.Max(
				Mathf.Max(verticalSize, horizontalSize) * margin,
				GetMinOrthographicSize(focusPart));
			_camera.transform.position = target + viewDirection * 2.2f;
			_camera.transform.LookAt(target);
			return;
		}

		float halfFov = _camera.fieldOfView * 0.5f * Mathf.Deg2Rad;
		float verticalDistance = bounds.extents.y / Mathf.Tan(halfFov);
		float horizontalDistance = bounds.extents.x / (Mathf.Tan(halfFov) * _camera.aspect);
		float distance = (Mathf.Max(verticalDistance, horizontalDistance) + bounds.extents.z) * margin;

		_camera.transform.position = target + viewDirection * Mathf.Max(distance, MinCameraDistance);
		_camera.transform.LookAt(target);
	}

	private Bounds AdjustFocusBounds(Bounds bounds, MontageParts focusPart) {
		Vector3 center = bounds.center;
		Vector3 size = bounds.size;

		switch (focusPart) {
			case MontageParts.Eyebrows:
				size = new Vector3(
					Mathf.Max(size.x, 0.16f),
					Mathf.Max(size.y, 0.08f),
					Mathf.Max(size.z, 0.06f));
				break;
			case MontageParts.Beard:
				size = new Vector3(
					Mathf.Max(size.x, 0.22f),
					Mathf.Max(size.y, 0.14f),
					Mathf.Max(size.z, 0.06f));
				break;
			case MontageParts.Arms:
				// 손 파츠는 좌우가 멀리 떨어져 있어서 전체 중심을 쓰면 빈 공간을 찍는다.
				// 몽타주 카메라 기준 오른쪽 끝 손을 단서 대상으로 잡는다.
				center += _camera.transform.right * bounds.extents.x * 0.55f;
				size = new Vector3(
					Mathf.Max(size.x * 0.24f, 0.08f),
					Mathf.Max(size.y * 0.75f, 0.1f),
					Mathf.Max(size.z, 0.05f));
				break;
		}

		return new Bounds(center, size);
	}

	private static float GetFramingMargin(MontageParts focusPart) {
		switch (focusPart) {
			case MontageParts.Beard:
			case MontageParts.Eyebrows:
				return 0.6f;
			case MontageParts.Glasses:
				return 0.7f;
			case MontageParts.Masks:
				return 1.0f;
			case MontageParts.Hair:
			case MontageParts.Hats:
			case MontageParts.Headphones:
				return 0.92f;
			case MontageParts.Shoes:
				return 0.75f;
			case MontageParts.Torso:
			case MontageParts.Pants:
				return 1.15f;
			case MontageParts.Arms:
				return 0.75f;
			default:
				return 0.9f;
		}
	}

	private static float GetMinOrthographicSize(MontageParts focusPart) {
		switch (focusPart) {
			case MontageParts.Beard:
			case MontageParts.Eyebrows:
				return 0.04f;
			case MontageParts.Glasses:
				return 0.06f;
			case MontageParts.Masks:
				return 0.12f;
			case MontageParts.Hair:
			case MontageParts.Hats:
			case MontageParts.Headphones:
				return 0.16f;
			case MontageParts.Shoes:
				return 0.08f;
			case MontageParts.Arms:
				return 0.06f;
			case MontageParts.Torso:
			case MontageParts.Pants:
				return 0.28f;
			default:
				return 0.12f;
		}
	}

	private LightState[] CaptureLightStates() {
		Light[] lights = GetCaptureLightRoot().GetComponentsInChildren<Light>(true);
		LightState[] states = new LightState[lights.Length];

		for (int i = 0; i < lights.Length; i++) {
			states[i] = new LightState(lights[i]);
		}

		return states;
	}

	private void ApplyClueLightBoost(MontageParts focusPart) {
		Light[] lights = GetCaptureLightRoot().GetComponentsInChildren<Light>(true);
		float intensityMultiplier = GetLightIntensityMultiplier(focusPart);

		foreach (Light light in lights) {
			if (light == null) {
				continue;
			}

			light.gameObject.SetActive(true);
			light.intensity *= intensityMultiplier;
		}
	}

	private GameObject CreateClueFillLight(MontageParts focusPart) {
		float intensity = GetFillLightIntensity(focusPart);
		if (intensity <= 0f) {
			return null;
		}

		GameObject lightObject = new("Clue Capture Fill Light");
		lightObject.transform.SetParent(GetCaptureLightRoot(), false);
		lightObject.transform.rotation = _camera.transform.rotation;

		Light light = lightObject.AddComponent<Light>();
		light.type = LightType.Directional;
		light.color = Color.white;
		light.intensity = intensity;
		light.shadows = LightShadows.None;

		return lightObject;
	}

	private static float GetFillLightIntensity(MontageParts focusPart) {
		switch (focusPart) {
			case MontageParts.Hair:
			case MontageParts.Hats:
			case MontageParts.Headphones:
				return FillLightIntensity * 1.35f;
			case MontageParts.Arms:
			case MontageParts.Pants:
				return FillLightIntensity * 1.15f;
			case MontageParts.Torso:
				return FillLightIntensity * 0.45f;
			case MontageParts.Shoes:
				return FillLightIntensity * 0.35f;
			default:
				return FillLightIntensity * 0.75f;
		}
	}

	private static float GetLightIntensityMultiplier(MontageParts focusPart) {
		switch (focusPart) {
			case MontageParts.Shoes:
				return 1.15f;
			case MontageParts.Torso:
				return 1.25f;
			default:
				return LightIntensityMultiplier;
		}
	}

	private Transform GetCaptureLightRoot() {
		if (_camera != null && _camera.transform.parent != null) {
			return _camera.transform.parent;
		}

		if (_montage != null && _montage.transform.parent != null) {
			return _montage.transform.parent;
		}

		return _montage.transform;
	}

	private static void RestoreLightStates(LightState[] states) {
		foreach (LightState state in states) {
			state.Restore();
		}
	}

	private bool TryGetMontageBounds(out Bounds bounds) {
		Renderer[] renderers = _montage.GetComponentsInChildren<Renderer>(true);
		bool hasBounds = false;
		bounds = default;

		foreach (Renderer renderer in renderers) {
			if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy) {
				continue;
			}

			if (!hasBounds) {
				bounds = renderer.bounds;
				hasBounds = true;
				continue;
			}

			bounds.Encapsulate(renderer.bounds);
		}

		return hasBounds;
	}

	private Vector3 GetCameraViewDirection(Vector3 target) {
		Vector3 viewDirection = (_camera.transform.position - target).normalized;
		if (viewDirection == Vector3.zero) {
			viewDirection = -_camera.transform.forward;
		}

		return viewDirection;
	}

	private Texture2D CopyCameraTexture() {
		RenderTexture renderTexture = _camera.targetTexture;
		if (renderTexture == null) {
			renderTexture = new RenderTexture(256, 256, 24, RenderTextureFormat.ARGB32);
			_camera.targetTexture = renderTexture;
		}

		_camera.Render();

		RenderTexture previous = RenderTexture.active;
		try {
			RenderTexture.active = renderTexture;
			Texture2D texture = new Texture2D(
				renderTexture.width,
				renderTexture.height,
				TextureFormat.RGBA32,
				false);

			texture.ReadPixels(new Rect(0, 0, renderTexture.width, renderTexture.height), 0, 0);
			texture.Apply();
			return texture;
		}
		finally {
			RenderTexture.active = previous;
		}
	}

	private readonly struct LightState {
		private readonly Light _light;
		private readonly bool _active;
		private readonly float _intensity;

		public LightState(Light light) {
			_light = light;
			_active = light != null && light.gameObject.activeSelf;
			_intensity = light != null ? light.intensity : 0f;
		}

		public void Restore() {
			if (_light == null) {
				return;
			}

			_light.gameObject.SetActive(_active);
			_light.intensity = _intensity;
		}
	}
}
