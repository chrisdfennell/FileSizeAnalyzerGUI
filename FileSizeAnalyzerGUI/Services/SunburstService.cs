using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Shapes;

namespace FileSizeAnalyzerGUI.Services
{
	public class SunburstSegment
	{
		public required FileSystemNode Node { get; set; }
		public double StartAngle { get; set; }
		public double EndAngle { get; set; }
		public double InnerRadius { get; set; }
		public double OuterRadius { get; set; }
		public Color Color { get; set; }
		public int Depth { get; set; }
	}

	public class SunburstService
	{
		private static readonly Color[] ColorPalette = new[]
		{
			Color.FromRgb(52, 152, 219),   // Blue
			Color.FromRgb(155, 89, 182),   // Purple
			Color.FromRgb(46, 204, 113),   // Green
			Color.FromRgb(241, 196, 15),   // Yellow
			Color.FromRgb(230, 126, 34),   // Orange
			Color.FromRgb(231, 76, 60),    // Red
			Color.FromRgb(26, 188, 156),   // Turquoise
			Color.FromRgb(52, 73, 94),     // Dark blue-gray
			Color.FromRgb(149, 165, 166),  // Gray
			Color.FromRgb(243, 156, 18)    // Dark orange
		};

		public List<SunburstSegment> GenerateSunburst(FileSystemNode root, double centerX, double centerY, double maxRadius)
		{
			var segments = new List<SunburstSegment>();

			if (root == null || !root.IsDirectory || root.Size == 0)
				return segments;

			// Generate segments recursively
			GenerateSegmentsRecursive(root, 0, 360, 0, maxRadius, 0, segments, 0);

			return segments;
		}

		private void GenerateSegmentsRecursive(
			FileSystemNode node,
			double startAngle,
			double endAngle,
			double innerRadius,
			double maxRadius,
			int depth,
			List<SunburstSegment> segments,
			int colorIndex)
		{
			if (node == null || node.Size == 0)
				return;

			// Calculate this node's radius
			double radiusPerLevel = maxRadius / 5.0; // Support up to 5 levels
			double outerRadius = Math.Min(innerRadius + radiusPerLevel, maxRadius);

			// Create segment for this node (only if it's not the root or if it has no children to show)
			if (depth > 0 || (node.Children == null || node.Children.Count == 0))
			{
				var segment = new SunburstSegment
				{
					Node = node,
					StartAngle = startAngle,
					EndAngle = endAngle,
					InnerRadius = innerRadius,
					OuterRadius = outerRadius,
					Depth = depth,
					Color = GetColorForDepth(depth, colorIndex)
				};
				segments.Add(segment);
			}

			// Process children
			if (node.Children != null && node.Children.Count > 0 && depth < 4) // Limit depth to 4 levels
			{
				var children = node.Children.Where(c => c.Size > 0).OrderByDescending(c => c.Size).ToList();
				if (children.Count == 0)
					return;

				double totalSize = children.Sum(c => c.Size);
				double currentAngle = startAngle;
				int childColorIndex = 0;

				foreach (var child in children)
				{
					double angleFraction = (child.Size / (double)totalSize) * (endAngle - startAngle);
					double childEndAngle = currentAngle + angleFraction;

					// Only show segments that are large enough to be visible (at least 1 degree)
					if (angleFraction >= 1.0)
					{
						GenerateSegmentsRecursive(
							child,
							currentAngle,
							childEndAngle,
							outerRadius,
							maxRadius,
							depth + 1,
							segments,
							childColorIndex % ColorPalette.Length);

						childColorIndex++;
					}

					currentAngle = childEndAngle;
				}
			}
		}

		private Color GetColorForDepth(int depth, int index)
		{
			var baseColor = ColorPalette[index % ColorPalette.Length];

			// Lighten color based on depth
			double factor = 1.0 - (depth * 0.15);
			factor = Math.Max(0.3, Math.Min(1.0, factor));

			return Color.FromRgb(
				(byte)(baseColor.R * factor),
				(byte)(baseColor.G * factor),
				(byte)(baseColor.B * factor));
		}

		public Path CreateSegmentPath(SunburstSegment segment, double centerX, double centerY)
		{
			// Convert angles to radians
			double startRad = (segment.StartAngle - 90) * Math.PI / 180.0;
			double endRad = (segment.EndAngle - 90) * Math.PI / 180.0;

			// Calculate points
			Point innerStart = new Point(
				centerX + segment.InnerRadius * Math.Cos(startRad),
				centerY + segment.InnerRadius * Math.Sin(startRad));

			Point innerEnd = new Point(
				centerX + segment.InnerRadius * Math.Cos(endRad),
				centerY + segment.InnerRadius * Math.Sin(endRad));

			Point outerStart = new Point(
				centerX + segment.OuterRadius * Math.Cos(startRad),
				centerY + segment.OuterRadius * Math.Sin(startRad));

			Point outerEnd = new Point(
				centerX + segment.OuterRadius * Math.Cos(endRad),
				centerY + segment.OuterRadius * Math.Sin(endRad));

			// Determine if we need a large arc
			bool isLargeArc = (segment.EndAngle - segment.StartAngle) > 180;

			// Create path geometry
			var pathFigure = new PathFigure
			{
				StartPoint = innerStart,
				IsClosed = true
			};

			// Inner arc
			pathFigure.Segments.Add(new ArcSegment
			{
				Point = innerEnd,
				Size = new Size(segment.InnerRadius, segment.InnerRadius),
				SweepDirection = SweepDirection.Clockwise,
				IsLargeArc = isLargeArc
			});

			// Line to outer arc
			pathFigure.Segments.Add(new LineSegment { Point = outerEnd });

			// Outer arc (back to start, so reverse direction)
			pathFigure.Segments.Add(new ArcSegment
			{
				Point = outerStart,
				Size = new Size(segment.OuterRadius, segment.OuterRadius),
				SweepDirection = SweepDirection.Counterclockwise,
				IsLargeArc = isLargeArc
			});

			// Line back to start
			pathFigure.Segments.Add(new LineSegment { Point = innerStart });

			var pathGeometry = new PathGeometry();
			pathGeometry.Figures.Add(pathFigure);

			var path = new Path
			{
				Data = pathGeometry,
				Fill = new SolidColorBrush(segment.Color),
				Stroke = new SolidColorBrush(Colors.White),
				StrokeThickness = 1,
				Tag = segment
			};

			return path;
		}
	}
}
