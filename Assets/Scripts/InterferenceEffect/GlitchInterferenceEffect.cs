using System.Linq;
using GlitchSample;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Rendering;
using URPGlitch;

public sealed class GlitchInterferenceEffect : InterferenceEffectBase
{
    [SerializeField] private Volume _globalVolume;

    private AnalogGlitchVolume _analogGlitch;
    private BlockGlitchVolume _blockGlitch;

    public override InterferenceEffectType _type => InterferenceEffectType.Glitch;

    private void Awake()
    {
        _globalVolume.profile.TryGet<AnalogGlitchVolume>(out _analogGlitch);
        _globalVolume.profile.TryGet<BlockGlitchVolume>(out _blockGlitch);
    }

    public override ulong[] GetTargetClientsList()
    {
        return NetworkManager.ConnectedClientsIds.ToArray();
    }

    protected override void OnActivateEffect()
    {
        _analogGlitch.active = true;
        _blockGlitch.active = true;
    }

    protected override void OnDeactivateEffect(InterferenceEndReason reason)
    {
        _analogGlitch.active = false;
        _blockGlitch.active = false;
    }
}
