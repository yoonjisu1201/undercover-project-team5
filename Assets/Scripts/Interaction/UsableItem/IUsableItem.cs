public interface IUsableItem
{
    bool TryGetSelectedItemUse(out string message, out bool requiresHold);
    bool TryCompleteSelectedItemUse(out string message);
}
