using UnityEngine;
using UnityEngine.UI;

// 제압기 전용 에너지/재장전 게이지. InventoryCanvas 프리팹 안의 UI를 제어한다.
public sealed class ArrestToolGaugeUI : MonoBehaviour
{
    [SerializeField, Min(0.01f)] private float _fadeSeconds = 0.08f;

    [Tooltip("평소 게이지 색")]
    [SerializeField] private Color _normalColor = Color.white;

    [Tooltip("끝까지 다 썼을 때의 색")]
    [SerializeField] private Color _dangerColor = new Color(0.90f, 0.20f, 0.20f, 1f);

    // 게이지가 호라서 일반 슬라이더처럼 일자로 깎으면 모양이 안 맞는다.
    // 호를 정사각형 캔버스에 담아 Radial360 으로 부채꼴을 쓸어내고, 채움 값은
    // 호가 실제로 놓인 각도 구간에만 대응시킨다. 그래야 호를 따라 깎인다.
    [Tooltip("호가 그려진 정사각형 스프라이트를 쓰는 Image. Radial360 으로 채운다")]
    [SerializeField] private Image _arcFill;

    [Tooltip("게이지가 0 일 때의 fillAmount. 호의 시작 각도에 해당한다")]
    [SerializeField, Range(0f, 1f)] private float _arcFillEmpty = 0.1569f;

    [Tooltip("게이지가 1 일 때의 fillAmount. 호의 끝 각도에 해당한다")]
    [SerializeField, Range(0f, 1f)] private float _arcFillFull = 0.3431f;

    private Slider _slider;
    private CanvasGroup _canvasGroup;
    private Image _fillImage;
    private float _targetAlpha;
    private bool _danger;

    public static ArrestToolGaugeUI Resolve()
    {
        return FindFirstObjectByType<ArrestToolGaugeUI>(FindObjectsInactive.Include);
    }

    private void Awake()
    {
        ResolveReferences();
    }

    public void SetGauge(float value01)
    {
        if (_arcFill == null && _slider == null)
        {
            ResolveReferences();
        }

        float clamped = Mathf.Clamp01(value01);

        if (_arcFill != null)
        {
            _arcFill.fillAmount = Mathf.Lerp(_arcFillEmpty, _arcFillFull, clamped);
        }
        else if (_slider != null)
        {
            _slider.value = clamped;
        }
        else
        {
            return;
        }

        SetVisible(true);
    }

    public void SetVisible(bool visible)
    {
        _targetAlpha = visible ? 1f : 0f;
    }

    // 끝까지 다 쓴 동안에는 빨갛게 보여 준다. 색만 바꾸고 값은 건드리지 않는다.
    public void SetDanger(bool danger)
    {
        if (_danger == danger)
        {
            return;
        }

        _danger = danger;
        ApplyFillColor();
    }

    private void ApplyFillColor()
    {
        if (_fillImage == null)
        {
            ResolveReferences();
        }

        Image target = _arcFill != null ? _arcFill : _fillImage;
        if (target != null)
        {
            target.color = _danger ? _dangerColor : _normalColor;
        }
    }

    private void Update()
    {
        if (_canvasGroup == null)
        {
            ResolveReferences();
        }

        if (_canvasGroup == null)
        {
            return;
        }

        float step = _fadeSeconds > 0f ? Time.unscaledDeltaTime / _fadeSeconds : 1f;
        _canvasGroup.alpha = Mathf.MoveTowards(_canvasGroup.alpha, _targetAlpha, step);
    }

    private void ResolveReferences()
    {
        _canvasGroup = GetComponent<CanvasGroup>();
        _slider = GetComponent<Slider>();

        if (_fillImage == null && _slider != null && _slider.fillRect != null)
        {
            _fillImage = _slider.fillRect.GetComponent<Image>();
        }
    }
}
