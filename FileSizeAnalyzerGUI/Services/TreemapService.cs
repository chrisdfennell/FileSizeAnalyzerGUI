using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;

namespace FileSizeAnalyzerGUI.Services
{
	public class TreemapRectangle
	{
		public Rect Bounds { get; set; }
		public FileSystemNode Node { get; set; }
		public Color Color { get; set; }
		public double Percentage { get; set; }

		public TreemapRectangle(Rect bounds, FileSystemNode node, Color color, double percentage)
		{
			Bounds = bounds;
			Node = node;
			Color = color;
			Percentage = percentage;
		}
	}

	public class TreemapService
	{
		private static readonly Dictionary<string, Color> FileTypeColors = new Dictionary<string, Color>(StringComparer.OrdinalIgnoreCase)
		{
			[".mp4"] = Color.FromRgb(100, 149, 237),
			[".avi"] = Color.FromRgb(100, 149, 237),
			[".mkv"] = Color.FromRgb(100, 149, 237),
			[".mov"] = Color.FromRgb(100, 149, 237),
			[".wmv"] = Color.FromRgb(100, 149, 237),
			[".flv"] = Color.FromRgb(100, 149, 237),
			[".webm"] = Color.FromRgb(100, 149, 237),
			
			[".jpg"] = Color.FromRgb(60, 179, 113),
			[".jpeg"] = Color.FromRgb(60, 179, 113),
			[".png"] = Color.FromRgb(60, 179, 113),
			[".gif"] = Color.FromRgb(60, 179, 113),
			[".bmp"] = Color.FromRgb(60, 179, 113),
			[".svg"] = Color.FromRgb(60, 179, 113),
			[".webp"] = Color.FromRgb(60, 179, 113),
			[".ico"] = Color.FromRgb(60, 179, 113),
			
			[".mp3"] = Color.FromRgb(218, 112, 214),
			[".wav"] = Color.FromRgb(218, 112, 214),
			[".flac"] = Color.FromRgb(218, 112, 214),
			[".aac"] = Color.FromRgb(218, 112, 214),
			[".ogg"] = Color.FromRgb(218, 112, 214),
			[".wma"] = Color.FromRgb(218, 112, 214),
			[".m4a"] = Color.FromRgb(218, 112, 214),
			
			[".pdf"] = Color.FromRgb(220, 20, 60),
			[".doc"] = Color.FromRgb(65, 105, 225),
			[".docx"] = Color.FromRgb(65, 105, 225),
			[".xls"] = Color.FromRgb(34, 139, 34),
			[".xlsx"] = Color.FromRgb(34, 139, 34),
			[".ppt"] = Color.FromRgb(255, 140, 0),
			[".pptx"] = Color.FromRgb(255, 140, 0),
			[".txt"] = Color.FromRgb(169, 169, 169),
			[".rtf"] = Color.FromRgb(169, 169, 169),
			
			[".zip"] = Color.FromRgb(255, 215, 0),
			[".rar"] = Color.FromRgb(255, 215, 0),
			[".7z"] = Color.FromRgb(255, 215, 0),
			[".tar"] = Color.FromRgb(255, 215, 0),
			[".gz"] = Color.FromRgb(255, 215, 0),
			[".bz2"] = Color.FromRgb(255, 215, 0),
			
			[".exe"] = Color.FromRgb(178, 34, 34),
			[".dll"] = Color.FromRgb(178, 34, 34),
			[".msi"] = Color.FromRgb(178, 34, 34),
			[".bat"] = Color.FromRgb(178, 34, 34),
			[".cmd"] = Color.FromRgb(178, 34, 34),
			[".ps1"] = Color.FromRgb(178, 34, 34),
			
			[".cs"] = Color.FromRgb(147, 112, 219),
			[".java"] = Color.FromRgb(147, 112, 219),
			[".py"] = Color.FromRgb(147, 112, 219),
			[".js"] = Color.FromRgb(147, 112, 219),
			[".ts"] = Color.FromRgb(147, 112, 219),
			[".cpp"] = Color.FromRgb(147, 112, 219),
			[".c"] = Color.FromRgb(147, 112, 219),
			[".h"] = Color.FromRgb(147, 112, 219),
			[".html"] = Color.FromRgb(147, 112, 219),
			[".css"] = Color.FromRgb(147, 112, 219),
			[".json"] = Color.FromRgb(147, 112, 219),
			[".xml"] = Color.FromRgb(147, 112, 219),
		};

		private static readonly Color DefaultFileColor = Color.FromRgb(128, 128, 128);
		private static readonly Color DirectoryColor = Color.FromRgb(70, 130, 180);

		public List<TreemapRectangle> GenerateTreemap(List<FileSystemNode> nodes, Rect bounds)
		{
			var rectangles = new List<TreemapRectangle>();
			if (nodes == null || !nodes.Any() || bounds.Width <= 1 || bounds.Height <= 1)
				return rectangles;

			var sortedNodes = nodes.OrderByDescending(n => n.Size).ToList();
			var totalSize = (double)sortedNodes.Sum(n => n.Size);

			Squarify(sortedNodes, new List<FileSystemNode>(), bounds, totalSize, rectangles);

			return rectangles;
		}

