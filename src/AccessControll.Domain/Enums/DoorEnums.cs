namespace AccessControll.Domain.Enums;

public enum DoorAction
{
    Open = 1,
    Close = 2,
    Lock = 3,
    Unlock = 4,
    ForceOpen = 5
}

public enum AccessResult
{
    Success = 1,
    Denied = 2,
    DoorDisabled = 3,
    NoPermission = 4,
    OutsideAllowedHours = 5,
    SystemError = 6
}
