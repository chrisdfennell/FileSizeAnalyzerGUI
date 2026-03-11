# Changelog

All notable changes to FileSizeAnalyzer Pro will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

---

## [3.0.0] - 2026-02-07

### 🎉 Major Release - Enterprise Features

This is a massive update transforming FileSizeAnalyzer Pro into a professional-grade disk management tool with enterprise features, CLI automation, and advanced analytics.

### ✨ Added

#### 📊 **Dashboard with Visual Analytics**
- **New Dashboard Tab** with real-time statistics and visual charts
- **File Type Distribution Chart** - Visual bar chart showing top 10 file types by size
- **Largest Folders Chart** - Top 10 largest directories with size badges
- **Disk Usage Gauge** - Circular gauge with color-coded warnings:
  - 🟢 Green (< 50% used)
  - 🟡 Yellow (50-75% used)
  - 🟠 Orange (75-90% used)
  - 🔴 Red (> 90% used)
- **Summary Statistics** - Total files, directories, and disk metrics
- **Refresh Dashboard** button for on-demand updates
- `DashboardService` - Backend service for data aggregation and analysis

#### 🗂️ **Safe File Operations Suite**
- **Delete Files to Recycle Bin** - Safe deletion with Windows Recycle Bin integration
- **Move Files** - Bulk move operations with automatic duplicate name handling
- **Compress to ZIP** - Create compressed archives with progress reporting
- **Operation History** - Tracks last 50 file operations
- **Progress Dialogs** - Real-time progress bars for all operations
- **Comprehensive Error Handling** - Detailed error reporting with retry counts
- `FileOperationsService` - Professional file management backend
- Integration with Microsoft.VisualBasic for Recycle Bin support

#### 🎯 **Enhanced Duplicate Management**
- **6 Auto-Select Rules** with intelligent file selection:
  - **Keep Newest** - Preserves most recently modified files
  - **Keep Oldest** - Preserves oldest files by modification date
  - **Keep Largest** - Ideal for keeping highest resolution media
  - **Keep Smallest** - Useful for thumbnail management
  - **Shortest Path** - Keeps files with shortest full path
  - **1 per Folder** - Keeps one copy per directory tree
- **Real-time Selection Statistics** - Shows selected file count and space savings
- **Duplicate File Operations** - Delete, Move, or Compress selected duplicates
- **Enhanced UI Tooltips** - Helpful descriptions for each auto-select rule
- `DuplicateManagementService` - Advanced duplicate analysis and management
- Duplicate statistics calculator with wasted space analysis
- Filtering by size, extension, and path

#### 💻 **Command Line Interface (CLI)**
- **Headless Console Mode** for automation and scripting
- **4 Primary Commands**:
  - `scan` - Scan directories with customizable output
  - `duplicates` - Find and report duplicate files
  - `recommend` - Generate cleanup recommendations
  - `help` - Show comprehensive help
  - `version` - Display version information
- **Command Options**:
  - `--output <file>` - Save results to file
  - `--top <n>` - Limit results to top N items
  - `--min-size <bytes>` - Set minimum file size threshold
- **Real-time Progress** - Console progress indicators during scans
- **Exit Codes** - Proper exit codes for script integration
- **Rich Text Output** - Formatted, human-readable console output
- `CLIHandler` - Complete CLI command processing engine
- Automatic CLI mode detection in `App.xaml.cs`

#### 📈 **Enhanced Export Features**
- **Expanded CSV Export** - Comprehensive data export with all file metadata
- **JSON Export** - Structured JSON output for API integration
- **Enhanced HTML Reports** - Improved styling and data presentation
- **Export Options Class** - Configurable export settings
- `ExportService` - Unified export service for all formats

#### 🔍 **Advanced Search Service**
- **Regex Search** - Regular expression pattern matching
- **Wildcard Search** - Traditional wildcard support (*.txt, photo?.jpg)
- **Multi-Criteria Filtering** - Filter by name, size, date, extension, type, path
- **Search History** - Automatic tracking of last 20 searches
- **Saved Searches** - Save and reuse frequent search patterns
- **Quick Templates** - Pre-configured search templates
- `SearchService` - Full-featured search engine

#### 📊 **Trends & Analytics**
- **Historical Trend Analysis** - Track disk usage over time
- **Growth Predictions** - 30-day, 90-day, and 1-year projections
- **Space Breakdown** - Categorization by extension, age, and file type
- **Disk Full Predictions** - Estimates when disk will be full
- **Scan History Tracking** - Compare current vs. previous scans
- `TrendsService` - Analytics and prediction engine

