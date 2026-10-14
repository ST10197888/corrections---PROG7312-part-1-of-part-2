using System;
using System.Windows.Controls;

namespace SmartX.UI.Views
{
    public partial class CommandStreamView : UserControl, INavigablePage
    {
        public event EventHandler<string> NavigationRequested;

        public CommandStreamView()
        {
            InitializeComponent();
        }

        private void NavBar_NavigationRequested(object sender, string screenKey) =>
            NavigationRequested?.Invoke(this, screenKey);

        public void ActivateNav(string screenKey) => NavBar.SetActive(screenKey);
    }
}