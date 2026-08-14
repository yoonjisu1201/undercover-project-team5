using UnityEngine;
using UnityEngine.UI;

// 기존 몽타주 알림 카드(NotificationState)를 Tab 허브의 버튼으로 쓰기 위한 컴포넌트.
// 몽타주 UI는 InfoHubController와 같은 캔버스 안에 있으므로 정렬은 형제 순서로 해결되고,
// 여기서는 Tab으로 열려 있는 동안에만 클릭을 받도록 켜고 끄는 일만 한다.
[RequireComponent(typeof(Button))]
public sealed class MontageHubButton : MonoBehaviour
{
    [SerializeField] private MontageShareUI _montageShareUI;

    private Button _button;

    private void Awake()
    {
        _button = GetComponent<Button>();
    }

    private void OnEnable()
    {
        InfoHubController.HubStateChanged += SetClickable;
        _button.onClick.AddListener(ShowMontage);
        SetClickable(InfoHubController.IsHubOpen);
    }

    private void OnDisable()
    {
        InfoHubController.HubStateChanged -= SetClickable;
        _button.onClick.RemoveListener(ShowMontage);
    }

    private void SetClickable(bool clickable)
    {
        _button.interactable = clickable;

        // 닫혀 있을 때는 클릭 판정 자체를 넘겨 뒤쪽 UI가 가려지지 않게 한다.
        if (_button.targetGraphic != null)
        {
            _button.targetGraphic.raycastTarget = clickable;
        }
    }

    private void ShowMontage()
    {
        if (_montageShareUI == null)
        {
            _montageShareUI = FindFirstObjectByType<MontageShareUI>(FindObjectsInactive.Include);
        }

        if (_montageShareUI == null)
        {
            Debug.LogError("[MontageHubButton] 몽타주 UI를 찾지 못했습니다.", this);
            return;
        }

        _montageShareUI.Expand();
    }
}
