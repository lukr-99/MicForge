using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using MicForge.ViewModels;

namespace MicForge;

/// <summary>Drag-and-drop reordering of the processing-chain cards, with a live reflow preview.</summary>
public partial class MainWindow
{
    private Point _dragStart;
    private StageViewModel _dragStage;
    private List<StageViewModel> _dragOriginalOrder;

    private void StagesPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        var handle = FindTagged(e.OriginalSource as DependencyObject, "draghandle");
        _dragStage = (handle as FrameworkElement)?.DataContext as StageViewModel;
        if (_dragStage != null) _dragStart = e.GetPosition(null);
    }

    private void StagesPreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_dragStage == null || e.LeftButton != MouseButtonState.Pressed) return;
        var p = e.GetPosition(null);
        if (Math.Abs(p.X - _dragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(p.Y - _dragStart.Y) < SystemParameters.MinimumVerticalDragDistance) return;

        var stage = _dragStage;
        _dragStage = null;
        _dragOriginalOrder = new List<StageViewModel>(_vm.Stages);
        _vm.SetDragging(stage, true);

        var effect = DragDrop.DoDragDrop(StagesList, new DataObject("MicForgeStage", stage), DragDropEffects.Move);

        _vm.SetDragging(stage, false);
        if (effect == DragDropEffects.Move) _vm.CommitOrder();
        else _vm.RestoreOrder(_dragOriginalOrder);
        _dragOriginalOrder = null;
    }

    private void StagesDragOver(object sender, DragEventArgs e)
    {
        e.Handled = true;
        if (e.Data.GetData("MicForgeStage") is not StageViewModel dragged)
        {
            e.Effects = DragDropEffects.None;
            return;
        }
        e.Effects = DragDropEffects.Move;

        // Live preview: reflow the cards as the cursor moves over a different one.
        var target = StageAt(e.GetPosition(StagesList));
        if (target != null && target != dragged) _vm.MoveStageLive(dragged, target);
    }

    private void StagesDrop(object sender, DragEventArgs e) => e.Handled = true;

    private StageViewModel StageAt(Point p)
    {
        var hit = StagesList.InputHitTest(p) as DependencyObject;
        while (hit != null)
        {
            if (hit is FrameworkElement fe && fe.DataContext is StageViewModel s && _vm.Stages.Contains(s))
                return s;
            hit = VisualTreeHelper.GetParent(hit);
        }
        return null;
    }

    private static DependencyObject FindTagged(DependencyObject d, string tag)
    {
        while (d != null)
        {
            if (d is FrameworkElement fe && (fe.Tag as string) == tag) return d;
            d = VisualTreeHelper.GetParent(d);
        }
        return null;
    }
}
