using System;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

[RequireComponent(typeof(PlayerMoveSample), typeof(PlayerInventory), typeof(PlayerInteraction))]
public class Player : NetworkBehaviour {
	// 외부에서 GetComponent<Player>() 후 바로 필요한 컴포넌트 찾아갈 수 있도록 컴포넌트 Public으로 노출
	[HideInInspector] public PlayerMoveSample PlayerMove;
	[HideInInspector] public PlayerInventory PlayerInventory;
	[HideInInspector] public PlayerInteraction PlayerInteraction;
	[HideInInspector] public PlayerNamePresenter PlayerNamePresenter;
	
	private bool _isNetworkStarted => NetworkManager != null && NetworkManager.Singleton.IsListening;

	// 플레이어명. 모두 조회 가능하고, 자기 자신만 수정 가능하도록
	private readonly NetworkVariable<FixedString32Bytes> _playerName = new NetworkVariable<FixedString32Bytes>(
		null,
		NetworkVariableReadPermission.Everyone,
		NetworkVariableWritePermission.Owner
	);
	
	private void Awake() {
		PlayerMove = GetComponent<PlayerMoveSample>();
		PlayerInventory = GetComponent<PlayerInventory>();
		PlayerInteraction = GetComponent<PlayerInteraction>();
		PlayerNamePresenter = GetComponent<PlayerNamePresenter>();
		
		// 메인 카메라는 MinimapOnly인 레이어를 보지 못하도록
		Layers.HideLayerFromCamera(GetComponentInChildren<Camera>(), Layers.MinimapOnly);
	}

	public override void OnNetworkSpawn() {
		_playerName.OnValueChanged += PlayerNamePresenter.HandlePlayerNameChanged;
		
		PlayerNamePresenter.HandlePlayerNameChanged(null, PlayerName);
	}

	// PlayerName을 가져오도록 하는 Property. 닉네임을 설정했으면 설정한 닉네임을 제공하고, 설정되지 않았다면 Player 1같은 값을 반환한다.
	public string PlayerName {
		get {
			if (!_isNetworkStarted) { throw new InvalidOperationException($"[Player] 네트워크에 연결되지 않았는데 PlayerName을 요청했습니다."); }
			if (_playerName.Value != null) { return _playerName.Value.ToString(); }
			return $"Player {OwnerClientId + 1}";
		}
	}
}
