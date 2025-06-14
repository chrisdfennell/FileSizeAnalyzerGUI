# FileSizeAnalyzer Pro

## Overview

**FileSizeAnalyzer Pro** is a powerful and modern Windows utility for analyzing disk space usage. Built with WPF and C#, it provides a fast, asynchronous scanning engine and a rich set of tools to help you understand, visualize, and manage your files.

It features a clean, dark-themed UI inspired by modern development tools, making it both functional and pleasant to use.

---

## Features

### 🚀 High-Performance Scanning
- Multi-threaded, asynchronous scanning engine.
- Quickly analyzes directories without freezing the UI.

### 🎨 Modern UI
- Sleek dark-themed interface.
- Custom title bar and styled controls for a professional feel.

### ☁️ OneDrive Aware
- Safely scans OneDrive folders with "Files On-Demand" enabled.
- Reads file metadata without triggering downloads of cloud-only files.

### 🗂 Interactive Directory Tree
- Hierarchical view of your file system.
- Visual size bars for quick understanding of space usage.
- Automatically sorts contents by size.

### 📊 Advanced Analysis Tabs
- **File List**: Filterable list of all scanned files.
- **Largest Files**: View top 100 largest files instantly.
- **Empty Folders**: Locate and remove clutter.
- **Grouped Duplicates**: Identify and delete redundant files via TreeView.
- **Treemap Visualization**: Interactive layout showing size proportionally.
- **File Types & Age**: Summary reports by extension and modification date.
- **Scan History**: Access logs of previous scans.

### 🔍 Powerful Filtering
- Filter by:
  - File size
  - Date range (including custom)
  - Multiple extensions (e.g., `.jpg`, `.zip`, `.log`)

### 🧰 File Management
- Right-click context menu for files/folders.
- **Move to Recycle Bin** safely.
- **Open Containing Folder** in Explorer.

### 📤 Export & 📘 Help
- Export filtered lists to `.csv`.
- Built-in Help and About dialogs.

---

## 🛠 Tech Stack

- **Framework**: .NET 8  
- **UI**: Windows Presentation Foundation (WPF)  
- **Language**: C#

### Core Libraries

- `System.IO.Hashing` – Fast file hashing (XxHash64)
- `Microsoft.VisualBasic` – Safe Recycle Bin operations

---

## 🚀 How to Use

1. Clone the repository.
2. Open `FileSizeAnalyzerGUI.sln` in Visual Studio.
3. Ensure .NET 8 SDK is installed.
4. Add the `System.Drawing.Common` NuGet package (for icon support).
5. Add a project reference to the `Microsoft.VisualBasic` assembly.
6. Build and run the application.

---

## 📦 2. Publishing as a Single File

This process bundles the .NET runtime and all dependencies into your `.exe`.

### Using Visual Studio:

1. Right-click the project in **Solution Explorer** and select **Publish**.
2. Choose **Folder** as the publishing target and click **Next**.
3. Choose a folder location (the default `bin\Release\net8.0-windows\publish\` is fine) and click **Finish**.
4. In the publish summary screen, click the **Show all settings** link.
5. In the **Profile Settings** window:
    - Set **Deployment mode** to **Self-contained**.
    - Set **File publish options** to **Produce single file**.
    - Click **Save**.
6. Finally, click the **Publish** button. Your single `.exe` file will be in the folder you chose.

### Using the Command Line:

1. Open a terminal (like PowerShell or Command Prompt) in your project's root directory.
2. Run the following command:

    ```bash
    dotnet publish -r win-x64 -c Release --self-contained true /p:PublishSingleFile=true
    ```

3. Your single `.exe` file will be located in the `\bin\Release\net8.0-windows\win-x64\publish` directory.

---

## 🔮 Future Development Ideas

- **Interactive Pie Chart** for the *File Types* tab.
- **Save/Load Scans** for persistent scan sessions.
- **Bulk Actions** in file grids.
- **Windows Explorer Integration**:
  - Right-click > *Scan with FileSizeAnalyzer*
