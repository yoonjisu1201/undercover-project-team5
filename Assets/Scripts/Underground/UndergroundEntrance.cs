using UnityEngine;

public class UndergroundEntrance : EntranceDoor {
    // 외계 기지로 들어간다.
    protected override SoundKey DoorSoundKey => SoundKey.Door_In;

	// 안개는 UndergroundFogApplier가 카메라 위치로 정한다. 문에서 토글하면 관전이나
	// 순간이동으로 카메라만 옮겨갔을 때 화면과 어긋난다.
}
