using UnityEngine;

// 지하 맵이 절차적으로 (재)생성될 때마다, 그 결과 범위에 맞춰 Basement MapRegion을 갱신하고 해금한다.
// Basement는 지상 A~E 구역의 배타적 추첨(RoundManager.Region)과 무관하게 항상 별도로 관리된다.
[RequireComponent(typeof(UndergroundRandomMapGenerator))]
public sealed class BasementRegionSync : MonoBehaviour
{
    [SerializeField] private MapRegion _basementRegion;

    private UndergroundRandomMapGenerator _generator;

    // 최초 생성 때만 ClueSpawner에 재시도를 알린다. 2라운드부터는 RoundManager가
    // RegenerateForNewRound() 이후 SpawnForNextRound()를 직접, 순서대로 호출하므로
    // 여기서 또 알리면 같은 라운드에 단서가 두 번 스폰된다.
    private bool _hasNotifiedInitialGeneration;

    private void Awake()
    {
        _generator = GetComponent<UndergroundRandomMapGenerator>();
    }

    private void OnEnable()
    {
        _generator.Generated += HandleGenerated;
    }

    private void OnDisable()
    {
        _generator.Generated -= HandleGenerated;
    }

    private void HandleGenerated()
    {
        if (_basementRegion == null)
        {
            Debug.LogError("[BasementRegionSync] Basement MapRegion이 설정되지 않았습니다.", this);
            return;
        }

        _basementRegion.SetBoundsFromWorld(_generator.GetGeneratedBounds());
        _basementRegion.Unlock();

        if (!_hasNotifiedInitialGeneration)
        {
            _hasNotifiedInitialGeneration = true;
            // ClueSpawner의 최초 스폰 시도가 이 시점보다 먼저 실행돼 실패했을 수 있으니 해금 직후 다시 시도해준다.
            ClueSpawner.Instance?.RetrySpawnIfPending();
        }
    }
}
