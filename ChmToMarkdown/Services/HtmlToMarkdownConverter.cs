using HtmlAgilityPack;
using ReverseMarkdown;
using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace ChmToMarkdown.Services
{
    /// <summary>
    /// 将 HTML 文件转换为 AI 友好的 Markdown（UTF-8 输出，图片复制到本地）
    /// </summary>
    public class HtmlToMarkdownConverter
    {
        private static readonly Converter _converter = new(new Config
        {
            UnknownTags = Config.UnknownTagsOption.PassThrough,
            GithubFlavored = true,
            RemoveComments = true,
            SmartHrefHandling = true
        });

        /// <summary>
        /// 转换单个 HTML 文件为 Markdown 字符串，同时将图片复制到 outputImagesDir
        /// </summary>
        public string Convert(string htmlPath, string extractedDir, string outputImagesDir, string imageRelativePrefix)
        {
            string html = ReadWithFallbackEncoding(htmlPath);

            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            // 处理图片：复制到输出目录，替换 src 为相对路径
            var imgNodes = doc.DocumentNode.SelectNodes("//img");
            if (imgNodes != null)
            {
                foreach (var img in imgNodes)
                {
                    string src = img.GetAttributeValue("src", "");
                    if (string.IsNullOrEmpty(src) || src.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                        continue;

                    string imgAbsPath = ResolveImagePath(htmlPath, extractedDir, src);
                    if (!File.Exists(imgAbsPath)) continue;

                    string imgFileName = Path.GetFileName(imgAbsPath);
                    string destPath = Path.Combine(outputImagesDir, imgFileName);
                    Directory.CreateDirectory(outputImagesDir);

                    if (!File.Exists(destPath))
                        File.Copy(imgAbsPath, destPath);

                    img.SetAttributeValue("src", $"{imageRelativePrefix}/{imgFileName}");
                }
            }

            // 移除 head/script/style/导航噪音
            RemoveNodes(doc, "//head");
            RemoveNodes(doc, "//script");
            RemoveNodes(doc, "//style");
            RemoveNodes(doc, "//*[@class='pageHeader']");
            RemoveNodes(doc, "//*[@id='pageFooter']");
            RemoveNodes(doc, "//*[@class='pageFooter']");

            // 优先取主体内容区，去掉页眉页脚
            var contentNode =
                doc.DocumentNode.SelectSingleNode("//*[@id='TopicContent']") ??
                doc.DocumentNode.SelectSingleNode("//*[@class='topicContent']") ??
                doc.DocumentNode.SelectSingleNode("//div[@class='topic']") ??
                doc.DocumentNode.SelectSingleNode("//body") ??
                doc.DocumentNode;

            string cleanHtml = contentNode.InnerHtml;

            string md = _converter.Convert(cleanHtml);

            // 压缩多余空行
            md = Regex.Replace(md, @"\n{3,}", "\n\n");
            return md.Trim();
        }

        private static string ResolveImagePath(string htmlPath, string extractedDir, string src)
        {
            string? htmlDir = Path.GetDirectoryName(htmlPath);
            if (htmlDir != null)
            {
                string relative = Path.GetFullPath(Path.Combine(htmlDir, src));
                if (File.Exists(relative)) return relative;
            }
            return Path.GetFullPath(Path.Combine(extractedDir, src.TrimStart('/')));
        }

        private static void RemoveNodes(HtmlDocument doc, string xpath)
        {
            var nodes = doc.DocumentNode.SelectNodes(xpath);
            if (nodes == null) return;
            foreach (var n in nodes) n.Remove();
        }

        private static string ReadWithFallbackEncoding(string path)
        {
            byte[] bytes = File.ReadAllBytes(path);

            // BOM 检测
            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
                return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);

            // 从 <meta charset> 检测
            string tentative = Encoding.UTF8.GetString(bytes);
            var match = Regex.Match(tentative, @"charset\s*=\s*[""']?([\w-]+)", RegexOptions.IgnoreCase);
            if (match.Success)
            {
                try
                {
                    var enc = Encoding.GetEncoding(match.Groups[1].Value);
                    return enc.GetString(bytes);
                }
                catch { }
            }

            // 默认 GB2312
            try { return Encoding.GetEncoding("GB2312").GetString(bytes); }
            catch { return tentative; }
        }
    }
}
