using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class MinimapMarkerController : MonoBehaviour {
	[Header("=== 미니맵 카메라 등록 ===")]
	[SerializeField] private Camera _minimapCamera;

	[Header("=== 마커 등록할 부모 Transform ===")]
	[SerializeField] private Transform _markerParent;

	[Header("=== 미니맵 마커 등록 ===")]
	[SerializeField] private GameObject _cctvMarkerPrefab;

	[Header("=== CCTV Hub 등록 ===")]
	[SerializeField] private CCTVHub _cctvHub;

	private static readonly Color ConnectedColor = Color.green;
	private static readonly Color PartialColor = Color.yellow;
	private static readonly Color DisconnectedColor = Color.red;

	private readonly List<MarkerInstance> _markerInstances = new List<MarkerInstance>();

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
	}

	private void OnDisable() {
		foreach (MarkerInstance instance in _markerInstances) {
			instance.Point.OnConnectionStateChanged -= instance.OnStateChanged;
			Destroy(instance.RectTransform.gameObject);
		}
		_markerInstances.Clear();
	}

	// 미니맵은 열려 있는 동안 드래그·줌으로 카메라가 계속 움직이므로 매 프레임 위치를 다시 계산한다.
	private void LateUpdate() {
		RectTransform displayRect = _markerParent.parent as RectTransform;
		if (displayRect == null) {
			return;
		}

		Rect rect = displayRect.rect;
		foreach (MarkerInstance instance in _markerInstances) {
			Vector3 viewportPoint = _minimapCamera.WorldToViewportPoint(instance.Point.transform.position);
			instance.RectTransform.anchoredPosition = new Vector2(
				(viewportPoint.x - 0.5f) * rect.width,
				(viewportPoint.y - 0.5f) * rect.height);
		}
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
