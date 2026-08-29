using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Ailo.AI.Skills;
using Ailo.Models;
using Ailo.ViewModels;

namespace Ailo.Views.Settings;

public partial class SkillSettingsView : UserControl
{
    private const double DragThreshold = 6;
    private Skill? _pendingDragSkill;
    private PointerPressedEventArgs? _dragTriggerEvent;
    private Point _pointerPressedPosition;

    public SkillSettingsView()
    {
        InitializeComponent();
        DragDrop.SetAllowDrop(SkillList, true);
        SkillList.AddHandler(InputElement.PointerPressedEvent, OnSkillPointerPressed, RoutingStrategies.Bubble, true);
        SkillList.AddHandler(InputElement.PointerMovedEvent, OnSkillPointerMoved, RoutingStrategies.Bubble, true);
        SkillList.AddHandler(InputElement.PointerReleasedEvent, OnSkillPointerReleased, RoutingStrategies.Bubble, true);
    }

    private void OnSkillPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(SkillList).Properties.PointerUpdateKind != PointerUpdateKind.LeftButtonPressed)
        {
            return;
        }

        var item = e.Source as ListBoxItem ?? (e.Source as Visual)?.FindAncestorOfType<ListBoxItem>();
        if (item?.DataContext is not Skill skill)
        {
            return;
        }

        _pendingDragSkill = skill;
        _dragTriggerEvent = e;
        _pointerPressedPosition = e.GetPosition(SkillList);
    }

    private async void OnSkillPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_pendingDragSkill is not { } skill ||
            _dragTriggerEvent is not { } triggerEvent ||
            !e.GetCurrentPoint(SkillList).Properties.IsLeftButtonPressed)
        {
            return;
        }

        var currentPosition = e.GetPosition(SkillList);
        var offset = currentPosition - _pointerPressedPosition;
        if (Math.Abs(offset.X) < DragThreshold && Math.Abs(offset.Y) < DragThreshold)
        {
            return;
        }

        _pendingDragSkill = null;
        _dragTriggerEvent = null;
        var data = new DataTransfer();
        data.Add(DataTransferItem.CreateText(skill.Id));
        DragPreviewName.Text = skill.Name;
        DragPreviewDescription.Text = skill.Description;
        DragPreview.IsVisible = true;
        UpdateDragPreviewPosition(e.GetPosition(this));
        try
        {
            await DragDrop.DoDragDropAsync(triggerEvent, data, DragDropEffects.Move);
        }
        finally
        {
            DragPreview.IsVisible = false;
        }
    }

    private void OnSkillPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        _pendingDragSkill = null;
        _dragTriggerEvent = null;
    }

    private void OnSkillDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = e.DataTransfer.TryGetText() is not null ? DragDropEffects.Move : DragDropEffects.None;
        UpdateDragPreviewPosition(e.GetPosition(this));
        e.Handled = true;
    }

    private async void OnSkillDrop(object? sender, DragEventArgs e)
    {
        if (DataContext is not SkillSettingsViewModel viewModel || e.DataTransfer.TryGetText() is not string draggedId)
        {
            return;
        }

        var draggedSkill = viewModel.Skills.FirstOrDefault(skill => skill.Id == draggedId);
        if (draggedSkill is null)
        {
            return;
        }

        var targetItem = (SkillList.InputHitTest(e.GetPosition(SkillList)) as Visual)?.FindAncestorOfType<ListBoxItem>();
        var targetSkill = targetItem?.DataContext as Skill;
        var insertAfter = targetItem is not null && e.GetPosition(targetItem).Y > targetItem.Bounds.Height / 2;
        await viewModel.ReorderSkillsAsync(draggedSkill, targetSkill, insertAfter);
        e.Handled = true;
    }

    private void UpdateDragPreviewPosition(Point point)
    {
        Canvas.SetLeft(DragPreview, Math.Clamp(point.X + 12, 0, Math.Max(0, Bounds.Width - DragPreview.Bounds.Width)));
        Canvas.SetTop(DragPreview, Math.Clamp(point.Y + 12, 0, Math.Max(0, Bounds.Height - DragPreview.Bounds.Height)));
    }
}
