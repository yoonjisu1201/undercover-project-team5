using UnityEngine;
using UnityEngine.UI;

// 화면에 상시 노출되는 가이드북 아이콘. 지정 키를 누르면 전체화면 가이드북을 연다.
// 가이드북이 열려 있는 동안에는 아이콘을 숨겨서 겹치지 않게 한다.
public class GuideBookHud : MonoBehaviour
{
    [SerializeField] private GameObject _icon;
    [SerializeField] private GuideBook _guideBook;
    [SerializeField] private Image _dimmer;

    private CustomInputActions _actions;

    private void Awake()
    {
        _actions = new CustomInputActions();
        _guideBook.gameObject.SetActive(false);
        _dimmer.gameObject.SetActive(false);
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
            OpenGuideBook();
        }
    }
    
    // 가이드북 열릴 때 필요한 동작 수행할 HandleGuideBookClosed함수 추가
    private void OpenGuideBook() {
        _guideBook.Show();
        _dimmer.gameObject.SetActive(true);
        _guideBook.OnClose += HandleGuideBookClosed;
    }
    
    // Dimmer도 같이 비활성화한다. 그리고 이벤트에서도 제외함
    private void HandleGuideBookClosed() {
        _dimmer.gameObject.SetActive(false);
        _guideBook.OnClose -= HandleGuideBookClosed;
    }
}
