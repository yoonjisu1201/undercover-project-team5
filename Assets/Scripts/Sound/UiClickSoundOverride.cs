using UnityEngine;
using UnityEngine.UI;

// 이 버튼만 기본 클릭음 대신 다른 소리를 낸다. None으로 두면 소리가 나지 않는다.
[RequireComponent(typeof(Button))]
public class UiClickSoundOverride : MonoBehaviour
{
    [SerializeField] private SoundKey _key = SoundKey.Ui_Click;

    public SoundKey Key => _key;
}
