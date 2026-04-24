using ChmToMarkdown.Services;
using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows;

namespace ChmToMarkdown.ViewModels
{
    public enum AppStep { Idle, Extracting, WaitingConfirm, Converting, Done }

    public class MainViewModel : INotifyPropertyChanged
    {
        private readonly ConversionService _service = new();
        private readonly StringBuilder _logBuilder = new();

        private string _chmPath = string.Empty;
        private string _outputDir = string.Empty;
        private string _statusText = "请选择 CHM 文件和输出目录。";
        private string _logText = string.Empty;
        private int _progress;
        private bool _isBusy;
        private bool _isMultiFile = true;
        private AppStep _step = AppStep.Idle;
        private string? _extractedDir;

        public MainViewModel()
        {
            var s = SettingsService.Load();
            _chmPath = s.ChmPath;
            _outputDir = s.OutputDir;
            _isMultiFile = s.IsMultiFile;
            // 启动时自动检测已解压目录
            if (!string.IsNullOrWhiteSpace(_outputDir))
                DetectExistingExtracted();
        }

        private void SaveSettings() =>
            SettingsService.Save(new Services.AppSettings { ChmPath = _chmPath, OutputDir = _outputDir, IsMultiFile = _isMultiFile });

        public string ChmPath
        {
            get => _chmPath;
            set { _chmPath = value; OnPropertyChanged(); OnPropertyChanged(nameof(CanExtract)); SaveSettings(); }
        }

        public string OutputDir
        {
            get => _outputDir;
            set
            {
                _outputDir = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CanExtract));
                SaveSettings();
                DetectExistingExtracted();
            }
        }
        public string StatusText { get => _statusText; set { _statusText = value; OnPropertyChanged(); } }
        public string LogText { get => _logText; private set { _logText = value; OnPropertyChanged(); } }
        public int Progress { get => _progress; set { _progress = value; OnPropertyChanged(); } }
        public bool IsMultiFile { get => _isMultiFile; set { _isMultiFile = value; OnPropertyChanged(); SaveSettings(); } }

        public bool IsBusy
        {
            get => _isBusy;
            set { _isBusy = value; OnPropertyChanged(); OnPropertyChanged(nameof(CanExtract)); OnPropertyChanged(nameof(CanConvert)); }
        }

        public AppStep Step
        {
            get => _step;
            private set { _step = value; OnPropertyChanged(); OnPropertyChanged(nameof(CanExtract)); OnPropertyChanged(nameof(CanConvert)); OnPropertyChanged(nameof(ExtractButtonText)); }
        }

        public bool CanExtract => !IsBusy && !string.IsNullOrWhiteSpace(ChmPath) && !string.IsNullOrWhiteSpace(OutputDir) && (Step == AppStep.Idle || Step == AppStep.Done);
        public bool CanConvert => !IsBusy && Step == AppStep.WaitingConfirm;
        public string ExtractButtonText => Step == AppStep.Done ? "重新解压" : "第一步：解压 CHM";

        public void ClearLog() { _logBuilder.Clear(); LogText = string.Empty; }

        /// <summary>
        /// 检测 OutputDir 下是否已有 extracted 目录，有则自动跳到 WaitingConfirm
        /// </summary>
        private void DetectExistingExtracted()
        {
            if (string.IsNullOrWhiteSpace(_outputDir)) return;
            string candidate = Path.Combine(_outputDir, "extracted");
            if (Directory.Exists(candidate) &&
                Directory.GetFiles(candidate, "*", SearchOption.AllDirectories).Length > 0 &&
                (Step == AppStep.Idle || Step == AppStep.Done))
            {
                _extractedDir = candidate;
                Step = AppStep.WaitingConfirm;
                AppendLog($"[检测] 发现已解压目录：{candidate}");
                AppendLog("可直接点击【第二步：开始转换】，或重新解压。");
            }
        }

        public string LogFilePath =>
            Path.Combine(AppContext.BaseDirectory, "conversion.log");

        private void AppendLog(string msg)
        {
            string line = $"[{DateTime.Now:HH:mm:ss}] {msg}";
            _logBuilder.AppendLine(line);
            LogText = _logBuilder.ToString();
            StatusText = msg;
            try
            {
                File.AppendAllText(LogFilePath, line + Environment.NewLine, System.Text.Encoding.UTF8);
            }
            catch (Exception ex)
            {
                // 写日志失败时把原因追加到 UI（不递归调用 AppendLog）
                string errLine = $"[{DateTime.Now:HH:mm:ss}] [日志写入失败] {ex.Message} | 路径: {LogFilePath}";
                _logBuilder.AppendLine(errLine);
                LogText = _logBuilder.ToString();
            }
        }

        public async Task ExtractAsync()
        {
            IsBusy = true;
            Step = AppStep.Extracting;
            Progress = 0;
            _extractedDir = null;
            AppendLog("开始解压...");
            try
            {
                var p = new Progress<string>(msg => Application.Current.Dispatcher.Invoke(() => AppendLog(msg)));
                _extractedDir = await _service.ExtractAsync(ChmPath, OutputDir, p);
                Step = AppStep.WaitingConfirm;
            }
            catch (Exception ex)
            {
                Application.Current.Dispatcher.Invoke(() => { AppendLog($"解压错误: {ex.Message}"); if (ex.InnerException != null) AppendLog($"详情: {ex.InnerException.Message}"); });
                Step = AppStep.Idle;
            }
            finally { IsBusy = false; }
        }

        public async Task ConvertAsync()
        {
            if (_extractedDir == null) return;
            IsBusy = true;
            Step = AppStep.Converting;
            Progress = 0;
            AppendLog("开始转换...");
            try
            {
                var mode = IsMultiFile ? ConversionMode.MultiFile : ConversionMode.SingleFile;
                var p = new Progress<string>(msg => Application.Current.Dispatcher.Invoke(() => AppendLog(msg)));
                var pp = new Progress<int>(pct => Application.Current.Dispatcher.Invoke(() => Progress = pct));
                await _service.ConvertAsync(_extractedDir, OutputDir, mode, p, pp);
                Step = AppStep.Done;
            }
            catch (Exception ex)
            {
                Application.Current.Dispatcher.Invoke(() => { AppendLog($"转换错误: {ex.Message}"); if (ex.InnerException != null) AppendLog($"详情: {ex.InnerException.Message}"); });
                Step = AppStep.WaitingConfirm;
            }
            finally { IsBusy = false; }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
