using ChmToMarkdown.Services;
using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ChmToMarkdown.ViewModels
{
    public enum AppStep { Idle, Extracting, WaitingConfirm, Converting, Done }

    public class MainViewModel : INotifyPropertyChanged
    {
        private readonly ConversionService _service = new();
        private readonly StringBuilder _logBuilder = new();
        private DateTime _lastLogFlush = DateTime.MinValue;
        private CancellationTokenSource? _cts;

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
        private bool _hasUnfinishedTask;

        public MainViewModel()
        {
            var s = SettingsService.Load();
            _chmPath = s.ChmPath;
            _outputDir = s.OutputDir;
            _isMultiFile = s.IsMultiFile;
            if (!string.IsNullOrWhiteSpace(_outputDir))
            {
                DetectExistingExtracted();
                DetectUnfinishedConversion();
            }
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
                DetectUnfinishedConversion();
            }
        }

        public string StatusText       { get => _statusText;       set { _statusText = value;       OnPropertyChanged(); } }
        public string LogText          { get => _logText;          private set { _logText = value;  OnPropertyChanged(); } }
        public int    Progress         { get => _progress;         set { _progress = value;         OnPropertyChanged(); } }
        public string ProgressText     { get => _progressText;     set { _progressText = value;     OnPropertyChanged(); } }
        public bool   IsMultiFile      { get => _isMultiFile;      set { _isMultiFile = value;      OnPropertyChanged(); SaveSettings(); } }
        public bool   HasUnfinishedTask{ get => _hasUnfinishedTask; set { _hasUnfinishedTask = value; OnPropertyChanged(); OnPropertyChanged(nameof(CanReset)); } }

        public bool IsBusy
        {
            get => _isBusy;
            set
            {
                _isBusy = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CanExtract));
                OnPropertyChanged(nameof(CanConvert));
                OnPropertyChanged(nameof(CanCancel));
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
                OnPropertyChanged(nameof(CanReset));
                OnPropertyChanged(nameof(ExtractButtonText));
                OnPropertyChanged(nameof(IsExtracting));
                OnPropertyChanged(nameof(IsConverting));
            }
        }

        public bool   CanExtract        => !IsBusy && !string.IsNullOrWhiteSpace(ChmPath) && !string.IsNullOrWhiteSpace(OutputDir) && (Step == AppStep.Idle || Step == AppStep.Done);
        public bool   CanConvert        => !IsBusy && Step == AppStep.WaitingConfirm;
        public bool   CanCancel         => IsBusy && (Step == AppStep.Extracting || Step == AppStep.Converting);
        public bool   CanReset          => !IsBusy && (Step != AppStep.Idle || HasUnfinishedTask);
        public bool   IsExtracting      => IsBusy && Step == AppStep.Extracting;
        public bool   IsConverting      => IsBusy && Step == AppStep.Converting;
        public string ExtractButtonText => Step == AppStep.Done ? "重新解压" : "第一步：解压 CHM";

        public void ClearLog() { _logBuilder.Clear(); LogText = string.Empty; }

        public void Cancel()
        {
            _cts?.Cancel();
            AppendLog("正在取消...", true);
        }

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

        private void DetectUnfinishedConversion()
        {
            if (string.IsNullOrWhiteSpace(_outputDir)) return;
            var prog = ConversionService.LoadProgress(_outputDir);
            if (prog != null && !prog.Finished && prog.CompletedFiles.Count > 0)
            {
                HasUnfinishedTask = true;
                AppendLog($"[检测] 发现未完成的转换任务（已完成 {prog.CompletedFiles.Count} 个文件），点击【继续转换】可从断点继续。", true);
            }
            else
            {
                HasUnfinishedTask = false;
            }
        }

        public void Reset()
        {
            // 删除断点文件
            if (!string.IsNullOrWhiteSpace(_outputDir))
            {
                var progFile = ConversionService.GetProgressFilePath(_outputDir);
                if (File.Exists(progFile))
                    try { File.Delete(progFile); } catch { }
            }
            // 重置状态
            _extractedDir = null;
            HasUnfinishedTask = false;
            Step = AppStep.Idle;
            Progress = 0;
            ProgressText = string.Empty;
            StatusText = "已重置，请重新选择 CHM 文件和输出目录，或重新解压。";
            AppendLog("已重置任务，可从头开始。", true);
        }

        public string LogFilePath => Path.Combine(AppContext.BaseDirectory, "conversion.log");

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
            _cts = new CancellationTokenSource();
            IsBusy = true;
            Step = AppStep.Extracting;
            Progress = 0;
            ProgressText = string.Empty;
            _extractedDir = null;
            AppendLog("开始解压...", true);
            try
            {
                var p = new Progress<string>(msg => AppendLog(msg));
                _extractedDir = await _service.ExtractAsync(ChmPath, OutputDir, p, _cts.Token);
                AppendLog("解压完成。", true);
                Step = AppStep.WaitingConfirm;
            }
            catch (OperationCanceledException)
            {
                AppendLog("已取消解压。", true);
                Step = AppStep.Idle;
            }
            catch (Exception ex)
            {
                AppendLog($"解压错误: {ex.Message}", true);
                if (ex.InnerException != null) AppendLog($"详情: {ex.InnerException.Message}", true);
                Step = AppStep.Idle;
            }
            finally { IsBusy = false; _cts = null; }
        }

        public async Task ConvertAsync()
        {
            if (_extractedDir == null) return;
            _cts = new CancellationTokenSource();
            IsBusy = true;
            Step = AppStep.Converting;
            HasUnfinishedTask = false;

            var existing = ConversionService.LoadProgress(_outputDir);
            int startPct = existing != null && !existing.Finished
                ? existing.CompletedFiles.Count * 100 / Math.Max(1,
                    Directory.GetFiles(_extractedDir, "*.htm", SearchOption.AllDirectories).Length +
                    Directory.GetFiles(_extractedDir, "*.html", SearchOption.AllDirectories).Length)
                : 0;
            Progress = startPct;
            ProgressText = startPct > 0 ? $"{startPct}% (继续)" : "0%";
            AppendLog(startPct > 0 ? $"从断点继续转换（已完成约 {startPct}%）..." : "开始转换...", true);

            try
            {
                var mode = IsMultiFile ? ConversionMode.MultiFile : ConversionMode.SingleFile;
                var p  = new Progress<string>(msg => AppendLog(msg));
                var pp = new Progress<int>(pct => { Progress = pct; ProgressText = $"{pct}%"; });
                await _service.ConvertAsync(_extractedDir, OutputDir, mode, p, pp, _cts.Token);
                ProgressText = "100%";
                AppendLog("转换完成！", true);
                HasUnfinishedTask = false;
                Step = AppStep.Done;
            }
            catch (OperationCanceledException)
            {
                AppendLog("已取消转换，进度已保存，下次启动可继续。", true);
                HasUnfinishedTask = true;
                Step = AppStep.WaitingConfirm;
            }
            catch (Exception ex)
            {
                AppendLog($"转换错误: {ex.Message}", true);
                if (ex.InnerException != null) AppendLog($"详情: {ex.InnerException.Message}", true);
                Step = AppStep.WaitingConfirm;
            }
            finally { IsBusy = false; _cts = null; }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
