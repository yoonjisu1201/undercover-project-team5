using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.UI;
using UnityEngine.Video;

// 모든 대기방 전시물이 공유하는 설명 UI로, 선택한 전시물의 문구와 영상을 교체해 표시한다.
// UI가 열린 동안 월드 입력은 차단되므로 닫기 입력과 미디어 수명 주기를 이 컴포넌트가 직접 관리한다.
public sealed class WaitingRoomObjectTutorialUI : MonoBehaviour, IClosableUi
{
    [SerializeField] private Button _closeButton;
    [SerializeField] private RawImage _videoImage;
    [SerializeField] private VideoPlayer _videoPlayer;
    [SerializeField] private WaitingRoomTutorialInfoView _informationView;

    private CustomInputActions _actions;
    private bool _isOpen;
    private bool _canCloseWithInteract;

    private void OnEnable()
    {
        _actions ??= new CustomInputActions();
        _actions.Enable();
        _closeButton.onClick.AddListener(Close);
    }

    private void Update()
    {
        if (!_isOpen)
        {
            return;
        }

        if (!_canCloseWithInteract)
        {
            // UI를 연 E 입력이 계속 눌린 상태에서는 닫지 않고, 키를 놓은 뒤의 새 입력부터 닫기로 인정한다.
            _canCloseWithInteract = !_actions.Player.Interact.IsPressed();
            return;
        }

        if (_actions.Player.Interact.WasPressedThisFrame())
        {
            Close();
        }
    }

    public void Open(
        LocalizedString title,
        LocalizedString subtitle,
        LocalizedString body,
        VideoClip videoClip)
    {
        gameObject.SetActive(true);

        if (!_isOpen)
        {
            // 이미 열린 UI의 내용만 교체할 때는 등록과 카운터를 중복 적용하지 않아 해제 호출과 균형을 맞춘다.
            _isOpen = true;
            GameplayUiMode.Instance?.RegisterUi(this);
            GameplayUiMode.Instance?.ActivateCursor();
        }

        _canCloseWithInteract = false;
        _informationView.SetContent(title, subtitle, body);
        SetVideo(videoClip);
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
        _closeButton.onClick.RemoveListener(Close);
        _actions?.Disable();

        if (_isOpen)
        {
            // Close를 거치지 않고 씬 전환 등으로 비활성화돼도 입력 차단과 이벤트 구독이 남지 않게 정리한다.
            ReleaseOpenState();
        }
    }

    private void OnDestroy()
    {
        _actions?.Dispose();
    }

    private void SetVideo(VideoClip videoClip)
    {
        // 이전 영상의 재생 상태와 마지막 프레임이 새 전시물에 남지 않도록 정지한 뒤 Clip과 표시 여부를 교체한다.
        _videoPlayer.Stop();
        _videoPlayer.clip = videoClip;
        _videoImage.enabled = videoClip != null;

        if (videoClip != null)
        {
            _videoPlayer.Play();
        }
    }

    private void ReleaseOpenState()
    {
        // Open에서 등록한 UI 스택과 입력 차단을 함께 해제해 닫힌 뒤 플레이어 조작이 정상 복원되게 한다.
        _isOpen = false;

        _videoPlayer.Stop();
        _videoPlayer.clip = null;
        _videoImage.enabled = false;

        GameplayUiMode.Instance?.UnregisterUi(this);
        GameplayUiMode.Instance?.DeactivateCursor();
    }
}
