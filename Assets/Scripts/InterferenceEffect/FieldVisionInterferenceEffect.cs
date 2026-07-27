// 플레이어의 시야를 방해하는 효과를 식별합니다.
using Unity.Netcode;
using Unity.Services.Matchmaker.Models;
using Unity.VisualScripting;
using UnityEngine;

public sealed class FieldVisionInterferenceEffect : InterferenceEffectBase
{
    // 카메라 앞에 생성할 안개 파티클 프리팹입니다.
    [SerializeField]
    private ParticleSystem _fogPrefab;

    // 현재 로컬 카메라 앞에 생성된 안개입니다.
    private ParticleSystem _activeFog;

    // 시야 방해 효과 식별자를 가져옵니다.
    public override InterferenceEffectType _type => InterferenceEffectType.FieldVision;

    private void Awake()
    {
        var player = NetworkManager.Singleton.LocalClient.PlayerObject.GetComponent<Player>();
        var position = player.PlayerMove.HeadPivot.transform.localPosition;
        //position += transform.forward * _cameraDistance;

        _activeFog = Instantiate(_fogPrefab, player.PlayerMove.HeadPivot.transform);
        _activeFog.transform.localPosition = position;
        _activeFog.gameObject.SetActive(false);
    }

    public override void Activate()
    {
        _activeFog.gameObject.SetActive(true);

        base.Activate();
    }

    public override void Deactivate(InterferenceEndReason reason)
    {
        _activeFog.gameObject.SetActive(false);

        base.Deactivate(reason);
    }
}
