using System;
using System.Collections.Generic;
using System.ComponentModel;
using EPOOutline;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using Random = UnityEngine.Random;

[RequireComponent(typeof(PlayerMoveSample),
	typeof(PlayerInventory),
	typeof(PlayerInteraction))]
[RequireComponent(typeof(PlayerHealth))]
[RequireComponent(typeof(PlayerRenderer))]
public class Player : NetworkBehaviour
{
	// 외부에서 GetComponent<Player>() 후 바로 필요한 컴포넌트 찾아갈 수 있도록 컴포넌트 Public으로 노출
	[HideInInspector] public PlayerMoveSample PlayerMove;
	[HideInInspector] public PlayerInventory PlayerInventory;
	[HideInInspector] public PlayerInteraction PlayerInteraction;
	[HideInInspector] public PlayerInfoPresenter PlayerInfoPresenter;
	[HideInInspector] public PlayerRenderer PlayerRenderer;
	[HideInInspector] public PlayerHealth PlayerHealth;

	public const int MaxPlayerNameLength = 6;

	// 오너가 자기 음성 상태를 다시 확인하는 간격.
	private const float VoiceStateRefreshSeconds = 0.1f;
	private float _nextVoiceStateRefreshTime;

	private bool _isNetworkStarted => NetworkManager != null && NetworkManager.Singleton.IsListening;

	// 현재 스폰되어 접속 중인 Player만 모아둔다. 클라이언트마다 로컬로 유지되며, 스폰/디스폰 시
	// 자신을 등록/해제하므로 FindObjectsByType 없이 전체 접속자 목록을 바로 조회할 수 있다.
	private static readonly List<Player> _activeInstances = new();
	public static IReadOnlyList<Player> ActiveInstances => _activeInstances;

	// 플레이어명. 모두 조회 가능하고, 자기 자신만 수정 가능하도록
	private readonly NetworkVariable<FixedString32Bytes> _playerName = new NetworkVariable<FixedString32Bytes>(
		null,
		NetworkVariableReadPermission.Everyone,
		NetworkVariableWritePermission.Owner
	);

	// 플레이어가 사용할 색상
	private readonly NetworkVariable<Color> _playerColor = new NetworkVariable<Color>(
		Color.white,
		NetworkVariableReadPermission.Everyone,
		NetworkVariableWritePermission.Owner
	);

	// 음성은 NGO가 아니라 Vivox가 따로 관리하고, Vivox는 자기 상태만 확실하게 알려준다.
	// 그래서 각자 자기 음성 상태를 판단해 여기 올려두고, 다른 피어는 이 값만 읽는다.
	// (Vivox 참가자와 화면의 플레이어를 ID로 맞추는 방식은 오디오가 도달해야만 동작해서 쓰지 않는다.)
	private readonly NetworkVariable<bool> _isSpeaking = new NetworkVariable<bool>(
		false,
		NetworkVariableReadPermission.Everyone,
		NetworkVariableWritePermission.Owner
	);

	private readonly NetworkVariable<bool> _micMuted = new NetworkVariable<bool>(
		false,
		NetworkVariableReadPermission.Everyone,
		NetworkVariableWritePermission.Owner
	);

	public bool IsSpeaking => _isSpeaking.Value;
	public bool IsMicMuted => _micMuted.Value;

	// 외부에서 변경 감지 구독
	public event Action<FixedString32Bytes, FixedString32Bytes> PlayerNameChanged;
	public event Action<Color, Color> PlayerColorChanged;

	// PlayerName을 가져오도록 하는 Property. 닉네임을 설정했으면 설정한 닉네임을 제공하고, 설정되지 않았다면 Player 1같은 값을 반환한다.
	public string PlayerName
	{
		get
		{
			if (!_isNetworkStarted) { throw new InvalidOperationException($"[Player] 네트워크에 연결되지 않았는데 PlayerName을 요청했습니다."); }
			if (_playerName.Value != null) { return _playerName.Value.ToString(); }
			return $"Player {OwnerClientId + 1}";
		}
	}

	public Color PlayerColor => _playerColor.Value;

