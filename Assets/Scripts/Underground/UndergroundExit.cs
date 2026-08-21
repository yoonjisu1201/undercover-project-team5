using UnityEngine;

public class UndergroundExit : EntranceDoor {
	public override string InteractionText => "본부로 이동하기";

	public override void Interact(GameObject interactor) {
		base.Interact(interactor);
		UndergroundFog.ApplySurface();
	}
}
