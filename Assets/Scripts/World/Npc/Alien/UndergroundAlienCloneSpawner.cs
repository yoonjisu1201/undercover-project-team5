// 지하(Basement) 구역용 외계인 분신 스포너. 공통 로직은 AlienCloneSpawnerBase 참고.
// 지상과 달리 Basement MapRegion은 필드 구역의 해금 추첨과 무관하게 항상 별도로 관리되므로,
// 스폰 시도 전에 이 스포너가 직접 전용 MapRegionController의 NavMesh 캐시를 갱신해야 한다.
public class UndergroundAlienCloneSpawner : AlienCloneSpawnerBase
{
    protected override void OnRoundStarting()
    {
        // 지상과 달리 Basement용 MapRegionController는 필드 구역 해금 추첨(SetActiveRegion)의
        // 대상이 아니라 아무도 자동으로 갱신해주지 않는다. 지하 맵은 라운드마다 절차적으로 다시
        // 생성되므로(RoundManager -> UndergroundRandomMapGenerator.RegenerateForNewRound), 여기서
        // 직접 최신 NavMesh 기준으로 갱신해야 이번 라운드의 새 지하 맵 위에서 스폰 지점을 찾을 수 있다.
        RegionController?.RefreshSpawnAreas();
    }
}
