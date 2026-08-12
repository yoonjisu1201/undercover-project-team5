using System;
using System.Collections.Generic;
using System.ComponentModel;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using Random = UnityEngine.Random;

[RequireComponent(typeof(PlayerMoveSample),
	typeof(PlayerInventory),
	typeof(PlayerInteraction))]
[RequireComponent(typeof(PlayerHealth))]
[RequireComponent(typeof(PlayerRenderer))]
public class Player : NetworkBehaviour {
	// 외부에서 GetComponent<Player>() 후 바로 필요한 컴포넌트 찾아갈 수 있도록 컴포넌트 Public으로 노출
	[HideInInspector] public PlayerMoveSample PlayerMove;
	[HideInInspector] public PlayerInventory PlayerInventory;
	[HideInInspector] public PlayerInteraction PlayerInteraction;
	[HideInInspector] public PlayerInfoPresenter PlayerInfoPresenter;
	[HideInInspector] public PlayerRenderer PlayerRenderer;
	[HideInInspector] public PlayerHealth PlayerHealth;

    public const int MaxPlayerNameLength = 6;

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
	private readonly NetworkVariable<Color> _playerColor =  new NetworkVariable<Color>(
		Color.white,
		NetworkVariableReadPermission.Everyone,
		NetworkVariableWritePermission.Owner
	);
	
	// 외부에서 변경 감지 구독
	public event Action<FixedString32Bytes, FixedString32Bytes> PlayerNameChanged;
	public event Action<Color, Color> PlayerColorChanged;

	// PlayerName을 가져오도록 하는 Property. 닉네임을 설정했으면 설정한 닉네임을 제공하고, 설정되지 않았다면 Player 1같은 값을 반환한다.
	public string PlayerName {
		get {
			if (!_isNetworkStarted) { throw new InvalidOperationException($"[Player] 네트워크에 연결되지 않았는데 PlayerName을 요청했습니다."); }
			if (_playerName.Value != null) { return _playerName.Value.ToString(); }
			return $"Player {OwnerClientId + 1}";
		}
	}
	
	public Color PlayerColor => _playerColor.Value;
	
	private void Awake() {
		PlayerMove = GetComponent<PlayerMoveSample>();
		PlayerInventory = GetComponent<PlayerInventory>();
		PlayerInteraction = GetComponent<PlayerInteraction>();
		PlayerInfoPresenter = GetComponent<PlayerInfoPresenter>();
		PlayerRenderer = GetComponent<PlayerRenderer>();
		PlayerHealth = GetComponent<PlayerHealth>();

		// 메인 카메라는 MinimapOnly인 레이어를 보지 못하도록
		Layers.HideLayerFromCamera(GetComponentInChildren<Camera>(), Layers.MinimapOnly);
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

    public override void OnNetworkSpawn() {
		_activeInstances.Add(this);

		if (IsOwner) {
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

	public override void OnNetworkDespawn() {
		_playerName.OnValueChanged -= HandlePlayerNameChanged;
		_playerColor.OnValueChanged -= HandlePlayerColorChanged;
		_activeInstances.Remove(this);
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
