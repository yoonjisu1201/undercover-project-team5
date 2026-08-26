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
    Boss_TeleportCue = 52,

    // 상호작용
    Item_Pickup = 10,
    Inventory_SlotSelect = 11,
    Interact_Fail = 12,
    // 본부·지하 출입문. 들어갈 때와 나올 때 소리가 달라야 해서 나눠 둔다.
    // Door_Open(13)은 둘로 나누기 전에 쓰던 키다. 저장된 에셋과 어긋나지 않게 번호는 비워 둔다.
    Door_In = 16,
    Door_Out = 17,

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
    Arrest_Success = 35,
    Arrest_Wrong = 36,

    // UI
    Ui_Click = 40,
    Ui_PopupOpen = 41,
    Ui_PopupClose = 42,
    Ui_ClueToast = 43,
    Ui_Click2 = 44,
}
