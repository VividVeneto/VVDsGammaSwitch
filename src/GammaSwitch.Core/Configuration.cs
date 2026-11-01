namespace GammaSwitch.Core;

public sealed record Hotkey(uint Modifiers, uint Key);
public static class ShortcutValidation
{
    public static void Validate(IEnumerable<Hotkey?> keys)
    {
        var used = new HashSet<Hotkey>();
        foreach (var key in keys)
        {
            if (key is null) continue;
            if ((key.Modifiers & ~15U) != 0 || key.Key < 8 || key.Key > 254 ||
                key.Key is 0x10 or 0x11 or 0x12 or 0x5B or 0x5C or 0x7B || key.Key is >= 0xA0 and <= 0xA5)
                throw new ArgumentException("请选择有效按键；F12 为系统调试保留键。");
            if (key.Modifiers == 0 && !(key.Key is >= 0x70 and <= 0x87))
                throw new ArgumentException("字母、数字和普通按键需要搭配 Ctrl / Alt / Shift / Win，以免拦截正常输入。");
            if (!used.Add(key)) throw new ArgumentException("两个已启用动作使用了相同的快捷键。");
        }
    }
}

public static class ActionValidation
{
    public static void Validate(AppSettings settings)
    {
        if (settings.Version != 1) throw new InvalidDataException("不支持此配置文件版本。");
        if (settings.Actions is null || settings.Actions.Count > 300) throw new InvalidDataException("动作列表无效。");
        var ids = new HashSet<string>();
        foreach (var action in settings.Actions)
        {
            if (action is null || string.IsNullOrWhiteSpace(action.Id) || !ids.Add(action.Id) || string.IsNullOrWhiteSpace(action.Name))
                throw new ArgumentException("动作名称和标识不能为空或重复。");
            if (!Enum.IsDefined(action.Kind) || action.Targets is null || action.Targets.Any(t => t is null || !Enum.IsDefined(t.Mode)))
                throw new ArgumentException("动作类型或目标无效。");
            if (action.Kind != ActionKind.RestoreAll && (action.Targets.Count == 0 || action.Targets.Any(t=>string.IsNullOrWhiteSpace(t.DisplayId))))
                throw new ArgumentException("请选择目标显示器。");
            if (action.Kind is ActionKind.Apply or ActionKind.Cycle && action.Targets.Any(t=>string.IsNullOrWhiteSpace(t.ProfilePath)))
                throw new ArgumentException("请选择 ICC / ICM 文件。");
            if (action.Kind == ActionKind.Apply && action.Targets.Select(t=>t.DisplayId).Distinct(StringComparer.OrdinalIgnoreCase).Count() != action.Targets.Count)
                throw new ArgumentException("同一多屏场景中，每台显示器只能出现一次。");
            if (action.Kind == ActionKind.Cycle && action.Targets.Select(t=>t.DisplayId).Distinct(StringComparer.OrdinalIgnoreCase).Count() != 1)
                throw new ArgumentException("循环切换的所有配置必须指向同一台显示器。");
            if (action.Kind == ActionKind.RestoreDisplay && action.Targets.Count != 1)
                throw new ArgumentException("单屏恢复动作只能选择一台显示器。");
        }
        ShortcutValidation.Validate(settings.Actions.Where(a=>a.Enabled).Select(a=>a.Shortcut));
    }
}
