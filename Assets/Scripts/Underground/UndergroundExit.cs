using UnityEngine;

public class UndergroundExit : EntranceDoor {
    // 외계 기지에서 나온다.
    protected override SoundKey DoorSoundKey => SoundKey.Door_Out;

    // 이 프리팹은 Assets/Imported 안에 있어 gitignore 대상이다. 인스펙터로 키를 지정하면
    // 팀원에게 전파되지 않으므로 코드에 남긴다.
    public override string InteractionText => LocalizeInteractionText("hq_entrance_interaction");

	// 안개는 UndergroundFogApplier가 카메라 위치로 정한다. 문에서 토글하면 관전이나
	// 순간이동으로 카메라만 옮겨갔을 때 화면과 어긋난다.
}
