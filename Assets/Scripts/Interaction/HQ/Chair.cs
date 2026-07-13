using UnityEngine;

public class Chair : IInteractable {
	public string InteractionText { get; }
	public void Interact(GameObject interactor) {
		PlayerMoveSample _moveSample = interactor.GetComponent<PlayerMoveSample>();
	}
}