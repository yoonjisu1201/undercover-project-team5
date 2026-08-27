// 사운드 구간 식별자. 값을 명시적으로 고정해서, 나중에 항목을 추가해도
// 이미 저장된 SoundData 에셋의 값이 밀리지 않게 한다.
// 새 사운드를 추가할 땐 마지막 값 다음 번호를 이어서 쓸 것.
public enum SoundKey
{
    None = -1,

    // 플레이어
    Player_FootstepWalk = 0,
    Player_FootstepRun = 1,
    Player_Jump = 2,

    // 착지. 점프와 뜻이 달라서 따로 둔다 — 한 SoundData 에 같이 넣으면 둘 중 하나가 무작위로 난다.
    Player_Land = 55,
    Player_Hit = 3,
    Player_Downed = 4,
    Player_Revive = 5,

    // 심장 박동. 긴장도가 올라갈수록 빠른 클립으로 바꿔 단다.
    // 키를 BPM 으로 짓지 않는 이유는, 나중에 템포를 조정해도 키와 호출부를 안 고치기 위해서다.
    Player_HeartBeat_Tired = 6,      // 스태미나가 빨간 구간에 들어섬 (70 BPM)
    Player_HeartBeat_Exhausted = 7,  // 스태미나가 거의 바닥 (90 BPM)
    Player_HeartBeat_Hiding = 8,     // 보스가 수색 중 — 숨어 있는 상태 (120 BPM)
    Player_HeartBeat_Spotted = 9,    // 숨어 있다 발각됨 (180 BPM)

    // 보스
    Boss_FootstepWalk = 50,
    Boss_FootstepRun = 51,

    // 휘두르는 순간. 맞은 쪽이 내는 Player_Hit 과 달리, 빗나가도 난다.
    Boss_Attack = 57,

    // 순간이동. 떠난 자리에서 사라지는 소리를 내고, 도착한 자리에서 나타나는 소리를 낸다.
    // Boss_Disappear(52)는 둘로 나누기 전에 쓰던 Boss_TeleportCue 와 같은 에셋이라 번호를 그대로 쓴다.
    Boss_Disappear = 52,
    Boss_Appear = 53,

    // 상호작용
    Item_Pickup = 10,
    Inventory_SlotSelect = 11,
    Interact_Fail = 12,
    // 본부·지하 출입문. 들어갈 때와 나올 때 소리가 달라야 해서 나눠 둔다.
    // Door_Open(13)은 둘로 나누기 전에 쓰던 키다. 저장된 에셋과 어긋나지 않게 번호는 비워 둔다.
    Door_In = 16,
    Door_Out = 17,

    // 대기실에 들어왔을 때. 본부 출입문과 소리가 달라서 따로 둔다.
    Room_In = 54,

    Lever_Toggle = 14,

    // 지하 미로 문. 본부 출입구(Door_Open)와 소리가 달라야 해서 따로 둔다.
    Basement_Door_Open = 15,

    // 미션
    Mission_UiOpen = 20,
    Mission_ItemInsert = 21,
    Mission_Complete = 22,

    // 게임 흐름
    Round_Start = 30,
    Round_Clear = 31,
    Round_TimeWarning = 32,
    Game_Success = 33,
    Game_Fail = 34,
    // 체포하는 순간(수갑 체결). 결과를 알리는 아래 두 소리보다 먼저 난다.
    Arrest = 37,
    Arrest_Success = 35,
    Arrest_Wrong = 36,

    // UI
    Ui_Click = 40,
    Ui_PopupOpen = 41,

    // Tab 으로 정보 허브를 여닫을 때. 팝업 공통음(Ui_PopupOpen/Close)과 달라야 해서 따로 둔다.
    Tab_Open = 58,
    Tab_Close = 59,
    Ui_PopupClose = 42,
    Ui_ClueToast = 43,

    // 본부가 새 몽타주를 보냈을 때 뜨는 알림. 단서 알림과 구분되어야 어느 쪽이 왔는지 소리로 안다.
    Ui_MontageShared = 56,
    Ui_Click2 = 44,

    // Button
    Button_Ready = 45,
    Button_Start = 46,
}
