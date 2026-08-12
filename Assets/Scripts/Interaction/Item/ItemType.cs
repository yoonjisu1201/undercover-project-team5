// 아이템 종류 식별자. 값을 명시적으로 고정해서, 나중에 항목을 추가해도
// 이미 저장된 ItemData 에셋의 값이 밀리지 않게 한다.
// 새 아이템을 추가할 땐 마지막 값 다음 번호를 이어서 쓸 것 (기존 값 변경 금지).
public enum ItemType
{
    None = -1,
    AlienCaptureGun = 0,
    AlienShotgun = 1,
    Antenna = 2,
    Battery_20 = 3,
    Battery_30 = 4,
    Battery_40 = 5,
    Battery_50 = 6,
    BeaconTracker = 7,
    Book = 8,
    Clue = 9,
    GuideBook = 10,
    UnusedTraceAnalyzer = 11,
    EnergyBar = 12,
    RefillPack = 13,
    ContaminatedSample = 14,
}
