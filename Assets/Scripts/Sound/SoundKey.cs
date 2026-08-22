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

    // 상호작용
    Item_Pickup = 10,
    Inventory_SlotSelect = 11,
    Interact_Fail = 12,
    Door_Open = 13,
    Lever_Toggle = 14,

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
}