#### 🎨 **File Preview Enhancements**
- **40+ Text File Extensions** - Comprehensive text file support
- **Binary File Detection** - Automatic hex dump for unknown files
- **Image Preview** - Visual preview for image files
- **Encoding Detection** - Auto-detect file encoding
- **File Type Information** - Detailed metadata display
- `FilePreviewService` - Professional preview engine

#### 🧹 **Smart Cleanup Recommendations**
- **8 Cleanup Categories**:
  1. Duplicate Files
  2. Temporary Files
  3. Old Large Files (100MB+, 1+ year old)
  4. Very Old Files (2+ years)
  5. Large Media Files (500MB+ videos)
  6. Empty Directories
  7. Old Downloads (90+ days)
  8. Large Log Files (10MB+, 30+ days old)
- **Priority-Based Recommendations** (High/Medium/Low)
- **Potential Savings Calculation** - Shows recoverable space
- **Sample Files Display** - Example files for each category
- **Actionable Suggestions** - Specific cleanup actions recommended
- `CleanupRecommendationService` - Intelligent cleanup analysis engine

### 🔧 Improved

- **Service Architecture** - All new features implemented as injectable services
- **Dependency Injection** - Comprehensive DI throughout application
- **Error Handling** - Enhanced error reporting and user feedback
- **Progress Reporting** - Consistent IProgress<T> implementation across all async operations
- **UI Responsiveness** - All long-running operations moved to background threads
- **Code Organization** - Clean separation of concerns with service layer

### 🎨 UI/UX Improvements

- **Dashboard Tab** - Professional analytics visualization
- **Enhanced Duplicates Tab** - Better controls and statistics display
- **Reports Tab** - Complete redesign with cleanup recommendations
- **Progress Dialogs** - Consistent progress feedback for all operations
- **Tooltips** - Helpful tooltips on all buttons and controls
- **Color-Coded Feedback** - Visual indicators for status and priority
- **Responsive Layout** - Better handling of different window sizes

### 🛠️ Technical Changes

- **New Dependencies**:
  - `LiveChartsCore.SkiaSharpView.WPF` - Chart rendering (added but not yet integrated for visualization)
  - `Microsoft.VisualBasic` - Recycle Bin support
- **New Services** (8 total):
  - `FileOperationsService`
  - `DashboardService`
  - `DuplicateManagementService`
  - `CLIHandler`
  - `ExportService`
  - `SearchService`
  - `TrendsService`
  - `FilePreviewService`
  - `CleanupRecommendationService`
- **Service Registration** - All services registered in `ServiceConfiguration.cs`
- **MVVM Enhancements** - New ObservableCollections for reactive UI
- **Async/Await** - Comprehensive async implementation for responsiveness

### 📝 Documentation

- Added comprehensive CHANGELOG.md
- CLI help documentation (`FileSizeAnalyzerGUI help`)
- Inline code documentation for all new services
- Version information display in CLI and GUI

### 🐛 Bug Fixes

- Fixed property name mismatches in ExportService
- Added missing GetScanHistory method to ScanMetadataService
- Fixed XML entity escaping for ampersands in XAML
- Resolved namespace conflicts with Forms controls
- Fixed nullable reference warnings

### 🎯 Performance

- Parallel file operations support
- Efficient duplicate grouping algorithms
- Optimized chart data calculations
- Background threading for all I/O operations

---

## [2.0.0] - Previous Release

### Features
- Duplicate detection with hash verification
- Treemap and Sunburst visualizations
- File type analysis
- Temporary files detection
- Stale files identification
- Large rarely-used files tracking
- Scan history and metadata
- Filter presets
- Basic export (CSV, HTML)

---

## [1.0.0] - Initial Release

### Features
- Basic directory scanning
- File size analysis
- Directory tree visualization
- File list view
- Empty folder detection
- Dark theme UI
- Basic filtering

---

## Future Roadmap

### Planned for v3.1.0
- [ ] Full LiveCharts integration for interactive charts
- [ ] Network drive optimization
- [ ] Hard link creation for duplicates
- [ ] Visual file comparison (diff viewer)
- [ ] Scheduled scan automation
- [ ] Cloud storage integration (OneDrive, Dropbox)

### Planned for v4.0.0
- [ ] Plugin system for custom analyzers
- [ ] Machine learning-based cleanup suggestions
- [ ] Real-time folder monitoring
- [ ] Multi-language support (i18n)
- [ ] Portable mode (no installation)
- [ ] Advanced compression algorithms (7z, tar.gz)
- [ ] File deduplication with junction points
- [ ] Integration with Windows Search

---

## Contributing

We welcome contributions! See the main README.md for contribution guidelines.

## License

Open Source - See LICENSE file for details.

---

**[3.0.0]**: https://github.com/yourusername/FileSizeAnalyzerGUI/releases/tag/v3.0.0
