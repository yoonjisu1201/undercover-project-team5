using UnityEngine;

public class UndergroundEntrance : EntranceDoor {
    // 외계 기지로 들어간다.
    protected override SoundKey DoorSoundKey => SoundKey.Door_In;

	public override void Interact(GameObject interactor) {
		base.Interact(interactor);
		UndergroundFog.ApplyUnderground();
	}
}
