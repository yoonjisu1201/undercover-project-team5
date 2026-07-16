using UnityEngine;

public interface IInteractable
{
    string InteractionText { get; }
    bool CanInteract { get; }
    void Interact(GameObject interactor);
}