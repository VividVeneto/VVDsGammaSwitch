namespace GammaSwitch.Core;

public sealed class SwitchService
{
    private readonly IDisplayBackend backend;
    private readonly IRecoveryStore store;
    private RecoveryState recovery;
    private readonly Dictionary<string, int> cycleIndices = [];
    public SwitchService(IDisplayBackend backend, IRecoveryStore store)
    {
        this.backend = backend; this.store = store; recovery = store.Load();
        if (recovery.Version != 1 || recovery.Displays is null || recovery.Displays.Any(s=>s is null || string.IsNullOrWhiteSpace(s.DisplayId) || s.Gamma is not { Length:768 }) ||
            recovery.Displays.Select(s=>s.DisplayId).Distinct(StringComparer.OrdinalIgnoreCase).Count() != recovery.Displays.Count)
            throw new InvalidDataException("恢复文件损坏，已停止操作。请保留 recovery.json 以便排查。");
    }
    public bool HasRecovery => recovery.Displays.Count > 0;
    public int RecoveryCount => recovery.Displays.Count;
    public IReadOnlyList<string> RecoveryDisplayIds => recovery.Displays.Select(s=>s.DisplayId).ToArray();

    private DisplayInfo Find(IReadOnlyList<DisplayInfo> displays, string id)
    {
        var matches = displays.Where(d=>string.Equals(d.Id,id,StringComparison.OrdinalIgnoreCase)).ToArray();
        if (matches.Length != 1) throw new InvalidOperationException("目标显示器已断开或身份不唯一，请刷新显示器并重新绑定。");
        if (matches[0].Mirrored) throw new InvalidOperationException("镜像 / 复制显示模式无法独立控制，请切换到扩展桌面。");
        return matches[0];
    }
    public void Apply(IReadOnlyList<ProfileTarget> targets)
    {
        if (targets.Count == 0 || targets.Select(t=>t.DisplayId).Distinct(StringComparer.OrdinalIgnoreCase).Count() != targets.Count)
            throw new InvalidOperationException("场景必须包含目标，且每台显示器只能出现一次。");
        var displays = backend.Enumerate();
        var planned = targets.Select(t=>
        {
            if (!Enum.IsDefined(t.Mode)) throw new InvalidOperationException("未知的应用模式。");
            var display = Find(displays,t.DisplayId);
            var profile = IccProfile.Load(t.ProfilePath);
            if (t.Mode == ProfileMode.GammaOnly && profile.Gamma is null)
                throw new InvalidOperationException($"{Path.GetFileName(t.ProfilePath)} 不含 VCGT，不能使用“仅曲线”模式。");
            return (Target:t, Display:display, Profile:profile, Before:backend.Capture(display));
        }).ToArray();
        var next = new RecoveryState { Displays = recovery.Displays.ToList() };
        foreach (var item in planned)
            if (!next.Displays.Any(s=>string.Equals(s.DisplayId,item.Display.Id,StringComparison.OrdinalIgnoreCase))) next.Displays.Add(item.Before);
        store.Save(next); // Durable original state is required before the first native write.
        recovery = next;
        var touched = new List<(DisplayInfo Display, DisplaySnapshot Before)>();
        try
        {
            foreach (var item in planned)
            {
                touched.Add((item.Display,item.Before)); // Also roll back a partially written failing display.
                if (item.Target.Mode != ProfileMode.GammaOnly) backend.SetProfile(item.Display,item.Profile.Path);
                if (item.Target.Mode != ProfileMode.IccOnly)
                {
                    var baseline = recovery.Displays.Single(s=>string.Equals(s.DisplayId,item.Display.Id,StringComparison.OrdinalIgnoreCase));
                    backend.SetGamma(item.Display,item.Profile.Gamma ?? baseline.Gamma);
                }
            }
        }
        catch (Exception ex)
        {
            var failures = new List<string>();
            foreach (var item in touched.AsEnumerable().Reverse())
                try { backend.Restore(item.Display,item.Before); }
                catch (Exception rollback) { failures.Add($"{item.Display.Name}: {rollback.Message}"); }
            throw new InvalidOperationException("切换失败：" + ex.Message +
                (failures.Count == 0 ? "\n已回滚至切换前状态。" : "\n部分回滚失败，恢复文件已保留：\n"+string.Join("\n",failures)),ex);
        }
    }
    public void RestoreAll()
    {
        var errors = new List<string>();
        foreach (var state in recovery.Displays.ToArray())
            try { RestoreDisplay(state.DisplayId); }
            catch (Exception ex) { errors.Add(ex.Message); }
        if (errors.Count > 0) throw new InvalidOperationException("以下显示器未能恢复，恢复文件已保留：\n"+string.Join("\n",errors));
        cycleIndices.Clear();
    }
    public void RestoreDisplay(string id)
    {
        var state = recovery.Displays.SingleOrDefault(s=>string.Equals(s.DisplayId,id,StringComparison.OrdinalIgnoreCase));
        if (state is null) return;
        backend.Restore(Find(backend.Enumerate(),id),state);
        var next = new RecoveryState { Displays = recovery.Displays.Where(s=>s != state).ToList() };
        store.Save(next); recovery = next;
    }
    public void Execute(SwitchAction action)
    {
        switch (action.Kind)
        {
            case ActionKind.Apply: Apply(action.Targets); break;
            case ActionKind.RestoreAll: RestoreAll(); break;
            case ActionKind.RestoreDisplay:
                if (action.Targets.Count != 1) throw new InvalidOperationException("请选择一台恢复目标显示器。");
                RestoreDisplay(action.Targets[0].DisplayId); break;
            case ActionKind.Cycle:
                if (action.Targets.Count == 0 || action.Targets.Select(t=>t.DisplayId).Distinct(StringComparer.OrdinalIgnoreCase).Count()!=1)
                    throw new InvalidOperationException("循环预设必须指向同一显示器。");
                var index=cycleIndices.GetValueOrDefault(action.Id) % action.Targets.Count;
                Apply([action.Targets[index]]); cycleIndices[action.Id]=(index+1)%action.Targets.Count; break;
            default: throw new InvalidOperationException("未知动作类型。");
        }
    }
}
