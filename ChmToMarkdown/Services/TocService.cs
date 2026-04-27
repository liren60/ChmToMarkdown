using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace ChmToMarkdown.Services
{
    public class TocEntry
    {
        public string Name { get; set; } = string.Empty;
        public string? LocalFile { get; set; }
        public int Depth { get; set; }
        public List<TocEntry> Children { get; set; } = new();
    }

    public static class TocService
    {
        // 匹配 <param name="Name" value="..."> 和 <param name="Local" value="...">
        private static readonly Regex RxName  = new(@"<param\s+name=""Name""\s+value=""([^""]*)""\s*/?>",  RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex RxLocal = new(@"<param\s+name=""Local""\s+value=""([^""]*)""\s*/?>", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// 在 extractedDir 中查找第一个 .hhc 文件并解析，返回扁平条目列表（含 Depth）。
        /// 使用逐行扫描，不受嵌套深度和文件大小限制。
        /// </summary>
        public static List<TocEntry> ParseHhc(string extractedDir)
        {
            var hhcFiles = Directory.GetFiles(extractedDir, "*.hhc", SearchOption.TopDirectoryOnly);
            if (hhcFiles.Length == 0) return new();

            var result = new List<TocEntry>();
            int depth = 0;           // 当前 <UL> 嵌套深度
            bool inObject = false;
            string curName  = string.Empty;
            string? curLocal = null;

            // 用 StreamReader 逐行读，避免一次性加载 7MB 到内存
            using var sr = new StreamReader(hhcFiles[0], Encoding.UTF8);
            string? line;
            while ((line = sr.ReadLine()) != null)
            {
                var trimmed = line.Trim();

                // 进入 <OBJECT type="text/sitemap">
                if (trimmed.IndexOf("<object", StringComparison.OrdinalIgnoreCase) >= 0
                    && trimmed.IndexOf("text/sitemap", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    inObject = true;
                    curName  = string.Empty;
                    curLocal = null;
                    continue;
                }

                // 离开 </OBJECT>
                if (inObject && trimmed.IndexOf("</object>", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    inObject = false;
                    if (!string.IsNullOrEmpty(curName))
                    {
                        result.Add(new TocEntry
                        {
                            Name      = curName,
                            LocalFile = curLocal,
                            Depth     = Math.Max(0, depth - 1)  // UL深度减1对应缩进
                        });
                    }
                    continue;
                }

                // 在 object 内解析 param
                if (inObject)
                {
                    var mName  = RxName.Match(trimmed);
                    var mLocal = RxLocal.Match(trimmed);
                    if (mName.Success)  curName  = mName.Groups[1].Value;
                    if (mLocal.Success) curLocal = mLocal.Groups[1].Value;
                    continue;
                }

                // 统计 <UL> / </UL> 深度
                int ulOpen  = CountTag(trimmed, "<ul");
                int ulClose = CountTag(trimmed, "</ul>");
                depth += ulOpen - ulClose;
            }

            return result;
        }

        private static int CountTag(string line, string tag)
        {
            int count = 0, idx = 0;
            while ((idx = line.IndexOf(tag, idx, StringComparison.OrdinalIgnoreCase)) >= 0)
            {
                count++;
                idx += tag.Length;
            }
            return count;
        }

        /// <summary>
        /// 生成 index.md，写入 mdDir 根目录。
        /// mdFileMap: htm文件名(小写) → "pages/xxx.md"
        /// </summary>
        public static void GenerateIndex(List<TocEntry> toc, string mdDir,
            Dictionary<string, string> mdFileMap)
        {
            // 用 StreamWriter 直接写，避免 StringBuilder 占用大量内存
            using var sw = new StreamWriter(Path.Combine(mdDir, "index.md"), false, Encoding.UTF8);
            sw.WriteLine("# 目录索引");
            sw.WriteLine();
            sw.WriteLine($"> 由 ChmToMarkdown 自动生成，共 {toc.Count} 个条目");
            sw.WriteLine();

            foreach (var e in toc)
            {
                string indent = new string(' ', e.Depth * 2);
                string link   = BuildLink(e.LocalFile, mdFileMap);

                if (string.IsNullOrEmpty(link))
                    sw.WriteLine($"{indent}- {e.Name}");
                else
                    sw.WriteLine($"{indent}- [{e.Name}]({link})");
            }
        }

        private static string BuildLink(string? localFile, Dictionary<string, string> mdFileMap)
        {
            if (string.IsNullOrEmpty(localFile)) return string.Empty;
            string key = Path.GetFileName(localFile).ToLowerInvariant();
            return mdFileMap.TryGetValue(key, out var md) ? md : string.Empty;
        }
    }
}
