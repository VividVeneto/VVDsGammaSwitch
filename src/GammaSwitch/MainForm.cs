using GammaSwitch.Core;

namespace GammaSwitch;

internal sealed class MainForm : Form
{
    private readonly WindowsDisplayBackend backend=new();
    private readonly JsonStore<AppSettings> settingsStore;
    private readonly SwitchService service;
    private AppSettings settings;
    private IReadOnlyList<DisplayInfo> displays=[];
    private readonly Dictionary<string,DisplaySnapshot> snapshots=new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string,DateTime> lastRun=[];
    private readonly DataGridView displayGrid=Ui.Grid();
    private readonly DataGridView actionGrid=Ui.Grid();
    private readonly CurveView curve=new(){Dock=DockStyle.Fill};
    private readonly StatusLine status=new(){Dock=DockStyle.Fill};
    private readonly CheckBox restoreOnExit=new(){AutoSize=true,Text="退出时恢复原始 ICC 与伽马表",Anchor=AnchorStyles.Left,Margin=Padding.Empty};
    private readonly RoundButton hotkeyChip,restoreSelectedButton,applyButton,editButton,addButton,copyButton,toggleButton,upButton,downButton,deleteButton;
    private readonly ContextMenuStrip actionMenu=new(),displayMenu=new();
    private readonly NotifyIcon tray;
    private readonly Icon trayIcon,pausedIcon;
    private readonly HotkeyRegistry hotkeys;
    private readonly string dataDirectory;
    private readonly string? settingsLoadError;
    private bool exiting,paused,loading=true,trayHintShown;

