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

	public void Initialize(
		string headerText,
		string subtitleText,
		uint pageNum, 
		uint totalPageNumber) 
	{
		if (_pageIndicatorText != null) _pageIndicatorText.text = $"{pageNum:D2} / {totalPageNumber:D2}";
		if (_sectionNumber != null) _sectionNumber.text = $"{pageNum:D2}";
		if (_headerText != null) _headerText.text = headerText;
		if (_subtitleText != null) _subtitleText.text = subtitleText;
	}
}