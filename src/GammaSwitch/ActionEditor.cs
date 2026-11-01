using System.Media;
using GammaSwitch.Core;

namespace GammaSwitch;

internal sealed class ActionEditor : Form
{
    private sealed record RowInfo(string Text,Tone Tone,string Tip,ushort[]? Gamma);

    private readonly TextBox nameBox=new(){Dock=DockStyle.Fill,PlaceholderText="例如：塔科夫 · 明亮",Margin=Ui.P(0,4,16,4)};
    private readonly ComboBox kindBox=new(){Dock=DockStyle.Fill,DropDownStyle=ComboBoxStyle.DropDownList,Margin=Ui.P(0,4,0,4)};
    private readonly HotkeyBox hotkeyBox=new(){Dock=DockStyle.Fill,Margin=Ui.P(0,4,8,4)};
    private readonly CheckBox enabledBox=new(){Text="启用此动作及其快捷键",AutoSize=true,Anchor=AnchorStyles.Left,Margin=Ui.P(0,4,0,4)};
    private readonly StatusLine hotkeyHint=new(){Anchor=AnchorStyles.Left|AnchorStyles.Right|AnchorStyles.Top,Margin=Ui.P(0,0,16,0)};
    private readonly Label kindHint=new(){AutoSize=true,Anchor=AnchorStyles.Left|AnchorStyles.Right|AnchorStyles.Top,ForeColor=Ui.Muted,Font=Ui.Small,Margin=Ui.P(0,2,0,0)};
    private readonly DataGridView targets=Ui.Grid();
    private readonly CurveView curve=new(){Dock=DockStyle.Fill,Margin=Ui.P(16,0,0,0)};
    private readonly Label restoreAllNote=new(){Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleCenter,ForeColor=Ui.Muted,BackColor=Ui.NeutralSoft,
        Text="“恢复所有显示器”无需选择目标。\n执行时，本程序修改过的每一台屏幕都会恢复到首次修改前的 ICC 与伽马表。",Visible=false};
    private readonly Label dropHint=new(){AutoSize=true,ForeColor=Ui.Muted,Font=Ui.Small,Margin=Ui.P(0,10,0,0),
        Text="提示：可把 .icc / .icm 文件直接拖进表格；点击单元格即可修改。“ICC + 曲线 / 仅 ICC”要求文件已安装到系统颜色目录。"};
    private readonly StatusLine message=new(){Dock=DockStyle.Fill,Margin=Ui.P(0,0,16,0)};
    private readonly RoundButton addButton,removeButton,upButton,downButton,saveButton;
    private readonly SwitchAction original;
    private readonly IReadOnlyList<DisplayInfo> displays;
    private readonly List<SwitchAction> others;
    private readonly ColumnStyle curveColumn=new(SizeType.Absolute,Ui.S(230));
    private bool updating;
    public SwitchAction? Result {get;private set;}

