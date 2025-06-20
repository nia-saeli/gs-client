using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility;
using Dalamud.Plugin.Services;
using GagSpeak.CkCommons.FileSystem;
using GagSpeak.CkCommons.FileSystem.Selector;
using GagSpeak.CkCommons.Gui;
using GagSpeak.Services.Mediator;
using GagSpeak.Utils;
using ImGuiNET;
using OtterGui;
using Dalamud.Interface.Utility.Raii;
using OtterGui.Text;
using GagSpeak.CkCommons.Widgets;
using GagSpeak.PlayerClient;
using GagSpeak.State.Managers;
using GagSpeak.State.Models;

namespace GagSpeak.FileSystems;

// Continue reworking this to integrate a combined approach if we can figure out a better file management system.
public sealed class PatternFileSelector : CkFileSystemSelector<Pattern, PatternFileSelector.PatternState>, IMediatorSubscriber, IDisposable
{
    private readonly FavoritesManager _favorites;
    private readonly PatternManager _manager;
    public GagspeakMediator Mediator { get; init; }

    /// <summary> 
    /// For now, use this 'state storage', it is a list of attributes linked to each leaf.
    /// To be honest im not sure why to not just access this from the path item directly during the draw, but whatever.
    /// We will find out later if anything.
    /// </summary>
    /// <remarks> This allows each item in here to be accessed efficiently at runtime during the draw loop. </remarks>
    public record struct PatternState(uint Color) { }

    /// <summary> This is the currently selected leaf in the file system. </summary>
    public new PatternFileSystem.Leaf? SelectedLeaf
    => base.SelectedLeaf;

    public PatternFileSelector(ILogger<PatternFileSelector> log, GagspeakMediator mediator,
        FavoritesManager favorites, PatternManager manager, PatternFileSystem fileSystem) 
        : base(fileSystem, log, Svc.KeyState, "##PatternsFS")
    {
        Mediator = mediator;
        _favorites = favorites;
        _manager = manager;

        Mediator.Subscribe<ConfigPatternChanged>(this, (msg) => OnPatternChange(msg.Type, msg.Item, msg.OldString));

        // we can add, or unsubscribe from buttons here. Remember this down the line, it will become useful.
    }

    private void RenameLeafPattern(PatternFileSystem.Leaf leaf)
    {
        ImGui.Separator();
        RenameLeaf(leaf);
    }

    private void RenamePattern(PatternFileSystem.Leaf leaf)
    {
        ImGui.Separator();
        var currentName = leaf.Value.Label;
        if (ImGui.IsWindowAppearing())
            ImGui.SetKeyboardFocusHere(0);
        ImGui.TextUnformatted("Rename Pattern:");
        if (ImGui.InputText("##RenamePattern", ref currentName, 256, ImGuiInputTextFlags.EnterReturnsTrue))
        {
            _manager.Rename(leaf.Value, currentName);
            ImGui.CloseCurrentPopup();
        }
        ImGuiUtil.HoverTooltip("Enter a new name here to rename the changed pattern.");
    }

    public override void Dispose()
    {
        base.Dispose();
        Mediator.Unsubscribe<ConfigPatternChanged>(this);
    }

