using UnityEngine;

public class PickupItem : MonoBehaviour, IInteractable
{
    [SerializeField] private ItemData _itemData;

    public string InteractionText => _itemData != null ? $"{_itemData.DisplayName} 줍기" : "줍기";

    public void Interact(GameObject interactor)
    {
        if (_itemData == null)
        {
            return;
        }

        PlayerInventory inventory = interactor.GetComponent<PlayerInventory>();

        if (inventory == null)
        {
            return;
        }

        if (inventory.TryAddItem(_itemData.ItemId))
        {
            Destroy(gameObject);
        }
    }
}
