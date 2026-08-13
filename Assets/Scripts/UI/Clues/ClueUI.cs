using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(SceneCursorSettings))]
public class ClueUI : MonoBehaviour, IClosableUi
{
    [Header("Clue Window")]
    [SerializeField] private GameObject _hintObject;    // 단서 UI를 표시할 때 활성화되는 힌트 오브젝트

    [Header("Clue Description")]
    [SerializeField] private TMP_Text _discriptionText; // 단서 설명 텍스트
    [SerializeField] private TMP_Text _discriptionHint; // 단서 힌트 텍스트

    [Header("Clue Image")]
    [SerializeField] private RawImage _clueImage;  // 단서 이미지를 표시할 이미지 UI
    [SerializeField] private GameObject _magnifiedMark; // 단서 이미지가 없는 경우 표시되는 확대 표시 마크
    [SerializeField] private TMP_Text _imageLabel;  // 단서 종류 라벨 텍스트

    [Header("Buttons")]
    [SerializeField] private Button _closeButton;   // 단서 UI를 닫는 버튼

    [Header("등장/퇴장 연출")]
    // 가이드북과 같은 방식으로 작게 시작해 제자리 크기로 커진다.
    [SerializeField] private RectTransform _content;   // 비우면 이 오브젝트 자신을 쓴다
    [SerializeField, Min(0f)] private float _openDuration = 0.22f;
    [SerializeField, Range(0.1f, 1f)] private float _collapsedScale = 0.85f;

    private Tween _scaleTween;
    private bool _isClosing;

    private SceneCursorSettings _sceneCursorSettings;   // 씬 커서 설정을 관리하는 컴포넌트
    private CustomInputActions _actions;    // 사용자 입력을 처리하는 커스텀 입력 액션
    private string _descriptionTemplate;    // {parts} 자리표시자를 포함한 원본 설명 문구 (부위명 치환용)
    private bool _waitingForInteractRelease;    // 창을 연 E 입력이 그대로 닫기로 이어지지 않게 막는 동안 true

    private void Awake()
    {
        EnsureInitialized();    // 씬 커서 설정 초기화
        MoveBackgroundBehindClueImage();
    }

    // ClueDisplay 프리팹에서는 배경이 RawImage의 자식이다. Canvas가 아이템별 인스턴스가 되어도
    // 이미지 위를 덮지 않도록 Awake에서 한 번만 같은 부모의 뒤쪽으로 옮긴다.
    private void MoveBackgroundBehindClueImage()
    {
        if (_clueImage == null)
        {
            return;
        }

        Transform background = _clueImage.transform.Find("BackGround");
        if (background == null)
        {
            return;
        }

        background.SetParent(_clueImage.transform.parent, false);
        background.SetAsFirstSibling();
        background.gameObject.SetActive(true);
    }

    private void OnEnable()
    {
        EnsureInitialized();    // 씬 커서 설정 초기화
        GameplayUiMode.Instance?.RegisterUi(this);
        _closeButton.onClick.AddListener(Close);
        GameplayUiMode.Instance?.ActivateCursor();

        _actions.Enable();
        // 단서를 여는 것도 E라서, 창이 열린 프레임의 입력이 그대로 닫기로 이어지지 않게 한 번은 떼도록 한다.
        _waitingForInteractRelease = true;
    }

    private void OnDisable()
    {
        GameplayUiMode.Instance?.UnregisterUi(this);
        _closeButton.onClick.RemoveListener(Close);
        GameplayUiMode.Instance?.DeactivateCursor();
        _actions?.Disable();
        _scaleTween?.Kill();
        _isClosing = false;
    }

    // 단서 창은 E로도 닫는다. 창이 열려 있는 동안에는 플레이어 상호작용이 잠기므로 E가 겹치지 않는다.
    private void Update()
    {
        if (_actions == null)
        {
            return;
        }

        if (_waitingForInteractRelease)
        {
            _waitingForInteractRelease = _actions.Player.Interact.IsPressed();
            return;
        }

        if (_actions.Player.Interact.WasPressedThisFrame())
        {
            Close();
        }
    }


