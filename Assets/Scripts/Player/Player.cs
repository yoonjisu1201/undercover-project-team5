using System;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

[RequireComponent(typeof(PlayerMoveSample), typeof(PlayerInventory), typeof(PlayerInteraction))]
public class Player : NetworkBehaviour {
	// 외부에서 Player를 기반으로 Player 검색 후 바로 필요한 컴포넌트 찾아갈 수 있도록
	[HideInInspector] public PlayerMoveSample PlayerMove;
	[HideInInspector] public PlayerInventory PlayerInventory;
	[HideInInspector] public PlayerInteraction PlayerInteraction;
	[HideInInspector] public PlayerNamePresenter PlayerNamePresenter;
	
	private bool _isNetworkStarted => NetworkManager != null && NetworkManager.Singleton.IsListening;
	
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

		Layers.HideLayerFromCamera(GetComponentInChildren<Camera>(), Layers.MinimapOnly);
	}

	public override void OnNetworkSpawn() {
		_playerName.OnValueChanged += PlayerNamePresenter.HandlePlayerNameChanged;
		
		PlayerNamePresenter.HandlePlayerNameChanged(null, PlayerName);
	}
	
	public string PlayerName {
		get {
			if (!_isNetworkStarted) { throw new InvalidOperationException($"[Player] 네트워크에 연결되지 않았는데 PlayerName을 요청했습니다."); }
			if (_playerName.Value != null) { return _playerName.Value.ToString(); }
			return $"Player {OwnerClientId + 1}";
		}
	}
}