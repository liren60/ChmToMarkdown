using ChmToMarkdown.ViewModels;
using Microsoft.Win32;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Media.Animation;

namespace ChmToMarkdown
{
    public partial class MainWindow : Window
    {
        private readonly MainViewModel _vm = new();
        private Storyboard? _spinnerSB;
        private Storyboard? _glowSB;

        public MainWindow()
        {
            InitializeComponent();
            DataContext = _vm;
            _vm.PropertyChanged += Vm_PropertyChanged;
            BuildAnimations();
        }

        private void BuildAnimations()
        {
            // Spinner：SpinnerRotate 旋转 360°
            var spinAnim = new DoubleAnimation(0, 360, new Duration(System.TimeSpan.FromSeconds(1)))
            {
                RepeatBehavior = RepeatBehavior.Forever
            };
            Storyboard.SetTargetName(spinAnim, "SpinnerRotate");
            Storyboard.SetTargetProperty(spinAnim, new System.Windows.PropertyPath("Angle"));
            _spinnerSB = new Storyboard();
            _spinnerSB.Children.Add(spinAnim);

            // Glow：ProgressGlow Opacity 呼吸动画
            var glowAnim = new DoubleAnimation(0.15, 0.9, new Duration(System.TimeSpan.FromSeconds(0.8)))
            {
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever
            };
            Storyboard.SetTargetName(glowAnim, "ProgressGlow");
            Storyboard.SetTargetProperty(glowAnim, new System.Windows.PropertyPath("Opacity"));
            _glowSB = new Storyboard();
            _glowSB.Children.Add(glowAnim);
        }

        private void Vm_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(_vm.IsExtracting))
            {
                if (_vm.IsExtracting) _spinnerSB?.Begin(this, true);
                else _spinnerSB?.Stop(this);
            }
            else if (e.PropertyName == nameof(_vm.IsConverting))
            {
                if (_vm.IsConverting) _glowSB?.Begin(this, true);
                else _glowSB?.Stop(this);
            }
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

        private async void Extract_Click(object sender, RoutedEventArgs e) => await _vm.ExtractAsync();
        private async void Convert_Click(object sender, RoutedEventArgs e) => await _vm.ConvertAsync();
        private void ClearLog_Click(object sender, RoutedEventArgs e) => _vm.ClearLog();
        private void Cancel_Click(object sender, RoutedEventArgs e) => _vm.Cancel();

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