    private void EnsureInitialized()    // 씬 커서 설정과 사용자 입력 액션이 초기화되어 있는지 확인하고, 초기화되지 않은 경우 초기화하는 메서드
    {
        _actions ??= new CustomInputActions();
        _sceneCursorSettings ??= GetComponent<SceneCursorSettings>();
    }


    // 단서 목록에서 고른 번호의 단서를 연다. 캡처 결과는 씬의 ClueModulePreview가 들고 있다.
    public void ShowClue(int clueNumber)
    {
        // 비활성 상태에서는 Awake가 아직 안 돌았을 수 있어, 켠 다음에 내용을 채운다.
        gameObject.SetActive(true);

        ClueModulePreview preview = FindFirstObjectByType<ClueModulePreview>(FindObjectsInactive.Include);
        if (preview == null || !preview.TryApplyTo(clueNumber, this))
        {
            ClearClueImage($"단서 {clueNumber}");
        }

        PlayOpenAnimation();
    }

    // 가이드북과 같은 등장 연출.
    private void PlayOpenAnimation()
    {
        RectTransform target = GetAnimationTarget();
        if (target == null)
        {
            return;
        }

        _isClosing = false;
        _scaleTween?.Kill();
        target.localScale = Vector3.one * _collapsedScale;
        _scaleTween = target.DOScale(1f, _openDuration).SetEase(Ease.OutBack);
    }

    private RectTransform GetAnimationTarget()
    {
        return _content != null ? _content : transform as RectTransform;
    }

    public void ShowClueImage(Texture clueTexture, string clueType, string partName = null)
    {
        bool hasImage = clueTexture != null;

        if (_clueImage != null)
        {
            _clueImage.texture = clueTexture;
        }

        if (_magnifiedMark != null) // 단서 이미지가 없는 경우 확대 표시 마크를 활성화하고, 단서 이미지가 있는 경우 비활성화
        {
            _magnifiedMark.SetActive(!hasImage);
        }

        if (_imageLabel != null)    // 단서 종류 라벨 텍스트를 설정
        {
            _imageLabel.text = clueType;
        }

        if (partName != null)   // 설명 문구의 {parts}를 실제 부위명으로 치환
        {
            ApplyDescriptionPart(partName);
        }
    }

    // 최초 문구를 템플릿으로 캐싱한 뒤 {parts}를 부위명으로 바꿔 설명 텍스트에 반영한다.
    private void ApplyDescriptionPart(string partName)
    {
        if (_discriptionText == null)
        {
            return;
        }

        _descriptionTemplate ??= _discriptionText.text;

        // 부위명을 볼드 + 짙은 빨간색으로 강조한다. (TMP Rich Text 필요)
        string highlighted = $"<b><size=110%><color=#8B0000>{partName}</color></size></b>";
        _discriptionText.text = _descriptionTemplate.Replace("{parts}", highlighted);
    }



    public void ClearClueImage(string clueType)
    {
        ShowClueImage(null, clueType, "???");
    }

    // 가이드북과 같은 퇴장 연출. 다 줄어든 뒤에 꺼진다.
    public void Close()
    {
        if (!gameObject.activeSelf || _isClosing)
        {
            return;
        }

        RectTransform target = GetAnimationTarget();
        if (target == null)
        {
            gameObject.SetActive(false);
            return;
        }

        _isClosing = true;
        _scaleTween?.Kill();
        _scaleTween = target.DOScale(_collapsedScale, _openDuration * 0.7f).SetEase(Ease.InBack)
            .OnComplete(() =>
            {
                _isClosing = false;
                target.localScale = Vector3.one;
                gameObject.SetActive(false);
            });
    }
}
