// 플레이어의 시야를 방해하는 효과를 식별합니다.
using GlitchSample;
using Unity.Netcode;
using Unity.Services.Matchmaker.Models;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.Rendering;
using URPGlitch;

public sealed class GlitchInterferenceEffect : InterferenceEffectBase
{
    [SerializeField] private Volume _globalVolume;

    private AnalogGlitchVolume _analogGlitch;
    private BlockGlitchVolume _blockGlitch;

    // 시야 방해 효과 식별자를 가져옵니다.
    public override InterferenceEffectType _type => InterferenceEffectType.Glitch;

    private void Awake()
    {
        _globalVolume.profile.TryGet<AnalogGlitchVolume>(out _analogGlitch);
        _globalVolume.profile.TryGet<BlockGlitchVolume>(out _blockGlitch);
    }

    public override void Activate()
    {
        _analogGlitch.active = true;
        _blockGlitch.active = true;

        base.Activate();
    }

    public override void Deactivate(InterferenceEndReason reason)
    {
        _analogGlitch.active = false;
        _blockGlitch.active = false;

        base.Deactivate(reason);
    }
}
