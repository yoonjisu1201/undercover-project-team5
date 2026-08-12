using Cysharp.Threading.Tasks;
using Unity.Services.Vivox;
using UnityEngine;

// 마이크 입력을 그대로 스피커로 흘려서 자기 목소리를 들려준다.
//
// Vivox 에코 채널을 쓰지 않는 이유가 있다. 에코 채널은 서버가 소리를 되돌려주는 구조라
// 왕복 지연이 그대로 들려서(노래방 에코) 마이크 확인용으로 쓸 수 없다.
// 여기는 네트워크를 거치지 않으므로 그런 지연이 없다.
public sealed class MicMonitor
{
	// 음량을 계산할 때 한 번에 보는 샘플 수.
	private const int EnergySampleCount = 256;

	// RMS는 값이 작아서 막대가 거의 안 움직인다. 보기 좋은 정도로 키우는 배율.
	private const float EnergyGain = 4f;

	private readonly GameObject _host;
	private readonly float[] _energySamples = new float[EnergySampleCount];

	private AudioSource _source;
	private AudioClip _clip;
	private string _device;

	public MicMonitor(GameObject host)
	{
		_host = host;
	}

	public bool IsRunning => _clip != null && !string.IsNullOrEmpty(_device);

	// 현재 마이크 음량(0~1). 재생 중이 아니면 0이다.
	public float Energy01
	{
		get
		{
			if (!IsRunning)
			{
				return 0f;
			}

			int position = Microphone.GetPosition(_device) - EnergySampleCount;
			if (position < 0)
			{
				return 0f;
			}

			_clip.GetData(_energySamples, position);

			float sum = 0f;
			foreach (float sample in _energySamples)
			{
				sum += sample * sample;
			}

			return Mathf.Clamp01(Mathf.Sqrt(sum / EnergySampleCount) * EnergyGain);
		}
	}

	// keepRunning이 false를 돌려주면(기다리는 동안 테스트가 끝난 경우) 재생하지 않고 정리한다.
	public async UniTask StartAsync(System.Func<bool> keepRunning)
	{
		_device = ResolveDevice();

		if (string.IsNullOrEmpty(_device))
		{
			Debug.LogWarning("[MicMonitor] 사용할 수 있는 마이크가 없어 소리를 들려줄 수 없습니다.");
			return;
		}

		_clip = Microphone.Start(_device, true, 1, AudioSettings.outputSampleRate);
		EnsureSource();

		_source.clip = _clip;
		_source.loop = true;

		// 캡처가 시작되기 전에 재생하면 빈 구간이 먼저 나가 끊기는 소리가 난다.
		await UniTask.WaitUntil(() => Microphone.GetPosition(_device) > 0);

		if (keepRunning != null && !keepRunning())
		{
			Stop();
			return;
		}

		_source.Play();
	}

	public void Stop()
	{
		if (_source != null)
		{
			_source.Stop();
			_source.clip = null;
		}

		if (!string.IsNullOrEmpty(_device))
		{
			Microphone.End(_device);
			_device = null;
		}

		_clip = null;
	}

	private void EnsureSource()
	{
		if (_source != null)
		{
			return;
		}

		_source = _host.AddComponent<AudioSource>();
		_source.playOnAwake = false;
		_source.spatialBlend = 0f;        // 2D로 재생
		_source.bypassEffects = true;     // 씬의 오디오 효과가 섞이지 않게 한다
		_source.bypassReverbZones = true;
	}

	// Vivox에서 고른 입력 장치와 같은 것을 쓰도록 이름으로 맞춘다. 못 찾으면 첫 번째 장치를 쓴다.
	private static string ResolveDevice()
	{
		string[] devices = Microphone.devices;
		if (devices.Length == 0)
		{
			return null;
		}

		string activeDeviceName = VivoxService.Instance?.ActiveInputDevice?.DeviceName;
		if (!string.IsNullOrEmpty(activeDeviceName))
		{
			foreach (string device in devices)
			{
				if (device == activeDeviceName)
				{
					return device;
				}
			}
		}

		return devices[0];
	}
}
