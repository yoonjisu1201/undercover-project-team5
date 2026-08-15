using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public class BasicCart : CartBase {

	[Header("=== 잡았을 때의 스케일 비율 ===")]
	[SerializeField] private float _holdingScaleMultiplier = 0.7f;
	[Header("=== 카트 Transform 등록(잡았을 때 사이즈 줄이기 위해) ===")]
	[SerializeField] private MeshRenderer _cartRenderer;
	[Header("=== 힐 영역 Collider 등록 ===")]
	[SerializeField] private AreaCollider _areaCollider;
	[Header("=== 매 초 체력 얼마나 찰지 ===")] 
	[SerializeField] private float _healPerSecond = 10f;
	[Header("=== 카트가 가진 총 회복량 ===")] 
	[SerializeField] private float _maxHealAmount = 1000f;
	[Header("=== 체력량 캔버스 ===")] 
	[SerializeField] private HealthBarCanvas healthBarCanvas;
	
	// 남은 체력량은 서버가 관리
	private NetworkVariable<float> _remainingHealAmount = new NetworkVariable<float>();
	
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
		
		// 카트 남은 체력량 바뀌면 렌더링 다시 하게 이벤트. 전체 구독한다. 알아서 관리할 것이라
		_remainingHealAmount.OnValueChanged += HandleHealingRemainAmountChanged;
		
		// 체력 회복 관련된 이벤트 구독
		// 서버만 구독하면 된다. 체력은 서버 권한으로 관리될 것이기 때문
		if (!IsServer) { return; }
		
		// 회복량은 꽉 채우고 시작
		_remainingHealAmount.Value = _maxHealAmount;
		
		// 플레이어가 특정 영역에 들어오고 나갔을 떄
		_areaCollider.OnPlayerEnter += HandlePlayerEnter;
		_areaCollider.OnPlayerExit += HandlePlayerExit;
		_areaCollider.Initialize();
	}

	public override void OnNetworkDespawn() {
		base.OnNetworkDespawn();

		_remainingHealAmount.OnValueChanged -= HandleHealingRemainAmountChanged;

		_areaCollider.OnPlayerEnter -= HandlePlayerEnter;
		_areaCollider.OnPlayerExit -= HandlePlayerExit;
	}
	
	private void HandleHealingRemainAmountChanged(float oldVal, float newVal) {
		// 남은 회복량이 있으면 파티클 이펙트 켜기
		if (newVal > 0f) { _areaCollider.PlayAreaParticleEffect(); }
		// 그 외(0 이하)에는 파티클 이펙트 끄기
		else { _areaCollider.StopAreaParticleEffect(); }
		// UI도 남은 체력량에 맞춰 갱신
		healthBarCanvas.SetBarFillAmount(_remainingHealAmount.Value / _maxHealAmount);
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
	
	// 기본 카트는 라운드 전환 시 남은 회복량도 가득 채워야 해서 override
	public override void ResetForNewRound() {
		base.ResetForNewRound();

		if (!IsServer) { return; }

		RefillHealingAmount();
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
		
		// 카트 체력 없으면 회복하지 않음
		if (_remainingHealAmount.Value <= 0) {
			return;
		}
		
		foreach (var player in _playersInHealingArea) {
			HealPlayer(player, _healPerSecond * Time.deltaTime);
		}
	}
	
	// 가능한 회복량만큼 플레이어를 회복시킨다.
	private void HealPlayer(Player player, float amount) {
		// 회복 가능량 계산. amount만큼만 회복시키는데, 남은 회복량이 더 작으면 남은 회복량만큼만
		float healAmount = Mathf.Min(amount, _remainingHealAmount.Value);
		// 플레이어 회복 시도
		float realAmount = player.PlayerHealth.RestoreHealth(healAmount);
		// 실제 회복에 사용된 양 만큼만 차감
		_remainingHealAmount.Value -= realAmount;
	}
	
	public void RefillHealingAmount() {
		_remainingHealAmount.Value = _maxHealAmount;
	}
}