using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Runtime.InteropServices;
using System.Xml.Linq;

namespace HitEducation.App;

public static class RandomPickerImporter
{
	private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
	{
		".json", ".csv", ".tsv", ".txt", ".xlsx", ".ppt", ".pptx"
	};

	public static bool IsSupported(string path) => SupportedExtensions.Contains(Path.GetExtension(path));

	public static List<RandomPickerMember> Import(string path, string thumbnailDirectory)
	{
		if (!IsSupported(path))
		{
			throw new InvalidOperationException("不支持的名单文件格式。");
		}

		string extension = Path.GetExtension(path).ToLowerInvariant();
		bool isPowerPoint = extension is ".ppt" or ".pptx";
		List<RandomPickerMember> members = extension switch
		{
			".json" => ImportJson(path),
			".xlsx" => ImportXlsx(path),
			".ppt" or ".pptx" => ImportPowerPoint(path, thumbnailDirectory),
			_ => ImportTextTable(path)
		};

		List<RandomPickerMember> distinctMembers = members
			.Where(x => !string.IsNullOrWhiteSpace(x.Name))
			.GroupBy(x => x.Name.Trim(), StringComparer.CurrentCultureIgnoreCase)
			.Select(x => new RandomPickerMember { Name = x.Key, Weight = ClampWeight(x.First().Weight), ThumbnailPath = x.First().ThumbnailPath })
			.ToList();

		return isPowerPoint
			? distinctMembers
			: distinctMembers.OrderBy(x => x.Name, ChineseNameComparer.Instance).ToList();
	}

	private sealed class ChineseNameComparer : IComparer<string>
	{
		public static readonly ChineseNameComparer Instance = new();

		public int Compare(string? x, string? y)
		{
			return CultureInfo.GetCultureInfo("zh-CN").CompareInfo.Compare(x ?? string.Empty, y ?? string.Empty, CompareOptions.StringSort | CompareOptions.IgnoreCase);
		}
	}

	private static List<RandomPickerMember> ImportJson(string path)
	{
		string json = File.ReadAllText(path);
		using JsonDocument document = JsonDocument.Parse(json);
		JsonElement root = document.RootElement;
		if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("members", out JsonElement membersElement))
		{
			root = membersElement;
		}

		if (root.ValueKind != JsonValueKind.Array)
		{
			throw new InvalidOperationException("JSON 必须是名单数组，或包含 members 数组。");
		}

		var result = new List<RandomPickerMember>();
		foreach (JsonElement item in root.EnumerateArray())
		{
			if (item.ValueKind == JsonValueKind.String)
			{
				AddMember(result, item.GetString(), 1.0);
			}
			else if (item.ValueKind == JsonValueKind.Object)
			{
				string? name = TryGetString(item, "name") ?? TryGetString(item, "姓名") ?? TryGetString(item, "member");
				double weight = TryGetDouble(item, "weight") ?? TryGetDouble(item, "权重") ?? 1.0;
				AddMember(result, name, weight);
			}
		}

