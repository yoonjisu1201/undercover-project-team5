using Cysharp.Threading.Tasks;
using System;
using System.Collections.Generic;
using Unity.Services.Vivox;
using UnityEngine;

// Vivox 입출력 장치 목록을 다루는 순수 헬퍼. 상태를 갖지 않는다.
// 장치 목록 필터링과 순환 선택 규칙이 VivoxManager의 로그인·채널 흐름과 섞여 있어서 분리했다.
public static class VivoxAudioDevices
{
	// Vivox가 제공하는 기본 장치 ID들. "Default Communication Device"는 목록에 노출하지 않고
	// 초기 선택용으로만 쓰고, "Default System Device"는 목록 맨 앞에 둔다.
	private const string DefaultSystemDeviceId = "Default System Device";
	private const string DefaultCommunicationDeviceId = "Default Communication Device";

	// direction만큼 다음/이전 입력 장치로 옮긴다. 목록 양 끝에서는 순환한다.
	// 실제로 바뀌었는지와 무관하게, 화면을 갱신해야 하면 true를 돌려준다.
	public static async UniTask<bool> SelectNextInputAsync(int direction)
	{
		var service = VivoxService.Instance;
		List<VivoxInputDevice> devices = GetSelectableInputDevices();

		if (devices.Count == 0)
		{
			return true;
		}

		int currentIndex = FindDeviceIndex(devices, service.ActiveInputDevice?.DeviceID);
		int nextIndex = WrapIndex(currentIndex + direction, devices.Count);

		try
		{
			await service.SetActiveInputDeviceAsync(devices[nextIndex]);
			return true;
		}
		catch (Exception e)
		{
			Debug.LogError($"[Vivox] 입력 장치 변경 실패: {e.Message}");
			return false;
		}
	}

	public static async UniTask<bool> SelectNextOutputAsync(int direction)
	{
		var service = VivoxService.Instance;
		List<VivoxOutputDevice> devices = GetSelectableOutputDevices();

		if (devices.Count == 0)
		{
			return true;
		}

		int currentIndex = FindDeviceIndex(devices, service.ActiveOutputDevice?.DeviceID);
		int nextIndex = WrapIndex(currentIndex + direction, devices.Count);

		try
		{
			await service.SetActiveOutputDeviceAsync(devices[nextIndex]);
			return true;
		}
		catch (Exception e)
		{
			Debug.LogError($"[Vivox] 출력 장치 변경 실패: {e.Message}");
			return false;
		}
	}

	// 로그인 직후 통신용 기본 장치를 골라 둔다. 목록에 없으면 아무것도 하지 않는다.
	public static async UniTask SelectDefaultCommunicationDevicesAsync()
	{
		List<VivoxInputDevice> inputDevices = GetSelectableInputDevices();
		List<VivoxOutputDevice> outputDevices = GetSelectableOutputDevices();

		int inputIndex = FindDeviceIndex(inputDevices, DefaultCommunicationDeviceId);
		if (inputDevices.Count > 0 && inputDevices[inputIndex].DeviceID == DefaultCommunicationDeviceId)
		{
			await VivoxService.Instance.SetActiveInputDeviceAsync(inputDevices[inputIndex]);
		}

		int outputIndex = FindDeviceIndex(outputDevices, DefaultCommunicationDeviceId);
		if (outputDevices.Count > 0 && outputDevices[outputIndex].DeviceID == DefaultCommunicationDeviceId)
		{
			await VivoxService.Instance.SetActiveOutputDeviceAsync(outputDevices[outputIndex]);
		}
	}

	private static List<VivoxInputDevice> GetSelectableInputDevices()
	{
		var result = new List<VivoxInputDevice>();

		foreach (var device in VivoxService.Instance.AvailableInputDevices)
		{
			AddSelectable(result, device, device.DeviceID);
		}

		return result;
	}

	private static List<VivoxOutputDevice> GetSelectableOutputDevices()
	{
		var result = new List<VivoxOutputDevice>();

		foreach (var device in VivoxService.Instance.AvailableOutputDevices)
		{
			AddSelectable(result, device, device.DeviceID);
		}

		return result;
	}

	// 통신용 기본 장치는 목록에서 빼고, 시스템 기본 장치는 맨 앞에 놓는다.
	private static void AddSelectable<T>(List<T> result, T device, string deviceId)
	{
		if (deviceId == DefaultCommunicationDeviceId)
		{
			return;
		}

		if (deviceId == DefaultSystemDeviceId)
		{
			result.Insert(0, device);
		}
		else
		{
			result.Add(device);
		}
	}

	// 현재 장치가 목록에 없으면 0을 돌려준다.
	private static int FindDeviceIndex(List<VivoxInputDevice> devices, string deviceId)
	{
		for (int i = 0; i < devices.Count; i++)
		{
			if (devices[i].DeviceID == deviceId)
			{
				return i;
			}
		}

		return 0;
	}

	private static int FindDeviceIndex(List<VivoxOutputDevice> devices, string deviceId)
	{
		for (int i = 0; i < devices.Count; i++)
		{
			if (devices[i].DeviceID == deviceId)
			{
				return i;
			}
		}

		return 0;
	}

	// 음수 인덱스도 올바르게 순환시킨다.
	private static int WrapIndex(int index, int count)
	{
		return (index % count + count) % count;
	}
}
