using System;
using System.Collections.Generic;
using UnityEngine;

public class CCTVRegion : MonoBehaviour {
	[Header("=== RegionId 등록 ===")]
	[SerializeField] public RegionId RegionId;
	
	private List<CCTVPoint> _points = new();
	public IReadOnlyList<CCTVPoint> Points => _points;

	public void Initialize() {
		CCTVPoint[] points = GetComponentsInChildren<CCTVPoint>();
		foreach (CCTVPoint point in points) {
			_points.Add(point);
		}
	}
}