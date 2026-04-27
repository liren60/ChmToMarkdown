using ChmToMarkdown.ViewModels;
using Microsoft.Win32;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace ChmToMarkdown
{
    public partial class MainWindow : Window
    {
        private readonly MainViewModel _vm = new();
        private Storyboard? _spinnerSB;
        private Storyboard? _glowSB;
        private Storyboard? _extractDoneSB;
        private Storyboard? _convertDoneSB;

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
            Storyboard.SetTargetProperty(spinAnim, new PropertyPath("Angle"));
            _spinnerSB = new Storyboard();
            _spinnerSB.Children.Add(spinAnim);

            // Glow：ProgressGlow Opacity 呼吸动画
            var glowAnim = new DoubleAnimation(0.15, 0.9, new Duration(System.TimeSpan.FromSeconds(0.8)))
            {
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever
            };
            Storyboard.SetTargetName(glowAnim, "ProgressGlow");
            Storyboard.SetTargetProperty(glowAnim, new PropertyPath("Opacity"));
            _glowSB = new Storyboard();
            _glowSB.Children.Add(glowAnim);

            // 完成徽章：3次快速缩放闪烁后稳定
            _extractDoneSB = BuildDoneBadgeSB("ExtractDoneBadge");
            _convertDoneSB = BuildDoneBadgeSB("ConvertDoneBadge");
        }

        private static Storyboard BuildDoneBadgeSB(string targetName)
        {
            var sb = new Storyboard();
            var dur = new Duration(System.TimeSpan.FromMilliseconds(120));

            // 3次 scale 脉冲：1→1.25→1 ×3，然后稳定在1
            double[] keyTimes = [0, 120, 240, 360, 480, 600, 720];
            double[] scaleX   = [1, 1.28, 1, 1.20, 1, 1.12, 1];

            var animX = new DoubleAnimationUsingKeyFrames();
            var animY = new DoubleAnimationUsingKeyFrames();
            for (int i = 0; i < keyTimes.Length; i++)
            {
                var kfX = new LinearDoubleKeyFrame(scaleX[i],
                    KeyTime.FromTimeSpan(System.TimeSpan.FromMilliseconds(keyTimes[i])));
                var kfY = new LinearDoubleKeyFrame(scaleX[i],
                    KeyTime.FromTimeSpan(System.TimeSpan.FromMilliseconds(keyTimes[i])));
                animX.KeyFrames.Add(kfX);
                animY.KeyFrames.Add(kfY);
            }

            Storyboard.SetTargetName(animX, targetName);
            Storyboard.SetTargetProperty(animX, new PropertyPath("RenderTransform.ScaleX"));
            Storyboard.SetTargetName(animY, targetName);
            Storyboard.SetTargetProperty(animY, new PropertyPath("RenderTransform.ScaleY"));

            // 同时做一次颜色从白色闪到正常的 Opacity 效果：0→1
            var opacAnim = new DoubleAnimationUsingKeyFrames();
            opacAnim.KeyFrames.Add(new LinearDoubleKeyFrame(0,   KeyTime.FromTimeSpan(System.TimeSpan.Zero)));
            opacAnim.KeyFrames.Add(new LinearDoubleKeyFrame(1,   KeyTime.FromTimeSpan(System.TimeSpan.FromMilliseconds(60))));
            opacAnim.KeyFrames.Add(new LinearDoubleKeyFrame(0.4, KeyTime.FromTimeSpan(System.TimeSpan.FromMilliseconds(180))));
            opacAnim.KeyFrames.Add(new LinearDoubleKeyFrame(1,   KeyTime.FromTimeSpan(System.TimeSpan.FromMilliseconds(300))));
            opacAnim.KeyFrames.Add(new LinearDoubleKeyFrame(0.6, KeyTime.FromTimeSpan(System.TimeSpan.FromMilliseconds(420))));
            opacAnim.KeyFrames.Add(new LinearDoubleKeyFrame(1,   KeyTime.FromTimeSpan(System.TimeSpan.FromMilliseconds(540))));
            Storyboard.SetTargetName(opacAnim, targetName);
            Storyboard.SetTargetProperty(opacAnim, new PropertyPath("Opacity"));

            sb.Children.Add(animX);
            sb.Children.Add(animY);
            sb.Children.Add(opacAnim);
            return sb;
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
            else if (e.PropertyName == nameof(_vm.ExtractDone))
            {
                if (_vm.ExtractDone)
                {
                    // 为徽章设置 ScaleTransform 中心点
                    ExtractDoneBadge.RenderTransformOrigin = new Point(0.5, 0.5);
                    ExtractDoneBadge.RenderTransform = new ScaleTransform(1, 1);
                    _extractDoneSB?.Begin(this, true);
                }
            }
            else if (e.PropertyName == nameof(_vm.ConvertDone))
            {
                if (_vm.ConvertDone)
                {
                    ConvertDoneBadge.RenderTransformOrigin = new Point(0.5, 0.5);
                    ConvertDoneBadge.RenderTransform = new ScaleTransform(1, 1);
                    _convertDoneSB?.Begin(this, true);
                }
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
        private void Reset_Click(object sender, RoutedEventArgs e) => _vm.Reset();
        private void OpenLogDir_Click(object sender, RoutedEventArgs e)
        {
            string logDir = System.IO.Path.GetDirectoryName(_vm.LogFilePath) ?? AppContext.BaseDirectory;
            System.Diagnostics.Process.Start("explorer.exe", logDir);
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
