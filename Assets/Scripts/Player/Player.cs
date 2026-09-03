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
[RequireComponent(typeof(PlayerStamina))]
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
	[HideInInspector] public PlayerStamina PlayerStamina;

	public const int MaxPlayerNameLength = 8;

	// 오너가 자기 음성 상태를 다시 확인하는 간격.
	private const float VoiceStateRefreshSeconds = 0.1f;
	private float _nextVoiceStateRefreshTime;

	private bool _isNetworkStarted => NetworkManager != null && NetworkManager.Singleton.IsListening;

	// 현재 스폰되어 접속 중인 Player만 모아둔다. 클라이언트마다 로컬로 유지되며, 스폰/디스폰 시
	// 자신을 등록/해제하므로 FindObjectsByType 없이 전체 접속자 목록을 바로 조회할 수 있다.
	private static readonly List<Player> _activeInstances = new();
	public static IReadOnlyList<Player> ActiveInstances => _activeInstances;

	// 플레이어명. 모두 조회 가능하고, 중복 검사를 위해 서버만 수정한다.
	private readonly NetworkVariable<FixedString32Bytes> _playerName = new NetworkVariable<FixedString32Bytes>(
		null,
		NetworkVariableReadPermission.Everyone,
		NetworkVariableWritePermission.Server
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

	// 서버의 이름 요청 처리 결과를 요청한 본인에게만 알린다. true면 반영됐다.
	public event Action<bool> NameRequestResolved;

	// 말하기 시작·종료 시점을 알아야 하는 쪽(음성 오버레이)이 순서까지 정확히 받도록 전환을 그대로 흘려준다.
	// 매 갱신마다 전체 플레이어를 훑지 않아도 된다.
	public event Action<bool> SpeakingChanged;

	// 이름을 정하지 않은 플레이어에게 보여줄 기본 이름. 대기방 입력칸도 같은 규칙을 써야 한다.
	public static string GetDefaultName(ulong clientId) => $"Player {clientId + 1}";

	// PlayerName을 가져오도록 하는 Property. 닉네임을 설정했으면 설정한 닉네임을 제공하고, 설정되지 않았다면 Player 1같은 값을 반환한다.
	public string PlayerName
	{
		get
		{
			if (!_isNetworkStarted) { throw new InvalidOperationException($"[Player] 네트워크에 연결되지 않았는데 PlayerName을 요청했습니다."); }
			if (_playerName.Value != null) { return _playerName.Value.ToString(); }
			return GetDefaultName(OwnerClientId);
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
		PlayerStamina = GetComponent<PlayerStamina>();

		// 메인 카메라는 MinimapOnly인 레이어를 보지 못하도록
		Camera playerCamera = GetComponentInChildren<Camera>();
		Layers.HideLayerFromCamera(playerCamera, Layers.MinimapOnly);
		Layers.HideLayerFromCamera(playerCamera, Layers.CCTVPostProcessing);
		Layers.HideLayerFromCamera(playerCamera, Layers.NavMeshOnly);

		// 아이템 외곽선은 CCTV 화면에서만 보여야 하므로 1인칭 카메라의 Outliner에서는 해당 EPO 레이어를 끈다.
		Outliner playerOutliner = playerCamera != null ? playerCamera.GetComponent<Outliner>() : null;
		if (playerOutliner != null)
		{
			playerOutliner.OutlineLayerMask &= ~CctvHighlight.AllKindsMask;
		}
	}

	public void SetPlayerName(string playerName)
	{
		if (!IsOwner || string.IsNullOrWhiteSpace(playerName))
		{
			return;
		}

		// FixedString32Bytes는 담을 수 있는 바이트를 넘기면 예외를 던지므로, RPC 인자를 만들기 전에 자른다.
		string trimmedName = playerName.Trim();
		string limitedName = trimmedName.Length > MaxPlayerNameLength
			? trimmedName.Substring(0, MaxPlayerNameLength)
			: trimmedName;

		RequestPlayerNameRpc(new FixedString32Bytes(limitedName));
	}

	// 중복 검사는 전원의 이름을 볼 수 있는 서버만 할 수 있다. 소유자가 직접 쓰면
	// 두 사람이 같은 이름을 동시에 확정할 때 둘 다 통과한다.
	[Rpc(SendTo.Server)]
	private void RequestPlayerNameRpc(FixedString32Bytes requestedName)
	{
		bool accepted = !IsNameUsedByOtherPlayer(requestedName.ToString());
		if (accepted)
		{
			_playerName.Value = requestedName;
		}

		// 같은 이름을 다시 확정하면 값이 안 바뀌어 OnValueChanged가 발동하지 않는다.
		// 성공도 함께 알려야 요청한 쪽이 결과를 알 수 있다.
		NotifyNameResultOwnerRpc(accepted, RpcTarget.Single(OwnerClientId, RpcTargetUse.Temp));
	}

	// 표시되는 이름으로 비교한다. 이름을 정하지 않은 사람은 "Player 2"처럼 보이는데, 원본 값(빈 문자열)으로
	// 비교하면 그 이름을 다른 사람이 가져갈 수 있다. 자기 이름을 그대로 재확정하는 경우는 중복이 아니다.
	private bool IsNameUsedByOtherPlayer(string requestedName)
	{
		foreach (Player player in _activeInstances)
		{
			if (player != this && player.PlayerName == requestedName)
			{
				return true;
			}
		}

		return false;
	}

	// 요청한 본인만 결과를 알아야 한다. 남이 어떤 이름을 시도했다 실패했는지는 알 필요가 없다.
	[Rpc(SendTo.SpecifiedInParams)]
	private void NotifyNameResultOwnerRpc(bool accepted, RpcParams rpcParams = default)
	{
		NameRequestResolved?.Invoke(accepted);
	}

	public override void OnNetworkSpawn()
	{
		_activeInstances.Add(this);

		if (IsOwner)
		{
			// 색상은 처음 스폰 시에 랜덤하게 정한다. 추후 설정할 수 있게 해도 됨
			_playerColor.Value = Random.ColorHSV();

			// 1인칭 화면에서는 내 몸이 보이면 안 된다.
			// 손은 PlayerCameraController 와 PlayerItemIK 가 따로 관리한다.
			PlayerRenderer.SetLocalBodyLayer(Layers.LocalPlayerHead);
			// 몸이 숨겨지면 그림자도 같이 사라지므로 전용 그림자 캐스터를 켠다
			PlayerRenderer.SetBodyShadowCastersActive(true);
		}

		_playerName.OnValueChanged += HandlePlayerNameChanged;
		_playerColor.OnValueChanged += HandlePlayerColorChanged;
		_isSpeaking.OnValueChanged += HandleSpeakingChanged;

		PlayerNameChanged += PlayerInfoPresenter.HandlePlayerNameChanged;

		// 접속 시 한번 적용하기
		PlayerInfoPresenter.HandlePlayerNameChanged(null, PlayerName);
	}

	public override void OnNetworkDespawn()
	{
		_playerName.OnValueChanged -= HandlePlayerNameChanged;
		_playerColor.OnValueChanged -= HandlePlayerColorChanged;
		_isSpeaking.OnValueChanged -= HandleSpeakingChanged;
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

	private void HandleSpeakingChanged(bool previousValue, bool newValue)
	{
		SpeakingChanged?.Invoke(newValue);
	}
}
