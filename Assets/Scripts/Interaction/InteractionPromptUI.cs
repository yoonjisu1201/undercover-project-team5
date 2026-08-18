using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// 상호작용 안내 문구와 아이템 사용/소생 홀드 게이지를 표시한다.
// 인벤토리 슬롯 표시(InventoryUI)와는 별개의 책임이라 분리되어 있다.
public class InteractionPromptUI : MonoBehaviour
{
    [SerializeField] private TMP_Text _interactionPromptText;
    [SerializeField, Min(0f)] private float _selectedItemPromptDuration = 2f;   // 선택 아이템 안내 문구가 사라지기 전까지 유지되는 시간
    [SerializeField] private Image _useHoldProgressImage;

    private Coroutine _selectedItemPromptRoutine;
    private string _interactionText;
    private Sprite _useHoldProgressSprite;

    private void Awake()
    {
        if (_interactionPromptText == null)
        {
            _interactionPromptText = transform.Find("InteractionPrompt")?.GetComponent<TMP_Text>();
        }

        SetInteractionPrompt(null);
        SetUseHoldProgress(0f, false);
    }

    // 상호작용 프롬프트 텍스트 설정. showKeyHint가 false면 " : [E]" 힌트 없이 문구만 보여준다
    // (예: 눌러도 아무 동작이 없는 안내성 문구).
    public void SetInteractionPrompt(string interactionText, bool showKeyHint = true)
    {
        _interactionText = interactionText;

        // 월드 아이템이나 플레이어 등 실제 상호작용 안내가 선택 아이템 이름보다 우선한다.
        if (!string.IsNullOrWhiteSpace(interactionText))
        {
            if (_selectedItemPromptRoutine != null)
            {
                StopCoroutine(_selectedItemPromptRoutine);
                _selectedItemPromptRoutine = null;
            }

            ApplyInteractionPrompt(interactionText, showKeyHint);
            return;
        }

        // 상호작용 대상이 사라져도 진행 중인 선택 아이템 안내는 남은 시간 동안 유지한다.
        if (_selectedItemPromptRoutine != null)
        {
            return;
        }

        ApplyInteractionPrompt(interactionText);
    }

    // showKeyHint가 true면 상시 안내와 같은 " : [E]" 형식으로 보여준다 (예: "단서 확인 : [E]").
    public void ShowTemporaryPrompt(string message, bool showKeyHint = false)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        if (_selectedItemPromptRoutine != null)
        {
            StopCoroutine(_selectedItemPromptRoutine);
        }

        _selectedItemPromptRoutine = StartCoroutine(ShowSelectedItemPrompt(message, showKeyHint));
    }

    public void SetUseHoldProgress(float progress, bool visible)
    {
        EnsureUseHoldProgressImage();

        if (_useHoldProgressImage == null)
        {
            return;
        }

        _useHoldProgressImage.fillAmount = Mathf.Clamp01(progress);
        _useHoldProgressImage.gameObject.SetActive(visible);
    }

    private void EnsureUseHoldProgressImage()
    {
        if (_useHoldProgressImage != null)
        {
            return;
        }

        GameObject progressObject = new("UseHoldProgress", typeof(RectTransform), typeof(Image));
        progressObject.transform.SetParent(transform, false);

        RectTransform rectTransform = (RectTransform)progressObject.transform;
        rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
        rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
        rectTransform.pivot = new Vector2(0.5f, 0.5f);
        rectTransform.anchoredPosition = Vector2.zero;
        rectTransform.sizeDelta = new Vector2(72f, 72f);

        _useHoldProgressImage = progressObject.GetComponent<Image>();
        _useHoldProgressImage.sprite = GetUseHoldProgressSprite();
        _useHoldProgressImage.color = new Color(1f, 1f, 1f, 0.45f);
        _useHoldProgressImage.raycastTarget = false;
        _useHoldProgressImage.type = Image.Type.Filled;
        _useHoldProgressImage.fillMethod = Image.FillMethod.Radial360;
        _useHoldProgressImage.fillOrigin = (int)Image.Origin360.Top;
        _useHoldProgressImage.fillClockwise = true;
        _useHoldProgressImage.fillAmount = 0f;
        _useHoldProgressImage.gameObject.SetActive(false);
    }

    private Sprite GetUseHoldProgressSprite()
    {
        if (_useHoldProgressSprite != null)
        {
            return _useHoldProgressSprite;
        }

        const int textureSize = 64;
        const float radius = textureSize * 0.5f - 1f;
        Vector2 center = new(textureSize * 0.5f, textureSize * 0.5f);
        Texture2D texture = new(textureSize, textureSize, TextureFormat.RGBA32, false)
        {
            name = "UseHoldProgressCircle"
        };

        Color32[] pixels = new Color32[textureSize * textureSize];
        for (int y = 0; y < textureSize; y++)
        {
            for (int x = 0; x < textureSize; x++)
            {
                float distance = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), center);
                pixels[y * textureSize + x] = distance <= radius
                    ? new Color32(255, 255, 255, 255)
                    : new Color32(255, 255, 255, 0);
            }
        }

        texture.SetPixels32(pixels);
        texture.Apply();

        _useHoldProgressSprite = Sprite.Create(
            texture,
            new Rect(0f, 0f, textureSize, textureSize),
            new Vector2(0.5f, 0.5f),
            textureSize);
        return _useHoldProgressSprite;
    }

    private IEnumerator ShowSelectedItemPrompt(string message, bool showKeyHint)
    {
        if (_interactionPromptText != null)
        {
            _interactionPromptText.text = showKeyHint ? $"{message} : [E]" : message;
            _interactionPromptText.gameObject.SetActive(true);
        }

        yield return new WaitForSecondsRealtime(_selectedItemPromptDuration);

        _selectedItemPromptRoutine = null;
        ApplyInteractionPrompt(_interactionText);
    }

    private void ApplyInteractionPrompt(string interactionText, bool showKeyHint = true)
    {
        if (_interactionPromptText == null)
        {
            return;
        }

        bool isVisible = !string.IsNullOrWhiteSpace(interactionText);
        if (isVisible)
        {
            _interactionPromptText.text = showKeyHint ? $"{interactionText} : [E]" : interactionText;
        }

        _interactionPromptText.gameObject.SetActive(isVisible);
    }
}
