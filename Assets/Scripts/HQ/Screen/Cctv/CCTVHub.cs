using System;
using System.Collections.Generic;
using UnityEngine;

public class CCTVHub : MonoBehaviour {
	private readonly List<Transform> _cctvPoints = new List<Transform>();
	[SerializeField] private Camera _cctvCamera;

	// CCTV를 특정 포인트들로 옮겨가면서 여러 위치의 CCTV를 구현
	private int _usingCctvNumber;
	public int UsingCctvNumber => _usingCctvNumber;

	public event Action<int> OnCctvNumberChanged;
	
	private void Awake() {
		foreach (Transform point in transform) {
			_cctvPoints.Add(point);
		}
		
		_usingCctvNumber = 0;
		SwitchCCTV(_usingCctvNumber);
	}

	public void SwitchToPrevious() {
		SwitchCCTV(_usingCctvNumber - 1);	
	}
	public void SwitchToNext() {
		SwitchCCTV(_usingCctvNumber + 1);
	}
	
	private void SwitchCCTV(int number) {
		number = NormalizeIndex(number);
		_usingCctvNumber = number;
		
		// CCTV 포인트와 완전히 동일한 위치에 놓이도록 할 것
		_cctvCamera.transform.SetParent(_cctvPoints[number]);
		_cctvCamera.transform.localPosition = Vector3.zero;
		_cctvCamera.transform.localRotation = Quaternion.identity;
		
		OnCctvNumberChanged?.Invoke(_usingCctvNumber);
	}
	
	// 값 자체를 0 ~ CctvPoints.Count - 1 안의 값으로 넣어주기 위한 함수.
	private int NormalizeIndex(int index) {
		return (index % _cctvPoints.Count + _cctvPoints.Count)
		       % _cctvPoints.Count;
	}
}
