using System;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using Random = UnityEngine.Random;

public enum Role : byte
{
	Field,
	Headquarter,
	None
}

[RequireComponent(typeof(PlayerMoveSample),
	typeof(PlayerInventory),
	typeof(PlayerInteraction))]
[RequireComponent(typeof(PlayerRenderer))]
public class Player : NetworkBehaviour {
	// 외부에서 GetComponent<Player>() 후 바로 필요한 컴포넌트 찾아갈 수 있도록 컴포넌트 Public으로 노출
	[HideInInspector] public PlayerMoveSample PlayerMove;
	[HideInInspector] public PlayerInventory PlayerInventory;
	[HideInInspector] public PlayerInteraction PlayerInteraction;
	[HideInInspector] public PlayerInfoPresenter PlayerInfoPresenter;
	[HideInInspector] public PlayerRenderer PlayerRenderer;
	
	private bool _isNetworkStarted => NetworkManager != null && NetworkManager.Singleton.IsListening;

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
	
	private readonly NetworkVariable<Role> _playerRole = new NetworkVariable<Role>(
		Role.Field,
		NetworkVariableReadPermission.Everyone,
		NetworkVariableWritePermission.Server
	);
	
	// PlayerName을 가져오도록 하는 Property. 닉네임을 설정했으면 설정한 닉네임을 제공하고, 설정되지 않았다면 Player 1같은 값을 반환한다.
	public string PlayerName {
		get {
			if (!_isNetworkStarted) { throw new InvalidOperationException($"[Player] 네트워크에 연결되지 않았는데 PlayerName을 요청했습니다."); }
			if (_playerName.Value != null) { return _playerName.Value.ToString(); }
			return $"Player {OwnerClientId + 1}";
		}
	}
	
	public Color PlayerColor => _playerColor.Value;
	public Role PlayerRole {
		get => _playerRole.Value;
		set {
			if (!IsServer) {
				Debug.LogError("Role은 서버에서만 변경할 수 있습니다."); 
				return;
			}
			
			_playerRole.Value = value;
		}
	}
	
	private void Awake() {
		PlayerMove = GetComponent<PlayerMoveSample>();
		PlayerInventory = GetComponent<PlayerInventory>();
		PlayerInteraction = GetComponent<PlayerInteraction>();
		PlayerInfoPresenter = GetComponent<PlayerInfoPresenter>();
		PlayerRenderer = GetComponent<PlayerRenderer>();
		
		// 메인 카메라는 MinimapOnly인 레이어를 보지 못하도록
		Layers.HideLayerFromCamera(GetComponentInChildren<Camera>(), Layers.MinimapOnly);
	}

	public override void OnNetworkSpawn() {
		
		if (IsOwner) {
			// 색상은 처음 스폰 시에 랜덤하게 정한다. 추후 설정할 수 있게 해도 됨
			_playerColor.Value = Random.ColorHSV();
			
			// 스폰 시 내 머리 안보이게 해야 함
			PlayerRenderer.SetHeadObjectsLayer(Layers.LocalPlayerHead);
		}
		
		// 추후 이름 변경되거나, 색이 변경되면 알맞은 함수 호출하도록
		_playerName.OnValueChanged += PlayerInfoPresenter.HandlePlayerNameChanged;
		_playerColor.OnValueChanged += PlayerInfoPresenter.HandlePlayerColorChanged;
		PlayerInfoPresenter.HandlePlayerColorChanged(Color.white, _playerColor.Value);
		PlayerInfoPresenter.HandlePlayerNameChanged(null, PlayerName);
	}
}
