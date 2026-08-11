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

    private SceneCursorSettings _sceneCursorSettings;   // 씬 커서 설정을 관리하는 컴포넌트
    private CustomInputActions _actions;    // 사용자 입력을 처리하는 커스텀 입력 액션
    private string _descriptionTemplate;    // {parts} 자리표시자를 포함한 원본 설명 문구 (부위명 치환용)

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
    }

    private void OnDisable()
    {
        GameplayUiMode.Instance?.UnregisterUi(this);
        _closeButton.onClick.RemoveListener(Close);
        GameplayUiMode.Instance?.DeactivateCursor();
    }

    private void Update()
    {

    }


    private void EnsureInitialized()    // 씬 커서 설정과 사용자 입력 액션이 초기화되어 있는지 확인하고, 초기화되지 않은 경우 초기화하는 메서드
    {
        _actions ??= new CustomInputActions();
        _sceneCursorSettings ??= GetComponent<SceneCursorSettings>();
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

    public void Close()
    {
        gameObject.SetActive(false);
    }
}
