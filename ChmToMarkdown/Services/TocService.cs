using HtmlAgilityPack;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace ChmToMarkdown.Services
{
    public class TocEntry
    {
        public string Name { get; set; } = string.Empty;
        public string? LocalFile { get; set; }   // e.g. "html/abc.htm"
        public int Depth { get; set; }
        public List<TocEntry> Children { get; set; } = new();
    }

    public static class TocService
    {
        /// <summary>
        /// 在 extractedDir 中查找第一个 .hhc 文件并解析，返回顶层条目列表。
        /// </summary>
        public static List<TocEntry> ParseHhc(string extractedDir)
        {
            var hhcFiles = Directory.GetFiles(extractedDir, "*.hhc", SearchOption.TopDirectoryOnly);
            if (hhcFiles.Length == 0) return new();

            var doc = new HtmlDocument();
            doc.Load(hhcFiles[0], Encoding.UTF8);

            // hhc 的顶层 <UL> 在 <BODY> 下
            var body = doc.DocumentNode.SelectSingleNode("//body");
            if (body == null) return new();

            var topUl = body.SelectSingleNode("ul");
            if (topUl == null) return new();

            var result = new List<TocEntry>();
            ParseUl(topUl, result, 0);
            return result;
        }

        private static void ParseUl(HtmlNode ul, List<TocEntry> list, int depth)
        {
            foreach (var li in ul.ChildNodes)
            {
                if (!li.Name.Equals("li", StringComparison.OrdinalIgnoreCase)) continue;

                // 每个 <LI> 里有一个 <OBJECT type="text/sitemap">，含多个 <param>
                var obj = li.SelectSingleNode("object");
                if (obj == null) continue;

                string name = string.Empty;
                string? local = null;

                foreach (var param in obj.SelectNodes("param") ?? new HtmlNodeCollection(obj))
                {
                    string pname = param.GetAttributeValue("name", "");
                    string pval  = param.GetAttributeValue("value", "");
                    if (pname.Equals("Name",  StringComparison.OrdinalIgnoreCase)) name  = pval;
                    if (pname.Equals("Local", StringComparison.OrdinalIgnoreCase)) local = pval;
                }

                var entry = new TocEntry { Name = name, LocalFile = local, Depth = depth };
                list.Add(entry);

                // 同级 <UL> 是子节点（在父 <LI> 之后，或在后续兄弟 <LI> 中包含的 <UL>）
                // hhc 中子 <UL> 作为 <LI> 的兄弟出现，紧随其后
            }

            // 子 <UL> 直接是 ul 的子节点，与 <LI> 平级
            // 需要按顺序处理：LI → 下一个 UL 是上一个 LI 的子节点
            // 重新处理：顺序扫描子节点
            list.Clear();
            ParseUlOrdered(ul, list, depth);
        }

        private static void ParseUlOrdered(HtmlNode ul, List<TocEntry> list, int depth)
        {
            TocEntry? lastEntry = null;
            foreach (var child in ul.ChildNodes)
            {
                if (child.Name.Equals("li", StringComparison.OrdinalIgnoreCase))
                {
                    var obj = child.SelectSingleNode("object");
                    if (obj == null) continue;

                    string name = string.Empty;
                    string? local = null;
                    foreach (var param in obj.SelectNodes("param") ?? new HtmlNodeCollection(obj))
                    {
                        string pname = param.GetAttributeValue("name", "");
                        string pval  = param.GetAttributeValue("value", "");
                        if (pname.Equals("Name",  StringComparison.OrdinalIgnoreCase)) name  = pval;
                        if (pname.Equals("Local", StringComparison.OrdinalIgnoreCase)) local = pval;
                    }

                    lastEntry = new TocEntry { Name = name, LocalFile = local, Depth = depth };
                    list.Add(lastEntry);
                }
                else if (child.Name.Equals("ul", StringComparison.OrdinalIgnoreCase))
                {
                    // 这个 UL 是上一个 LI 条目的子节点
                    var target = lastEntry ?? (list.Count > 0 ? list[list.Count - 1] : null);
                    if (target != null)
                        ParseUlOrdered(child, target.Children, depth + 1);
                }
            }
        }

        /// <summary>
        /// 根据 TOC 条目列表生成 index.md，输出到 mdDir。
        /// mdFileMap: htm文件名（不含路径，小写）→ 实际 md 文件名（不含路径）
        /// </summary>
        public static void GenerateIndex(List<TocEntry> toc, string mdDir,
            Dictionary<string, string> mdFileMap)
        {
            var sb = new StringBuilder();
            sb.AppendLine("# 目录索引");
            sb.AppendLine();
            sb.AppendLine("> 由 ChmToMarkdown 自动生成，共 " + CountAll(toc) + " 个条目");
            sb.AppendLine();

            WriteEntries(toc, sb, mdFileMap);

            File.WriteAllText(Path.Combine(mdDir, "index.md"), sb.ToString(), Encoding.UTF8);
        }

        private static void WriteEntries(List<TocEntry> entries, StringBuilder sb,
            Dictionary<string, string> mdFileMap)
        {
            foreach (var e in entries)
            {
                string indent = new string(' ', e.Depth * 2);
                string link = BuildLink(e.LocalFile, mdFileMap);

                if (string.IsNullOrEmpty(link))
                    sb.AppendLine($"{indent}- {e.Name}");
                else
                    sb.AppendLine($"{indent}- [{e.Name}]({link})");

                if (e.Children.Count > 0)
                    WriteEntries(e.Children, sb, mdFileMap);
            }
        }

        private static string BuildLink(string? localFile, Dictionary<string, string> mdFileMap)
        {
            if (string.IsNullOrEmpty(localFile)) return string.Empty;
            // localFile 形如 "html/abc-def.htm"，取文件名小写作为 key
            string key = Path.GetFileName(localFile).ToLowerInvariant();
            return mdFileMap.TryGetValue(key, out var md) ? md : string.Empty;
        }

        private static int CountAll(List<TocEntry> list)
        {
            int n = list.Count;
            foreach (var e in list) n += CountAll(e.Children);
            return n;
        }
    }
}