		private void Squarify(List<FileSystemNode> nodes, List<FileSystemNode> row, Rect bounds, double totalSize, List<TreemapRectangle> rectangles)
		{
			if (!nodes.Any() || bounds.Width <= 0 || bounds.Height <= 0 || totalSize <= 0)
			{
				if (row.Any() && bounds.Width > 0 && bounds.Height > 0)
					LayoutRow(row, bounds, totalSize, rectangles);
				return;
			}

			var node = nodes.First();
			var remainingNodes = nodes.Skip(1).ToList();

			if (ShouldAddToRow(row, node, bounds))
			{
				row.Add(node);
				Squarify(remainingNodes, row, bounds, totalSize, rectangles);
			}
			else
			{
				if (row.Any())
				{
					var newBounds = LayoutRow(row, bounds, totalSize, rectangles);
					if (newBounds.Width > 0 && newBounds.Height > 0)
						Squarify(nodes, new List<FileSystemNode>(), newBounds, totalSize, rectangles);
				}
				else
				{
					row.Add(node);
					Squarify(remainingNodes, row, bounds, totalSize, rectangles);
				}
			}
		}

		private bool ShouldAddToRow(List<FileSystemNode> row, FileSystemNode node, Rect bounds)
		{
			if (!row.Any()) return true;

			var currentWorst = WorstAspectRatio(row, bounds);
			var newRow = new List<FileSystemNode>(row) { node };
			var newWorst = WorstAspectRatio(newRow, bounds);

			return currentWorst >= newWorst;
		}

		private double WorstAspectRatio(List<FileSystemNode> row, Rect bounds)
		{
			if (!row.Any()) return double.MaxValue;

			var rowSum = (double)row.Sum(n => n.Size);
			if (rowSum <= 0) return double.MaxValue;

			var rowMin = (double)row.Min(n => n.Size);
			var rowMax = (double)row.Max(n => n.Size);

			var shortSide = Math.Min(bounds.Width, bounds.Height);
			if (shortSide <= 0) return double.MaxValue;

			var shortSideSquared = shortSide * shortSide;
			var rowSumSquared = rowSum * rowSum;

			if (rowSumSquared <= 0 || rowMin <= 0) return double.MaxValue;

			return Math.Max(
				(shortSideSquared * rowMax) / rowSumSquared,
				rowSumSquared / (shortSideSquared * rowMin)
			);
		}

		private Rect LayoutRow(List<FileSystemNode> row, Rect bounds, double totalSize, List<TreemapRectangle> rectangles)
		{
			if (!row.Any() || totalSize <= 0) return bounds;

			var rowSum = (double)row.Sum(n => n.Size);
			if (rowSum <= 0) return bounds;

			var isHorizontal = bounds.Width >= bounds.Height;

			double x = bounds.Left;
			double y = bounds.Top;

			foreach (var node in row)
			{
				if (node.Size <= 0) continue;

				var percentage = (node.Size / totalSize) * 100.0;
				var color = GetColorForNode(node);

				Rect rect;
				if (isHorizontal)
				{
					var width = bounds.Width * (node.Size / rowSum);
					var height = bounds.Height * (rowSum / totalSize);
					rect = new Rect(x, y, Math.Max(0, width), Math.Max(0, height));
					x += width;
				}
				else
				{
					var width = bounds.Width * (rowSum / totalSize);
					var height = bounds.Height * (node.Size / rowSum);
					rect = new Rect(x, y, Math.Max(0, width), Math.Max(0, height));
					y += height;
				}

				if (rect.Width > 0 && rect.Height > 0)
					rectangles.Add(new TreemapRectangle(rect, node, color, percentage));
			}

			if (isHorizontal)
			{
				var rowHeight = bounds.Height * (rowSum / totalSize);
				var newHeight = Math.Max(0, bounds.Height - rowHeight);
				return new Rect(bounds.Left, bounds.Top + rowHeight, bounds.Width, newHeight);
			}
			else
			{
				var rowWidth = bounds.Width * (rowSum / totalSize);
				var newWidth = Math.Max(0, bounds.Width - rowWidth);
				return new Rect(bounds.Left + rowWidth, bounds.Top, newWidth, bounds.Height);
			}
		}

		private Color GetColorForNode(FileSystemNode node)
		{
			if (node.IsDirectory)
				return DirectoryColor;

			var extension = node.Extension?.ToLowerInvariant();
			if (!string.IsNullOrEmpty(extension) && FileTypeColors.TryGetValue(extension, out var color))
				return color;

			return DefaultFileColor;
		}

		public static Color ApplyCushionEffect(Color baseColor, double depth)
		{
			var factor = 1.0 - (depth * 0.15);
			return Color.FromRgb(
				(byte)(baseColor.R * factor),
				(byte)(baseColor.G * factor),
				(byte)(baseColor.B * factor)
			);
		}

		public static LinearGradientBrush CreateCushionBrush(Color baseColor)
		{
			var lightColor = Color.FromRgb(
				(byte)Math.Min(255, baseColor.R + 40),
				(byte)Math.Min(255, baseColor.G + 40),
				(byte)Math.Min(255, baseColor.B + 40)
			);

			var darkColor = Color.FromRgb(
				(byte)Math.Max(0, baseColor.R - 30),
				(byte)Math.Max(0, baseColor.G - 30),
				(byte)Math.Max(0, baseColor.B - 30)
			);

			var brush = new LinearGradientBrush();
			brush.StartPoint = new Point(0, 0);
			brush.EndPoint = new Point(1, 1);
			brush.GradientStops.Add(new GradientStop(lightColor, 0.0));
			brush.GradientStops.Add(new GradientStop(baseColor, 0.5));
			brush.GradientStops.Add(new GradientStop(darkColor, 1.0));

			return brush;
		}
	}
}
