using UnityEngine;

public sealed class MiniGameUIController : MonoBehaviour
{
    private MiniGameInteractable _owner;
    private bool _isClosing;

    public void Initialize(MiniGameInteractable owner)
    {
        _owner = owner;
    }

    // Button 컴포넌트의 On Click()에서 직접 호출한다.
    public void Close()
    {
        if (_isClosing)
        {
            return;
        }

        _isClosing = true;
        GameplayUiMode.Instance?.DeactivateCursor();
        _owner?.NotifyUIClosed(this);
        Destroy(gameObject);
    }
}
