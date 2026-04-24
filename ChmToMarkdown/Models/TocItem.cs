using System.Collections.Generic;

namespace ChmToMarkdown.Models
{
    public class TocItem
    {
        public string Title { get; set; } = string.Empty;
        public string? LocalPath { get; set; }   // CHM 内部路径，如 /html/topic1.htm
        public int Level { get; set; }
        public List<TocItem> Children { get; set; } = new();
        public string? OutputMdPath { get; set; } // 输出后的 MD 相对路径
    }
}
