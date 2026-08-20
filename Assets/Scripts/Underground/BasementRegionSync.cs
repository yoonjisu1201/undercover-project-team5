using UnityEngine;

// 지하 맵이 절차적으로 (재)생성될 때마다, 그 결과 범위에 맞춰 Basement MapRegion을 갱신하고 해금한다.
// Basement는 지상 A~E 구역의 배타적 추첨(RoundManager.Region)과 무관하게 항상 별도로 관리된다.
[RequireComponent(typeof(UndergroundRandomMapGenerator))]
public sealed class BasementRegionSync : MonoBehaviour
{
    [SerializeField] private MapRegion _basementRegion;

    private UndergroundRandomMapGenerator _generator;

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
    }
}
