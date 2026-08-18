using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

// 인벤토리 핫바 위에서 다른 플레이어와 본부가 어느 방향에 있는지 알려주는 가로 바.
// 바의 중앙이 내 캐릭터의 정면이고 좌우 끝이 등 뒤(±180°)라, 360°가 끊김 없이 이어진다.
public sealed class PlayerLocatorBar : MonoBehaviour
{
	[Header("=== 마커가 놓일 가로 영역 ===")]
	[SerializeField] private RectTransform _barArea;

	[Header("=== 마커 프리팹 ===")]
	[SerializeField] private RectTransform _playerMarkerPrefab;
	[SerializeField] private RectTransform _hqMarkerPrefab;

	[Header("=== 높이 필터 ===")]
	// 지하실과 필드를 높이로 가른다. 본부는 훨씬 아래에 있어 본부 안에서는 아무 마커도 뜨지 않는다.
	[Tooltip("나와의 높이 차이가 이 값을 넘는 대상은 다른 층으로 보고 표시하지 않는다.")]
	[SerializeField, Min(0f)] private float _heightRange = 100f;

	[Header("=== 거리에 따른 흐려짐 ===")]
	[Tooltip("이 거리 이상 떨어지면 가장 흐린 상태가 된다. 사라지지는 않는다.")]
	[SerializeField, Min(1f)] private float _fadeDistance = 50f;
	[Tooltip("가장 멀 때 남길 선명도. 0이면 배경색에 완전히 묻힌다.")]
	[SerializeField, Range(0f, 1f)] private float _minVisibility = 0.3f;

	// 알파를 낮추면 겹쳐 그린 아이콘이 뒤쪽 사각형에 묻혀 아이콘만 사라진다.
	// 대신 마커 색을 이 색 쪽으로 섞어서 마커 전체가 함께 흐려지게 한다.
	[Tooltip("멀어질수록 마커가 섞여 들어갈 색. 보통 바 배경색으로 맞춘다.")]
	[SerializeField] private Color _fadeColor = new Color(0.1f, 0.1f, 0.14f, 1f);

	private readonly List<Player> _otherPlayers = new();
	private readonly List<Marker> _playerMarkers = new();

	private Player _localPlayer;
	private Transform _hqPoint;
	private Marker _hqMarker;

	// PlayScene은 라운드 시작 전 전원 스폰이 보장되므로 HpStatusUI와 같이 시작 시 한 번만 스캔한다.
	private void Start()
	{
		Player[] players = FindObjectsByType<Player>(FindObjectsSortMode.None);
		Array.Sort(players, (a, b) => a.OwnerClientId.CompareTo(b.OwnerClientId));

		foreach (Player player in players)
		{
			if (player.IsOwner)
			{
				_localPlayer = player;
				continue;
			}

			_otherPlayers.Add(player);
		}

		HqEntrance entrance = FindFirstObjectByType<HqEntrance>();
		_hqPoint = entrance != null ? entrance.transform : null;

		if (NetworkManager.Singleton != null)
		{
			NetworkManager.Singleton.OnConnectionEvent += HandleConnectionEvent;
		}
	}

	private void OnDestroy()
	{
		if (NetworkManager.Singleton != null)
		{
			NetworkManager.Singleton.OnConnectionEvent -= HandleConnectionEvent;
		}
	}

	// 나간 플레이어의 마커가 마지막 위치에 그대로 남지 않도록 목록에서 뺀다.
	private void HandleConnectionEvent(NetworkManager networkManager, ConnectionEventData data)
	{
		if (data.EventType != ConnectionEvent.ClientDisconnected &&
			data.EventType != ConnectionEvent.PeerDisconnected)
		{
			return;
		}

		_otherPlayers.RemoveAll(player => player == null || player.OwnerClientId == data.ClientId);
	}

	private void LateUpdate()    // 플레이어가 이동한 후에 마커를 갱신해야 하므로 LateUpdate에서 처리한다.
	{
		if (_localPlayer == null || _barArea == null)
		{
			HideAllMarkers();
			return;
		}

		Transform viewer = _localPlayer.transform;
		int usedMarkerCount = 0;

		foreach (Player player in _otherPlayers)
		{
			if (player == null || !IsWithinHeightRange(viewer.position, player.transform.position))
			{
				continue;
			}

			Marker marker = GetPlayerMarker(usedMarkerCount);
			usedMarkerCount++;

			// 색은 HP 바와 같은 출처(Player.PlayerColor)를 쓰고, 거리는 색이 옅어지는 정도로 표현한다.
			marker.SetRootBaseColor(player.PlayerColor);
			marker.ApplyFade(GetFadeAmount(viewer.position, player.transform.position), _fadeColor);
			PlaceMarker(marker.Rect, viewer, player.transform.position);
		}

		for (int i = usedMarkerCount; i < _playerMarkers.Count; i++)
		{
			_playerMarkers[i].SetActive(false);
		}

		UpdateHqMarker(viewer);
	}