    public MainForm(string dataDirectory)
    {
        this.dataDirectory=dataDirectory;
        settingsStore=new(Path.Combine(dataDirectory,"settings.json"));
        displays=backend.Enumerate();
        service=new(backend,new RecoveryStore(Path.Combine(dataDirectory,"recovery.json")));
        try
        {
            settings=File.Exists(settingsStore.FilePath)?settingsStore.Load():CreateDefaults();
            ActionValidation.Validate(settings);
        }
        catch(Exception ex) when(ex is System.Text.Json.JsonException or InvalidDataException or ArgumentException or IOException or UnauthorizedAccessException)
        {
            settingsLoadError=ex.Message;
            settings=new(){Actions=[new(){Name="恢复所有显示器",Kind=ActionKind.RestoreAll}]};
        }
        Text="GammaSwitch · ICC 与伽马快捷切换";Font=Ui.Body;ForeColor=Ui.Ink;BackColor=Ui.Canvas;
        AutoScaleMode=AutoScaleMode.None;StartPosition=FormStartPosition.CenterScreen;
        MinimumSize=Ui.S(940,720);Size=Ui.S(1100,840);Icon=Ui.AppIcon;

        // Commands are created first: selection events below depend on them.
        hotkeyChip=Ui.Button("",(_,_)=>TogglePause(),ButtonStyle.Chip);hotkeyChip.Anchor=AnchorStyles.Right;hotkeyChip.Margin=Padding.Empty;
        restoreSelectedButton=Ui.Button("恢复所选屏幕",(_,_)=>RestoreSelected(),ButtonStyle.Normal,"把所选屏幕恢复到首次修改前的 ICC 与伽马表");
        applyButton=Ui.Button("立即应用",(_,_)=>ApplySelected(),ButtonStyle.Primary,"执行所选动作（Enter）");
        editButton=Ui.Button("编辑 / 快捷键",(_,_)=>EditSelected(),ButtonStyle.Normal,"修改名称、目标、ICC 与快捷键（F2 或双击）");
        addButton=Ui.Button("＋ 添加动作",(_,_)=>AddAction(),ButtonStyle.Normal,"新建预设、多屏场景、循环或恢复动作（Ctrl+N）");
        copyButton=Ui.Button("复制",(_,_)=>CopySelected(),ButtonStyle.Normal,"复制为新动作（Ctrl+D）");
        toggleButton=Ui.Button("禁用",(_,_)=>ToggleSelected(),ButtonStyle.Normal,"启用或禁用所选动作及其快捷键");
        upButton=Ui.Button("上移",(_,_)=>MoveSelected(-1),ButtonStyle.Normal,"上移所选动作，托盘菜单顺序同步（Ctrl+↑）");
        downButton=Ui.Button("下移",(_,_)=>MoveSelected(1),ButtonStyle.Normal,"下移所选动作，托盘菜单顺序同步（Ctrl+↓）");
        deleteButton=Ui.Button("删除",(_,_)=>DeleteSelected(),ButtonStyle.Danger,"删除所选动作（Delete）");

        var root=new TableLayoutPanel{Dock=DockStyle.Fill,Padding=Ui.P(22,16,22,12),ColumnCount=1,RowCount=5};
        root.ColumnStyles.Add(new(SizeType.Percent,100));
        root.RowStyles.Add(new(SizeType.AutoSize));
        root.RowStyles.Add(new(SizeType.Absolute,Ui.S(68)));
        root.RowStyles.Add(new(SizeType.Absolute,Ui.S(236)));
        root.RowStyles.Add(new(SizeType.Percent,100));
        root.RowStyles.Add(new(SizeType.AutoSize));
        Controls.Add(root);

        // Header: logo, title and global hotkey switch.
        var header=new TableLayoutPanel{Dock=DockStyle.Fill,AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,ColumnCount=3,RowCount=1,Margin=Ui.P(0,0,0,12)};
        header.ColumnStyles.Add(new(SizeType.AutoSize));header.ColumnStyles.Add(new(SizeType.Percent,100));header.ColumnStyles.Add(new(SizeType.AutoSize));
        var logo=new PictureBox{Image=Ui.RenderLogo(Ui.S(46)),Size=Ui.S(46,46),Margin=Ui.P(0,0,14,0),Anchor=AnchorStyles.Left};
        var titles=new FlowLayoutPanel{FlowDirection=FlowDirection.TopDown,WrapContents=false,AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,Anchor=AnchorStyles.Left,Margin=Padding.Empty};
        titles.Controls.Add(new Label{Text="GammaSwitch",Font=Ui.Title,ForeColor=Ui.Ink,AutoSize=true,Margin=Padding.Empty});
        titles.Controls.Add(new Label{Text="为每台屏幕一键切换 ICC 与伽马曲线，并给每个场景设定全局快捷键。",ForeColor=Ui.Muted,AutoSize=true,Margin=Ui.P(2,0,0,0)});
        header.Controls.Add(logo,0,0);header.Controls.Add(titles,1,0);header.Controls.Add(hotkeyChip,2,0);
        root.Controls.Add(header,0,0);

        // Notice banner: baseline reminder, or the recovery-mode explanation.
        var normal=settingsLoadError is null;
        var banner=new Card{BackColor=normal?Ui.AccentSoft:Ui.WarningSoft,BorderColor=normal?Color.FromArgb(198,226,233):Color.FromArgb(238,211,160),Padding=Ui.P(16,8,16,8)};
        banner.Controls.Add(new Label
        {
            Dock=DockStyle.Fill,AutoSize=false,TextAlign=ContentAlignment.MiddleLeft,BackColor=Color.Transparent,
            ForeColor=normal?Color.FromArgb(16,78,94):Ui.Warning,
            Text=normal
                ?"首次应用前，请保持当前的日常显示状态：程序会在第一次修改时保存恢复基准。关闭窗口后程序驻留在系统托盘。\n写入后会回读核对接口状态，但游戏内效果仍需目视确认；改变显示模式后可再次执行同一动作。"
                :"设置文件无法读取，已进入恢复模式：原文件已保留，你仍可恢复显示设置。修复后请重启程序。\n"+settingsStore.FilePath+"  ·  "+settingsLoadError
        });
        root.Controls.Add(banner,0,1);

        // Displays card: list on the left, gamma preview on the right.
        var displayCard=new Card();
        var displayBody=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2,RowCount=2,Margin=Padding.Empty};
        displayBody.ColumnStyles.Add(new(SizeType.Percent,100));displayBody.ColumnStyles.Add(new(SizeType.Absolute,Ui.S(220)));
        displayBody.RowStyles.Add(new(SizeType.AutoSize));displayBody.RowStyles.Add(new(SizeType.Percent,100));
        var displayHeader=Ui.CardHeader("显示器","选中一行，右侧显示该屏当前的伽马表",
            Ui.Button("刷新显示器",(_,_)=>RefreshDisplays(true),ButtonStyle.Normal,"重新检测显示器与当前 ICC（F5）"),
            restoreSelectedButton,
            Ui.Button("恢复全部",(_,_)=>Run(new(){Name="恢复全部",Kind=ActionKind.RestoreAll},true),ButtonStyle.Normal,"恢复本程序修改过的所有屏幕"));
        displayBody.Controls.Add(displayHeader,0,0);displayBody.SetColumnSpan(displayHeader,2);
        displayGrid.ReadOnly=true;displayGrid.StandardTab=true;displayGrid.Margin=Ui.P(0,0,16,0);
        displayGrid.Columns.Add(new DataGridViewTextBoxColumn{HeaderText="在线显示器",FillWeight=48});
        displayGrid.Columns.Add(new DataGridViewTextBoxColumn{HeaderText="当前默认 ICC",FillWeight=28});
        displayGrid.Columns.Add(new DataGridViewTextBoxColumn{HeaderText="状态",FillWeight=24});
        displayGrid.CellPainting+=PaintDisplayCell;
        displayGrid.SelectionChanged+=(_,_)=>{UpdateCurve();UpdateCommands();};
        displayGrid.CellMouseDown+=(_,e)=>{if(e.Button==MouseButtons.Right && e.RowIndex>=0)displayGrid.CurrentCell=displayGrid.Rows[e.RowIndex].Cells[Math.Max(0,e.ColumnIndex)];};
        displayGrid.ContextMenuStrip=displayMenu;
        displayBody.Controls.Add(displayGrid,0,1);displayBody.Controls.Add(curve,1,1);
        displayCard.Controls.Add(displayBody);root.Controls.Add(displayCard,0,2);

