using UnityEngine;

public class UndergroundExit : EntranceDoor {
    // 이 프리팹은 Assets/Imported 안에 있어 gitignore 대상이다. 인스펙터로 키를 지정하면
    // 팀원에게 전파되지 않으므로 코드에 남긴다.
    public override string InteractionText => LocalizeInteractionText("hq_entrance_interaction");

	public override void Interact(GameObject interactor) {
		base.Interact(interactor);
		UndergroundFog.ApplySurface();
	}
}
