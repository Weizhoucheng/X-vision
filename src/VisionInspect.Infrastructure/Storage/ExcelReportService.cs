using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using VisionInspect.Core;
using VisionInspect.Core.Abstractions;
using VisionInspect.Core.Configuration;
using VisionInspect.Core.Models;

namespace VisionInspect.Infrastructure.Storage
{
    /// <summary>
    /// 生成真正的 .xlsx（OpenXML）批次报表，不依赖 Office 或第三方组件。
    /// 包含“统计”与“明细”两个工作表，可在 WPS/Excel 直接打开。
    /// </summary>
    public sealed class ExcelReportService : IReportService
    {
        private readonly IDetectionLogRepository _repository;
        private readonly string _defaultDirectory;
        private readonly AppSettings _settings;
        private readonly IAppLogger _logger;

        public ExcelReportService(IDetectionLogRepository repository, string defaultDirectory,
            AppSettings settings, IAppLogger logger)
        {
            if (repository == null) throw new ArgumentNullException("repository");
            _repository = repository;
            _defaultDirectory = defaultDirectory;
            _settings = settings;
            _logger = logger;
            Directory.CreateDirectory(_defaultDirectory);
        }

        public async Task<string> ExportAsync(DateTime fromInclusive, DateTime toInclusive,
            string outputFile, CancellationToken cancellationToken)
        {
            if (toInclusive < fromInclusive)
                throw new ArgumentException("报表结束时间不能早于开始时间。");

            string path = string.IsNullOrWhiteSpace(outputFile)
                ? Path.Combine(_defaultDirectory,
                    string.Format("检测报表_{0:yyyyMMdd_HHmmss}.xlsx", DateTime.Now))
                : outputFile;

            IReadOnlyList<DetectionRecord> records = await Task.Run(
                () => _repository.Query(fromInclusive, toInclusive), cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();

            await Task.Run(() => SimpleXlsxWriter.Write(path, fromInclusive, toInclusive, records, _settings, cancellationToken),
                cancellationToken).ConfigureAwait(false);

            if (_logger != null)
                _logger.Info("Report", string.Format("导出报表成功：{0}，{1} 条记录。", path, records.Count));
            return path;
        }
    }

    /// <summary>极简 OpenXML 写入器，只覆盖本系统的两张表需求。</summary>
    internal static class SimpleXlsxWriter
    {
        private sealed class Cell
        {
            public string Text;
            public double Number;
            public bool IsNumber;
            public int Style;
        }

