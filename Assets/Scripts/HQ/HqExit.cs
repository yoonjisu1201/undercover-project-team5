using UnityEngine;

public class HqExit : EntranceDoor {
    // 본부에서 현장으로 나간다.
    protected override SoundKey DoorSoundKey => SoundKey.Door_Out;

	// 전력 복구 안내는 현장으로 나가는 이 문에서만 띄운다.
	public override void Interact(GameObject interactor) {
		base.Interact(interactor);

		if (interactor.TryGetComponent(out GameplayGuideController guide)) {
			guide.NotifyExitedHqToField();
		}
	}
}
