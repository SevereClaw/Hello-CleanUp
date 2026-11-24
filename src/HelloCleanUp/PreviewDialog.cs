using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;

namespace HelloCleanUp
{
    /// <summary>Lists every registry value, service and task that is about to change, and asks to confirm.</summary>
    public sealed class PreviewDialog : Window
    {
        public PreviewDialog(Window owner, string heading, string intro, string body, string okText)
        {
            Owner = owner;
            Title = "Hello, CleanUp. — " + heading;
            Width = 760;
            Height = 540;
            MinWidth = 480;
            MinHeight = 320;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ShowInTaskbar = false;
            ResizeMode = ResizeMode.CanResizeWithGrip;
            Background = Res<Brush>("Paper");
            FontFamily = Res<FontFamily>("UiFont");
            SourceInitialized += (s, e) => UseDarkTitleBar();

            var grid = new Grid { Margin = new Thickness(26, 22, 26, 20) };
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var h = new TextBlock { Text = heading, Style = Res<Style>("H1") };
            grid.Children.Add(h);

            var sub = new TextBlock { Text = intro, Style = Res<Style>("Sub"), Margin = new Thickness(0, 4, 0, 14), TextWrapping = TextWrapping.Wrap };
            Grid.SetRow(sub, 1);
            grid.Children.Add(sub);

            var box = new TextBox
            {
                Text = body,
                IsReadOnly = true,
                FontFamily = Res<FontFamily>("MonoFont"),
                FontSize = 12,
                Background = Res<Brush>("Card"),
                Foreground = Res<Brush>("Text"),
                BorderBrush = Res<Brush>("Line"),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(12, 10, 12, 10),
                TextWrapping = TextWrapping.NoWrap,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                CaretBrush = Brushes.Transparent
            };
            Grid.SetRow(box, 2);
            grid.Children.Add(box);

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
            var cancel = new Button { Content = "Отмена", Style = Res<Style>("GhostButton"), IsCancel = true, Margin = new Thickness(0, 0, 12, 0) };
            var ok = new Button { Content = okText, Style = Res<Style>("PrimaryButton"), IsDefault = true };
            ok.Click += (s, e) => { DialogResult = true; };
            buttons.Children.Add(cancel);
            buttons.Children.Add(ok);
            Grid.SetRow(buttons, 3);
            grid.Children.Add(buttons);

            Content = grid;
        }

        private static T Res<T>(string key) where T : class
        {
            return Application.Current.TryFindResource(key) as T;
        }

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

        private void UseDarkTitleBar()
        {
            try
            {
                var hwnd = new WindowInteropHelper(this).Handle;
                int on = 1;
                // DWMWA_USE_IMMERSIVE_DARK_MODE: 20 on Windows 10 20H1+ / 11, 19 on older Windows 10 builds.
                if (DwmSetWindowAttribute(hwnd, 20, ref on, sizeof(int)) != 0)
                    DwmSetWindowAttribute(hwnd, 19, ref on, sizeof(int));
            }
            catch
            {
                // Cosmetic only.
            }
        }
    }
}
