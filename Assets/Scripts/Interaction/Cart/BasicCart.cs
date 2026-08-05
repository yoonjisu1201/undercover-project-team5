using System;
using System.Collections.Generic;
using UnityEngine;

public class BasicCart : CartBase {

	[Header("=== 잡았을 때의 스케일 비율 ===")]
	[SerializeField] private float _holdingScaleMultiplier = 0.7f;
	
	[Header("=== 카트 Transform 등록(잡았을 때 사이즈 줄이기 위해) ===")]
	[SerializeField] private MeshRenderer _cartRenderer;

	[Header("=== 힐 영역 Collider 등록 ===")]
	[SerializeField] private AreaCollider _areaCollider;

	[Header("=== 힐 영역 내부 플레이어들 체력 얼마나 찰지 ===")] 
	[SerializeField] private float _healingAmount = 15f;
	
	private List<Player> _playersInHealingArea = new(); 
	
	public override string CartName => "";
	
	private float _originalScale;

	protected override void Awake() {
		base.Awake();
		
		// 시작 시에 원래 스케일 저장
		_originalScale = _cartRenderer.transform.localScale.x;
	}

	public override void OnNetworkSpawn() {
		base.OnNetworkSpawn();
		
		// 체력 회복 영역에 플레이어 들어오고 나가는 경우 사용될 이벤트 구독
		// 서버만 구독하면 된다. 체력은 서버 권한으로 관리될 것이기 때문
		if (!IsServer) { return; }
		_areaCollider.OnPlayerEnter += HandlePlayerEnter;
		_areaCollider.OnPlayerExit += HandlePlayerExit;
		_areaCollider.Initialize();
	}


	// 기본 카트는 HolderId가 변경되었을 때 해야 할 추가적인 조치(내가 잡았을 땐 사이즈 줄이기)가 있어 override
	protected override void HandleHolderIdChanged(ulong oldId, ulong newId) {
		base.HandleHolderIdChanged(oldId, newId);
		
		// 내가 새로 잡았으면 스케일 작게 하기
		if (newId == NetworkManager.LocalClientId) {
			_cartRenderer.transform.localScale = Vector3.one * _originalScale * _holdingScaleMultiplier;
		}
		
		// 내가 잡고있다 놓았으면, 원래 스케일로 돌리기
		if (oldId == NetworkManager.LocalClientId) {
			_cartRenderer.transform.localScale = Vector3.one * _originalScale;
		}
	}
	
	// 영역 안에 들어온 플레이어 
	private void HandlePlayerEnter(Player player) {
		_playersInHealingArea.Add(player);
	}
	
	
	private void HandlePlayerExit(Player player) {
		if (!_playersInHealingArea.Remove(player)) {
			Debug.LogWarning($"[BasicCart] 힐 영역 안에 존재하지 않는 플레이어를 제거하려 했습니다.");
		}
	}

	private void Update() {
		if (!IsServer) { return; }
		foreach (var player in _playersInHealingArea) {
			player.PlayerHealth.RestoreHealth(_healingAmount * Time.deltaTime);
		}
	}
}