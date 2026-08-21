using UnityEngine;

// 지상/지하 Fog 값 전환. 클래스가 처음 쓰이는 시점의 RenderSettings 값을 캐싱해 사용한다.
public static class UndergroundFog {
	private static readonly FogMode s_surfaceMode;
	private static readonly Color s_surfaceColor;
	private static readonly float s_surfaceStart;
	private static readonly float s_surfaceEnd;

	static UndergroundFog() {
		s_surfaceMode = RenderSettings.fogMode;
		s_surfaceColor = RenderSettings.fogColor;
		s_surfaceStart = RenderSettings.fogStartDistance;
		s_surfaceEnd = RenderSettings.fogEndDistance;
	}

	public static void ApplySurface() {
		RenderSettings.fogMode = s_surfaceMode;
		RenderSettings.fogColor = s_surfaceColor;
		RenderSettings.fogStartDistance = s_surfaceStart;
		RenderSettings.fogEndDistance = s_surfaceEnd;
	}

	public static void ApplyUnderground() {
		RenderSettings.fogMode = FogMode.Linear;
		RenderSettings.fogColor = Color.black;
		RenderSettings.fogStartDistance = 3f;
		RenderSettings.fogEndDistance = 7f;
	}
}
