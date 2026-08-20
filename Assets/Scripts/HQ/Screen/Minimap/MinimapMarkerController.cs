using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// 미니맵 스프라이트 위에 CCTV·본부 마커를 얹는다.
// 위치는 MinimapScreenController가 구역 경계를 기준으로 계산해준다.
public class MinimapMarkerController : MonoBehaviour {
	[Header("=== 미니맵 스크린 등록 ===")]
	[SerializeField] private MinimapScreenController _minimapScreen;

	[Header("=== 마커 등록할 부모 Transform (미니맵 Image의 자식) ===")]
	[SerializeField] private Transform _markerParent;

	[Header("=== 미니맵 마커 등록 ===")]
	[SerializeField] private GameObject _cctvMarkerPrefab;

	[Header("=== CCTV Hub 등록 ===")]
	[SerializeField] private CCTVHub _cctvHub;

	[Header("=== 본부(StartPoint) 마커 등록 ===")]
	[SerializeField] private Transform _startPoint;
	[SerializeField] private GameObject _startPointMarkerPrefab;

	private static readonly Color ConnectedColor = Color.green;
	private static readonly Color PartialColor = Color.yellow;
	private static readonly Color DisconnectedColor = Color.red;

	private readonly List<MarkerInstance> _markerInstances = new List<MarkerInstance>();
	private RectTransform _startPointMarkerRect;

	// 인스턴스화한 마커 하나와, 나중에 이벤트 구독 해제·위치 갱신에 필요한 참조들을 함께 들고 다니기 위한 묶음
	private class MarkerInstance {
		public CCTVPoint Point;
		public RectTransform RectTransform;
		public Image Icon;
		public Action<CCTVConnectionState> OnStateChanged;
	}

	private void OnEnable() {
		foreach (CCTVPoint point in _cctvHub.CCTVPoints) {
			GameObject markerObject = Instantiate(_cctvMarkerPrefab, _markerParent);

			TMP_Text label = markerObject.GetComponentInChildren<TMP_Text>();
			if (label != null) {
				label.text = $"Cam {point.CameraNumber + 1:D2}";
			}

			MarkerInstance instance = new MarkerInstance {
				Point = point,
				RectTransform = markerObject.GetComponent<RectTransform>(),
				Icon = markerObject.transform.Find("CctvIcon").GetComponent<Image>()
			};

			// 상태가 바뀔 때마다 색을 갱신하고, 등록 시점 상태도 바로 반영
			instance.OnStateChanged = state => ApplyConnectionColor(instance.Icon, state);
			point.OnConnectionStateChanged += instance.OnStateChanged;
			ApplyConnectionColor(instance.Icon, point.ConnectionState);

			_markerInstances.Add(instance);
		}

		GameObject startPointMarkerObject = Instantiate(_startPointMarkerPrefab, _markerParent);
		_startPointMarkerRect = startPointMarkerObject.GetComponent<RectTransform>();
	}

	private void OnDisable() {
		foreach (MarkerInstance instance in _markerInstances) {
			instance.Point.OnConnectionStateChanged -= instance.OnStateChanged;
			Destroy(instance.RectTransform.gameObject);
		}
		_markerInstances.Clear();

		if (_startPointMarkerRect != null) {
			Destroy(_startPointMarkerRect.gameObject);
			_startPointMarkerRect = null;
		}
	}

	// 구역이 바뀌거나 대상이 움직일 수 있으므로 매 프레임 위치를 다시 계산한다.
	private void LateUpdate() {
		foreach (MarkerInstance instance in _markerInstances) {
			PlaceMarker(instance.RectTransform, instance.Point.transform.position);
		}

		if (_startPointMarkerRect != null) {
			PlaceMarker(_startPointMarkerRect, _startPoint.position);
		}
	}

	// 활성 구역 밖에 있는 대상은 미니맵에 올릴 자리가 없으므로 숨긴다.
	private void PlaceMarker(RectTransform markerRect, Vector3 worldPosition) {
		if (_minimapScreen.TryProjectToMap(worldPosition, out Vector2 anchoredPosition)) {
			markerRect.gameObject.SetActive(true);
			markerRect.anchoredPosition = anchoredPosition;
			return;
		}

		markerRect.gameObject.SetActive(false);
	}

	private static void ApplyConnectionColor(Image icon, CCTVConnectionState state) {
		if (icon == null) {
			return;
		}

		icon.color = state switch {
			CCTVConnectionState.Connected => ConnectedColor,
			CCTVConnectionState.Partial => PartialColor,
			_ => DisconnectedColor
		};
	}
}
