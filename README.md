# FileSizeAnalyzer Pro

![Screenshot of FileSizeAnalyzer Pro](assets/image/screenshot.png)

## Overview
FileSizeAnalyzer Pro is a powerful, professional-grade Windows utility for analyzing disk space usage. Built with WPF and C#, it provides a fast asynchronous scanning engine, visual analytics, smart cleanup recommendations, duplicate management, and a full CLI for automation.

It features a clean, theme-aware UI inspired by modern development tools, making it both functional and pleasant to use.

## Features

### High-Performance Scanning
- Multi-threaded, asynchronous scanning engine
- Quickly analyzes directories without freezing the UI
- Customizable exclusion lists for folders and file patterns (e.g., `*\node_modules\*`, `*.tmp`)
- Skip system files and Windows directory options

### OneDrive Aware
- Safely scans OneDrive folders with "Files On-Demand" enabled
- Reads file metadata without triggering downloads of cloud-only files

### Dashboard with Visual Analytics
- Real-time statistics with disk usage gauge (color-coded: green/yellow/orange/red)
- File type distribution chart (top 10 by size)
- Largest folders chart with size badges
- Summary statistics for total files, directories, and disk metrics

### Interactive Directory Tree
- Hierarchical view of your file system with visual size bars
- Automatically sorts contents by size
- Right-click context menu to open folders or delete files

### Analysis Tabs
- **File List** - Filterable list of all scanned files
- **Largest Files** - Top 100 largest files at a glance
- **Empty Folders** - Locate and remove clutter
- **Duplicates** - Identify redundant files with 6 auto-select rules (keep newest, oldest, largest, smallest, shortest path, or one per folder)
- **Treemap** - Interactive proportional size visualization
- **Sunburst Chart** - Hierarchical disk usage visualization
- **File Types** - Summary report by extension
- **File Age** - Summary report by modification date
- **Temporary Files** - Detect temp files for cleanup
- **Stale Files** - Find files that haven't been accessed in a long time
- **Large Rarely-Used** - Identify big files that are rarely accessed

### Advanced Search
- Regex and wildcard pattern matching
- Multi-criteria filtering by name, size, date, extension, type, and path
- Search history (last 20 searches) and saved searches
- Quick search templates for common patterns

### Smart Cleanup Recommendations
- 8 cleanup categories: duplicate files, temp files, old large files (100MB+), very old files (2+ years), large media (500MB+), empty directories, old downloads (90+ days), and large log files
- Priority-based recommendations (High / Medium / Low)
- Potential space savings calculation with sample files

### Safe File Operations
- **Delete to Recycle Bin** - Safe deletion with Windows Recycle Bin integration
- **Move Files** - Bulk move with automatic duplicate name handling
- **Compress to ZIP** - Create archives with progress reporting
- Operation history tracking (last 50 operations)

### Trends & Analytics
- Historical trend analysis tracking disk usage over time
- Growth predictions (30-day, 90-day, 1-year)
- Disk full predictions
- Scan history comparison

### File Preview
- 40+ text file extensions supported
- Binary file detection with hex dump
- Image preview
- Automatic encoding detection

### Enhanced Export
- **CSV** - Comprehensive data export with all file metadata
- **JSON** - Structured output for API integration
- **HTML** - Styled, shareable reports
- Legacy CSV and HTML formats also available

### Filtering & Presets
- Filter by file size, date range (including custom), and multiple extensions
- Save and load filter presets for one-click reuse

### Modern UI
- Theme-aware interface adapting to Windows Light/Dark settings
- Custom title bar with standard top menu bar
- Color-coded feedback and tooltips throughout
- Progress dialogs for all long-running operations

## Command-Line Interface

FileSizeAnalyzer Pro includes a full CLI for automation and scripting. Run any command by passing it as the first argument.

### Commands

| Command | Description |
|---------|-------------|
| `scan <path>` | Scan a directory and show file statistics |
| `duplicates <path>` | Find duplicate files |
| `recommend <path>` | Get cleanup recommendations |
| `version` | Show version information |
| `help` | Show help |

### Options

| Option | Description |
|--------|-------------|
| `--output <file>` | Save results to a file |
| `--top <n>` | Limit results to top N items |
| `--min-size <bytes>` | Set minimum file size threshold |

### Examples

```powershell
# Scan a directory
FileSizeAnalyzerGUI.exe scan C:\Users\MyFolder

# Scan and save top 50 results to a file
FileSizeAnalyzerGUI.exe scan C:\Data --output report.txt --top 50

# Find large duplicate files (>10MB)
FileSizeAnalyzerGUI.exe duplicates C:\Photos --min-size 10485760

# Get cleanup recommendations
FileSizeAnalyzerGUI.exe recommend C:\Downloads
```

### Legacy GUI Arguments

You can also pass arguments directly for GUI-mode automation:

| Argument | Description |
|----------|-------------|
| `-path "C:\Folder"` | Directory to scan on launch |
| `-export "report.csv"` | Export file list to CSV |
| `-exit` | Close after scan and export complete |
| `-no-skip-system` | Include system files |
| `-no-skip-windows` | Include Windows directory |

```powershell
FileSizeAnalyzerGUI.exe -path "C:\Windows" -export "windows_report.csv" -no-skip-system -no-skip-windows -exit
```

## Installation

Download the latest installer from the [Releases](https://github.com/chrisdfennell/FileSizeAnalyzerGUI/releases) page, or build from source.

### Build from Source

```powershell
git clone https://github.com/chrisdfennell/FileSizeAnalyzerGUI.git
cd FileSizeAnalyzerGUI
dotnet build -c Release
```

## License

Open Source - See LICENSE file for details.
