using System;
using System.Collections.Generic;
using UnityEngine;

public class CCTVRegion : MonoBehaviour {
	[Header("=== RegionId 등록 ===")]
	[SerializeField] public RegionId RegionId;
	
	private List<CCTVPoint> _points = new();
	public IReadOnlyList<CCTVPoint> Points => _points;

	// 두 번 호출되어도 포인트가 누적되지 않도록 매번 처음부터 채운다.
	public void Initialize() {
		_points.Clear();

		CCTVPoint[] points = GetComponentsInChildren<CCTVPoint>();
		foreach (CCTVPoint point in points) {
			_points.Add(point);
		}
	}
}