using UnityEngine;

public interface IInteractable
{
    string InteractionText { get; }
    bool CanInteract(GameObject interactor);
    void Interact(GameObject interactor);
}