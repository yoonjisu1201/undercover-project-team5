using System;
using System.Collections.Generic;
using UnityEngine;

public class CCTVHub : MonoBehaviour {
	private readonly List<CCTVPoint> _cctvPoints = new List<CCTVPoint>();
	[SerializeField] private Camera _cctvCamera;

	[Header("=== CCTV 위치 리스트 ===")]
	[SerializeField] private Transform[] _cctvAreas;

	public IReadOnlyList<CCTVPoint> CCTVPoints => _cctvPoints;
	public int CameraCount => _cctvPoints.Count;

	// CCTV를 특정 포인트들로 옮겨가면서 여러 위치의 CCTV를 구현
	private int _usingCctvNumber;
	public int UsingCctvNumber => _usingCctvNumber;
	
	// 사용하는 CCTV 외부에서 접근 가능하도록 공유
	public Camera CctvCamera => _cctvCamera;

	public event Action<int> OnCctvNumberChanged;

	// 개별 CCTV 포인트의 연결 상태 변경을 카메라 번호와 함께 한 곳에서 받고 싶은 소비자를 위한 집계 이벤트
	public event Action<int, CCTVConnectionState> OnAnyPointStateChanged;

	// 스폰 시에 Cctv 리스트 확인
	private void Awake() {
		foreach (Transform area in _cctvAreas) {
			foreach (Transform pointTransform in area) {
				CCTVPoint point = pointTransform.GetComponent<CCTVPoint>();
				if (point == null) {
					Debug.LogError($"'{pointTransform.name}'에 CCTVPoint 컴포넌트가 없습니다.", pointTransform);
					continue;
				}

				point.Initialize(_cctvPoints.Count);
				// CCTV 상태 변경 시에 변경된 CCTV 번호와 상태를 발행해주는 이벤트
				point.OnConnectionStateChanged += state => OnAnyPointStateChanged?.Invoke(point.CameraNumber, state);
				_cctvPoints.Add(point);
			}
		}

		_usingCctvNumber = 0;
		SwitchCCTV(_usingCctvNumber);
	}

	// 카메라 번호로 해당 CCTV 포인트를 바로 찾아간다.
	public CCTVPoint GetPoint(int cameraNumber) {
		return _cctvPoints[NormalizeIndex(cameraNumber)];
	}

	// 포인트 참조 없이 카메라 번호만 아는 소비자(네트워크 레이어 등)를 위한 전달 창구.
	public void ApplyConnectionMask(int cameraNumber, int connectionMask) {
		GetPoint(cameraNumber).ApplyConnectionMask(connectionMask);
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
		_cctvCamera.transform.SetParent(_cctvPoints[number].transform);
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
