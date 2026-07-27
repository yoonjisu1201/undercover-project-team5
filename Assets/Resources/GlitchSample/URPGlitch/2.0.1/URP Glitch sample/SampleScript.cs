using UnityEngine;
using UnityEngine.Rendering;
using URPGlitch;

public class SampleScript : MonoBehaviour
{
    [SerializeField] private Volume volume;
    private bool isEnabled = true;

    private AnalogGlitchVolume analogGlitchVolume;
    private DigitalGlitchVolume digitalGlitchVolume;

    private void Start()
    {
        volume.profile.TryGet<AnalogGlitchVolume>(out analogGlitchVolume);
        volume.profile.TryGet<DigitalGlitchVolume>(out digitalGlitchVolume);
    }

    public void ToggleEffects()
    {
        isEnabled = !isEnabled;
        analogGlitchVolume.active = isEnabled ? true : false;
        digitalGlitchVolume.active = isEnabled ? true : false;
    }

    public void RandomSettings()
    {
        analogGlitchVolume.scanLineJitter.value = Random.Range(0f, 1f);
        analogGlitchVolume.verticalJump.value = Random.Range(0f, 1f);
        analogGlitchVolume.horizontalShake.value = Random.Range(0f, 1f);
        analogGlitchVolume.colorDrift.value = Random.Range(0f, 1f);

        digitalGlitchVolume.intensity.value = Random.Range(0f, 1f);
    }
}
