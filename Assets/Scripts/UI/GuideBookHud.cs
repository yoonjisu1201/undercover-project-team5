using UnityEngine;
using UnityEngine.UI;

// 화면에 상시 노출되는 가이드북 아이콘. 클릭하거나 지정 키를 누르면 전체화면 가이드북을 연다.
// 가이드북이 열려 있는 동안에는 아이콘을 숨겨서 겹치지 않게 한다.
public class GuideBookHud : MonoBehaviour
{
    [SerializeField] private GameObject _icon;
    [SerializeField] private Button _iconButton;
    [SerializeField] private GuideBook _guideBook;

    private CustomInputActions _actions;

    private void Awake()
    {
        _actions = new CustomInputActions();
        _iconButton.onClick.AddListener(_guideBook.Show);
    }

    private void OnEnable()
    {
        _actions.UI.Enable();
    }

    private void OnDisable()
    {
        _actions.UI.Disable();
    }

    private void Update()
    {
        bool guideOpen = _guideBook.gameObject.activeSelf;
        if (_icon.activeSelf == guideOpen)
        {
            _icon.SetActive(!guideOpen);
        }

        if (!guideOpen && _actions.UI.OpenGuideBook.WasPressedThisFrame())
        {
            _guideBook.Show();
        }
    }
}
