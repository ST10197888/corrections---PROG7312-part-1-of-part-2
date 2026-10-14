using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SmartX.UI.Views
{
    public partial class NavBarControl : UserControl
    {
        // calls the screen key from the clicked button's Tag (Landing, Telemetry, CommandStream, Topology)
        public event EventHandler<string> NavigationRequested;

        public NavBarControl()
        {
            InitializeComponent();
        }

        private void NavButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string key)
                NavigationRequested?.Invoke(this, key);
        }

        // Call after navigating so the opne screen's button is visibly highlighted.
        public void SetActive(string screenKey)
        {
            foreach (var btn in new[] { BtnLanding, BtnTelemetry, BtnCommandStream, BtnTopology })
            {
                bool isActive = (string)btn.Tag == screenKey;
                btn.BorderThickness = new Thickness(isActive ? 3 : 1);
                btn.Foreground = isActive ? Brushes.White : (Brush)new BrushConverter().ConvertFrom("#00d4ff");
            }
        }
    }
}