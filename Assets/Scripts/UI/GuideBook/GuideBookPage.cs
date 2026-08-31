using System;
using TMPro;
using UnityEngine;

[RequireComponent(typeof(RectTransform))]
public class GuideBookPage : MonoBehaviour {
	[Header("=== 가이드북 내부 요소 ===")]
	[SerializeField] private TMP_Text _headerText;
	[SerializeField] private TMP_Text _subtitleText;
	[SerializeField] private TMP_Text _sectionNumber;
	[SerializeField] private TMP_Text _pageIndicatorText;
	
	private RectTransform _rectTransform;
	public RectTransform RectTransform => _rectTransform ??= GetComponent<RectTransform>();

	// 페이지 번호는 한 번만 정하면 되므로 현지화되는 문구와 분리한다.
	public void Initialize(uint pageNum, uint totalPageNumber)
	{
		if (_pageIndicatorText != null) _pageIndicatorText.text = $"{pageNum:D2} / {totalPageNumber:D2}";
		if (_sectionNumber != null) _sectionNumber.text = $"{pageNum:D2}";
	}

	// 가이드북을 열 때와 언어가 바뀔 때마다 다시 호출된다.
	public void ApplyTexts(string headerText, string subtitleText)
	{
		if (_headerText != null) _headerText.text = headerText;
		if (_subtitleText != null) _subtitleText.text = subtitleText;
	}
}