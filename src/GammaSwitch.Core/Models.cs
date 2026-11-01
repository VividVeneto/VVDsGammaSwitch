namespace GammaSwitch.Core;

public enum ActionKind { Apply, Cycle, RestoreDisplay, RestoreAll }
public enum ProfileMode { Combined, GammaOnly, IccOnly }

public sealed class AppSettings
{
    public int Version { get; set; } = 1;
    public bool RestoreOnExit { get; set; } = true;
    public List<SwitchAction> Actions { get; set; } = [];
}

public sealed class SwitchAction
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "新预设";
    public bool Enabled { get; set; } = true;
    public ActionKind Kind { get; set; }
    public Hotkey? Shortcut { get; set; }
    public List<ProfileTarget> Targets { get; set; } = [];
}

public sealed class ProfileTarget
{
    public string DisplayId { get; set; } = "";
    public string ProfilePath { get; set; } = "";
    public ProfileMode Mode { get; set; } = ProfileMode.Combined;
}

public sealed record DisplayInfo(string Id, string DeviceName, string ColorDeviceKey, string Name, bool Primary, bool Mirrored);
public sealed record DisplaySnapshot(string DisplayId, string? EffectiveProfile, string? UserProfile, bool UsePerUser, ushort[] Gamma);
public sealed class RecoveryState
{
    public int Version { get; set; } = 1;
    public List<DisplaySnapshot> Displays { get; set; } = [];
}

public interface IDisplayBackend
{
    IReadOnlyList<DisplayInfo> Enumerate();
    DisplaySnapshot Capture(DisplayInfo display);
    void SetProfile(DisplayInfo display, string path);
    void SetGamma(DisplayInfo display, ushort[] gamma);
    void Restore(DisplayInfo display, DisplaySnapshot state);
}

public interface IRecoveryStore
{
    RecoveryState Load();
    void Save(RecoveryState state);
}