        public static void Write(string path, DateTime from, DateTime to,
            IReadOnlyList<DetectionRecord> records, AppSettings settings,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            if (records == null) throw new ArgumentNullException("records");
            cancellationToken.ThrowIfCancellationRequested();
            // Excel 每张表最多 1,048,576 行，明细首行为表头，禁止生成无法打开的工作簿。
            if (records.Count > 1048575)
                throw new VisionInspectException("报表明细超过 Excel 上限（1,048,575 条），请缩短报表时间范围后重新导出。");

            string fullPath = Path.GetFullPath(path);
            string directory = Path.GetDirectoryName(fullPath);
            Directory.CreateDirectory(directory);
            string temp = Path.Combine(directory, "." + Path.GetFileName(fullPath) + "." + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                // 同目录临时文件保证最终替换在同一卷内；所有 XML 和 ZIP 尾部写完才发布。
                using (var output = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                using (var zip = new ZipArchive(output, ZipArchiveMode.Create))
                {
                    WriteText(zip, "[Content_Types].xml", ContentTypes());
                    WriteText(zip, "_rels/.rels", RootRelationships());
                    WriteText(zip, "xl/workbook.xml", Workbook());
                    WriteText(zip, "xl/_rels/workbook.xml.rels", WorkbookRelationships());
                    WriteText(zip, "xl/styles.xml", Styles());
                    WriteText(zip, "xl/worksheets/sheet1.xml", BuildSummarySheet(from, to, records, settings ?? new AppSettings(), cancellationToken));
                    WriteDetailSheet(zip, records, cancellationToken);
                }
                cancellationToken.ThrowIfCancellationRequested();
                if (File.Exists(fullPath)) File.Replace(temp, fullPath, null, true);
                else File.Move(temp, fullPath);
            }
            finally
            {
                // 取消、磁盘满、文件被占用等情况均保留旧报表，只清理本次临时文件。
                try { if (File.Exists(temp)) File.Delete(temp); } catch { }
            }
        }

        private static void WriteText(ZipArchive zip, string entryName, string content)
        {
            ZipArchiveEntry entry = zip.CreateEntry(entryName, CompressionLevel.Optimal);
            using (Stream stream = entry.Open())
            using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
            {
                writer.Write(content);
            }
        }

        private static string BuildSummarySheet(DateTime from, DateTime to,
            IReadOnlyList<DetectionRecord> records, AppSettings settings, CancellationToken cancellationToken)
        {
            int ok = 0;
            double duration = 0;
            foreach (DetectionRecord record in records)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (record.IsOk) ok++;
                duration += record.DurationMs;
            }
            int ng = records.Count - ok;
            double yield = records.Count == 0 ? 0 : (double)ok * 100.0 / records.Count;
            double averageMs = records.Count == 0 ? 0 : duration / records.Count;

            string title = string.IsNullOrWhiteSpace(settings.Report.CompanyName)
                ? "VisionInspect 视觉检测批次报表"
                : settings.Report.CompanyName + " · VisionInspect 视觉检测批次报表";
            var rows = new List<List<Cell>>
            {
                new List<Cell> { Text(title, 2) },
                new List<Cell>(),
                new List<Cell> { Text("产线", 3), Text(settings.LineName), Text("工位", 3), Text(settings.StationName) },
                new List<Cell> { Text("统计区间", 3), Text(from.ToString("yyyy-MM-dd HH:mm:ss")), Text("至", 3), Text(to.ToString("yyyy-MM-dd HH:mm:ss")) },
                new List<Cell> { Text("生成时间", 3), Text(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")), Text("记录条数", 3), Number(records.Count) },
                new List<Cell>(),
                new List<Cell> { Text("总检测数", 3), Number(records.Count), Text("OK 数", 3), Number(ok, 4), Text("NG 数", 3), Number(ng, 5) },
                new List<Cell> { Text("合格率", 3), Text(yield.ToString("F2", CultureInfo.InvariantCulture) + "%"),
                    Text("平均耗时(ms)", 3), Number(averageMs, 6), Text("NG 率", 3), Text((records.Count == 0 ? 0 : (double)ng * 100.0 / records.Count).ToString("F2", CultureInfo.InvariantCulture) + "%") },
                new List<Cell>(),
                new List<Cell> { Text("说明：明细数据来自每日检测日志；图片路径为归档原始图片（若启用）。", 7) }
            };

            var widths = new[] { 16, 22, 14, 24, 12, 16 };
            var sb = new StringBuilder();
            sb.Append(Header());
            sb.Append("<sheetViews><sheetView tabSelected=\"1\" workbookViewId=\"0\"/></sheetViews>");
            sb.Append("<cols>");
            for (int i = 0; i < widths.Length; i++)
                sb.AppendFormat("<col min=\"{0}\" max=\"{0}\" width=\"{1}\" customWidth=\"1\"/>", i + 1, widths[i]);
            sb.Append("</cols><sheetData>");

            for (int r = 0; r < rows.Count; r++)
            {
                sb.AppendFormat("<row r=\"{0}\">", r + 1);
                for (int c = 0; c < rows[r].Count; c++)
                {
                    sb.Append(CellXml(r + 1, c + 1, rows[r][c]));
                }
                sb.Append("</row>");
            }
            sb.Append("</sheetData>");
            sb.Append("</worksheet>");
            return sb.ToString();
        }

        private static void WriteDetailSheet(ZipArchive zip, IReadOnlyList<DetectionRecord> records,
            CancellationToken cancellationToken)
        {
            string[] headers =
            {
                "序号", "检测时间", "结果", "方案", "流程", "耗时(ms)", "输入源",
                "图片路径", "判定依据", "输出摘要", "错误信息"
            };

            ZipArchiveEntry entry = zip.CreateEntry("xl/worksheets/sheet2.xml", CompressionLevel.Optimal);
            using (Stream stream = entry.Open())
            using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
            {
            writer.Write(Header());
            writer.Write("<sheetViews><sheetView workbookViewId=\"0\"><pane ySplit=\"1\" topLeftCell=\"A2\" activePane=\"bottomLeft\" state=\"frozen\"/></sheetView></sheetViews>");
            int[] widths = { 10, 22, 8, 26, 18, 12, 22, 48, 16, 60, 32 };
            writer.Write("<cols>");
            for (int i = 0; i < widths.Length; i++)
                writer.Write("<col min=\"{0}\" max=\"{0}\" width=\"{1}\" customWidth=\"1\"/>", i + 1, widths[i]);
            writer.Write("</cols><sheetData>");

            writer.Write("<row r=\"1\">");
            for (int c = 0; c < headers.Length; c++)
                writer.Write(CellXml(1, c + 1, Text(headers[c], 1)));
            writer.Write("</row>");

            int rowIndex = 2;
            foreach (DetectionRecord record in records)
            {
                cancellationToken.ThrowIfCancellationRequested();
                // 逐单元格写 ZIP 流，额外内存只与单个单元格长度有关，不随明细总行数增长。
                writer.Write("<row r=\"{0}\">", rowIndex);
                writer.Write(CellXml(rowIndex, 1, Number(record.Sequence)));
                writer.Write(CellXml(rowIndex, 2, Text(record.Timestamp.ToString("yyyy-MM-dd HH:mm:ss.fff"))));
                writer.Write(CellXml(rowIndex, 3, Text(record.ResultText, record.IsOk ? 4 : 5)));
                writer.Write(CellXml(rowIndex, 4, Text(record.Solution)));
                writer.Write(CellXml(rowIndex, 5, Text(record.Procedure)));
                writer.Write(CellXml(rowIndex, 6, Number(record.DurationMs, 6)));
                writer.Write(CellXml(rowIndex, 7, Text(record.InputSource)));
                writer.Write(CellXml(rowIndex, 8, Text(Truncate(record.ImagePath, 2000))));
                writer.Write(CellXml(rowIndex, 9, Text(record.JudgeText)));
                writer.Write(CellXml(rowIndex, 10, Text(Truncate(record.OutputSummary, 30000))));
                writer.Write(CellXml(rowIndex, 11, Text(Truncate(record.Error, 2000))));
                writer.Write("</row>");
                rowIndex++;
            }
            writer.Write("</sheetData>");
            writer.Write("<autoFilter ref=\"A1:K{0}\"/>", Math.Max(1, rowIndex - 1));
            writer.Write("</worksheet>");
            }
        }

        private static string CellXml(int row, int column, Cell cell)
        {
            if (cell == null) return string.Empty;
            string reference = ColumnName(column) + row.ToString(CultureInfo.InvariantCulture);
            if (cell.IsNumber)
            {
                return string.Format(CultureInfo.InvariantCulture,
                    "<c r=\"{0}\" s=\"{1}\"><v>{2}</v></c>", reference, cell.Style, cell.Number);
            }

            return string.Format("<c r=\"{0}\" s=\"{1}\" t=\"inlineStr\"><is><t xml:space=\"preserve\">{2}</t></is></c>",
                reference, cell.Style, Escape(cell.Text));
        }

        private static Cell Text(string value, int style = 0)
        {
            return new Cell { Text = Truncate(value, 32767), Style = style };
        }

        private static Cell Number(double value, int style = 0)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
                return Text(value.ToString(CultureInfo.InvariantCulture), style);
            return new Cell { Number = value, IsNumber = true, Style = style };
        }

        private static string Truncate(string value, int max)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            if (value.Length <= max) return value;
            if (max > 0 && char.IsHighSurrogate(value[max - 1])) max--;
            return value.Substring(0, max);
        }

