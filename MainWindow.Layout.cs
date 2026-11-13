using System;
using System.Windows;
using System.Windows.Controls;

namespace nMIDI
{
    /// <summary>
    /// Keeps the top bar and the edit toolbar usable at any window width. Step by step, as space runs out:
    /// buttons lose their text (icons stay, tooltips explain), labels and the speed slider shrink, and
    /// finally the view options move to a second row of the top bar.
    /// </summary>
    public partial class MainWindow
    {
        private int _topStage = -1, _editStage = -1;
        private bool _layoutQueued;

        private void InitBarLayout()
        {
            SizeChanged += (_, _) => QueueBarLayout();
            gridTop.SizeChanged += (_, e) => { if (e.WidthChanged) QueueBarLayout(); };
            ThemeManager.Changed += () =>
            {
                _topStage = _editStage = -1;
                QueueBarLayout();
                // Things drawn in code take the new colours.
                viewer.InvalidateElements();
                InvalidateTree(this);
                if (_tracksWindow != null) InvalidateTree(_tracksWindow);
                if (_mixerWindow != null) InvalidateTree(_mixerWindow);
                _keyboardSignature = 0;
            };
        }

        private void QueueBarLayout()
        {
            if (_layoutQueued) return;
            _layoutQueued = true;
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, new Action(() =>
            {
                _layoutQueued = false;
                LayoutBars();
            }));
        }

        private void LayoutBars()
        {
            double width = gridTop.ActualWidth;
            if (width <= 0) return;

            // Top bar: the first stage at which buttons + transport + view options + a little file name fit.
            int stage = 0;
            for (; stage < 3; stage++)
            {
                SetTopStage(stage);
                double need = Desired(pnlTopLeft) + Desired(bdTransport) + Desired(pnlTopRight) + 150;
                if (need <= width) break;
            }
            SetTopStage(stage);

            // Edit toolbar: the tools on one row next to a short selection text, if at all possible.
            double editWidth = pnlEdit.ActualWidth;
            if (editWidth > 0)
            {
                int e = 0;
                for (; e < 2; e++)
                {
                    SetEditStage(e);
                    if (DesiredRow(pnlEditTools) + 180 <= editWidth) break;
                }
                SetEditStage(e);
            }
        }

        private static void InvalidateTree(System.Windows.DependencyObject root)
        {
            if (root is UIElement ui) ui.InvalidateVisual();
            int n = System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < n; i++) InvalidateTree(System.Windows.Media.VisualTreeHelper.GetChild(root, i));
        }

        private static double Desired(FrameworkElement element)
        {
            element.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            return element.DesiredSize.Width + element.Margin.Left + element.Margin.Right;
        }

        /// <summary>Width of a wrap panel's children laid out on one row.</summary>
        private static double DesiredRow(Panel panel)
        {
            double sum = 0;
            foreach (UIElement child in panel.Children)
            {
                if (child.Visibility == Visibility.Collapsed) continue;
                child.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                sum += child.DesiredSize.Width;
            }
            return sum;
        }

        private void SetTopStage(int stage)
        {
            if (stage == _topStage) return;
            _topStage = stage;
            var labels = stage >= 1 ? Visibility.Collapsed : Visibility.Visible;
            foreach (var t in new TextBlock[] { lblOpen, lblFollow, lblLoop, lblFit, lblLanes, lblMixer }) t.Visibility = labels;
            // Icon-only buttons: drop the gap the label left next to the icon.
            foreach (var t in new TextBlock[] { lblOpen, lblFollow, lblLoop, lblFit, lblLanes, lblMixer })
                if (t.Parent is StackPanel sp && sp.Children.Count > 0 && sp.Children[0] is TextBlock icon)
                    icon.Margin = stage >= 1 ? new Thickness(0) : new Thickness(0, 0, 6, 0);

            capColor.Visibility = stage >= 2 ? Visibility.Collapsed : Visibility.Visible;
            cmbColorMode.Margin = stage >= 2 ? new Thickness(8, 0, 0, 0) : new Thickness(0);
            sldSpeed.Width = stage >= 2 ? 90 : 130;
            runLogoEditor.Text = stage >= 2 ? "" : " Editor";

            bool twoRows = stage >= 3;
            Grid.SetRow(pnlTopRight, twoRows ? 1 : 0);
            Grid.SetColumn(pnlTopRight, twoRows ? 0 : 4);
            Grid.SetColumnSpan(pnlTopRight, twoRows ? 5 : 1);
            pnlTopRight.Margin = twoRows ? new Thickness(0, 6, 0, 0) : new Thickness(0);
        }

        private void SetEditStage(int stage)
        {
            if (stage == _editStage) return;
            _editStage = stage;
            var labels = stage >= 1 ? Visibility.Collapsed : Visibility.Visible;
            foreach (var t in new TextBlock[] { lblStep, lblProcess, lblSong, lblDim })
            {
                t.Visibility = labels;
                if (t.Parent is StackPanel sp && sp.Children.Count > 0 && sp.Children[0] is TextBlock icon)
                    icon.Margin = stage >= 1 ? new Thickness(0) : new Thickness(0, 0, 5, 0);
            }
            var caps = stage >= 2 ? Visibility.Collapsed : Visibility.Visible;
            foreach (var t in new TextBlock[] { capEdit, capGrid, capLength, capVelocity, capScale }) t.Visibility = caps;
            foreach (var c in new FrameworkElement[] { cmbLength, txtVelocity, cmbKey })
                c.Margin = stage >= 2 ? new Thickness(4, 0, 0, 0) : new Thickness(0);
            cmbScale.Width = stage >= 2 ? 104 : 130;
            cmbGrid.Margin = stage >= 2 ? new Thickness(10, 0, 0, 0) : new Thickness(0);
        }
    }
}
