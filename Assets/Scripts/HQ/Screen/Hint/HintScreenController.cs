using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class HintScreenController : ScreenBase {
	[Header("=== 힌트가 없을 때 나올 스크린 ===")]
	[SerializeField] private GameObject _noHintScreen;

	[Header("=== 힌트가 있을 때 나올 스크린 ===")]
	[SerializeField] private GameObject _hintScreen;

	[Header("=== 힌트 출력될 이미지 ===")]
	[SerializeField] private Image  _hintScreenImage;

	[Header("=== 힌트 인덱스 정보 출력용 텍스트 ===")] 
	[SerializeField] private TMP_Text _hintIndexText;
	
	// 힌트 출력에 사용될 Sprite 목록
	private readonly List<Sprite> _hintSprites = new List<Sprite>();
	
	// 마지막으로 보고 있었던 힌트의 Index
	private int _hintIdx;
	
	public event Action<int, int> OnHintNumberChanged;

	private void OnEnable() {
		OnHintNumberChanged += UpdateIdx;
		
		// 힌트 없으면 노힌트스크린 켜기
		if (_hintSprites.Count <= 0) {
			_noHintScreen.SetActive(true);
			_hintScreen.SetActive(false);
		}
		
		// 힌트 있으면 힌트스크린 바로 열어주기
		else {
			_noHintScreen.SetActive(false);
			_hintScreen.SetActive(true);
			
			OpenHint(_hintIdx);		
		}
	}

	private void OnDisable() {
		OnHintNumberChanged -= UpdateIdx;
	}

	private void OpenHint(int index) {
		_hintIdx = NormalizeIndex(index);
		_hintScreenImage.sprite = _hintSprites[_hintIdx];
		
		OnHintNumberChanged?.Invoke(_hintIdx, _hintSprites.Count);
	}
	
	// Index가 1부터 시작하도록 하기 위해 + 1
	private void UpdateIdx(int index, int count) {
		_hintIndexText.text = $"{index + 1} / {count}";
	}
	
	// 값 자체를 0 ~ CctvPoints.Count - 1 안의 값으로 넣어주기 위한 함수.
	private int NormalizeIndex(int index) {
		return (index % _hintSprites.Count + _hintSprites.Count)
		       % _hintSprites.Count;
	}
}
