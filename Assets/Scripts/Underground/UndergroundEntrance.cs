using UnityEngine;

public class UndergroundEntrance : EntranceDoor {
	public override string InteractionText => "외계 기지로 이동하기";

	public override void Interact(GameObject interactor) {
		base.Interact(interactor);
		UndergroundFog.ApplyUnderground();
	}
}