    protected override void DrawLeafInner(CkFileSystem<Pattern>.Leaf leaf, in PatternState state, bool selected)
    {
        // must be a valid drag-drop source, so use invisible button.
        ImGui.InvisibleButton("##leaf", new Vector2(ImGui.GetContentRegionAvail().X, ImGui.GetFrameHeight()));
        var hovered = ImGui.IsItemHovered();
        var rectMin = ImGui.GetItemRectMin();
        var rectMax = ImGui.GetItemRectMax();
        var bgColor = hovered ? ImGui.GetColorU32(ImGuiCol.FrameBgHovered) : CkGui.Color(new Vector4(0.25f, 0.2f, 0.2f, 0.4f));
        ImGui.GetWindowDrawList().AddRectFilled(rectMin, rectMax, bgColor, 5);

        using (ImRaii.Group())
        {
            ImGui.SetCursorScreenPos(rectMin with { X = rectMin.X + ImGui.GetStyle().ItemSpacing.X });
            ImGui.AlignTextToFramePadding();
            Icons.DrawFavoriteStar(_favorites, FavoriteIdContainer.Restraint, leaf.Value.Identifier);
            ImGui.SameLine();
            ImGui.Text(leaf.Value.Label);
            ImGui.SameLine((rectMax.X - rectMin.X) - CkGui.IconSize(FAI.Trash).X - ImGui.GetStyle().ItemSpacing.X);
            if (CkGui.IconButton(FAI.Trash, inPopup: true, disabled: !KeyMonitor.ShiftPressed()))
                _manager.Delete(leaf.Value);
            CkGui.AttachToolTip("Delete this pattern. This cannot be undone.--SEP--Must be holding SHIFT to remove.");
        }

/*        using (ImRaii.Group())
        {
            // display name, then display the downloads and likes on the other side.
            CkGui.GagspeakText(activeItem.Label);
            CkGui.HelpText("Description:--SEP--" + activeItem.Description);

            // playback button
            ImGui.SameLine(ImGui.GetContentRegionAvail().X - ImGui.GetStyle().ItemSpacing.X);
            // Draw the delete button
            ImGui.SameLine();
        }
        // next line:
        using (var group2 = ImRaii.Group())
        {
            ImGui.AlignTextToFramePadding();
            CkGui.IconText(FAI.Clock);
            ImUtf8.SameLineInner();
            CkGui.ColorText(durationTxt, ImGuiColors.DalamudGrey);
            CkGui.AttachToolTip("Total Length of the Pattern.");

            ImGui.SameLine();
            ImGui.AlignTextToFramePadding();
            CkGui.IconText(FAI.Stopwatch20);
            ImUtf8.SameLineInner();
            CkGui.ColorText(startpointTxt, ImGuiColors.DalamudGrey);
            CkGui.AttachToolTip("Start Point of the Pattern.");

            ImGui.SameLine(ImGui.GetContentRegionAvail().X - CkGui.IconSize(FAI.Sync).X - ImGui.GetStyle().ItemInnerSpacing.X);
            CkGui.IconText(FAI.Sync, activeItem.ShouldLoop ? ImGuiColors.ParsedPink : ImGuiColors.DalamudGrey2);
            CkGui.AttachToolTip(activeItem.ShouldLoop ? "Pattern is set to loop." : "Pattern does not loop.");
        }*/

        // the border if selected.
        if (selected)
        {
            ImGui.GetWindowDrawList().AddRectFilled(
                rectMin,
                new Vector2(rectMin.X + ImGuiHelpers.GlobalScale * 3, rectMax.Y),
                CkGui.Color(ImGuiColors.ParsedPink), 5);
        }
    }

    /// <summary> Just set the filter to dirty regardless of what happened. </summary>
    private void OnPatternChange(StorageChangeType type, Pattern pattern, string? oldString)
        => SetFilterDirty();


    /// <summary> Add the state filter combo-button to the right of the filter box. </summary>
    protected override float CustomFiltersWidth(float width)
    {
        var pos = ImGui.GetCursorPos();
        var remainingWidth = width
            - CkGui.IconButtonSize(FAI.Plus).X
            - CkGui.IconButtonSize(FAI.FolderPlus).X
            - ImGui.GetStyle().ItemInnerSpacing.X;

        var buttonsPos = new Vector2(pos.X + remainingWidth, pos.Y);

        ImGui.SetCursorPos(buttonsPos);
        if (CkGui.IconButton(FAI.Plus))
            ImGui.OpenPopup("##NewPattern");
        CkGui.AttachToolTip("Create a new Pattern.");

        ImUtf8.SameLineInner();
        DrawFolderButton();

        ImGui.SetCursorPos(pos);
        return remainingWidth - ImGui.GetStyle().ItemInnerSpacing.X;
    }

    protected override void DrawPopups()
    {
        // make this pull up the pattern maker later down the line.
        NewPatternPopup();
    }

    private void NewPatternPopup()
    {
        if (!ImGuiUtil.OpenNameField("##NewPattern", ref _newName))
            return;

        _manager.CreateNew(_newName);
        _newName = string.Empty;
    }
}

