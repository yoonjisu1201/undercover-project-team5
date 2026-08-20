// 지상 구역용 외계인 분신 스포너. 공통 로직은 AlienCloneSpawnerBase 참고.
// 스폰 위치는 플레이어와 무관한 맵 임의 위치로 정한다. 외계인은 배회하다 플레이어를 감지하면 추격한다.
public class GroundAlienCloneSpawner : AlienCloneSpawnerBase
{
    // 지하 전용 스포너(UndergroundAlienCloneSpawner)를 도입하면서 지상 자동 스폰은 기본적으로 꺼둔다.
    // 디버그 메뉴에서 다시 켤 수 있다.
    protected override bool GetDefaultSpawningEnabled() => false;
}
