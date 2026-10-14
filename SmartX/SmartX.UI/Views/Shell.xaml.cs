using SmartX.UI.Services;
using SmartX.UI.Views;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;

namespace SmartX.UI
{
    public partial class Shell : Window
    {
        private readonly Dictionary<string, UserControl> _pages = new();

        public Shell()
        {
            InitializeComponent();
            LocalTelemetryStore.EnsureStarted();
            Navigate("Landing");
        }

        private void Navigate(string screenKey)
        {
            if (!_pages.TryGetValue(screenKey, out var page))
            {
                page = CreatePage(screenKey);
                if (page == null) return;

                ((INavigablePage)page).NavigationRequested += (s, key) => Navigate(key);
                _pages[screenKey] = page;
            }

            MainContent.Content = page;
            ((INavigablePage)page).ActivateNav(screenKey);
        }

        private UserControl CreatePage(string screenKey) => screenKey switch
        {
            "Landing" => new LandingPageView(),
            "Telemetry" => new TelemetryView(),
            "CommandStream" => new CommandStreamView(),
            "Topology" => new NetworkTopologyView(),
            _ => null
        };
    }
}