		return result;
	}

	private static List<RandomPickerMember> ImportTextTable(string path)
	{
		var result = new List<RandomPickerMember>();
		foreach (string line in File.ReadLines(path, Encoding.UTF8))
		{
			string trimmed = line.Trim();
			if (string.IsNullOrWhiteSpace(trimmed))
			{
				continue;
			}

			string[] parts = SplitTableLine(trimmed);
			if (parts.Length == 0 || IsHeader(parts[0]))
			{
				continue;
			}

			double weight = parts.Length > 1 && double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed) ? parsed : 1.0;
			AddMember(result, parts[0], weight);
		}

		return result;
	}

	private static List<RandomPickerMember> ImportXlsx(string path)
	{
		using ZipArchive archive = ZipFile.OpenRead(path);
		Dictionary<int, string> sharedStrings = ReadSharedStrings(archive);
		ZipArchiveEntry? sheet = archive.GetEntry("xl/worksheets/sheet1.xml")
			?? archive.Entries.FirstOrDefault(x => x.FullName.StartsWith("xl/worksheets/", StringComparison.OrdinalIgnoreCase) && x.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase));
		if (sheet is null)
		{
			throw new InvalidOperationException("未找到表格工作表。");
		}

		XDocument document;
		using (Stream stream = sheet.Open())
		{
			document = XDocument.Load(stream);
		}

		XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
		var result = new List<RandomPickerMember>();
		foreach (XElement row in document.Descendants(ns + "row"))
		{
			List<string> values = row.Elements(ns + "c").Select(cell => ReadCell(cell, ns, sharedStrings)).ToList();
			if (values.Count == 0 || values.All(string.IsNullOrWhiteSpace) || IsHeader(values[0]))
			{
				continue;
			}

			double weight = values.Count > 1 && double.TryParse(values[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed) ? parsed : 1.0;
			AddMember(result, values[0], weight);
		}

		return result;
	}

	private static Dictionary<int, string> ReadSharedStrings(ZipArchive archive)
	{
		var result = new Dictionary<int, string>();
		ZipArchiveEntry? entry = archive.GetEntry("xl/sharedStrings.xml");
		if (entry is null)
		{
			return result;
		}

		XDocument document;
		using (Stream stream = entry.Open())
		{
			document = XDocument.Load(stream);
		}

		XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
		int index = 0;
		foreach (XElement item in document.Descendants(ns + "si"))
		{
			result[index++] = string.Concat(item.Descendants(ns + "t").Select(x => x.Value));
		}

		return result;
	}

	private static List<RandomPickerMember> ImportPowerPoint(string path, string thumbnailDirectory)
	{
		Type powerpointType = Type.GetTypeFromProgID("PowerPoint.Application")
			?? throw new InvalidOperationException("未检测到 Microsoft PowerPoint，无法导入 PPT/PPTX 缩略图。");
		Directory.CreateDirectory(thumbnailDirectory);
		foreach (string oldFile in Directory.GetFiles(thumbnailDirectory, "slide-*.png"))
		{
			File.Delete(oldFile);
		}

		dynamic? app = null;
		dynamic? presentation = null;
		try
		{
			app = Activator.CreateInstance(powerpointType);
			presentation = app.Presentations.Open(path, -1, 0, 0);
			var result = new List<RandomPickerMember>();
			int count = presentation.Slides.Count;
			for (int index = 1; index <= count; index++)
			{
				string imagePath = Path.Combine(thumbnailDirectory, $"slide-{index:000}.png");
				presentation.Slides[index].Export(imagePath, "PNG", 1280, 720);
				result.Add(new RandomPickerMember
				{
					Name = $"第 {index} 页",
					Weight = 1.0,
					ThumbnailPath = imagePath
				});
			}

			return result;
		}
		finally
		{
			try
			{
				presentation?.Close();
				app?.Quit();
			}
			catch
			{
			}

			if (presentation is not null) Marshal.FinalReleaseComObject(presentation);
			if (app is not null) Marshal.FinalReleaseComObject(app);
		}
	}

	private static string ReadCell(XElement cell, XNamespace ns, Dictionary<int, string> sharedStrings)
	{
		string type = cell.Attribute("t")?.Value ?? string.Empty;
		string value = cell.Element(ns + "v")?.Value ?? cell.Element(ns + "is")?.Descendants(ns + "t").FirstOrDefault()?.Value ?? string.Empty;
		if (type == "s" && int.TryParse(value, out int sharedIndex) && sharedStrings.TryGetValue(sharedIndex, out string? sharedValue))
		{
			return sharedValue.Trim();
		}

		return value.Trim();
	}

	private static string[] SplitTableLine(string line)
	{
		char separator = line.Contains('\t') ? '\t' : line.Contains(',') ? ',' : line.Contains('|') ? '|' : ' ';
		return line.Split(separator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
	}

	private static void AddMember(List<RandomPickerMember> members, string? name, double weight)
	{
		name = NormalizeName(name);
		if (!string.IsNullOrWhiteSpace(name))
		{
			members.Add(new RandomPickerMember { Name = name, Weight = ClampWeight(weight) });
		}
	}

	private static string NormalizeName(string? name) => Regex.Replace(name ?? string.Empty, "\\s+", " ").Trim();

	private static bool IsHeader(string value)
	{
		value = value.Trim();
		return value.Equals("name", StringComparison.OrdinalIgnoreCase) || value == "姓名" || value == "名单" || value == "成员";
	}

	private static string? TryGetString(JsonElement item, string propertyName)
	{
		return item.TryGetProperty(propertyName, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
	}

	private static double? TryGetDouble(JsonElement item, string propertyName)
	{
		if (!item.TryGetProperty(propertyName, out JsonElement value))
		{
			return null;
		}

		return value.ValueKind switch
		{
			JsonValueKind.Number when value.TryGetDouble(out double number) => number,
			JsonValueKind.String when double.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out double number) => number,
			_ => null
		};
	}

	private static double ClampWeight(double weight) => Math.Round(Math.Clamp(weight, 0.01, 10.0), 2);
}
