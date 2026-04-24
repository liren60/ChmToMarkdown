using ChmToMarkdown.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ChmToMarkdown.Services
{
    public enum ConversionMode { MultiFile, SingleFile }

    public class ConversionService
    {
        private readonly HtmlToMarkdownConverter _converter = new();

        /// <summary>
        /// 调用 hh.exe 解压 CHM 文件到 outputDir\extracted，返回解压目录路径。
        /// </summary>
        public Task<string> ExtractAsync(string chmPath, string outputDir, IProgress<string> progress)
        {
            return Task.Run(() =>
            {
                string extractedDir = Path.Combine(outputDir, "extracted");
                Directory.CreateDirectory(extractedDir);

                progress.Report($"正在解压 {Path.GetFileName(chmPath)} ...");

                // 使用 hh.exe -decompile 解压
                var psi = new ProcessStartInfo
                {
                    FileName = "hh.exe",
                    Arguments = $"-decompile \"{extractedDir}\" \"{chmPath}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                using var proc = Process.Start(psi)
                    ?? throw new InvalidOperationException("无法启动 hh.exe");
                proc.WaitForExit(60_000);

                int fileCount = Directory.GetFiles(extractedDir, "*", SearchOption.AllDirectories).Length;
                progress.Report($"解压完成，共 {fileCount} 个文件。");

                return extractedDir;
            });
        }

        /// <summary>
        /// 将 extractedDir 中的 HTML 文件转换为 Markdown，输出到 outputDir。
        /// </summary>
        public Task ConvertAsync(
            string extractedDir,
            string outputDir,
            ConversionMode mode,
            IProgress<string> progress,
            IProgress<int> progressPct)
        {
            return Task.Run(() =>
            {
                string imagesDir = Path.Combine(outputDir, "images");
                Directory.CreateDirectory(imagesDir);

                var htmlFiles = Directory.GetFiles(extractedDir, "*.htm", SearchOption.AllDirectories)
                    .Concat(Directory.GetFiles(extractedDir, "*.html", SearchOption.AllDirectories))
                    .OrderBy(f => f)
                    .ToList();

                if (htmlFiles.Count == 0)
                {
                    progress.Report("未找到 HTML 文件。");
                    return;
                }

                if (mode == ConversionMode.MultiFile)
                {
                    for (int i = 0; i < htmlFiles.Count; i++)
                    {
                        string htmlFile = htmlFiles[i];
                        string relPath = Path.GetRelativePath(extractedDir, htmlFile);
                        string mdRelPath = Path.ChangeExtension(relPath, ".md");
                        string mdOutPath = Path.Combine(outputDir, mdRelPath);

                        Directory.CreateDirectory(Path.GetDirectoryName(mdOutPath)!);

                        string imageRelativePrefix = Path.GetRelativePath(
                            Path.GetDirectoryName(mdOutPath)!, imagesDir).Replace('\\', '/');

                        progress.Report($"转换 ({i + 1}/{htmlFiles.Count}): {relPath}");
                        string md = _converter.Convert(htmlFile, extractedDir, imagesDir, imageRelativePrefix);
                        File.WriteAllText(mdOutPath, md, Encoding.UTF8);

                        progressPct.Report((i + 1) * 100 / htmlFiles.Count);
                    }
                    progress.Report($"转换完成，共 {htmlFiles.Count} 个文件。");
                }
                else
                {
                    // 单文件模式：合并所有 HTML 为一个 Markdown
                    string mdOutPath = Path.Combine(outputDir, "output.md");
                    var sb = new StringBuilder();

                    for (int i = 0; i < htmlFiles.Count; i++)
                    {
                        string htmlFile = htmlFiles[i];
                        string relPath = Path.GetRelativePath(extractedDir, htmlFile);
                        string imageRelativePrefix = "images";

                        progress.Report($"转换 ({i + 1}/{htmlFiles.Count}): {relPath}");
                        string md = _converter.Convert(htmlFile, extractedDir, imagesDir, imageRelativePrefix);
                        sb.AppendLine(md);
                        sb.AppendLine();

                        progressPct.Report((i + 1) * 100 / htmlFiles.Count);
                    }

                    File.WriteAllText(mdOutPath, sb.ToString(), Encoding.UTF8);
                    progress.Report($"转换完成，输出：{mdOutPath}");
                }
            });
        }
    }
}