	private void UpdateHqMarker(Transform viewer)
	{
		if (_hqMarkerPrefab == null || _hqPoint == null)
		{
			return;
		}

		if (!IsWithinHeightRange(viewer.position, _hqPoint.position))
		{
			_hqMarker?.SetActive(false);
			return;
		}

		if (_hqMarker == null)
		{
			_hqMarker = new Marker(Instantiate(_hqMarkerPrefab, _barArea));
		}

		_hqMarker.SetActive(true);
		_hqMarker.ApplyFade(GetFadeAmount(viewer.position, _hqPoint.position), _fadeColor);
		PlaceMarker(_hqMarker.Rect, viewer, _hqPoint.position);
	}

	// 정면을 0으로 두고 좌우 ±180°를 바의 폭 전체에 그대로 펼친다.
	private void PlaceMarker(RectTransform marker, Transform viewer, Vector3 targetPosition)
	{
		Vector3 forward = Vector3.ProjectOnPlane(viewer.forward, Vector3.up);
		Vector3 direction = Vector3.ProjectOnPlane(targetPosition - viewer.position, Vector3.up);

		if (forward.sqrMagnitude <= Mathf.Epsilon || direction.sqrMagnitude <= Mathf.Epsilon)
		{
			marker.anchoredPosition = Vector2.zero;
			return;
		}

		float angle = Vector3.SignedAngle(forward, direction, Vector3.up);
		marker.anchoredPosition = new Vector2(angle / 180f * (_barArea.rect.width * 0.5f), 0f);
	}

	// 0이면 원래 색 그대로, 1에 가까울수록 배경색에 묻힌다. 완전히 사라지지는 않는다.
	private float GetFadeAmount(Vector3 viewerPosition, Vector3 targetPosition)
	{
		float distance = Vector3.Distance(viewerPosition, targetPosition);
		return Mathf.Clamp01(distance / _fadeDistance) * (1f - _minVisibility);
	}

	// 높이 차이가 _heightRange를 넘으면 다른 층으로 보고 표시하지 않는다.
	private bool IsWithinHeightRange(Vector3 viewerPosition, Vector3 targetPosition)
	{
		return Mathf.Abs(targetPosition.y - viewerPosition.y) <= _heightRange;
	}

	private Marker GetPlayerMarker(int index)  // 플레이어 수가 바 영역에 표시할 수 있는 마커 수보다 많으면 새 마커를 생성한다.
	{
		while (_playerMarkers.Count <= index)
		{
			_playerMarkers.Add(new Marker(Instantiate(_playerMarkerPrefab, _barArea)));
		}

		Marker marker = _playerMarkers[index];
		marker.SetActive(true);
		return marker;
	}

	private void HideAllMarkers()   // 플레이어가 없거나 바 영역이 없으면 마커를 모두 숨긴다.
	{
		foreach (Marker marker in _playerMarkers)
		{
			marker.SetActive(false);
		}

		_hqMarker?.SetActive(false);
	}

	// 마커 하나가 가진 이미지들과 각자의 원래 색을 들고 있다가, 거리에 따라 함께 섞어준다.
	private sealed class Marker
	{
		private readonly Image[] _images;
		private readonly Color[] _baseColors;

		public RectTransform Rect { get; }

		public Marker(RectTransform rect)
		{
			Rect = rect;
			_images = rect.GetComponentsInChildren<Image>(true);
			_baseColors = new Color[_images.Length];

			for (int i = 0; i < _images.Length; i++)
			{
				_baseColors[i] = _images[i].color;
			}
		}

		public void SetActive(bool active)
		{
			Rect.gameObject.SetActive(active);
		}

		// 플레이어 색처럼 런타임에 정해지는 색은 루트 이미지의 기준색으로 갈아 끼운다.
		public void SetRootBaseColor(Color color)
		{
			if (_baseColors.Length > 0)
			{
				_baseColors[0] = color;
			}
		}

		public void ApplyFade(float amount, Color fadeColor)
		{
			for (int i = 0; i < _images.Length; i++)
			{
				_images[i].color = Color.Lerp(_baseColors[i], fadeColor, amount);
			}
		}
	}
}
