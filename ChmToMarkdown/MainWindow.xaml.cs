using ChmToMarkdown.ViewModels;
using Microsoft.Win32;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace ChmToMarkdown
{
    public partial class MainWindow : Window
    {
        private readonly MainViewModel _vm = new();

        public MainWindow()
        {
            InitializeComponent();
            DataContext = _vm;
        }

        private void BrowseChm_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog { Filter = "CHM 文件|*.chm|所有文件|*.*" };
            if (dlg.ShowDialog() == true)
                _vm.ChmPath = dlg.FileName;
        }

        private void BrowseOutput_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFolderDialog { Title = "选择输出目录" };
            if (dlg.ShowDialog() == true)
                _vm.OutputDir = dlg.FolderName;
        }

        private async void Extract_Click(object sender, RoutedEventArgs e)
        {
            await _vm.ExtractAsync();
        }

        private async void Convert_Click(object sender, RoutedEventArgs e)
        {
            await _vm.ConvertAsync();
        }

        private void ClearLog_Click(object sender, RoutedEventArgs e)
        {
            _vm.ClearLog();
        }

        private void File_DragOver(object sender, DragEventArgs e)
        {
            e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        }

        private void ChmPath_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
                _vm.ChmPath = files[0];
        }

        private void OutputDir_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
            {
                string path = files[0];
                _vm.OutputDir = Directory.Exists(path) ? path : Path.GetDirectoryName(path) ?? path;
            }
        }
    }
}