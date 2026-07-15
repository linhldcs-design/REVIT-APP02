using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using Autodesk.Revit.DB;
using RevitAPP.Models;
using Control = System.Windows.Controls.Control;
using Grid = System.Windows.Controls.Grid;

namespace RevitAPP.Views
{
    public class ScheduleRenumberOptionsWindow : Window
    {
        private readonly ComboBox _scheduleBox;
        private readonly ComboBox _targetFieldBox;
        private readonly ComboBox _formatBox;
        private readonly TextBox _startNumberBox;
        private readonly TextBox _stepBox;
        private readonly TextBox _prefixBox;
        private readonly TextBlock _prefixLabel;
        private readonly CheckBox _skipReadOnlyBox;
        private readonly IReadOnlyList<ScheduleRenumberScheduleOption> _schedules;

        public ScheduleRenumberOptionsWindow(
            IntPtr ownerHandle,
            IReadOnlyList<ScheduleRenumberScheduleOption> schedules,
            ElementId initialScheduleId)
        {
            _schedules = schedules;

            Title = "Đánh số bảng thống kê";
            Width = 460;
            MinWidth = 460;
            SizeToContent = SizeToContent.Height;
            ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;

            if (ownerHandle != IntPtr.Zero)
            {
                new WindowInteropHelper(this).Owner = ownerHandle;
            }

            var root = new Grid
            {
                Margin = new Thickness(18)
            };

            for (var index = 0; index < 7; index++)
            {
                root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            }

            _scheduleBox = new ComboBox
            {
                ItemsSource = _schedules,
                MinWidth = 380
            };
            _scheduleBox.SelectionChanged += (_, _) => RefreshFieldBoxes();
            AddLabeledControl(root, "Bảng thống kê", _scheduleBox, 0);

            _targetFieldBox = new ComboBox();
            AddLabeledControl(root, "Cột cần ghi số thứ tự", _targetFieldBox, 1);

            _formatBox = new ComboBox
            {
                ItemsSource = new[]
                {
                    new FormatOption(ScheduleRenumberFormat.Plain, "1, 2, 3"),
                    new FormatOption(ScheduleRenumberFormat.TwoDigits, "01, 02, 03"),
                    new FormatOption(ScheduleRenumberFormat.ThreeDigits, "001, 002, 003"),
                    new FormatOption(ScheduleRenumberFormat.Prefix, "Tiền tố + số")
                },
                SelectedIndex = 0
            };
            _formatBox.SelectionChanged += (_, _) => RefreshPrefixState();
            AddLabeledControl(root, "Kiểu đánh số", _formatBox, 2);

            var numberPanel = new Grid
            {
                Margin = new Thickness(0, 8, 0, 0)
            };
            numberPanel.ColumnDefinitions.Add(new ColumnDefinition());
            numberPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
            numberPanel.ColumnDefinitions.Add(new ColumnDefinition());

            _startNumberBox = new TextBox { Text = "1" };
            AddInlineLabeledControl(numberPanel, "Số bắt đầu", _startNumberBox, 0);

            _stepBox = new TextBox { Text = "1" };
            AddInlineLabeledControl(numberPanel, "Bước nhảy", _stepBox, 2);

            Grid.SetRow(numberPanel, 3);
            root.Children.Add(numberPanel);

            _prefixBox = new TextBox();
            _prefixLabel = AddLabeledControl(root, "Tiền tố", _prefixBox, 4);

            var checks = new StackPanel
            {
                Margin = new Thickness(0, 12, 0, 0)
            };
            _skipReadOnlyBox = new CheckBox
            {
                Content = "Bỏ qua dòng không ghi được",
                IsChecked = true
            };
            checks.Children.Add(_skipReadOnlyBox);
            Grid.SetRow(checks, 5);
            root.Children.Add(checks);

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 16, 0, 0)
            };

            var okButton = new Button
            {
                Content = "Đánh số",
                Width = 92,
                Height = 30,
                Margin = new Thickness(0, 0, 8, 0),
                IsDefault = true
            };
            okButton.Click += (_, _) => Accept();

