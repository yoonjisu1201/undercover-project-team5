using UnityEngine;

public class SpawnPointHub : MonoBehaviour {
	// 현장 스폰 포인트가 하나면 겹쳐서 생성됨. 따라서 배열로 관리
	[SerializeField] private Transform[] _siteSpawnPoint;
	[SerializeField] private Transform _hqSpawnPoint;
	private int _index = 0;

	private void Awake() {
		_index = 0;
	}

	// 스폰포인트 달라고 하면 알아서 인덱스만큼 내어주도록.
	public Transform SiteSpawnPoint => _siteSpawnPoint[_index++ % _siteSpawnPoint.Length];
	public Transform HQSpawnPoint => _hqSpawnPoint;
}
