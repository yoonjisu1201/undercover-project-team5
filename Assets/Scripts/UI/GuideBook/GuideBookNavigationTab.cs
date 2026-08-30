using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
[RequireComponent(typeof(Button))]
public sealed class GuideBookNavigationTab : MonoBehaviour
{
    [Header("탭 구성 요소")]
    [SerializeField] private Image _background;
    [SerializeField] private TMP_Text _numberText;
    [SerializeField] private TMP_Text _titleText;
    [SerializeField] private Canvas _sortingCanvas;

    [Header("색상")]
    [SerializeField] private Color _normalBackgroundColor = new(0.14f, 0.22f, 0.31f, 1f);
    [SerializeField] private Color _selectedBackgroundColor = new(0.86f, 0.47f, 0.16f, 1f);

    private Action<int> _onClick;
    private int _pageIndex;
    private int _selectedSortingOrder;

    public void Initialize(int pageIndex, Action<int> onClick)
    {
        _pageIndex = pageIndex;
        _onClick = onClick;

        Canvas parentCanvas = transform.parent.GetComponentInParent<Canvas>();
        if (_sortingCanvas != null && parentCanvas != null)
        {
            _selectedSortingOrder = parentCanvas.sortingOrder + 1;
        }

        if (_numberText != null) _numberText.text = (pageIndex + 1).ToString("D2");
    }

    // 언어가 바뀔 때마다 다시 호출된다.
    public void SetTitle(string title)
    {
        if (_titleText != null) _titleText.text = title;
    }

    public void SetSelected(bool selected)
    {
        if (_background != null)
        {
            _background.color = selected ? _selectedBackgroundColor : _normalBackgroundColor;
        }

        if (_sortingCanvas != null)
        {
            _sortingCanvas.overrideSorting = selected;
            if (selected) _sortingCanvas.sortingOrder = _selectedSortingOrder;
        }
    }

    public void OnClick()
    {
        _onClick?.Invoke(_pageIndex);
    }
}
