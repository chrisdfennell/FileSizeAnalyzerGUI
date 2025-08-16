# FileSizeAnalyzer Pro

![Screenshot of FileSizeAnalyzer Pro](assets/image/screenshot.png)

## Overview
FileSizeAnalyzer Pro is a powerful and modern Windows utility for analyzing disk space usage. Built with WPF and C#, it provides a fast, asynchronous scanning engine and a rich set of tools to help you understand, visualize, and manage your files.  

It features a clean, theme-aware UI inspired by modern development tools, making it both functional and pleasant to use.

## Features

### 🚀 High-Performance Scanning
- Multi-threaded, asynchronous scanning engine.
- Quickly analyzes directories without freezing the UI.

### 🎨 Modern UI
- Sleek, theme-aware interface that adapts to your Windows settings (Light/Dark).
- Custom title bar and a standard top menu bar for a professional feel.

### ☁️ OneDrive Aware
- Safely scans OneDrive folders with "Files On-Demand" enabled.
- Reads file metadata without triggering downloads of cloud-only files.

### 🗂️ Interactive Directory Tree
- Hierarchical view of your file system.
- Visual size bars for quick understanding of space usage.
- Automatically sorts contents by size.

### 📊 Advanced Analysis Tabs
- **File List**: Filterable list of all scanned files.
- **Largest Files**: View top 100 largest files instantly.
- **Empty Folders**: Locate and remove clutter.
- **Grouped Duplicates**: Identify redundant files and use the context menu to auto-select all but the newest or oldest for easy cleanup.
- **Treemap Visualization**: Interactive layout showing size proportionally.
- **File Types & Age**: Summary reports by extension and modification date.
- **Scan History**: Access logs of previous scans.

### 🔍 Powerful Filtering & Automation
- Filter by:
  - File size
  - Date range (including custom)
  - Multiple extensions (e.g., `.jpg`, `.zip`, `.log`)
- **Save and Load Filter Presets**: Save complex filter combinations and load them with a single click.
- **Customizable Exclusion Lists**: Specify folder paths and file patterns (e.g., `*\node_modules\*`, `*.tmp`) to be permanently ignored.
- **Command-Line Interface**: Automate scans and exports for scripting or scheduled tasks.

### 🧰 File Management
- Right-click context menu for files/folders.
- Move to Recycle Bin safely.
- Open Containing Folder in Explorer.

### 📤 Command-Line Usage
Run `FileSizeAnalyzerGUI.exe` from the command line to perform automated scans.

**Arguments:**
- `-path "C:\Your\Folder"`: (Required) The directory to scan.
- `-export "report.csv"`: (Optional) Exports the full file list to the specified CSV file.
- `-exit`: (Optional) Automatically closes the application after the scan and export are complete.
- `-no-skip-system`: (Optional) Includes system files in the scan.
- `-no-skip-windows`: (Optional) Includes the Windows directory in the scan.

**Example:**
```powershell
FileSizeAnalyzerGUI.exe -path "C:\Windows" -export "windows_report.csv" -no-skip-system -no-skip-windows -exit