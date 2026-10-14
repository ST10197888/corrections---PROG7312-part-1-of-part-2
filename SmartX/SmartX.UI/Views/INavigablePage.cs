using System;
using System.Collections.Generic;
using System.Text;

namespace SmartX.UI.Views
{
    public interface INavigablePage
    {
        event EventHandler<string> NavigationRequested;
        void ActivateNav(string screenKey);
    }
}