    public ActionEditor(SwitchAction action,IReadOnlyList<DisplayInfo> displays,List<SwitchAction> others)
    {
        original=action;this.displays=displays;this.others=others;
        var isNew=string.IsNullOrWhiteSpace(action.Name) || others.All(a=>a.Id!=action.Id);
        Text=(isNew?"新建动作":"编辑动作")+" · GammaSwitch";Font=Ui.Body;ForeColor=Ui.Ink;BackColor=Ui.Canvas;
        AutoScaleMode=AutoScaleMode.None;StartPosition=FormStartPosition.CenterParent;Icon=Ui.AppIcon;
        MinimumSize=Ui.S(880,640);Size=Ui.S(1020,740);ShowInTaskbar=false;MinimizeBox=false;

        addButton=Ui.Button("＋ 添加目标",(_,_)=>AddTarget(),ButtonStyle.Normal,"再添加一台屏幕（多屏场景）或一个循环配置");
        removeButton=Ui.Button("删除目标",(_,_)=>RemoveTarget(),ButtonStyle.Normal,"删除当前选中的一行");
        upButton=Ui.Button("上移",(_,_)=>MoveRow(-1),ButtonStyle.Normal,"调整循环顺序");
        downButton=Ui.Button("下移",(_,_)=>MoveRow(1),ButtonStyle.Normal,"调整循环顺序");
        upButton.Margin=downButton.Margin=removeButton.Margin=Ui.P(0,0,8,0);

        var root=new TableLayoutPanel{Dock=DockStyle.Fill,Padding=Ui.P(20,16,20,14),ColumnCount=1,RowCount=3};
        root.ColumnStyles.Add(new(SizeType.Percent,100));
        root.RowStyles.Add(new(SizeType.Absolute,Ui.S(212)));root.RowStyles.Add(new(SizeType.Percent,100));root.RowStyles.Add(new(SizeType.AutoSize));
        Controls.Add(root);

        // Basic information.
        var basic=new Card();
        var form=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=4,RowCount=4,Margin=Padding.Empty};
        form.ColumnStyles.Add(new(SizeType.AutoSize));form.ColumnStyles.Add(new(SizeType.Percent,55));
        form.ColumnStyles.Add(new(SizeType.AutoSize));form.ColumnStyles.Add(new(SizeType.Percent,45));
        for(var i=0;i<4;i++)form.RowStyles.Add(new(SizeType.AutoSize));
        var header=Ui.CardHeader(isNew?"新建动作":"动作设置","名称会显示在主界面列表和托盘菜单中");
        form.Controls.Add(header,0,0);form.SetColumnSpan(header,4);
        form.Controls.Add(FieldLabel("名称"),0,1);form.Controls.Add(nameBox,1,1);
        form.Controls.Add(FieldLabel("类型"),2,1);form.Controls.Add(kindBox,3,1);
        var hotkeyRow=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2,RowCount=1,AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,Margin=Ui.P(0,0,16,0)};
        hotkeyRow.ColumnStyles.Add(new(SizeType.Percent,100));hotkeyRow.ColumnStyles.Add(new(SizeType.AutoSize));
        var clear=Ui.Button("清除",(_,_)=>{hotkeyBox.Shortcut=null;hotkeyBox.Focus();},ButtonStyle.Normal,"移除快捷键（也可在输入框中按 Delete / Backspace）");
        clear.Anchor=AnchorStyles.Left;clear.Margin=Padding.Empty;
        hotkeyRow.Controls.Add(hotkeyBox,0,0);hotkeyRow.Controls.Add(clear,1,0);
        form.Controls.Add(FieldLabel("快捷键"),0,2);form.Controls.Add(hotkeyRow,1,2);
        form.Controls.Add(new Label{AutoSize=true,Margin=Padding.Empty},2,2);form.Controls.Add(enabledBox,3,2);
        hotkeyHint.Height=Ui.S(24);
        form.Controls.Add(hotkeyHint,1,3);form.Controls.Add(kindHint,3,3);
        Ui.Tips.SetToolTip(hotkeyBox,"点击后直接按下组合键，例如 Ctrl + Alt + 1；Delete 清除，Esc 离开输入框");
        basic.Controls.Add(form);root.Controls.Add(basic,0,0);

        // Targets.
        var targetCard=new Card();
        // OLE drag & drop needs an STA thread; the app's Main is [STAThread], hosts that are not simply lose drop support.
        var canDrop=Thread.CurrentThread.GetApartmentState()==ApartmentState.STA;
        targetCard.AllowDrop=canDrop;
        var body=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2,RowCount=3,Margin=Padding.Empty};
        body.ColumnStyles.Add(new(SizeType.Percent,100));body.ColumnStyles.Add(curveColumn);
        body.RowStyles.Add(new(SizeType.AutoSize));body.RowStyles.Add(new(SizeType.Percent,100));body.RowStyles.Add(new(SizeType.AutoSize));
        var targetHeader=Ui.CardHeader("目标与 ICC 配置","每一行 = 一台屏幕要应用的 ICC 文件与模式",upButton,downButton,removeButton,addButton);
        body.Controls.Add(targetHeader,0,0);body.SetColumnSpan(targetHeader,2);
        var holder=new Panel{Dock=DockStyle.Fill,Margin=Padding.Empty};
        holder.Controls.Add(targets);holder.Controls.Add(restoreAllNote);
        body.Controls.Add(holder,0,1);body.Controls.Add(curve,1,1);
        body.Controls.Add(dropHint,0,2);body.SetColumnSpan(dropHint,2);
        targetCard.Controls.Add(body);root.Controls.Add(targetCard,0,1);

        var monitorChoices=displays.Select(d=>new Choice<string>(d.Id,d.Name)).ToList();
        foreach(var id in action.Targets.Select(t=>t.DisplayId).Distinct())
            if(!monitorChoices.Any(c=>c.Value==id))monitorChoices.Add(new(id,"[已断开] "+id));
        targets.EditMode=DataGridViewEditMode.EditOnKeystrokeOrF2;targets.RowTemplate.Height=Ui.S(40);targets.AllowDrop=canDrop;
        targets.Columns.Add(new DataGridViewComboBoxColumn{Name="Display",HeaderText="目标显示器",DataSource=monitorChoices,ValueMember="Value",DisplayMember="Label",FillWeight=30,FlatStyle=FlatStyle.Flat});
        targets.Columns.Add(new DataGridViewTextBoxColumn{Name="Profile",HeaderText="ICC / ICM 文件（可粘贴完整路径）",FillWeight=34});
        targets.Columns.Add(new DataGridViewButtonColumn{Name="Browse",HeaderText="",Text="浏览…",UseColumnTextForButtonValue=true,FillWeight=9,MinimumWidth=Ui.S(64),FlatStyle=FlatStyle.Flat,
            DefaultCellStyle=new(){BackColor=Ui.Surface,ForeColor=Ui.Accent,SelectionBackColor=Ui.AccentSoft,SelectionForeColor=Ui.Accent,Padding=Ui.P(4,7,4,7),Alignment=DataGridViewContentAlignment.MiddleCenter}});
        targets.Columns.Add(new DataGridViewComboBoxColumn{Name="Mode",HeaderText="应用模式",DataSource=Enum.GetValues<ProfileMode>().Select(m=>new Choice<ProfileMode>(m,Ui.Mode(m))).ToList(),ValueMember="Value",DisplayMember="Label",FillWeight=17,MinimumWidth=Ui.S(120),FlatStyle=FlatStyle.Flat});
        targets.Columns.Add(new DataGridViewTextBoxColumn{Name="Info",HeaderText="文件检查",FillWeight=12,ReadOnly=true,MinimumWidth=Ui.S(92)});
        targets.DataError+=(_,e)=>{e.ThrowException=false;};
        targets.CellContentClick+=Browse;
        targets.CellClick+=(_,e)=>
        {
            if(e.RowIndex<0 || e.ColumnIndex<0)return;
            var name=targets.Columns[e.ColumnIndex].Name;
            if(name is "Display" or "Mode" or "Profile")
            {
                targets.BeginEdit(name=="Profile");
                if(targets.EditingControl is ComboBox combo)combo.DroppedDown=true;
            }
        };
        targets.CurrentCellDirtyStateChanged+=(_,_)=>{if(targets.IsCurrentCellDirty && targets.CurrentCell is DataGridViewComboBoxCell)targets.CommitEdit(DataGridViewDataErrorContexts.Commit);};
        targets.CellValueChanged+=(_,e)=>{if(e.RowIndex>=0 && !updating){UpdateRow(targets.Rows[e.RowIndex]);UpdateCurve();ClearMessage();}};
        targets.SelectionChanged+=(_,_)=>{UpdateCurve();UpdateTargetCommands();};
        targets.RowsAdded+=(_,_)=>UpdateTargetCommands();targets.RowsRemoved+=(_,_)=>UpdateTargetCommands();
        targets.CellPainting+=PaintInfo;
        foreach(Control dropTarget in new Control[]{targets,targetCard})
        {
            dropTarget.DragEnter+=(_,e)=>e.Effect=Kind is ActionKind.Apply or ActionKind.Cycle && DroppedProfiles(e).Length>0?DragDropEffects.Copy:DragDropEffects.None;
            dropTarget.DragDrop+=(_,e)=>AddDropped(DroppedProfiles(e));
        }

        // Footer.
        var footer=new TableLayoutPanel{Dock=DockStyle.Fill,AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,ColumnCount=3,RowCount=1,Margin=Padding.Empty};
        footer.ColumnStyles.Add(new(SizeType.Percent,100));footer.ColumnStyles.Add(new(SizeType.AutoSize));footer.ColumnStyles.Add(new(SizeType.AutoSize));
        message.Height=Ui.S(30);
        var cancel=Ui.Button("取消",(_,_)=>{DialogResult=DialogResult.Cancel;Close();},ButtonStyle.Normal,"放弃修改（Esc）");
        cancel.DialogResult=DialogResult.Cancel;CancelButton=cancel;cancel.Margin=Ui.P(0,0,8,0);
        saveButton=Ui.Button("保存并生效",Save,ButtonStyle.Primary,"保存设置并立即更新全局快捷键（Ctrl+S）");saveButton.Margin=Padding.Empty;
        footer.Controls.Add(message,0,0);footer.Controls.Add(cancel,1,0);footer.Controls.Add(saveButton,2,0);
        root.Controls.Add(footer,0,2);

        nameBox.Text=action.Name;enabledBox.Checked=action.Enabled;hotkeyBox.Shortcut=action.Shortcut;
        foreach(var kind in Enum.GetValues<ActionKind>())kindBox.Items.Add(new Choice<ActionKind>(kind,Ui.Kind(kind)));
        kindBox.SelectedIndex=(int)action.Kind;
        kindBox.SelectedIndexChanged+=(_,_)=>{if(Kind!=ActionKind.RestoreAll && targets.Rows.Count==0)AddTarget();UpdateKind();ClearMessage();};
        hotkeyBox.ShortcutChanged+=(_,_)=>{UpdateHotkeyHint();ClearMessage();};
        enabledBox.CheckedChanged+=(_,_)=>UpdateHotkeyHint();
        nameBox.TextChanged+=(_,_)=>ClearMessage();
        foreach(var target in action.Targets)targets.Rows.Add(target.DisplayId,target.ProfilePath,"浏览…",target.Mode);
        if(targets.Rows.Count==0 && action.Kind!=ActionKind.RestoreAll)AddTarget();
        foreach(DataGridViewRow row in targets.Rows)UpdateRow(row);
        if(targets.Rows.Count>0)targets.CurrentCell=targets.Rows[0].Cells["Display"];
        UpdateKind();UpdateHotkeyHint();ClearMessage();
        Shown+=(_,_)=>{if(isNew && string.IsNullOrEmpty(nameBox.Text))nameBox.Focus();};
    }
    private static Label FieldLabel(string text)=>new(){Text=text,AutoSize=true,Anchor=AnchorStyles.Left,ForeColor=Ui.Muted,Margin=Ui.P(0,4,12,4)};
    private ActionKind Kind=>((Choice<ActionKind>)kindBox.SelectedItem!).Value;
    private void ClearMessage()=>message.Set(Kind==ActionKind.RestoreAll?"保存后可在托盘菜单或快捷键中执行。":"保存后，快捷键与托盘菜单会立即更新。",Tone.Neutral);
    private void ShowError(string text,Control? focus=null)
    {
        message.Set(text,Tone.Error);SystemSounds.Beep.Play();
        focus?.Focus();
    }

    private void UpdateKind()
    {
        var kind=Kind;var restoreAll=kind==ActionKind.RestoreAll;var profiles=kind is ActionKind.Apply or ActionKind.Cycle;
        kindHint.Text=Ui.KindHint(kind);
        targets.Visible=!restoreAll;restoreAllNote.Visible=restoreAll;
        foreach(var column in new[]{"Profile","Browse","Mode","Info"})targets.Columns[column]!.Visible=profiles;
        curve.Visible=profiles;dropHint.Visible=profiles;curveColumn.Width=profiles?Ui.S(230):0;
        upButton.Visible=downButton.Visible=kind==ActionKind.Cycle;
        addButton.Visible=removeButton.Visible=!restoreAll;
        addButton.Text=kind switch{ActionKind.Cycle=>"＋ 添加配置",ActionKind.RestoreDisplay=>"＋ 选择显示器",_=>"＋ 添加屏幕"};
        UpdateTargetCommands();UpdateCurve();
    }
    private void UpdateTargetCommands()
    {
        var row=targets.CurrentRow;var count=targets.Rows.Count;
        addButton.Enabled=displays.Count>0 && !(Kind==ActionKind.RestoreDisplay && count>=1);
        removeButton.Enabled=row is not null;
        upButton.Enabled=row is not null && row.Index>0;
        downButton.Enabled=row is not null && row.Index<count-1;
    }
    private void UpdateHotkeyHint()
    {
        var shortcut=hotkeyBox.Shortcut;
        if(shortcut is null){hotkeyHint.Set("未设置快捷键：仍可从主界面或托盘菜单执行",Tone.Neutral);return;}
        try{ShortcutValidation.Validate([shortcut]);}
        catch(ArgumentException ex){hotkeyHint.Set(ex.Message,Tone.Error);return;}
        var conflict=others.FirstOrDefault(a=>a.Id!=original.Id && a.Enabled && a.Shortcut==shortcut);
        if(conflict is not null && enabledBox.Checked)hotkeyHint.Set($"已被“{conflict.Name}”占用，请换一个组合键",Tone.Warning);
        else if(!enabledBox.Checked)hotkeyHint.Set("动作已禁用：保存后此快捷键不会生效",Tone.Neutral);
        else hotkeyHint.Set("可用：保存后立即在全局生效",Tone.Success);
    }

    // ---------- Target rows ----------
    private string PickDisplay()
    {
        var used=targets.Rows.Cast<DataGridViewRow>().Select(r=>r.Cells["Display"].Value?.ToString()).ToHashSet();
        if(Kind==ActionKind.Cycle && targets.Rows.Count>0 && targets.Rows[0].Cells["Display"].Value?.ToString() is {} first)return first;
        return (displays.Where(d=>!used.Contains(d.Id)).OrderByDescending(d=>d.Primary).FirstOrDefault()
            ??displays.FirstOrDefault(d=>d.Primary)??displays[0]).Id;
    }
    private void AddTarget(string profile="")
    {
        if(displays.Count==0){ShowError("没有检测到可用显示器，请返回主窗口刷新。");return;}
        var index=targets.Rows.Add(PickDisplay(),profile,"浏览…",ProfileMode.Combined);
        UpdateRow(targets.Rows[index]);
        targets.CurrentCell=targets.Rows[index].Cells[profile.Length==0 && Kind!=ActionKind.RestoreDisplay?"Profile":"Display"];
        targets.Focus();UpdateCurve();
    }
    private void RemoveTarget()
    {
        if(targets.CurrentRow is not {} row)return;
        var index=row.Index;targets.Rows.Remove(row);
        if(targets.Rows.Count>0)targets.CurrentCell=targets.Rows[Math.Min(index,targets.Rows.Count-1)].Cells["Display"];
        UpdateCurve();
    }
    private void MoveRow(int delta)
    {
        if(targets.CurrentRow is not {} row)return;
        var index=row.Index;var next=index+delta;if(next<0 || next>=targets.Rows.Count)return;
        targets.EndEdit();
        var values=row.Cells.Cast<DataGridViewCell>().Select(c=>c.Value??"").ToArray();
        var tag=row.Cells["Info"].Tag;
        targets.Rows.RemoveAt(index);targets.Rows.Insert(next,values);
        targets.Rows[next].Cells["Info"].Tag=tag;targets.CurrentCell=targets.Rows[next].Cells["Display"];
    }
    private void Browse(object? sender,DataGridViewCellEventArgs e)
    {
        if(e.RowIndex<0 || e.ColumnIndex!=targets.Columns["Browse"]!.Index)return;
        var current=targets.Rows[e.RowIndex].Cells["Profile"].Value?.ToString();
        var folder=Path.GetDirectoryName(current) is {Length:>0} dir && Directory.Exists(dir)?dir:WindowsDisplayBackend.ColorDirectory;
        using var dialog=new OpenFileDialog{Title="选择 ICC / ICM 配置文件",Filter="ICC / ICM 配置|*.icc;*.icm|所有文件|*.*",InitialDirectory=folder,CheckFileExists=true};
        if(dialog.ShowDialog(this)==DialogResult.OK)targets.Rows[e.RowIndex].Cells["Profile"].Value=dialog.FileName;
    }
    private static string[] DroppedProfiles(DragEventArgs e)=>
        e.Data?.GetData(DataFormats.FileDrop) is string[] files
            ?files.Where(f=>Path.GetExtension(f).ToLowerInvariant() is ".icc" or ".icm").ToArray():[];
    private void AddDropped(string[] files)
    {
        foreach(var file in files)
        {
            var empty=targets.Rows.Cast<DataGridViewRow>().FirstOrDefault(r=>string.IsNullOrWhiteSpace(r.Cells["Profile"].Value?.ToString()));
            if(empty is not null){empty.Cells["Profile"].Value=file;targets.CurrentCell=empty.Cells["Display"];}
            else AddTarget(file);
        }
        if(files.Length>0)message.Set($"已添加 {files.Length} 个配置文件，请确认目标显示器与模式。",Tone.Info);
    }
    private static string Clean(object? value)=>(value?.ToString()??"").Trim().Trim('"').Trim();
    private void UpdateRow(DataGridViewRow row)
    {
        var raw=row.Cells["Profile"].Value?.ToString()??"";var path=Clean(raw);
        if(path!=raw){updating=true;try{row.Cells["Profile"].Value=path;}finally{updating=false;}}
        var mode=row.Cells["Mode"].Value is ProfileMode m?m:ProfileMode.Combined;
        row.Cells["Info"].Tag=Inspect(path,mode);
        row.Cells["Profile"].ToolTipText=path;
        targets.InvalidateRow(row.Index);
    }
    private static RowInfo Inspect(string path,ProfileMode mode)
    {
        if(path.Length==0)return new("未选择",Tone.Neutral,"点击“浏览…”选择文件，或把 .icc / .icm 拖进表格。",null);
        if(!File.Exists(path))return new("找不到文件",Tone.Error,"文件不存在："+path,null);
        ProfileData profile;
        try{profile=IccProfile.Load(path);}
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException){return new("无法读取",Tone.Error,ex.Message,null);}
        var gamma=profile.Gamma;
        if(mode==ProfileMode.GammaOnly && gamma is null)return new("无 VCGT",Tone.Error,"此文件不含 VCGT 曲线，“仅曲线”模式无法使用。请改用“ICC + 曲线”或“仅 ICC”。",null);
        if(mode!=ProfileMode.GammaOnly && !Installed(path))
            return new("未安装",Tone.Warning,"此文件尚未安装到系统颜色目录：\n"+WindowsDisplayBackend.ColorDirectory+"\n应用 ICC 时会失败。请右键该文件选择“安装配置文件”，或改用“仅曲线”。",gamma);
        return gamma is null
            ?new("无 VCGT",Tone.Neutral,mode==ProfileMode.IccOnly?"不含 VCGT；“仅 ICC”不需要曲线。":"不含 VCGT；应用时将使用首次修改前保存的日常曲线。",null)
            :new("含 VCGT",Tone.Success,"文件包含 VCGT 伽马曲线，右侧可预览。",gamma);
    }
    private static bool Installed(string path)
    {
        try
        {
            var installed=Path.Combine(WindowsDisplayBackend.ColorDirectory,Path.GetFileName(path));
            if(!File.Exists(installed))return false;
            if(string.Equals(Path.GetFullPath(installed),Path.GetFullPath(path),StringComparison.OrdinalIgnoreCase))return true;
            if(new FileInfo(installed).Length!=new FileInfo(path).Length)return false;
            return File.ReadAllBytes(installed).AsSpan().SequenceEqual(File.ReadAllBytes(path));
        }
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException){return false;}
    }
    private void PaintInfo(object? sender,DataGridViewCellPaintingEventArgs e)
    {
        if(e.RowIndex<0 || e.Graphics is not {} g)return;
        if(e.ColumnIndex==targets.Columns["Profile"]!.Index)
        {
            // Long paths keep both the folder start and the file name visible.
            var row=targets.Rows[e.RowIndex];
            e.PaintBackground(e.CellBounds,true);
            var area=Rectangle.Inflate(e.CellBounds,-Ui.S(8),0);
            var path=Clean(e.Value);
            TextRenderer.DrawText(g,path.Length==0?"点击“浏览…”或拖入文件":path,Ui.Body,area,path.Length==0?Ui.Faint:Ui.Ink,row.Selected?Ui.AccentSoft:Ui.Surface,
                TextFormatFlags.Left|TextFormatFlags.VerticalCenter|TextFormatFlags.SingleLine|TextFormatFlags.PathEllipsis|TextFormatFlags.NoPrefix);
            e.Handled=true;return;
        }
        if(e.ColumnIndex!=targets.Columns["Info"]!.Index)return;
        e.PaintBackground(e.CellBounds,true);
        var cell=targets.Rows[e.RowIndex].Cells[e.ColumnIndex];
        if(cell.Tag is RowInfo info)
        {
            Ui.DrawPill(g,info.Text,Ui.SmallBold,info.Tone,new(e.CellBounds.Left+Ui.S(8),e.CellBounds.Top+e.CellBounds.Height/2));
            cell.ToolTipText=info.Tip;
        }
        e.Handled=true;
    }
    private void UpdateCurve()
    {
        if(Kind is not (ActionKind.Apply or ActionKind.Cycle))return;
        if(targets.CurrentRow is {} row && row.Cells["Info"].Tag is RowInfo info)
        {
            var file=Path.GetFileName(Clean(row.Cells["Profile"].Value));
            curve.SetCurve(info.Gamma,"VCGT 预览 · 第 "+(row.Index+1)+" 行",file.Length==0?"尚未选择 ICC 文件":info.Gamma is null?file+"\n"+info.Text:"");
            if(info.Gamma is not null)Ui.Tips.SetToolTip(curve,file);
        }
        else curve.SetCurve(null,"VCGT 预览","选择一行查看 ICC 内的伽马曲线");
    }

    // ---------- Save ----------
    protected override bool ProcessCmdKey(ref Message msg,Keys keyData)
    {
        if(keyData==(Keys.Control|Keys.S) && !hotkeyBox.Focused){saveButton.PerformClick();return true;}
        return base.ProcessCmdKey(ref msg,keyData);
    }
    private void Save(object? sender,EventArgs e)
    {
        targets.EndEdit();
        var name=nameBox.Text.Trim();
        if(name.Length==0){ShowError("请填写动作名称。",nameBox);return;}
        var result=new SwitchAction{Id=original.Id,Name=name,Enabled=enabledBox.Checked,Kind=Kind,Shortcut=hotkeyBox.Shortcut};
        if(Kind!=ActionKind.RestoreAll)
        {
            if(targets.Rows.Count==0){ShowError(Kind==ActionKind.RestoreDisplay?"请选择要恢复的显示器。":"请至少添加一个目标。",addButton);return;}
            foreach(DataGridViewRow row in targets.Rows)
            {
                var target=new ProfileTarget{DisplayId=row.Cells["Display"].Value?.ToString()??"",ProfilePath=Clean(row.Cells["Profile"].Value),Mode=row.Cells["Mode"].Value is ProfileMode mode?mode:ProfileMode.Combined};
                var prefix=targets.Rows.Count>1?$"第 {row.Index+1} 行：":"";
                if(target.DisplayId.Length==0){ShowError(prefix+"请选择目标显示器。");Select(row,"Display");return;}
                if(Kind is ActionKind.Apply or ActionKind.Cycle)
                {
                    UpdateRow(row);
                    if(target.ProfilePath.Length==0){ShowError(prefix+"请选择 ICC / ICM 文件。");Select(row,"Profile");return;}
                    if(row.Cells["Info"].Tag is RowInfo {Tone:Tone.Error} info){ShowError(prefix+info.Tip.Split('\n')[0]);Select(row,"Profile");return;}
                }
                result.Targets.Add(target);
            }
        }
        try{ActionValidation.Validate(new(){Actions=others.Where(a=>a.Id!=original.Id).Append(result).ToList()});}
        catch(Exception ex) when(ex is ArgumentException or InvalidDataException)
        {
            var conflict=result.Enabled && result.Shortcut is not null?others.FirstOrDefault(a=>a.Id!=original.Id && a.Enabled && a.Shortcut==result.Shortcut):null;
            ShowError(conflict is not null?$"快捷键 {ShortcutText.Format(result.Shortcut)} 已被“{conflict.Name}”使用，请换一个或先禁用该动作。":ex.Message,
                conflict is not null || ex.Message.Contains("快捷键") || ex.Message.Contains("按键")?hotkeyBox:null);
            return;
        }
        Result=result;DialogResult=DialogResult.OK;Close();
    }
    private void Select(DataGridViewRow row,string column)
    {
        if(!targets.Columns[column]!.Visible)column="Display";
        targets.CurrentCell=row.Cells[column];targets.Focus();
    }
}
