using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.UI;
using UnityEngine.Video;

public sealed class WaitingRoomObjectTutorialUI : MonoBehaviour, IClosableUi
{
    [SerializeField] private RawImage _videoImage;
    [SerializeField] private VideoPlayer _videoPlayer;
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
            _canCloseWithInteract = !_actions.Player.Interact.IsPressed();
            return;
        }

        if (_actions.Player.Interact.WasPressedThisFrame())
        {
            Close();
        }
    }

    public void Open(LocalizedTexture informationImage, VideoClip videoClip)
    {
        gameObject.SetActive(true);

        if (!_isOpen)
        {
            _isOpen = true;
            GameplayUiMode.Instance?.RegisterUi(this);
            GameplayUiMode.Instance?.ActivateInputBlock();
        }

        _canCloseWithInteract = false;
        SetInformationImage(informationImage);
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
        _actions?.Disable();

        if (_isOpen)
        {
            ReleaseOpenState();
        }
    }

    private void OnDestroy()
    {
        _actions?.Dispose();
    }

    private void SetInformationImage(LocalizedTexture informationImage)
    {
        ReleaseInformationImage();
        _localizedInformationImage = informationImage;

        if (_localizedInformationImage != null)
        {
            _localizedInformationImage.AssetChanged += ApplyInformationImage;
        }
    }

    private void ApplyInformationImage(Texture texture)
    {
        _informationImage.texture = texture;
    }

    private void SetVideo(VideoClip videoClip)
    {
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
        _isOpen = false;
        ReleaseInformationImage();

        _videoPlayer.Stop();
        _videoPlayer.clip = null;
        _videoImage.enabled = false;

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
