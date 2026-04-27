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
                    Arguments = $"x \"{chmPath}\" -o\"{extractedDir}\" -y",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                using var proc = Process.Start(psi)
                    ?? throw new InvalidOperationException("无法启动 7z.exe");
                using var reg = ct.Register(() => { try { proc.Kill(); } catch { } });

                proc.WaitForExit(120_000);
                ct.ThrowIfCancellationRequested();

                int fileCount = Directory.GetFiles(extractedDir, "*", SearchOption.AllDirectories).Length;
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

                string imagesDir = Path.Combine(outputDir, "images");
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
                        string relPath = Path.GetRelativePath(extractedDir, htmlFile);

                        if (completedSet.Contains(relPath))
                        {
                            progressPct.Report((i + 1) * 100 / total);
                            continue;
                        }

                        string mdRelPath = Path.ChangeExtension(relPath, ".md");
                        string mdOutPath = Path.Combine(outputDir, mdRelPath);
                        Directory.CreateDirectory(Path.GetDirectoryName(mdOutPath)!);

                        string imageRelativePrefix = Path.GetRelativePath(
                            Path.GetDirectoryName(mdOutPath)!, imagesDir).Replace('\\', '/');

                        progress.Report($"转换 ({i + 1}/{total}): {relPath}");
                        string md = _converter.Convert(htmlFile, extractedDir, imagesDir, imageRelativePrefix);
                        File.WriteAllText(mdOutPath, md, Encoding.UTF8);

                        prog.CompletedFiles.Add(relPath);
                        SaveProgress(prog, outputDir);
                        progressPct.Report((i + 1) * 100 / total);
                    }
                }
                else
                {
                    string mdOutPath = Path.Combine(outputDir, "output.md");

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

                        progress.Report($"转换 ({i + 1}/{total}): {relPath}");
                        string md = _converter.Convert(htmlFile, extractedDir, imagesDir, "images");
                        File.AppendAllText(mdOutPath, md + "\n\n", Encoding.UTF8);

                        prog.CompletedFiles.Add(relPath);
                        SaveProgress(prog, outputDir);
                        progressPct.Report((i + 1) * 100 / total);
                    }
                }

                prog.Finished = true;
                DeleteProgress(outputDir);
                progress.Report($"转换完成！共 {total} 个文件。");

            }, ct);
        }
    }
}
