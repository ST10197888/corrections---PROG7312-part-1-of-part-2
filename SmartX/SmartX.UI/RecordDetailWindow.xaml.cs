using System.Collections.Generic;
using System.Windows;

namespace SmartX.UI
{
    public partial class RecordDetailWindow : Window
    {
        public RecordDetailWindow(string title, List<KeyValuePair<string, string>> details)
        {
            InitializeComponent();
            TxtDetailTitle.Text = title;
            DetailItemsControl.ItemsSource = details;
        }

        private void BtnCloseDetail_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}