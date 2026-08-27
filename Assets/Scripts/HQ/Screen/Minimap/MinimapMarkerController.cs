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

	[Header("=== CCTV 화면 컨트롤러 등록 ===")]
	[SerializeField] private HqScreenController _hqScreenController;

	[Header("=== 본부(StartPoint) 마커 등록 ===")]
	[SerializeField] private Transform _startPoint;
	[SerializeField] private GameObject _startPointMarkerPrefab;

	[Header("=== 플레이어(사람) 마커 등록 ===")]
	[SerializeField] private GameObject _playerMarkerPrefab;

	[Header("=== 외계인 마커 ===")]
	[Tooltip("점 모양 스프라이트. 플레이어 마커와 같은 원형 프레임을 쓴다.")]
	[SerializeField] private Sprite _alienMarkerSprite;

	[Tooltip("발소리가 사람에게 닿았을 때 지도에 찍히는 점의 색.")]
	[SerializeField] private Color _alienMarkerColor = Color.red;

	[Tooltip("점의 크기(px). 다른 마커와 같은 규칙으로 지도 배율에 맞춰 조정된다.")]
	[SerializeField, Min(1f)] private float _alienMarkerSize = 20f;

	[Tooltip("발소리가 끊긴 뒤 점을 남겨두는 시간(초). 걸음 간격(0.45~0.7초)보다 길어야 점이 깜빡이지 않는다.")]
	[SerializeField, Min(0f)] private float _alienMarkerLingerSeconds = 2f;

	[Tooltip("한 발소리 지점에서 다음 지점까지 미끄러지는 데 걸리는 대략의 시간(초). 짧을수록 튀고, 길수록 늦게 따라온다.")]
	[SerializeField, Min(0f)] private float _alienMarkerSmoothSeconds = 0.35f;

	[Header("=== 마커 크기 ===")]
	[Tooltip("마커 하나가 덮을 월드 크기(유닛). 지도 배율에 맞춰 마커도 같이 커지고 작아진다.")]
	[SerializeField, Min(0.1f)] private float _markerWorldSize = 8f;

	[Tooltip("마커가 지나치게 작아지거나 커지지 않도록 제한하는 배율 범위.")]
	[SerializeField] private Vector2 _markerScaleRange = new Vector2(0.3f, 1.2f);

	private static readonly Color ConnectedColor = Color.green;
	private static readonly Color PartialColor = Color.yellow;
	private static readonly Color DisconnectedColor = Color.red;

	private readonly List<MarkerInstance> _markerInstances = new List<MarkerInstance>();
	private readonly Dictionary<Player, PlayerMarker> _playerMarkers = new Dictionary<Player, PlayerMarker>();
	private readonly List<Player> _stalePlayers = new List<Player>();
	private RectTransform _startPointMarkerRect;

	private RectTransform _alienMarkerRect;
	private Vector3 _alienHeardPosition;
	private Vector3 _alienMarkerPosition;
	private Vector3 _alienMarkerVelocity;
	private float _alienMarkerRemainingSeconds;

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
		public CctvMapMarker ClickTarget;
		public Action<CCTVConnectionState> OnStateChanged;
		public Action OnClicked;
	}

	private void OnEnable() {
		// CCTVHub의 포인트 목록은 구역이 활성화될 때 통째로 바뀌므로, 그때마다 마커를 다시 만든다.
		_cctvHub.OnCctvPointsActivated += RebuildCctvMarkers;
		RebuildCctvMarkers();

		GameObject startPointMarkerObject = Instantiate(_startPointMarkerPrefab, _markerParent);
		_startPointMarkerRect = startPointMarkerObject.GetComponent<RectTransform>();

		_alienMarkerRect = CreateAlienMarker();
		BossController.FootstepPlayed += HandleAlienFootstep;
	}

	private void OnDisable() {
		_cctvHub.OnCctvPointsActivated -= RebuildCctvMarkers;
		ClearCctvMarkers();

		BossController.FootstepPlayed -= HandleAlienFootstep;

		if (_alienMarkerRect != null) {
			Destroy(_alienMarkerRect.gameObject);
			_alienMarkerRect = null;
		}
		_alienMarkerRemainingSeconds = 0f;

		if (_startPointMarkerRect != null) {
			Destroy(_startPointMarkerRect.gameObject);
			_startPointMarkerRect = null;
		}

		foreach (PlayerMarker marker in _playerMarkers.Values) {
			Destroy(marker.RectTransform.gameObject);
		}
		_playerMarkers.Clear();
	}

	private void RebuildCctvMarkers() {
		ClearCctvMarkers();

		foreach (CCTVPoint point in _cctvHub.CCTVPoints) {
			GameObject markerObject = Instantiate(_cctvMarkerPrefab, _markerParent);

			TMP_Text label = markerObject.GetComponentInChildren<TMP_Text>();
			if (label != null) {
				label.text = $"Cam {point.CameraNumber + 1:D2}";
			}

			MarkerInstance instance = new MarkerInstance {
				Point = point,
				RectTransform = markerObject.GetComponent<RectTransform>(),
				Icon = markerObject.transform.Find("CctvIcon").GetComponent<Image>(),
				ClickTarget = markerObject.AddComponent<CctvMapMarker>()
			};

			// 상태가 바뀔 때마다 색을 갱신하고, 등록 시점 상태도 바로 반영
			instance.OnStateChanged = state => ApplyConnectionColor(instance.Icon, state);
			point.OnConnectionStateChanged += instance.OnStateChanged;
			ApplyConnectionColor(instance.Icon, point.ConnectionState);

			instance.OnClicked = () => HandleCctvMarkerClicked(point);
			instance.ClickTarget.Clicked += instance.OnClicked;

			_markerInstances.Add(instance);
		}
	}

	private void ClearCctvMarkers() {
		foreach (MarkerInstance instance in _markerInstances) {
			instance.Point.OnConnectionStateChanged -= instance.OnStateChanged;
			instance.ClickTarget.Clicked -= instance.OnClicked;
			Destroy(instance.RectTransform.gameObject);
		}
		_markerInstances.Clear();
	}

	// 마커 클릭: 카메라를 해당 CCTV로 옮기고 CCTV 화면을 연다.
	private void HandleCctvMarkerClicked(CCTVPoint point) {
		_cctvHub.SwitchToIndex(point.CameraNumber);

		if (_hqScreenController == null) {
			Debug.LogWarning("[MinimapMarkerController] HqScreenController가 등록되지 않아 CCTV 화면을 열 수 없습니다.", this);
			return;
		}

		_hqScreenController.OpenCctvScreen();
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
		UpdateAlienMarker();
	}

	// 발소리는 낸 쪽이 알려주고, 그 소리가 사람에게 닿았는지는 여기서 판단한다.
	// 외계인 위치도 사람 위치도 이미 동기화돼 있어 통신 없이 각자 계산할 수 있다.
	private void HandleAlienFootstep(Vector3 position, SoundKey key) {
		SoundManager soundManager = SoundManager.Instance;
		if (soundManager == null) {
			return;
		}

		float audibleRange = soundManager.GetMaxDistance(key);
		if (audibleRange <= 0f) {
			return;
		}

		float sqrRange = audibleRange * audibleRange;
		foreach (Player player in Player.ActiveInstances) {
			if (player == null ||
				(player.transform.position - position).sqrMagnitude > sqrRange) {
				continue;
			}

			// 꺼져 있던 점이 다시 켜질 때는 지도를 가로질러 미끄러져 오면 안 된다. 그때는 바로 그 자리에 찍는다.
			if (_alienMarkerRemainingSeconds <= 0f) {
				_alienMarkerPosition = position;
				_alienMarkerVelocity = Vector3.zero;
			}

			// 점이 짚는 것은 외계인이 아니라 방금 들린 발소리 지점이다.
			_alienHeardPosition = position;
			_alienMarkerRemainingSeconds = _alienMarkerLingerSeconds;
			return;
		}
	}

	// 마지막으로 들린 발소리 자리로 점을 옮긴다. 걸음마다 순간이동하면 눈에 띄게 튀므로
	// 지점 사이를 미끄러지게 한다. 보여주는 자리는 여전히 들린 지점뿐이고, 한 걸음만큼 늦게 따라온다.
	// 걸음과 걸음 사이에 점이 깜빡이지 않도록 소리가 끊긴 뒤에도 잠깐 유지한다.
	private void UpdateAlienMarker() {
		if (_alienMarkerRect == null) {
			return;
		}

		if (_alienMarkerRemainingSeconds <= 0f) {
			_alienMarkerRect.gameObject.SetActive(false);
			return;
		}

		_alienMarkerRemainingSeconds -= Time.deltaTime;
		_alienMarkerPosition = Vector3.SmoothDamp(
			_alienMarkerPosition,
			_alienHeardPosition,
			ref _alienMarkerVelocity,
			_alienMarkerSmoothSeconds);
		PlaceMarker(_alienMarkerRect, _alienMarkerPosition);
	}

	// 점 하나가 전부라 프리팹을 따로 두지 않고 여기서 만든다.
	// 앵커·피벗은 다른 마커 프리팹과 같은 중앙 기준이어야 TryProjectToMap의 좌표가 맞는다.
	private RectTransform CreateAlienMarker() {
		GameObject markerObject = new GameObject("AlienMarker", typeof(RectTransform), typeof(Image));
		RectTransform markerRect = (RectTransform)markerObject.transform;
		markerRect.SetParent(_markerParent, false);
		markerRect.anchorMin = new Vector2(0.5f, 0.5f);
		markerRect.anchorMax = new Vector2(0.5f, 0.5f);
		markerRect.pivot = new Vector2(0.5f, 0.5f);
		markerRect.sizeDelta = new Vector2(_alienMarkerSize, _alienMarkerSize);

		Image icon = markerObject.GetComponent<Image>();
		icon.sprite = _alienMarkerSprite;
		icon.color = _alienMarkerColor;
		icon.raycastTarget = false;

		markerObject.SetActive(false);
		return markerRect;
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
			ApplyMarkerScale(markerRect);
			return true;
		}

		markerRect.gameObject.SetActive(false);
		return false;
	}

	// 지하 맵처럼 넓은 지도는 배율이 작아지는데 마커는 픽셀 크기가 고정이라 상대적으로 너무 커진다.
	// 마커가 항상 같은 월드 크기를 덮도록 배율을 맞춘다.
	private void ApplyMarkerScale(RectTransform markerRect) {
		if (!_minimapScreen.TryGetWorldToMapScale(out float worldToMap)) {
			return;
		}

		float nativeSize = markerRect.rect.width;
		if (nativeSize <= 0f) {
			return;
		}

		float scale = Mathf.Clamp(
			_markerWorldSize * worldToMap / nativeSize,
			_markerScaleRange.x,
			_markerScaleRange.y);
		markerRect.localScale = Vector3.one * scale;
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
