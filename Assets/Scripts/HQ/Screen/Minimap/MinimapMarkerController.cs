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

	[Header("=== 플레이어(사람) 마커 등록 ===")]
	[SerializeField] private GameObject _playerMarkerPrefab;

	private static readonly Color ConnectedColor = Color.green;
	private static readonly Color PartialColor = Color.yellow;
	private static readonly Color DisconnectedColor = Color.red;

	private readonly List<MarkerInstance> _markerInstances = new List<MarkerInstance>();
	private readonly Dictionary<Player, PlayerMarker> _playerMarkers = new Dictionary<Player, PlayerMarker>();
	private readonly List<Player> _stalePlayers = new List<Player>();
	private RectTransform _startPointMarkerRect;

	// 플레이어 마커는 접속·퇴장에 따라 수가 바뀌므로 Player별로 들고 다닌다.
	private class PlayerMarker {
		public RectTransform RectTransform;
		public Image Icon;
		public RectTransform DirectionArrow;
		public TMP_Text Label;
	}

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

		foreach (PlayerMarker marker in _playerMarkers.Values) {
			Destroy(marker.RectTransform.gameObject);
		}
		_playerMarkers.Clear();
	}

	// 구역이 바뀌거나 대상이 움직일 수 있으므로 매 프레임 위치를 다시 계산한다.
	private void LateUpdate() {
		foreach (MarkerInstance instance in _markerInstances) {
			PlaceMarker(instance.RectTransform, instance.Point.transform.position);
		}

		if (_startPointMarkerRect != null) {
			PlaceMarker(_startPointMarkerRect, _startPoint.position);
		}

		UpdatePlayerMarkers();
	}

	// 접속 중인 플레이어 수만큼 마커를 맞춰두고 위치·색·이름·바라보는 방향을 갱신한다.
	private void UpdatePlayerMarkers() {
		if (_playerMarkerPrefab == null) {
			return;
		}

		foreach (Player player in Player.ActiveInstances) {
			if (!_playerMarkers.TryGetValue(player, out PlayerMarker marker)) {
				marker = CreatePlayerMarker();
				_playerMarkers.Add(player, marker);
			}

			if (!PlaceMarker(marker.RectTransform, player.transform.position)) {
				continue;
			}

			if (marker.Icon != null) {
				marker.Icon.color = player.PlayerColor;
			}

			if (marker.Label != null) {
				marker.Label.text = player.PlayerName;
			}

			// 마커는 미니맵 회전을 상쇄해 똑바로 서 있으므로, 화살표는 화면 기준 각도만 주면 된다.
			if (marker.DirectionArrow != null) {
				Vector3 forward = player.transform.forward;
				float screenAngle = -Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg;
				marker.DirectionArrow.localRotation = Quaternion.Euler(0f, 0f, screenAngle);
			}
		}

		// 퇴장·디스폰한 플레이어의 마커를 치운다. ActiveInstances에서 빠지면 더 이상 표시할 대상이 아니다.
		foreach (KeyValuePair<Player, PlayerMarker> pair in _playerMarkers) {
			if (pair.Key == null || !IsActive(pair.Key)) {
				_stalePlayers.Add(pair.Key);
			}
		}

		foreach (Player stale in _stalePlayers) {
			if (_playerMarkers.TryGetValue(stale, out PlayerMarker marker)) {
				Destroy(marker.RectTransform.gameObject);
				_playerMarkers.Remove(stale);
			}
		}
		_stalePlayers.Clear();
	}

	private static bool IsActive(Player player) {
		IReadOnlyList<Player> players = Player.ActiveInstances;
		for (int i = 0; i < players.Count; i++) {
			if (players[i] == player) {
				return true;
			}
		}

		return false;
	}

	private PlayerMarker CreatePlayerMarker() {
		GameObject markerObject = Instantiate(_playerMarkerPrefab, _markerParent);
		return new PlayerMarker {
			RectTransform = markerObject.GetComponent<RectTransform>(),
			Icon = markerObject.transform.Find("Icon")?.GetComponent<Image>(),
			DirectionArrow = markerObject.transform.Find("DirectionArrow") as RectTransform,
			Label = markerObject.GetComponentInChildren<TMP_Text>()
		};
	}

	// 활성 구역 밖에 있는 대상은 미니맵에 올릴 자리가 없으므로 숨긴다.
	private bool PlaceMarker(RectTransform markerRect, Vector3 worldPosition) {
		if (_minimapScreen.TryProjectToMap(worldPosition, out Vector2 anchoredPosition)) {
			markerRect.gameObject.SetActive(true);
			markerRect.anchoredPosition = anchoredPosition;
			// 미니맵이 돌아간 구역에서도 아이콘과 라벨은 똑바로 보이게 한다.
			markerRect.localRotation = _minimapScreen.MarkerCounterRotation;
			return true;
		}

		markerRect.gameObject.SetActive(false);
		return false;
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
