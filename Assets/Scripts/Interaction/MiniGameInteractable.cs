using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;

public class MiniGameInteractable : InteractableBase
{
    [Header("미니게임 UI")]
    [SerializeField] private GameObject _uiPrefab;
    [SerializeField] private string _interactionText = "미니게임 시작";

    private GameObject _uiInstance;
    private static MiniGameInteractable _activeInteractable;

    public override string InteractionText => _interactionText;
    public override bool CanInteract(GameObject interactor) => _uiPrefab != null && _activeInteractable == null;

    public override void Interact(GameObject interactor)
    {
        if (!CanInteract(interactor))
        {
            return;
        }

        _activeInteractable = this;
        _uiInstance = Instantiate(_uiPrefab);
        _uiInstance.name = _uiPrefab.name;

        EnsureEventSystem(_uiInstance.transform);

        MiniGameUIController controller = _uiInstance.GetComponent<MiniGameUIController>();
        if (controller == null)
        {
            Debug.LogError($"'{_uiPrefab.name}'에 MiniGameUIController가 없습니다.", _uiPrefab);
            CloseUI();
            return;
        }

        controller.Initialize(this);
        GameplayUiMode.Instance?.ActivateCursor();
    }

    public void CloseUI()
    {
        if (_uiInstance == null)
        {
            ReleaseActiveState();
            return;
        }

        MiniGameUIController controller = _uiInstance.GetComponent<MiniGameUIController>();
        if (controller != null)
        {
            controller.Close();
            return;
        }

        Destroy(_uiInstance);
        GameplayUiMode.Instance?.DeactivateCursor();
        NotifyUIClosed(null);
    }

    public void NotifyUIClosed(MiniGameUIController controller)
    {
        if (controller == null || controller.gameObject == _uiInstance)
        {
            _uiInstance = null;
            ReleaseActiveState();
        }
    }

    private void ReleaseActiveState()
    {
        if (_activeInteractable == this)
        {
            _activeInteractable = null;
        }
    }

    private static void EnsureEventSystem(Transform uiRoot)
    {
        if (FindFirstObjectByType<EventSystem>() != null)
        {
            return;
        }

        GameObject eventSystemObject = new GameObject("MiniGameEventSystem");
        eventSystemObject.transform.SetParent(uiRoot, false);
        eventSystemObject.AddComponent<EventSystem>();
        eventSystemObject.AddComponent<InputSystemUIInputModule>();
    }

    public override void OnDestroy()
    {
        if (_uiInstance != null)
        {
            CloseUI();
        }
        else
        {
            ReleaseActiveState();
        }

        base.OnDestroy();
    }
}
