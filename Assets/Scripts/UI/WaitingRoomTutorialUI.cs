using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.UI;

// 그림 튜토리얼 Pedestal이 공유하는 UI로, 선택한 안내 이미지를 화면 중앙에 크게 표시한다.
// 오브젝트 전시 UI와 용도를 분리해 그림 튜토리얼의 레이아웃과 입력 수명 주기를 독립적으로 관리한다.
public sealed class WaitingRoomTutorialUI : MonoBehaviour, IClosableUi
{
    [SerializeField] private RawImage _informationImage;

    private CustomInputActions _actions;
    private LocalizedTexture _localizedInformationImage;
    private bool _isOpen;
    private bool _canCloseWithInteract;

    private void OnEnable()
    {
        _actions ??= new CustomInputActions();
        _actions.Enable();
    }

    private void Update()
    {
        if (!_isOpen)
        {
            return;
        }

        if (!_canCloseWithInteract)
        {
            // UI를 연 E 입력이 유지되는 동안에는 닫지 않고, 키를 놓은 뒤의 새 입력부터 닫기로 사용한다.
            _canCloseWithInteract = !_actions.Player.Interact.IsPressed();
            return;
        }

        if (_actions.Player.Interact.WasPressedThisFrame())
        {
            Close();
        }
    }

    public void Open(LocalizedTexture informationImage)
    {
        gameObject.SetActive(true);

        if (!_isOpen)
        {
            // 이미 열린 UI의 내용만 교체할 때는 UI 등록과 입력 차단을 중복 적용하지 않는다.
            _isOpen = true;
            GameplayUiMode.Instance?.RegisterUi(this);
            GameplayUiMode.Instance?.ActivateInputBlock();
        }

        _canCloseWithInteract = false;
        SetInformationImage(informationImage);
    }

    public void Close()
    {
        if (!_isOpen)
        {
            return;
        }

        ReleaseOpenState();
        gameObject.SetActive(false);
    }

    private void OnDisable()
    {
        _actions?.Disable();

        if (_isOpen)
        {
            // Close를 거치지 않는 씬 전환에서도 입력 차단과 Localization 구독이 남지 않도록 정리한다.
            ReleaseOpenState();
        }
    }

    private void OnDestroy()
    {
        _actions?.Dispose();
    }

    private void SetInformationImage(LocalizedTexture informationImage)
    {
        // 이전 Pedestal의 언어 변경 구독을 해제해 현재 선택한 이미지의 변경만 UI에 반영한다.
        ReleaseInformationImage();
        _localizedInformationImage = informationImage;

        if (_localizedInformationImage != null)
        {
            // 현재 언어 이미지를 적용하고, UI가 열린 중의 언어 변경도 같은 콜백으로 즉시 갱신한다.
            _localizedInformationImage.AssetChanged += ApplyInformationImage;
        }
    }

    private void ApplyInformationImage(Texture texture)
    {
        _informationImage.texture = texture;
    }

    private void ReleaseOpenState()
    {
        // Open에서 등록한 UI 스택과 입력 차단을 함께 해제해 닫힌 뒤 플레이어 조작을 복원한다.
        _isOpen = false;
        ReleaseInformationImage();
        GameplayUiMode.Instance?.UnregisterUi(this);
        GameplayUiMode.Instance?.DeactivateInputBlock();
    }

    private void ReleaseInformationImage()
    {
        if (_localizedInformationImage != null)
        {
            _localizedInformationImage.AssetChanged -= ApplyInformationImage;
            _localizedInformationImage = null;
        }

        _informationImage.texture = null;
    }
}
