using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.UI;

// 모든 대기방 전시물이 공유하는 설명 UI로, 선택한 전시물의 문구와 안내 영상을 교체해 표시한다.
// UI가 열린 동안 월드 입력은 차단되므로 닫기 입력과 재생 수명 주기를 이 컴포넌트가 직접 관리한다.
//
// 안내 영상은 아틀라스를 물려두고 보여줄 칸(uvRect)만 옮기는 방식이라, 열 때 준비할 것이 없다.
public sealed class WaitingRoomObjectTutorialUI : MonoBehaviour, IClosableUi
{
    [SerializeField] private Button _closeButton;
    [SerializeField] private RawImage _videoImage;
    [SerializeField] private WaitingRoomTutorialInfoView _informationView;

    private CustomInputActions _actions;
    private bool _isOpen;
    private bool _canCloseWithInteract;

    private TutorialFlipbook _flipbook;
    private float _playbackTime;
    private int _shownFrame;

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

        if (_flipbook != null)
        {
            // UI가 열린 동안 게임 시간이 멈추더라도 안내 영상은 계속 돌아야 한다.
            _playbackTime += Time.unscaledDeltaTime;
            ShowFrameAt(_playbackTime);
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
        TutorialFlipbook flipbook)
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
        SetFlipbook(flipbook);
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

    // 전시물을 바꿔 열 때도 이 함수만 다시 부른다. 어느 쪽이든 첫 칸부터 시작한다.
    private void SetFlipbook(TutorialFlipbook flipbook)
    {
        // 아틀라스가 없는 전시물은 영상이 없는 것으로 다룬다. 이후 판정이 _flipbook 하나로 끝난다.
        _flipbook = flipbook != null && flipbook.HasAtlas ? flipbook : null;
        _playbackTime = 0f;
        _shownFrame = -1;
        _videoImage.enabled = _flipbook != null;

        if (_flipbook != null)
        {
            ShowFrameAt(0f);
        }
    }

    // 같은 칸이면 손대지 않는다. uvRect 대입은 UI 메시를 다시 만들게 해서,
    // 게임이 60fps면 같은 칸을 네 번씩 다시 만드는 낭비가 된다.
    private void ShowFrameAt(float playbackTime)
    {
        int frame = _flipbook.GetFrameIndex(playbackTime);

        if (frame == _shownFrame)
        {
            return;
        }

        _shownFrame = frame;

        // 아틀라스가 여러 장이면 칸이 다음 장으로 넘어가는 순간 텍스처도 바꿔야 한다.
        _videoImage.texture = _flipbook.GetAtlas(frame);
        _videoImage.uvRect = _flipbook.GetUvRect(frame);
    }

    private void ReleaseOpenState()
    {
        // Open에서 등록한 UI 스택과 입력 차단을 함께 해제해 닫힌 뒤 플레이어 조작이 정상 복원되게 한다.
        _isOpen = false;

        _flipbook = null;
        _videoImage.enabled = false;

        GameplayUiMode.Instance?.UnregisterUi(this);
        GameplayUiMode.Instance?.DeactivateCursor();
    }
}