        private static string ColumnName(int index)
        {
            var sb = new StringBuilder();
            while (index > 0)
            {
                int mod = (index - 1) % 26;
                sb.Insert(0, (char)('A' + mod));
                index = (index - mod) / 26;
            }
            return sb.ToString();
        }

        private static string Escape(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            var sb = new StringBuilder(value.Length + 16);
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                // 保留合法 Unicode 代理对，过滤孤立代理项及 XML 1.0 禁止字符。
                if (char.IsHighSurrogate(c) && i + 1 < value.Length && char.IsLowSurrogate(value[i + 1]))
                {
                    sb.Append(c).Append(value[++i]);
                    continue;
                }
                if (!XmlConvert.IsXmlChar(c)) continue;
                switch (c)
                {
                    case '&': sb.Append("&amp;"); break;
                    case '<': sb.Append("&lt;"); break;
                    case '>': sb.Append("&gt;"); break;
                    case '"': sb.Append("&quot;"); break;
                    case '\'': sb.Append("&apos;"); break;
                    default: sb.Append(c); break;
                }
            }
            return sb.ToString();
        }

        private static string Header()
        {
            return "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                   "<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">";
        }

        private static string ContentTypes()
        {
            return "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                   "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">" +
                   "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>" +
                   "<Default Extension=\"xml\" ContentType=\"application/xml\"/>" +
                   "<Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/>" +
                   "<Override PartName=\"/xl/worksheets/sheet1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>" +
                   "<Override PartName=\"/xl/worksheets/sheet2.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>" +
                   "<Override PartName=\"/xl/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml\"/>" +
                   "</Types>";
        }

