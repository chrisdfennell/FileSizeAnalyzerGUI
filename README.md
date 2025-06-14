# FileSizeAnalyzer Pro

![FileSizeAnalyzer Pro Screenshot](https://imgur.com/a/CpPCjHv)

## Overview

**FileSizeAnalyzer Pro** is a powerful and modern Windows utility for analyzing disk space usage. Built with WPF and C#, it provides a fast, asynchronous scanning engine and a rich set of tools to help you understand, visualize, and manage your files.

It's designed with a clean, dark-themed UI inspired by modern development tools, making it both functional and pleasant to use.

---

## 🚀 Features

- **High-Performance Scanning:**  
  Multi-threaded, asynchronous scanning engine that quickly analyzes directories without freezing the UI.

- **Modern UI:**  
  Sleek, dark-themed interface with a custom title bar and styled controls for a professional feel.

- **OneDrive Aware:**  
  Safely scans OneDrive folders with "Files On-Demand" enabled, reading file metadata without triggering unnecessary downloads of cloud-only files.

- **Interactive Directory Tree:**
  - Hierarchical view of your file system
  - Visual size bars for at-a-glance folder usage
  - Automatically sorts contents by size (largest first)

- **Advanced Analysis Tabs:**
  - **File List:** A filterable list of all files in the scan
  - **Largest Files:** Instantly view the top 100 largest files
  - **Empty Folders:** Find and remove unused directories
  - **Grouped Duplicates:** `TreeView` layout groups identical files
  - **Treemap Visualization:** Clickable, interactive folder heatmap
  - **File Types & Age:** Summary reports by extension and modification date
  - **Scan History:** Log of your previous scans

- **Powerful Filtering:**
  - Filter by file size
  - Date range (including custom)
  - Multiple extensions (`.jpg`, `.zip`, `.log`, etc.)

- **File Management:**
  - Right-click any file or folder for context actions
  - **Move to Recycle Bin** safely
  - **Open Containing Folder** in Windows Explorer

- **Export & Help:**
  - Export filtered file lists to `.csv`
  - Built-in Help and About dialogs

---

## 🛠 Tech Stack

- **Framework:** .NET 8  
- **UI:** Windows Presentation Foundation (WPF)  
- **Language:** C#  
- **Core Libraries:**
  - `System.IO.Hashing` – fast hashing (XxHash64)
  - `Microsoft.VisualBasic` – for Recycle Bin operations

---

## 📘 How to Use

1. Clone the repository.
2. Open `FileSizeAnalyzerGUI.sln` in Visual Studio.
3. Ensure the **.NET 8 SDK** is installed.
4. **Install Required NuGet Packages:**
   - Right-click the project > **Manage NuGet Packages...**
   - Install:
     - `System.Drawing.Common` (for file icons)
     - `Microsoft.VisualBasic` (for Recycle Bin support)
5. Build and run the application.

---

## 📦 Deployment (Creating a Single EXE)

To package the application into a single `.exe` that can run on other machines:

### 🔐 1. Requiring Administrator Privileges

1. In Visual Studio's **Solution Explorer**, right-click the `FileSizeAnalyzerGUI` project > **Add > New Item...**
2. Search for `manifest`, select **Application Manifest File**, and click **Add**.
3. Open `app.manifest`.
4. Find the following line:

    ```xml
    <requestedExecutionLevel level="asInvoker" uiAccess="false" />
    ```

5. Change it to:

    ```xml
    <requestedExecutionLevel level="requireAdministrator" uiAccess="false" />
    ```

6. Right-click the project > **Properties**.
7. Under **Application**, ensure **Manifest** is set to *Embed manifest with default settings*.

---

### 📤 2. Publishing as a Single File

This process bundles the .NET runtime and all dependencies into one `.exe`.

#### 📁 Using Visual Studio:

1. Right-click the project in **Solution Explorer** and select **Publish**.
2. Choose **Folder** as the publishing target and click **Next**.
3. Choose a folder location (the default `bin\Release\net8.0-windows\publish\` is fine) and click **Finish**.
4. In the publish summary screen, click the **Show all settings** link.
5. In the **Profile Settings** window:
    - Set **Deployment mode** to **Self-contained**.
    - Set **File publish options** to **Produce single file**.
    - Click **Save**.
6. Finally, click the **Publish** button. Your single `.exe` file will be in the folder you chose.

#### 🖥️ Using the Command Line:

1. Open a terminal (like PowerShell or Command Prompt) in your project's root directory.
2. Run the following command:

    ```bash
    dotnet publish -r win-x64 -c Release --self-contained true /p:PublishSingleFile=true
    ```

3. Your single `.exe` file will be located in the following directory:

    ```
    \bin\Release\net8.0-windows\win-x64\publish
    ```

---

## 🌱 Future Development Ideas

- **Interactive Pie Chart** for the *File Types* tab
- **Save/Load Scans** for restoring scan sessions
- **Bulk Actions** for multi-select and delete
- **Windows Explorer Integration** with right-click "Scan with FileSizeAnalyzer"
