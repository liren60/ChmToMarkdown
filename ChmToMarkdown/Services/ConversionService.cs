using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace ChmToMarkdown.Services
{
    public enum ConversionMode { MultiFile, SingleFile }

    public class ConversionProgress
    {
        public string ExtractedDir { get; set; } = string.Empty;
        public string OutputDir { get; set; } = string.Empty;
        public ConversionMode Mode { get; set; }
        public List<string> CompletedFiles { get; set; } = new();
        public bool Finished { get; set; }
    }

    public class ConversionService
    {
        private readonly HtmlToMarkdownConverter _converter = new();

        private static string SevenZipPath =>
            Path.Combine(AppContext.BaseDirectory, "Tools", "7z.exe");

        public static string GetProgressFilePath(string outputDir) =>
            Path.Combine(outputDir, ".conversion_progress.json");

        public static ConversionProgress? LoadProgress(string outputDir)
        {
            string path = GetProgressFilePath(outputDir);
            if (!File.Exists(path)) return null;
            try { return JsonSerializer.Deserialize<ConversionProgress>(File.ReadAllText(path)); }
            catch { return null; }
        }

        private static void SaveProgress(ConversionProgress prog, string outputDir)
        {
            try
            {
                File.WriteAllText(GetProgressFilePath(outputDir),
                    JsonSerializer.Serialize(prog, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch { }
        }

        private static void DeleteProgress(string outputDir)
        {
            try { File.Delete(GetProgressFilePath(outputDir)); } catch { }
        }

        public Task<string> ExtractAsync(string chmPath, string outputDir,
            IProgress<string> progress, CancellationToken ct = default)
        {
            return ExtractAsync(chmPath, outputDir, progress, null, ct);
        }

        public Task<string> ExtractAsync(string chmPath, string outputDir,
            IProgress<string> progress, IProgress<int>? progressPct, CancellationToken ct = default)
        {
            return Task.Run(() =>
            {
                ct.ThrowIfCancellationRequested();

                string extractedDir = Path.Combine(outputDir, "extracted");
                Directory.CreateDirectory(extractedDir);
                progress.Report($"正在解压 {Path.GetFileName(chmPath)} ...");

                string sevenZip = SevenZipPath;
                if (!File.Exists(sevenZip))
                    throw new FileNotFoundException($"未找到内置 7z.exe，路径：{sevenZip}");

                var psi = new ProcessStartInfo
                {
                    FileName = sevenZip,
                    Arguments = $"x \"{chmPath}\" -o\"{extractedDir}\" -y -bsp1 -mmt",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                using var proc = Process.Start(psi)
                    ?? throw new InvalidOperationException("无法启动 7z.exe");
                using var reg = ct.Register(() => { try { proc.Kill(); } catch { } });

                // 实时读取输出解析进度百分比，每5%记一次日志
                string? line;
                int lastReportedPct = -1;
                while ((line = proc.StandardOutput.ReadLine()) != null)
                {
                    ct.ThrowIfCancellationRequested();
                    var trimmed = line.Trim();
                    int pctIdx = trimmed.IndexOf('%');
                    if (pctIdx > 0 && pctIdx < 5 && int.TryParse(trimmed.Substring(0, pctIdx).Trim(), out int pct))
                    {
                        progressPct?.Report(pct);
                        // 每5%才写一次日志，避免频繁UI刷新和磁盘写入
                        if (pct / 5 > lastReportedPct / 5)
                        {
                            lastReportedPct = pct;
                            progress.Report($"解压中 {pct}%...");
                        }
                    }
                }

                proc.WaitForExit(120_000);
                ct.ThrowIfCancellationRequested();

                int fileCount = Directory.GetFiles(extractedDir, "*", SearchOption.AllDirectories).Length;
                progressPct?.Report(100);
                progress.Report($"解压完成，共 {fileCount} 个文件。");

                return extractedDir;
            }, ct);
        }

        public Task ConvertAsync(
            string extractedDir,
            string outputDir,
            ConversionMode mode,
            IProgress<string> progress,
            IProgress<int> progressPct,
            CancellationToken ct = default)
        {
            return Task.Run(() =>
            {
                ct.ThrowIfCancellationRequested();

                string mdDir    = Path.Combine(outputDir, "MD");
                string imagesDir = Path.Combine(mdDir, "images");
                Directory.CreateDirectory(mdDir);
                Directory.CreateDirectory(imagesDir);

                var allHtmlFiles = Directory.GetFiles(extractedDir, "*.htm", SearchOption.AllDirectories)
                    .Concat(Directory.GetFiles(extractedDir, "*.html", SearchOption.AllDirectories))
                    .OrderBy(f => f)
                    .ToList();

                if (allHtmlFiles.Count == 0)
                {
                    progress.Report("未找到 HTML 文件。");
                    return;
                }

                var prog = LoadProgress(outputDir) ?? new ConversionProgress
                {
                    ExtractedDir = extractedDir,
                    OutputDir = outputDir,
                    Mode = mode,
                };

                var completedSet = new HashSet<string>(prog.CompletedFiles, StringComparer.OrdinalIgnoreCase);
                int total = allHtmlFiles.Count;

                if (completedSet.Count > 0)
                    progress.Report($"从断点继续，已完成 {completedSet.Count}/{total} 个文件。");

                if (mode == ConversionMode.MultiFile)
                {
                    for (int i = 0; i < allHtmlFiles.Count; i++)
                    {
                        ct.ThrowIfCancellationRequested();

                        string htmlFile = allHtmlFiles[i];
                        // 用相对路径作为断点key，但输出时只取文件名，直接放到outputDir根目录
                        string relPath = Path.GetRelativePath(extractedDir, htmlFile);

                        if (completedSet.Contains(relPath))
                        {
                            progressPct.Report((i + 1) * 100 / total);
                            continue;
                        }

                        // md 文件放 outputDir\MD\ 根目录
                        string mdOutPath = Path.Combine(mdDir,
                            Path.ChangeExtension(Path.GetFileName(htmlFile), ".md"));

                        // images 文件夹和 md 文件同级，相对路径直接是 "images"
                        string md = _converter.Convert(htmlFile, extractedDir, imagesDir, "images");
                        File.WriteAllText(mdOutPath, md, Encoding.UTF8);

                        prog.CompletedFiles.Add(relPath);
                        if (prog.CompletedFiles.Count % 20 == 0)
                            SaveProgress(prog, outputDir);

                        int pct = (i + 1) * 100 / total;
                        progressPct.Report(pct);
                        if ((i + 1) % 10 == 0 || i + 1 == total)
                            progress.Report($"转换进度 {pct}%，已完成 {i + 1}/{total} 个文件");
                    }
                    SaveProgress(prog, outputDir);
                }
                else
                {
                    string mdOutPath = Path.Combine(mdDir, "output.md");
                    // 单文件模式用 StreamWriter 一次打开，避免每次 AppendAllText 的 open/close 开销
                    bool isAppend = completedSet.Count > 0;
                    using var sw = new StreamWriter(mdOutPath, isAppend, Encoding.UTF8);

                    for (int i = 0; i < allHtmlFiles.Count; i++)
                    {
                        ct.ThrowIfCancellationRequested();

                        string htmlFile = allHtmlFiles[i];
                        string relPath = Path.GetRelativePath(extractedDir, htmlFile);

                        if (completedSet.Contains(relPath))
                        {
                            progressPct.Report((i + 1) * 100 / total);
                            continue;
                        }

                        string md = _converter.Convert(htmlFile, extractedDir, imagesDir, "images");
                        sw.Write(md);
                        sw.WriteLine();
                        sw.WriteLine();

                        prog.CompletedFiles.Add(relPath);
                        if (prog.CompletedFiles.Count % 20 == 0)
                            SaveProgress(prog, outputDir);

                        int pct = (i + 1) * 100 / total;
                        progressPct.Report(pct);
                        if ((i + 1) % 10 == 0 || i + 1 == total)
                            progress.Report($"转换进度 {pct}%，已完成 {i + 1}/{total} 个文件");
                    }
                    SaveProgress(prog, outputDir);
                }

                prog.Finished = true;
                DeleteProgress(outputDir);
                progress.Report($"转换完成！共 {total} 个文件。");

                // 生成目录索引 index.md
                progress.Report("正在生成目录索引 index.md ...");
                try
                {
                    var toc = TocService.ParseHhc(extractedDir);
                    if (toc.Count > 0)
                    {
                        // 建立 htm文件名(小写) → md文件名 的映射
                        var mdFileMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                        foreach (var f in Directory.GetFiles(mdDir, "*.md", SearchOption.TopDirectoryOnly))
                        {
                            string mdName  = Path.GetFileName(f);
                            string htmKey  = Path.ChangeExtension(mdName, ".htm").ToLowerInvariant();
                            mdFileMap[htmKey] = mdName;
                        }
                        TocService.GenerateIndex(toc, mdDir, mdFileMap);
                        progress.Report("index.md 生成完成。");
                    }
                    else
                    {
                        progress.Report("未找到 .hhc 目录文件，跳过 index.md 生成。");
                    }
                }
                catch (Exception ex)
                {
                    progress.Report($"生成 index.md 失败: {ex.Message}");
                }

            }, ct);
        }
    }
}