        private static string RootRelationships()
        {
            return "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                   "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
                   "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/>" +
                   "</Relationships>";
        }

        private static string Workbook()
        {
            return "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                   "<workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" " +
                   "xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\">" +
                   "<sheets>" +
                   "<sheet name=\"统计\" sheetId=\"1\" r:id=\"rId1\"/>" +
                   "<sheet name=\"明细\" sheetId=\"2\" r:id=\"rId2\"/>" +
                   "</sheets><calcPr calcId=\"191029\"/></workbook>";
        }

        private static string WorkbookRelationships()
        {
            return "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                   "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
                   "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet1.xml\"/>" +
                   "<Relationship Id=\"rId2\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet2.xml\"/>" +
                   "<Relationship Id=\"rId3\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/>" +
                   "</Relationships>";
        }

        private static string Styles()
        {
            return "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                   "<styleSheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">" +
                   "<fonts count=\"6\">" +
                   "<font><sz val=\"10\"/><name val=\"微软雅黑\"/></font>" +
                   "<font><b/><sz val=\"10\"/><name val=\"微软雅黑\"/></font>" +
                   "<font><b/><sz val=\"10\"/><color rgb=\"FFFFFFFF\"/><name val=\"微软雅黑\"/></font>" +
                   "<font><b/><sz val=\"16\"/><color rgb=\"FF1F4E79\"/><name val=\"微软雅黑\"/></font>" +
                   "<font><b/><sz val=\"10\"/><color rgb=\"FF1F8A4C\"/><name val=\"微软雅黑\"/></font>" +
                   "<font><b/><sz val=\"10\"/><color rgb=\"FFC00000\"/><name val=\"微软雅黑\"/></font>" +
                   "</fonts>" +
                   "<fills count=\"4\">" +
                   "<fill><patternFill patternType=\"none\"/></fill>" +
                   "<fill><patternFill patternType=\"gray125\"/></fill>" +
                   "<fill><patternFill patternType=\"solid\"><fgColor rgb=\"FF1F4E79\"/><bgColor indexed=\"64\"/></patternFill></fill>" +
                   "<fill><patternFill patternType=\"solid\"><fgColor rgb=\"FFE7EEF7\"/><bgColor indexed=\"64\"/></patternFill></fill>" +
                   "</fills>" +
                   "<borders count=\"2\">" +
                   "<border><left/><right/><top/><bottom/><diagonal/></border>" +
                   "<border><left style=\"thin\"><color rgb=\"FFB7C4D4\"/></left><right style=\"thin\"><color rgb=\"FFB7C4D4\"/></right><top style=\"thin\"><color rgb=\"FFB7C4D4\"/></top><bottom style=\"thin\"><color rgb=\"FFB7C4D4\"/></bottom><diagonal/></border>" +
                   "</borders>" +
                   "<cellStyleXfs count=\"1\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\"/></cellStyleXfs>" +
                   "<cellXfs count=\"8\">" +
                   "<xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\"/>" +
                   "<xf numFmtId=\"0\" fontId=\"2\" fillId=\"2\" borderId=\"1\" xfId=\"0\" applyFont=\"1\" applyFill=\"1\" applyBorder=\"1\" applyAlignment=\"1\"><alignment horizontal=\"center\" vertical=\"center\"/></xf>" +
                   "<xf numFmtId=\"0\" fontId=\"3\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyFont=\"1\"/>" +
                   "<xf numFmtId=\"0\" fontId=\"1\" fillId=\"3\" borderId=\"1\" xfId=\"0\" applyFont=\"1\" applyFill=\"1\" applyBorder=\"1\"/>" +
                   "<xf numFmtId=\"0\" fontId=\"4\" fillId=\"0\" borderId=\"1\" xfId=\"0\" applyFont=\"1\" applyBorder=\"1\"/>" +
                   "<xf numFmtId=\"0\" fontId=\"5\" fillId=\"0\" borderId=\"1\" xfId=\"0\" applyFont=\"1\" applyBorder=\"1\"/>" +
                   "<xf numFmtId=\"2\" fontId=\"0\" fillId=\"0\" borderId=\"1\" xfId=\"0\" applyNumberFormat=\"1\" applyBorder=\"1\"/>" +
                   "<xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\"><alignment wrapText=\"1\" vertical=\"top\"/></xf>" +
                   "</cellXfs>" +
                   "<cellStyles count=\"1\"><cellStyle name=\"Normal\" xfId=\"0\" builtinId=\"0\"/></cellStyles>" +
                   "</styleSheet>";
        }
    }
}
