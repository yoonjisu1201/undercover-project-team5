using System;
using Unity.Netcode;
using UnityEngine;

// 특정 영역 안에 플레이어가 들어오고 나가는 것만을 관리
// 영역 내부 플레이어 리스트 관리 등은 카트에서 수행
[RequireComponent(typeof(SphereCollider))]
public class AreaCollider : NetworkBehaviour {
	public event Action<Player> OnPlayerEnter;
	public event Action<Player> OnPlayerExit;

	[Header("=== 파티클 추가 ===")]
	[SerializeField] private ParticleSystem _particle;

	private void Awake() {
		// 이펙트 켜기(있다면)
		if (_particle != null) {
			var shape = _particle.shape;
			shape.radius = GetComponent<SphereCollider>().radius;
			_particle.Play();
		}
	}

	// Initialize시점에 이미 스폰지점 내에 존재하는 플레이어들 찾아서 OnPlayerEnter 호출해주기
	public void Initialize() {
		SphereCollider collider = GetComponent<SphereCollider>();
		
		var hits = Physics.OverlapSphere(transform.position, collider.radius);
		foreach (var hit in hits) {
			if (hit.TryGetComponent<Player>(out var player))
				OnPlayerEnter?.Invoke(player);
		}
	}

	public void OnTriggerEnter(Collider other) {
		// 플레이어가 아닌 사람이 들어오면 스킵
		if(!other.TryGetComponent<Player>(out var player)) { return; }
		
		// 플레이어가 영역 안에 들어왔으면 이벤트 발행
		OnPlayerEnter?.Invoke(player);
	}
	
	public void OnTriggerExit(Collider other) {
		// 플레이어가 아닌 사람이 들어오면 스킵
		if(!other.TryGetComponent<Player>(out var player)) { return; }
		
		// 플레이어가 영역 안에 들어왔으면 이벤트 발행
		OnPlayerExit?.Invoke(player);
	}
}