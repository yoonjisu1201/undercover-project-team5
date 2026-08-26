using UnityEngine;

public class UndergroundEntrance : EntranceDoor {
	public override void Interact(GameObject interactor) {
		base.Interact(interactor);
		UndergroundFog.ApplyUnderground();
	}
}
