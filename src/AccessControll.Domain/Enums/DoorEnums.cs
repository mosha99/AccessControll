namespace AccessControll.Domain.Enums;

/// <summary>
/// Determines the operating mode of an ESP8266 station.
/// </summary>
public enum StationType
{
    /// <summary>
    /// Has keyboard + display.  After 2FA, asks the user for a 2-digit output code
    /// and can control ANY output in the system (that the user has permission for),
    /// even outputs assigned to other stations.
    /// </summary>
    General = 0,

    /// <summary>
    /// Has keyboard + display.  After 2FA, automatically opens all outputs that
    /// are assigned to THIS station and that the authenticated user has access to —
    /// no output code entry needed.
    /// </summary>
    Door = 1,

    /// <summary>
    /// No keyboard, no display.  Pure relay output controller driven by the server.
    /// Users operate it indirectly via another station or the web UI.
    /// </summary>
    RemoteControl = 2,
}

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