        // Actions card.
        var actionCard=new Card();
        var actionBody=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=3,Margin=Padding.Empty};
        actionBody.ColumnStyles.Add(new(SizeType.Percent,100));
        actionBody.RowStyles.Add(new(SizeType.AutoSize));actionBody.RowStyles.Add(new(SizeType.AutoSize));actionBody.RowStyles.Add(new(SizeType.Percent,100));
        actionBody.Controls.Add(Ui.CardHeader("动作与快捷键","双击或 F2 编辑 · Enter 立即应用 · 右键查看更多操作",addButton),0,0);
        var toolbar=new FlowLayoutPanel{Dock=DockStyle.Fill,AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,Margin=Ui.P(0,0,0,10)};
        toolbar.Controls.AddRange([applyButton,editButton,copyButton,toggleButton,upButton,downButton,deleteButton]);
        upButton.Margin=Ui.P(16,0,8,0);deleteButton.Margin=Ui.P(16,0,0,0);
        actionBody.Controls.Add(toolbar,0,1);
        actionGrid.ReadOnly=true;actionGrid.StandardTab=true;actionGrid.RowTemplate.Height=Ui.S(50);
        actionGrid.Columns.Add(new DataGridViewTextBoxColumn{HeaderText="状态",FillWeight=11,MinimumWidth=Ui.S(70)});
        actionGrid.Columns.Add(new DataGridViewTextBoxColumn{HeaderText="动作 / 场景",FillWeight=29});
        actionGrid.Columns.Add(new DataGridViewTextBoxColumn{HeaderText="自定义快捷键",FillWeight=24});
        actionGrid.Columns.Add(new DataGridViewTextBoxColumn{HeaderText="目标与配置",FillWeight=36,DefaultCellStyle=new(){ForeColor=Ui.Muted,SelectionForeColor=Ui.Muted}});
        actionGrid.CellDoubleClick+=(_,e)=>{if(e.RowIndex>=0)EditSelected();};
        actionGrid.CellPainting+=PaintActionCell;
        actionGrid.SelectionChanged+=(_,_)=>UpdateCommands();
        actionGrid.CellMouseDown+=(_,e)=>{if(e.Button==MouseButtons.Right && e.RowIndex>=0)actionGrid.CurrentCell=actionGrid.Rows[e.RowIndex].Cells[Math.Max(0,e.ColumnIndex)];};
        actionGrid.Paint+=PaintEmptyActions;
        actionGrid.ContextMenuStrip=actionMenu;
        actionBody.Controls.Add(actionGrid,0,2);
        actionCard.Controls.Add(actionBody);root.Controls.Add(actionCard,0,3);

        // Footer: exit option, status, exit.
        var footer=new TableLayoutPanel{Dock=DockStyle.Fill,AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,ColumnCount=3,RowCount=1,Margin=Padding.Empty};
        footer.ColumnStyles.Add(new(SizeType.AutoSize));footer.ColumnStyles.Add(new(SizeType.Percent,100));footer.ColumnStyles.Add(new(SizeType.AutoSize));
        restoreOnExit.Checked=settings.RestoreOnExit;
        restoreOnExit.Enabled=settingsLoadError is null;
        restoreOnExit.CheckedChanged+=(_,_)=>
        {
            if(loading)return;
            try{var copy=Clone();copy.RestoreOnExit=restoreOnExit.Checked;Commit(copy);}
            catch(Exception ex){loading=true;restoreOnExit.Checked=settings.RestoreOnExit;loading=false;Ui.Error(this,ex);}
        };
        Ui.Tips.SetToolTip(restoreOnExit,"退出程序时，把本程序修改过的屏幕恢复为首次修改前的状态");
        status.Margin=Ui.P(20,0,12,0);
        footer.Controls.Add(restoreOnExit,0,0);footer.Controls.Add(status,1,0);
        footer.Controls.Add(Ui.Button("退出程序",(_,_)=>ExitApp(),ButtonStyle.Normal,"退出 GammaSwitch 并释放全局快捷键"),2,0);
        root.Controls.Add(footer,0,4);

        hotkeys=new(new WindowsHotkeyRegistrar(Handle));
        var iconSize=Math.Max(16,SystemInformation.SmallIconSize.Width);
        trayIcon=Ui.CreateIcon(iconSize);pausedIcon=Ui.CreateIcon(iconSize,true);
        tray=new(){Icon=trayIcon,Text="GammaSwitch · ICC 快捷切换",Visible=true};
        tray.MouseClick+=(_,e)=>{if(e.Button==MouseButtons.Left)ShowMain();};
        BuildMenus();RefreshDisplays();RefreshActions();BuildTray();UpdateHotkeyChip();UpdateCommands();loading=false;
        Shown+=(_,_)=>
        {
            try{hotkeys.Replace(settings.Actions);}catch(Exception ex){paused=true;BuildTray();UpdateHotkeyChip();Ui.Error(this,ex);}
            UpdateCommands();UpdateCurve();
            if(settingsLoadError is not null)SetStatus("恢复模式：请修复 "+settingsStore.FilePath,Tone.Warning);
            else if(paused)SetStatus("快捷键注册失败，已暂停；请编辑冲突的绑定后点击右上角恢复。",Tone.Warning);
            else SetStatus("就绪。双击动作即可设置快捷键。",Tone.Success);
            if(service.HasRecovery && MessageBox.Show(this,"发现上次尚未恢复的显示设置。是否现在恢复？\n选择“否”会保留原始恢复基准。","GammaSwitch · 恢复",MessageBoxButtons.YesNo,MessageBoxIcon.Question)==DialogResult.Yes)
                Run(new(){Name="恢复全部",Kind=ActionKind.RestoreAll},true);
        };
    }
    private AppSettings CreateDefaults()
    {
        var config=new AppSettings();var display=displays.FirstOrDefault(d=>d.Primary)??displays.FirstOrDefault();
        if(display is not null)
        {
            foreach(var (file,name) in new[]{("P27UEA.icm","日常 · P27UEA"),("tarkov.icc","塔科夫"),("tarkov_brighter.icc","塔科夫 · 明亮")})
            {
                var path=Path.Combine(WindowsDisplayBackend.ColorDirectory,file);
                if(File.Exists(path))config.Actions.Add(new(){Name=name,Targets=[new(){DisplayId=display.Id,ProfilePath=path}]});
            }
        }
        config.Actions.Add(new(){Name="恢复所有显示器",Kind=ActionKind.RestoreAll});
        return config;
    }
    private bool Editable=>settingsLoadError is null;
    private AppSettings Clone()=>JsonStore<AppSettings>.Copy(settings);
    private SwitchAction? SelectedAction=>actionGrid.CurrentRow?.Tag as SwitchAction;
    private DisplayInfo? SelectedDisplay=>displayGrid.CurrentRow?.Tag as DisplayInfo;
    private void SetStatus(string text,Tone tone=Tone.Info)=>
        status.Set($"{DateTime.Now:HH:mm}  {text}"+(service.HasRecovery?$"  ·  {service.RecoveryCount} 台屏幕待恢复":""),tone);

    // ---------- Displays ----------
    private void RefreshDisplays(bool announce=false)
    {
        var previous=SelectedDisplay?.Id;
        try
        {
            displays=backend.Enumerate();displayGrid.Rows.Clear();snapshots.Clear();
            var modified=new HashSet<string>(service.RecoveryDisplayIds,StringComparer.OrdinalIgnoreCase);
            foreach(var display in displays)
            {
                string profile,state,detail;Tone tone;
                try
                {
                    var snapshot=backend.Capture(display);snapshots[display.Id]=snapshot;
                    var file=Path.GetFileName(snapshot.EffectiveProfile);profile=string.IsNullOrEmpty(file)?"系统默认":file;
                    (state,tone,detail)=display.Mirrored?("复制模式",Tone.Warning,"镜像 / 复制模式下无法独立切换，请改用扩展桌面。")
                        :modified.Contains(display.Id)?("已修改 · 可恢复",Tone.Info,"本程序已保存此屏的原始状态，可随时恢复。")
                        :("未修改",Tone.Neutral,"可读取 ICC 与伽马表。");
                    detail+="\n"+(snapshot.EffectiveProfile??"系统默认 ICC");
                }
                catch(Exception ex){profile="读取失败";state="读取失败";tone=Tone.Error;detail=ex.Message;}
                var row=displayGrid.Rows[displayGrid.Rows.Add(display.Name,profile,state)];
                row.Tag=display;row.Cells[2].Tag=tone;
                row.Cells[0].ToolTipText=display.Name+(display.Primary?"（主屏）":"")+"\n"+display.DeviceName;
                row.Cells[1].ToolTipText=row.Cells[2].ToolTipText=detail;
                if(display.Id==previous)displayGrid.CurrentCell=row.Cells[0];
            }
            if(announce)SetStatus($"已检测到 {displays.Count} 台显示器。",Tone.Info);
        }
        catch(Exception ex){SetStatus("刷新失败："+ex.Message,Tone.Error);}
        UpdateCurve();
    }
    private void UpdateCurve()
    {
        if(SelectedDisplay is {} display && snapshots.TryGetValue(display.Id,out var snapshot))
            curve.SetCurve(snapshot.Gamma,"当前伽马表 · "+display.DeviceName.Replace(@"\\.\",""));
        else curve.SetCurve(null,"当前伽马表","选择一台显示器后在此查看曲线");
    }
    private void PaintDisplayCell(object? sender,DataGridViewCellPaintingEventArgs e)
    {
        if(e.RowIndex<0 || e.ColumnIndex is not (0 or 2) || e.Graphics is not {} g)return;
        var row=displayGrid.Rows[e.RowIndex];
        if(row.Tag is not DisplayInfo display)return;
        e.PaintBackground(e.CellBounds,true);
        var bounds=e.CellBounds;var pad=Ui.S(8);var center=bounds.Top+bounds.Height/2;
        var back=row.Selected?Ui.AccentSoft:Ui.Surface;
        if(e.ColumnIndex==0)
        {
            var x=bounds.Left+pad;
            if(display.Primary)x=Ui.DrawPill(g,"主屏",Ui.SmallBold,Tone.Info,new(x,center)).Right+Ui.S(8);
            TextRenderer.DrawText(g,display.Name,Ui.Body,new Rectangle(x,bounds.Top,Math.Max(0,bounds.Right-x-pad),bounds.Height),Ui.Ink,back,
                TextFormatFlags.Left|TextFormatFlags.VerticalCenter|TextFormatFlags.SingleLine|TextFormatFlags.EndEllipsis|TextFormatFlags.NoPrefix);
        }
        else Ui.DrawPill(g,e.Value?.ToString()??"",Ui.SmallBold,row.Cells[2].Tag is Tone tone?tone:Tone.Neutral,new(bounds.Left+pad,center));
        e.Handled=true;
    }

    // ---------- Actions ----------
    private string TargetText(SwitchAction action,string separator="；")
    {
        if(action.Kind==ActionKind.RestoreAll)return "本程序修改过的全部屏幕";
        var text=string.Join(separator,action.Targets.Select(t=>
        {
            var display=displays.FirstOrDefault(d=>string.Equals(d.Id,t.DisplayId,StringComparison.OrdinalIgnoreCase));
            var label=display?.DeviceName.Replace(@"\\.\","")??"[已断开]";
            return label+(action.Kind==ActionKind.RestoreDisplay?"":$" → {Path.GetFileName(t.ProfilePath)}（{Ui.Mode(t.Mode)}）");
        }));
        return action.Kind==ActionKind.Cycle?"依次："+text:text;
    }
    private void RefreshActions(string? selectedId=null)
    {
        var previous=selectedId??SelectedAction?.Id;actionGrid.Rows.Clear();
        foreach(var action in settings.Actions)
        {
            var row=actionGrid.Rows[actionGrid.Rows.Add(action.Enabled?"启用":"禁用",action.Name,ShortcutText.Format(action.Shortcut),TargetText(action))];
            row.Tag=action;
            row.Cells[1].ToolTipText=action.Name+"\n"+Ui.Kind(action.Kind);
            row.Cells[2].ToolTipText=action.Shortcut is null?"未绑定快捷键，可双击设置":"全局快捷键："+ShortcutText.Format(action.Shortcut);
            row.Cells[3].ToolTipText=Ui.Kind(action.Kind)+"\n"+TargetText(action,"\n");
            if(action.Id==previous)actionGrid.CurrentCell=row.Cells[0];
        }
        actionGrid.Invalidate();UpdateCommands();
    }
    private void PaintActionCell(object? sender,DataGridViewCellPaintingEventArgs e)
    {
        if(e.RowIndex<0 || e.ColumnIndex is < 0 or > 2 || e.Graphics is not {} g)return;
        var row=actionGrid.Rows[e.RowIndex];
        if(row.Tag is not SwitchAction action)return;
        e.PaintBackground(e.CellBounds,true);
        var bounds=e.CellBounds;var pad=Ui.S(8);var back=row.Selected?Ui.AccentSoft:Ui.Surface;
        const TextFormatFlags line=TextFormatFlags.Left|TextFormatFlags.SingleLine|TextFormatFlags.EndEllipsis|TextFormatFlags.NoPrefix;
        switch(e.ColumnIndex)
        {
            case 0:
                Ui.DrawPill(g,action.Enabled?"启用":"禁用",Ui.SmallBold,action.Enabled?Tone.Success:Tone.Neutral,new(bounds.Left+pad,bounds.Top+bounds.Height/2));
                break;
            case 1:
                var mid=bounds.Top+bounds.Height/2;var width=Math.Max(0,bounds.Width-pad*2);
                TextRenderer.DrawText(g,action.Name,Ui.Strong,new Rectangle(bounds.Left+pad,bounds.Top,width,mid-bounds.Top+Ui.S(1)),action.Enabled?Ui.Ink:Ui.Muted,back,line|TextFormatFlags.Bottom);
                var ran=lastRun.TryGetValue(action.Id,out var at);
                TextRenderer.DrawText(g,Ui.Kind(action.Kind)+(ran?$" · {at:HH:mm} 已应用":""),Ui.Small,new Rectangle(bounds.Left+pad,mid+Ui.S(2),width,bounds.Bottom-mid-Ui.S(2)),ran?Ui.Accent:Ui.Muted,back,line|TextFormatFlags.Top);
                break;
            default:
                Ui.DrawKeys(g,new Rectangle(bounds.Left+pad,bounds.Top,Math.Max(0,bounds.Width-pad*2),bounds.Height),action.Shortcut is null?null:ShortcutText.Parts(action.Shortcut),action.Enabled);
                break;
        }
        e.Handled=true;
    }
    private void PaintEmptyActions(object? sender,PaintEventArgs e)
    {
        if(actionGrid.Rows.Count>0)return;
        var area=actionGrid.ClientRectangle;area.Y+=actionGrid.ColumnHeadersHeight;area.Height-=actionGrid.ColumnHeadersHeight;
        TextRenderer.DrawText(e.Graphics,"还没有任何动作\n点击“＋ 添加动作”（Ctrl+N）创建预设、多屏场景或恢复动作",Ui.Body,area,Ui.Muted,
            TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.WordBreak|TextFormatFlags.NoPrefix);
    }
    private void UpdateCommands()
    {
        var action=SelectedAction;var has=action is not null;
        var index=action is null?-1:settings.Actions.FindIndex(a=>a.Id==action.Id);
        applyButton.Enabled=has;
        editButton.Enabled=copyButton.Enabled=toggleButton.Enabled=deleteButton.Enabled=has && Editable;
        upButton.Enabled=has && Editable && index>0;
        downButton.Enabled=has && Editable && index>=0 && index<settings.Actions.Count-1;
        addButton.Enabled=Editable;
        toggleButton.Text=action?.Enabled==false?"启用":"禁用";
        restoreSelectedButton.Enabled=SelectedDisplay is not null;
    }
    private static ToolStripMenuItem Item(string text,string keys,Action click)=>new(text,null,(_,_)=>click()){ShortcutKeyDisplayString=keys};
    private void BuildMenus()
    {
        actionMenu.Renderer=Ui.MenuRenderer();actionMenu.Font=Ui.Body;actionMenu.ShowImageMargin=false;
        var apply=Item("立即应用","Enter",ApplySelected);apply.Font=Ui.Strong;
        var edit=Item("编辑 / 快捷键…","F2",EditSelected);
        var copy=Item("复制为新动作","Ctrl+D",CopySelected);
        var toggle=Item("禁用","",ToggleSelected);
        var up=Item("上移","Ctrl+↑",()=>MoveSelected(-1));
        var down=Item("下移","Ctrl+↓",()=>MoveSelected(1));
        var add=Item("添加动作…","Ctrl+N",AddAction);
        var delete=Item("删除","Delete",DeleteSelected);delete.ForeColor=Ui.Danger;
        actionMenu.Items.AddRange([apply,edit,copy,toggle,new ToolStripSeparator(),up,down,new ToolStripSeparator(),add,new ToolStripSeparator(),delete]);
        actionMenu.Opening+=(_,_)=>
        {
            var action=SelectedAction;var has=action is not null;
            var index=action is null?-1:settings.Actions.FindIndex(a=>a.Id==action.Id);
            apply.Enabled=has;edit.Enabled=copy.Enabled=toggle.Enabled=delete.Enabled=has && Editable;
            up.Enabled=has && Editable && index>0;down.Enabled=has && Editable && index>=0 && index<settings.Actions.Count-1;
            add.Enabled=Editable;toggle.Text=action?.Enabled==false?"启用":"禁用";
        };
        displayMenu.Renderer=Ui.MenuRenderer();displayMenu.Font=Ui.Body;displayMenu.ShowImageMargin=false;
        var restore=Item("恢复此屏幕","",RestoreSelected);
        displayMenu.Items.AddRange([restore,Item("刷新显示器","F5",()=>RefreshDisplays(true))]);
        displayMenu.Opening+=(_,_)=>restore.Enabled=SelectedDisplay is not null;
    }
    private void Commit(AppSettings candidate)
    {
        if(settingsLoadError is not null)throw new InvalidOperationException("恢复模式下不会覆盖原设置，请先修复："+settingsStore.FilePath);
        ActionValidation.Validate(candidate);
        if(!paused)hotkeys.Replace(candidate.Actions);
        try{settingsStore.Save(candidate);}
        catch{hotkeys.Replace(paused?[]:settings.Actions);throw;}
        settings=candidate;
        RefreshActions();BuildTray();UpdateHotkeyChip();
        SetStatus("设置已保存，快捷键已更新。"+(paused?"当前快捷键处于暂停状态。":""),Tone.Success);
    }
    private void ApplySelected(){if(SelectedAction is {} action)Run(action,true);}
    private void AddAction(){if(Editable)Edit(new SwitchAction());}
    private void EditSelected(){if(Editable && SelectedAction is {} action)Edit(action);}
    private void Edit(SwitchAction action)
    {
        SwitchAction? result=null;
        try
        {
            hotkeys.Replace([]); // Capturing an existing shortcut must not execute its action.
            using var editor=new ActionEditor(JsonStore<AppSettings>.Copy(new(){Actions=[action]}).Actions[0],displays,settings.Actions);
            if(editor.ShowDialog(this)==DialogResult.OK)result=editor.Result;
        }
        catch(Exception ex){Ui.Error(this,ex);}
        finally
        {
            try{hotkeys.Replace(paused?[]:settings.Actions);}catch(Exception ex){paused=true;BuildTray();UpdateHotkeyChip();Ui.Error(this,ex);}
        }
        if(result is null)return;
        try
        {
            var candidate=Clone();var index=candidate.Actions.FindIndex(a=>a.Id==result.Id);
            if(index>=0)candidate.Actions[index]=result;else candidate.Actions.Add(result);
            Commit(candidate);RefreshActions(result.Id);
        }
        catch(Exception ex){Ui.Error(this,ex);}
    }
    private void CopySelected()
    {
        if(!Editable || SelectedAction is not {} selected)return;
        var copy=JsonStore<AppSettings>.Copy(new(){Actions=[selected]}).Actions[0];copy.Id=Guid.NewGuid().ToString("N");copy.Name+=" · 副本";copy.Shortcut=null;Edit(copy);
    }
    private void ToggleSelected()
    {
        if(!Editable || SelectedAction is not {} selected)return;
        try{var copy=Clone();var action=copy.Actions.Single(a=>a.Id==selected.Id);action.Enabled=!action.Enabled;Commit(copy);}catch(Exception ex){Ui.Error(this,ex);}
    }
    private void MoveSelected(int delta)
    {
        if(!Editable || SelectedAction is not {} selected)return;
        try
        {
            var copy=Clone();var index=copy.Actions.FindIndex(a=>a.Id==selected.Id);var next=index+delta;
            if(index<0 || next<0 || next>=copy.Actions.Count)return;
            (copy.Actions[index],copy.Actions[next])=(copy.Actions[next],copy.Actions[index]);
            Commit(copy);RefreshActions(selected.Id);
        }
        catch(Exception ex){Ui.Error(this,ex);}
    }
    private void DeleteSelected()
    {
        if(!Editable || SelectedAction is not {} selected)return;
        var shortcut=selected.Shortcut is null?"":$"\n快捷键 {ShortcutText.Format(selected.Shortcut)} 将被释放。";
        if(MessageBox.Show(this,$"删除动作“{selected.Name}”？{shortcut}\n此操作无法撤销。","GammaSwitch · 删除",MessageBoxButtons.YesNo,MessageBoxIcon.Question,MessageBoxDefaultButton.Button2)!=DialogResult.Yes)return;
        try{var copy=Clone();copy.Actions.RemoveAll(a=>a.Id==selected.Id);lastRun.Remove(selected.Id);Commit(copy);}catch(Exception ex){Ui.Error(this,ex);}
    }
    private void RestoreSelected()
    {
        if(SelectedDisplay is {} display)
            Run(new(){Name="恢复 "+display.Name,Kind=ActionKind.RestoreDisplay,Targets=[new(){DisplayId=display.Id}]},true);
    }
    private void Run(SwitchAction action,bool dialog)
    {
        var cursor=Cursor.Current;Cursor.Current=Cursors.WaitCursor;
        try
        {
            service.Execute(action);
            MarkApplied(action);
            RefreshDisplays();actionGrid.Invalidate();BuildTray();
            SetStatus("已执行："+action.Name+"。写入结果已回读核对。",Tone.Success);
            Log("OK "+action.Name);
            if(!Visible)tray.ShowBalloonTip(1500,"GammaSwitch","已执行："+action.Name,ToolTipIcon.Info);
        }
        catch(Exception ex)
        {
            SetStatus("操作失败："+ex.Message.Replace('\n',' '),Tone.Error);Log("ERROR "+action.Name+" "+ex);
            if(dialog)Ui.Error(this,ex);else tray.ShowBalloonTip(5000,"GammaSwitch · 操作失败",ex.Message,ToolTipIcon.Warning);
        }
        finally{Cursor.Current=cursor;}
    }
    /// <summary>Tracks which actions are believed to be in effect, so the list and tray can show it.</summary>
    private void MarkApplied(SwitchAction action)
    {
        if(action.Kind==ActionKind.RestoreAll){lastRun.Clear();return;}
        var touched=action.Targets.Select(t=>t.DisplayId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach(var other in settings.Actions.Where(a=>a.Targets.Any(t=>touched.Contains(t.DisplayId))))lastRun.Remove(other.Id);
        lastRun.Remove(action.Id);
        if(action.Kind is ActionKind.Apply or ActionKind.Cycle)lastRun[action.Id]=DateTime.Now;
    }
    private void Log(string text)
    {
        try
        {
            Directory.CreateDirectory(dataDirectory);var path=Path.Combine(dataDirectory,"activity.log");
            if(File.Exists(path)&&new FileInfo(path).Length>2*1024*1024)File.Move(path,path+".previous",true);
            File.AppendAllText(path,$"{DateTimeOffset.Now:O} {text}{Environment.NewLine}");
        }
        catch(IOException){ /* Logging is best effort; recovery persistence is mandatory elsewhere. */ }
        catch(UnauthorizedAccessException){ }
    }

    // ---------- Tray & hotkeys ----------
    private void BuildTray()
    {
        var old=tray.ContextMenuStrip;var menu=new ContextMenuStrip{Renderer=Ui.MenuRenderer(),Font=Ui.Body};
        menu.Items.Add(new ToolStripMenuItem("打开设置",null,(_,_)=>ShowMain()){Font=Ui.Strong});
        menu.Items.Add(new ToolStripSeparator());
        var enabled=settings.Actions.Where(a=>a.Enabled).ToList();
        if(enabled.Count==0)menu.Items.Add(new ToolStripMenuItem("（没有已启用的动作）"){Enabled=false});
        foreach(var action in enabled)
            menu.Items.Add(new ToolStripMenuItem(action.Name,null,(_,_)=>Run(action,false))
            {
                ShortcutKeyDisplayString=action.Shortcut is null?"":ShortcutText.Format(action.Shortcut),
                Checked=lastRun.ContainsKey(action.Id)
            });
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(paused?"恢复全局快捷键":"暂停全局快捷键",null,(_,_)=>TogglePause());
        menu.Items.Add("恢复全部并退出",null,(_,_)=>ExitApp(true));menu.Items.Add("退出",null,(_,_)=>ExitApp());
        tray.ContextMenuStrip=menu;old?.Dispose();
        tray.Icon=paused?pausedIcon:trayIcon;
        var current=settings.Actions.Where(a=>lastRun.ContainsKey(a.Id)).OrderByDescending(a=>lastRun[a.Id]).Select(a=>a.Name).FirstOrDefault();
        var tip=paused?"GammaSwitch · 快捷键已暂停":current is null?"GammaSwitch · ICC 快捷切换":"GammaSwitch · 当前："+current;
        tray.Text=tip.Length>63?tip[..62]+"…":tip;
    }
    private void TogglePause()
    {
        try
        {
            if(paused)hotkeys.Replace(settings.Actions);else hotkeys.Replace([]);
            paused=!paused;BuildTray();UpdateHotkeyChip();
            SetStatus(paused?"全局快捷键已暂停。":"全局快捷键已恢复。",paused?Tone.Warning:Tone.Success);
        }
        catch(Exception ex){Ui.Error(this,ex);}
    }
    private void UpdateHotkeyChip()
    {
        var count=settings.Actions.Count(a=>a.Enabled && a.Shortcut is not null);
        hotkeyChip.Text=paused?"●  快捷键已暂停":$"●  快捷键运行中 · {count}";
        hotkeyChip.ChipTone=paused?Tone.Warning:Tone.Success;
        Ui.Tips.SetToolTip(hotkeyChip,paused?"点击恢复全部全局快捷键":"点击暂停全部全局快捷键（例如打字时避免误触）");
    }
    private void ShowMain()
    {
        Show();if(WindowState==FormWindowState.Minimized)WindowState=FormWindowState.Normal;
        Activate();BringToFront();
    }
    private void ExitApp(bool forceRestore=false)
    {
        if((forceRestore||settings.RestoreOnExit)&&service.HasRecovery)
        {
            try{service.RestoreAll();}
            catch(Exception ex)
            {
                if(MessageBox.Show(this,ex.Message+"\n\n仍要退出吗？恢复文件会保留到下次启动。","GammaSwitch",MessageBoxButtons.YesNo,MessageBoxIcon.Warning)!=DialogResult.Yes)return;
            }
        }
        exiting=true;Close();
    }
    protected override bool ProcessCmdKey(ref Message msg,Keys keyData)
    {
        if(actionGrid.Focused)
            switch(keyData)
            {
                case Keys.Enter:ApplySelected();return true;
                case Keys.F2:EditSelected();return true;
                case Keys.Delete:DeleteSelected();return true;
                case Keys.Control|Keys.Up:MoveSelected(-1);return true;
                case Keys.Control|Keys.Down:MoveSelected(1);return true;
            }
        switch(keyData)
        {
            case Keys.Control|Keys.N:AddAction();return true;
            case Keys.Control|Keys.D:CopySelected();return true;
            case Keys.F5:RefreshDisplays(true);return true;
        }
        return base.ProcessCmdKey(ref msg,keyData);
    }
    protected override void WndProc(ref Message m)
    {
        if(m.Msg==0x0312 && !paused && hotkeys is not null && hotkeys.Actions.TryGetValue(m.WParam.ToInt32(),out var id))
        {
            var action=settings.Actions.FirstOrDefault(a=>a.Id==id);
            if(action is not null)BeginInvoke(()=>Run(action,false));
        }
        if(m.Msg==0x007E && IsHandleCreated)BeginInvoke(()=>{RefreshDisplays();RefreshActions();SetStatus("显示配置已变化；请确认目标屏幕后重新应用。",Tone.Warning);});
        base.WndProc(ref m);
    }
    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if(!exiting && e.CloseReason==CloseReason.UserClosing)
        {
            e.Cancel=true;Hide();
            if(!trayHintShown){trayHintShown=true;tray.ShowBalloonTip(3000,"GammaSwitch 仍在运行","快捷键继续生效。单击托盘图标打开设置，右键可执行动作或退出。",ToolTipIcon.Info);}
            base.OnFormClosing(e);return;
        }
        if(!exiting && settings.RestoreOnExit && service.HasRecovery)
            try{service.RestoreAll();}catch(Exception ex){Log("EXIT RESTORE ERROR "+ex);}
        hotkeys.Dispose();tray.Visible=false;tray.Dispose();base.OnFormClosing(e);
    }
    protected override void Dispose(bool disposing)
    {
        if(disposing){hotkeys?.Dispose();tray?.Dispose();actionMenu.Dispose();displayMenu.Dispose();trayIcon?.Dispose();pausedIcon?.Dispose();}
        base.Dispose(disposing);
    }
}
