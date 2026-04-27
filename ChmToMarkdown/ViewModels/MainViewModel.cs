using ChmToMarkdown.Services;
using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace ChmToMarkdown.ViewModels
{
    public enum AppStep { Idle, Extracting, WaitingConfirm, Converting, Done }

    public class MainViewModel : INotifyPropertyChanged
    {
        private readonly ConversionService _service = new();
        private readonly StringBuilder _logBuilder = new();
        private DateTime _lastLogFlush = DateTime.MinValue;

        private string _chmPath = string.Empty;
        private string _outputDir = string.Empty;
        private string _statusText = "请选择 CHM 文件和输出目录。";
        private string _logText = string.Empty;
        private int _progress;
        private string _progressText = string.Empty;
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
            if (!string.IsNullOrWhiteSpace(_outputDir))
                DetectExistingExtracted();
        }

        private void SaveSettings() =>
            SettingsService.Save(new AppSettings { ChmPath = _chmPath, OutputDir = _outputDir, IsMultiFile = _isMultiFile });

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

        public string StatusText   { get => _statusText;   set { _statusText = value;   OnPropertyChanged(); } }
        public string LogText      { get => _logText;      private set { _logText = value; OnPropertyChanged(); } }
        public int    Progress     { get => _progress;     set { _progress = value;     OnPropertyChanged(); } }
        public string ProgressText { get => _progressText; set { _progressText = value; OnPropertyChanged(); } }
        public bool   IsMultiFile  { get => _isMultiFile;  set { _isMultiFile = value;  OnPropertyChanged(); SaveSettings(); } }

        public bool IsBusy
        {
            get => _isBusy;
            set
            {
                _isBusy = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CanExtract));
                OnPropertyChanged(nameof(CanConvert));
                OnPropertyChanged(nameof(IsExtracting));
                OnPropertyChanged(nameof(IsConverting));
            }
        }

        public AppStep Step
        {
            get => _step;
            private set
            {
                _step = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CanExtract));
                OnPropertyChanged(nameof(CanConvert));
                OnPropertyChanged(nameof(ExtractButtonText));
                OnPropertyChanged(nameof(IsExtracting));
                OnPropertyChanged(nameof(IsConverting));
            }
        }

        public bool CanExtract    => !IsBusy && !string.IsNullOrWhiteSpace(ChmPath) && !string.IsNullOrWhiteSpace(OutputDir) && (Step == AppStep.Idle || Step == AppStep.Done);
        public bool CanConvert    => !IsBusy && Step == AppStep.WaitingConfirm;
        public bool IsExtracting  => IsBusy && Step == AppStep.Extracting;
        public bool IsConverting  => IsBusy && Step == AppStep.Converting;
        public string ExtractButtonText => Step == AppStep.Done ? "重新解压" : "第一步：解压 CHM";

        public void ClearLog() { _logBuilder.Clear(); LogText = string.Empty; }

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

        public string LogFilePath => Path.Combine(AppContext.BaseDirectory, "conversion.log");

        // 节流：每 150ms 最多刷新一次 UI 日志，避免大量文件时 UI 阻塞
        private void AppendLog(string msg, bool forceFlush = false)
        {
            string line = $"[{DateTime.Now:HH:mm:ss}] {msg}";
            _logBuilder.AppendLine(line);
            StatusText = msg;

            var now = DateTime.Now;
            if (forceFlush || (now - _lastLogFlush).TotalMilliseconds >= 150)
            {
                LogText = _logBuilder.ToString();
                _lastLogFlush = now;
            }

            try { File.AppendAllText(LogFilePath, line + Environment.NewLine, Encoding.UTF8); }
            catch { }
        }

        public async Task ExtractAsync()
        {
            IsBusy = true;
            Step = AppStep.Extracting;
            Progress = 0;
            ProgressText = string.Empty;
            _extractedDir = null;
            AppendLog("开始解压...", true);
            try
            {
                var p = new Progress<string>(msg => AppendLog(msg));
                _extractedDir = await _service.ExtractAsync(ChmPath, OutputDir, p);
                AppendLog("解压完成。", true);
                Step = AppStep.WaitingConfirm;
            }
            catch (Exception ex)
            {
                AppendLog($"解压错误: {ex.Message}", true);
                if (ex.InnerException != null) AppendLog($"详情: {ex.InnerException.Message}", true);
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
            ProgressText = "0%";
            AppendLog("开始转换...", true);
            try
            {
                var mode = IsMultiFile ? ConversionMode.MultiFile : ConversionMode.SingleFile;
                var p  = new Progress<string>(msg => AppendLog(msg));
                var pp = new Progress<int>(pct =>
                {
                    Progress = pct;
                    ProgressText = $"{pct}%";
                });
                await _service.ConvertAsync(_extractedDir, OutputDir, mode, p, pp);
                ProgressText = "100%";
                AppendLog("转换完成！", true);
                Step = AppStep.Done;
            }
            catch (Exception ex)
            {
                AppendLog($"转换错误: {ex.Message}", true);
                if (ex.InnerException != null) AppendLog($"详情: {ex.InnerException.Message}", true);
                Step = AppStep.WaitingConfirm;
            }
            finally { IsBusy = false; }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