            var cancelButton = new Button
            {
                Content = "Huy",
                Width = 92,
                Height = 30,
                IsCancel = true
            };

            buttons.Children.Add(okButton);
            buttons.Children.Add(cancelButton);
            Grid.SetRow(buttons, 6);
            root.Children.Add(buttons);

            Content = root;

            _scheduleBox.SelectedItem = _schedules.FirstOrDefault(schedule => schedule.ScheduleId == initialScheduleId)
                                        ?? _schedules.FirstOrDefault();
            RefreshPrefixState();
        }

        public ScheduleRenumberOptions Options { get; private set; } = new();

        private static TextBlock AddLabeledControl(Grid root, string label, Control control, int row)
        {
            var panel = new StackPanel
            {
                Margin = new Thickness(0, 8, 0, 0)
            };

            var labelBlock = new TextBlock
            {
                Text = label,
                Margin = new Thickness(0, 0, 0, 4)
            };
            panel.Children.Add(labelBlock);

            control.Height = 28;
            panel.Children.Add(control);
            Grid.SetRow(panel, row);
            root.Children.Add(panel);

            return labelBlock;
        }

        private static void AddInlineLabeledControl(Grid root, string label, Control control, int column)
        {
            var panel = new StackPanel();
            panel.Children.Add(new TextBlock
            {
                Text = label,
                Margin = new Thickness(0, 0, 0, 4)
            });

            control.Height = 28;
            panel.Children.Add(control);
            Grid.SetColumn(panel, column);
            root.Children.Add(panel);
        }

        private void RefreshFieldBoxes()
        {
            if (_scheduleBox.SelectedItem is not ScheduleRenumberScheduleOption schedule)
            {
                return;
            }

            _targetFieldBox.ItemsSource = schedule.Fields;
            _targetFieldBox.SelectedIndex = schedule.Fields.Count > 0 ? 0 : -1;
        }

        private void Accept()
        {
            if (_scheduleBox.SelectedItem is not ScheduleRenumberScheduleOption schedule)
            {
                MessageBox.Show(this, "Vui lòng chọn bảng thống kê.", Title, MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (_targetFieldBox.SelectedItem is not ScheduleRenumberFieldOption targetField)
            {
                MessageBox.Show(this, "Vui lòng chọn cột cần ghi số thứ tự.", Title, MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!int.TryParse(_startNumberBox.Text.Trim(), out var startNumber))
            {
                MessageBox.Show(this, "Số bắt đầu không hợp lệ.", Title, MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!int.TryParse(_stepBox.Text.Trim(), out var step) || step == 0)
            {
                MessageBox.Show(this, "Bước nhảy không hợp lệ.", Title, MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            Options = new ScheduleRenumberOptions
            {
                ScheduleId = schedule.ScheduleId,
                TargetFieldId = targetField.FieldId,
                Format = (_formatBox.SelectedItem as FormatOption)?.Format ?? ScheduleRenumberFormat.Plain,
                StartNumber = startNumber,
                Step = step,
                Prefix = _prefixBox.Text.Trim(),
                SkipReadOnlyElements = _skipReadOnlyBox.IsChecked == true
            };

            DialogResult = true;
        }

        private void RefreshPrefixState()
        {
            var isPrefixFormat = (_formatBox.SelectedItem as FormatOption)?.Format == ScheduleRenumberFormat.Prefix;
            _prefixBox.IsEnabled = isPrefixFormat;
            _prefixLabel.Opacity = isPrefixFormat ? 1 : 0.55;
            _prefixBox.Opacity = isPrefixFormat ? 1 : 0.55;
        }

        private class FormatOption
        {
            public FormatOption(ScheduleRenumberFormat format, string label)
            {
                Format = format;
                Label = label;
            }

            public ScheduleRenumberFormat Format { get; }
            public string Label { get; }

            public override string ToString()
            {
                return Label;
            }
        }
    }
}
