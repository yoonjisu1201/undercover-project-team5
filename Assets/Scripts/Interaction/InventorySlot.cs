[System.Serializable]

public class InventorySlot
{
    public string ItemId { get; private set; }
    public int Quantity { get; private set; }

    public bool IsEmpty => string.IsNullOrEmpty(ItemId);

    public void Set(string itemId)
    {
        ItemId = itemId;
    }

    public void Clear()
    {
        ItemId = null;
        Quantity = 0;
    }
}