	private void Awake()
	{
		PlayerMove = GetComponent<PlayerMoveSample>();
		PlayerInventory = GetComponent<PlayerInventory>();
		PlayerInteraction = GetComponent<PlayerInteraction>();
		PlayerInfoPresenter = GetComponent<PlayerInfoPresenter>();
		PlayerRenderer = GetComponent<PlayerRenderer>();
		PlayerHealth = GetComponent<PlayerHealth>();

		// 메인 카메라는 MinimapOnly인 레이어를 보지 못하도록
		Camera playerCamera = GetComponentInChildren<Camera>();
		Layers.HideLayerFromCamera(playerCamera, Layers.MinimapOnly);
		Layers.HideLayerFromCamera(playerCamera, Layers.CCTVPostProcessing);

		// 아이템 외곽선은 CCTV 화면에서만 보여야 하므로 1인칭 카메라의 Outliner에서는 해당 EPO 레이어를 끈다.
		Outliner playerOutliner = playerCamera != null ? playerCamera.GetComponent<Outliner>() : null;
		if (playerOutliner != null)
		{
			playerOutliner.OutlineLayerMask &= ~ItemBase.CctvOutlineMask;
		}
	}

	public void SetPlayerName(string playerName)
	{
		if (!IsOwner || string.IsNullOrWhiteSpace(playerName))
		{
			return;
		}

		string limitedName = playerName.Length > MaxPlayerNameLength
			? playerName.Substring(0, MaxPlayerNameLength)
			: playerName;

		_playerName.Value = new FixedString32Bytes(limitedName);
	}

	public override void OnNetworkSpawn()
	{
		_activeInstances.Add(this);

		if (IsOwner)
		{
			// 색상은 처음 스폰 시에 랜덤하게 정한다. 추후 설정할 수 있게 해도 됨
			_playerColor.Value = Random.ColorHSV();

			// 스폰 시 내 머리 안보이게 해야 함
			PlayerRenderer.SetHeadObjectsLayer(Layers.LocalPlayerHead);
		}

		_playerName.OnValueChanged += HandlePlayerNameChanged;
		_playerColor.OnValueChanged += HandlePlayerColorChanged;

		PlayerNameChanged += PlayerInfoPresenter.HandlePlayerNameChanged;
		PlayerColorChanged += PlayerInfoPresenter.HandlePlayerColorChanged;

		// 접속 시 한번 적용하기
		PlayerInfoPresenter.HandlePlayerColorChanged(Color.white, _playerColor.Value);
		PlayerInfoPresenter.HandlePlayerNameChanged(null, PlayerName);
	}

	public override void OnNetworkDespawn()
	{
		_playerName.OnValueChanged -= HandlePlayerNameChanged;
		_playerColor.OnValueChanged -= HandlePlayerColorChanged;
		_activeInstances.Remove(this);
	}

	// Vivox는 음성 상태를 로컬에만 알려주고 뮤트가 바뀌는 경로도 여러 곳이라(토글·마이크 테스트 시작·종료),
	// 오너가 자기 상태를 짧은 주기로 확인해 공유한다. NetworkVariable은 값이 바뀔 때만 전송된다.
	private void Update()
	{
		if (!IsOwner || !IsSpawned || Time.unscaledTime < _nextVoiceStateRefreshTime)
		{
			return;
		}

		_nextVoiceStateRefreshTime = Time.unscaledTime + VoiceStateRefreshSeconds;

		VivoxManager manager = VivoxManager.Instance;
		if (manager == null)
		{
			return;
		}

		// 마이크 테스트 중이면 팀원에게 안 들리는 상태이므로 뮤트로 표시한다.
		bool isMicMuted = manager.IsMutedForSessionChannel;
		bool isSpeaking = manager.IsLocalSpeaking;

		if (_micMuted.Value != isMicMuted)
		{
			_micMuted.Value = isMicMuted;
		}

		if (_isSpeaking.Value != isSpeaking)
		{
			_isSpeaking.Value = isSpeaking;
		}
	}

	private void HandlePlayerNameChanged(
		FixedString32Bytes previousValue,
		FixedString32Bytes newValue)
	{
		PlayerNameChanged?.Invoke(previousValue, newValue);
	}

	private void HandlePlayerColorChanged(Color previousValue, Color newValue)
	{
		PlayerColorChanged?.Invoke(previousValue, newValue);
	}
}
