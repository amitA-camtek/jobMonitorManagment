# system.md — Camtek Falcon Codebase Architecture

> **Generated:** 2026-04-04  
> **Repo:** `CamtekGit` (monorepo)  
> **CI/CD:** Azure DevOps Pipelines  
> **Domain:** Semiconductor Wafer Inspection (AOI / EBI / BSI-HR)

---

## 1. Repository & Project Layout

### 1.1 Top-Level Folder Structure

| Folder | Purpose |
|---|---|
| `BIS/` | **Board Inspection System** — the main Falcon AOI/EBI platform (largest component) |
| `BSIHR/` | **BSI High Resolution** — bump/wafer inspection, client-server architecture (gRPC) |
| `CMM/` | **Coordinate Measuring Machine** — ticket processing, converter engine, report generation |
| `CmmLightConverters/` | 300+ file-format converters for inspection results (KLARF, SEMI E142, CSV, Excel, etc.) |
| `CamtekSoftwareSolutions/` | Multi-project area: **DataServer**, **MDC**, **RMS**, shared utilities |
| `CamtekSoftwareSolutionsOld/` | Legacy snapshot of CamtekSoftwareSolutions (pre-.NET 4.8 upgrade) |
| `SystemCalibration/` | Hardware calibration desktop application (Prism/MEF + WPF) |
| `ToolAnalytics/` | Light Channel Calibration (LCC) validation tool (.NET 7 WPF) |
| `DevelopmentTools/` | `LogLens` — real-time log viewer/analyzer (.NET 7 WPF) |
| `Falcon/` | Machine data & configuration files (CDAT, Machine, ToolManagement) |
| `Install/` | Installer scripts, iTOOLS HTML vizualizations, Matlab utils, testing apps |
| `DeployUI/` | Developer workstation build & deployment orchestrator (PowerShell + WPF GUI) |
| `ExternalTools/` | FiltAir cleanroom system updater |
| `Packages/` | Locally committed NuGet packages (EF Core, etc.) |
| `UnitTestsData/` | Shared test fixture data files |
| `Logs/` | MSBuild binary logs and timing reports |
| `xbuild/` | Azure DevOps pipeline YAML definitions |
| `xgitscripts/` | `CreatePR.exe` — Azure DevOps PR creation CLI tool |
| `AZDO_72465/` | Investigation logs for Azure DevOps work item #72465 |
| `01-03-26_SW_QA-1_5.10.0-4501_Logs_GRAB_AFTER_MERGE/` | QA diagnostic log capture |

### 1.2 Project Type

**Monorepo** — a single Git repository containing ~15 distinct applications/services, shared libraries, build infrastructure, tooling, machine data, and 300+ converters. Uses **Git LFS** extensively for binary assets (`.dll`, `.exe`, `.ocx`, `.tlb`, `.lib`, `.pdb`, `.dat`, `.zip`).

### 1.3 Primary Languages & Frameworks per Area

| Area | Languages | Frameworks / Key Tech |
|---|---|---|
| **BIS** | C# (943 projs), C++ native (272 projs), VB.NET (24), VB6 (96) | WPF, WinForms, COM/ActiveX, Matrox MIL, OpenCL/GPU, ONNX, Prism, MSBuild |
| **BSIHR** | C#, C++/CLI, Protobuf | ASP.NET Core (Kestrel), gRPC, EF Core 6 (SQLite), WPF (.NET 6) |
| **CMM** | VB.NET, C# | WinForms, NUnit, .NET Framework 4.8 |
| **CmmLightConverters** | C# | .NET Framework 4.8, NUnit, Excel COM Interop |
| **DataServer** | C# | WCF (`net.tcp`), LinqToDB (SQLite), protobuf-net, .NET Framework 4.8 |
| **MDC** | C# | WPF, Prism 6 (Unity), WCF client proxies, .NET Framework 4.8 |
| **RMS** | C# | ASP.NET Core (.NET 6), gRPC (code-first protobuf-net), SignalR, EF Core 6 (SQLite), JWT, Serilog |
| **SystemCalibration** | C#, C++ (post-build) | WPF, Prism 5 (MEF), Telerik, .NET Framework 4.6.1 |
| **ToolAnalytics** | C# | WPF (.NET 7), CommunityToolkit.Mvvm, LiveCharts |
| **LogLens** | C# | WPF (.NET 7), .NET Standard 2.1, TCP streaming |
| **DeployUI** | PowerShell | WPF GUI (XAML), MSBuild orchestration |
| **Install/iTOOLS** | HTML/JavaScript | Plotly.js, math.js |

### 1.4 Configuration Files

| Type | Location | Notes |
|---|---|---|
| **CI/CD Pipelines** | `xbuild/pipeline_ci.yml` | CI build — extends `ci_pipeline.yml@PipelineRepo` (Azure DevOps) |
| | `xbuild/pipeline_pr.yml` | PR validation — extends `base_pipeline.yml@PipelineRepo` |
| | `xbuild/pipeline_rel.yml` | Release — extends `rel_pipeline.yml@PipelineRepo` |
| | `xbuild/pipeline_test.yml` | Compile test — params: singleThreaded, compileLoops, per-solution toggles |
| | `xbuild/sws.yml` | SWS (Software Solutions) build — extends `sws_pipeline.yml@PipelineRepo` |
| | `xbuild/sws_private.yml` | Private SWS build — params: MDC toggle, DATASERVER toggle |
| | `xbuild/sws_private_dev.yml` | Private SWS dev build |
| **Docker Compose** | `BIS/Sources/system/CamtekSystem/PubSub/env/docker-compose.yml` | RabbitMQ 3 (management-alpine), ports 5672/15672 |
| **MSBuild Props** | `BIS/build/Camtek.CSharp.Common.Properties.props` | Shared C# build properties |
| | `BIS/build/Camtek.Cpp.Common.Properties.props` | Shared C++ build properties |
| | `BIS/build/Camtek.VBNet.Common.Properties.props` | Shared VB.NET build properties |
| | `BIS/build/COMRegistration.targets` | COM registration post-build |
| | `BSIHR/build/Camtek.CSharp.Common.Properties.props` | BSIHR C# build properties |
| | `CamtekSoftwareSolutions/mdc/Directory.Packages.props` | Central NuGet package management for MDC |
| **NuGet** | `BIS/build/NuGet.config` | NuGet source configuration |
| | `ToolAnalytics/nuget.config` | Local `./packages` folder source only |
| **Git** | `.gitattributes` | Extensive LFS tracking (30+ binary extensions) |
| | `.gitignore` | 553 lines — VS standard + Camtek-specific (`/BIS/bin/x64`, `.claude/`) |
| **AI Workflows** | `.windsurf/workflows/req-verify.md` | Requirements verification workflow |
| | `.windsurf/workflows/coding.md` | Suggest-only coding assistant |
| | `.windsurf/workflows/code-review.md` | Pre-commit code review |
| | `req-build.md` | Requirements building/clarification command |
| **Kubernetes** | None | |
| **Terraform** | None | |
| **`.env` samples** | None found | Configuration via `App.config`, `appsettings.json`, INI files |

---

## 2. Service Inventory

### 2.1 BIS — Board Inspection System (Falcon)

| Field | Details |
|---|---|
| **Name** | BIS / Falcon |
| **Type** | Monolithic desktop application (AOI/EBI machine control) |
| **Language & Framework** | C# (WPF/WinForms), C++ native, C++/CLI, VB.NET, VB6 / .NET Framework 4.8 / COM/ActiveX |
| **Responsibility** | Core semiconductor wafer inspection — image acquisition, defect detection, alignment, calibration, recipe management, machine control, SECS/GEM tool management |
| **Entry Point** | Multiple: `Falcon.Net` (main app), `DdsSrv_d.exe` (DDS process), `PizzaServer.exe` (wafer handling), `StaminaUtils.exe`, `CamtekUtils.exe`, `ScenarioManager.exe`, `JobSelect.Net.exe`, and 60+ specialized apps in `Sources/apps/` |
| **Port** | COM-based IPC (DCOM/local servers), some TCP (WinSock), CAN bus, EtherCAT |
| **Database / Storage** | File-based (scan result files, ticket directories), `DataAccess.dll` (MDB/Access), INI files |

**Sub-module breakdown (key areas):**

| Sub-module | Type | Responsibility |
|---|---|---|
| `Sources/dds/` (~110 modules) | Processing Engine | Defect Detection Server — algorithm pipeline, GPU compute, frame processing |
| `Sources/machine/` (~130 modules) | Hardware Layer | EFEM/loader control, motion (Etel/EtherCAT), safety, IO, CAN bus, SECS/GEM |
| `Sources/objects/` (~65 modules) | Business Objects | Alignment, Job, AutoFocus, DataAccess, WaferInfo, ScanGeometry |
| `Sources/system/` (~120 modules) | Core System | Camera drivers (17 camera types), optics, MIL imaging, scenarios, calibration, PubSub |
| `Sources/Grabbing/` (~27 modules) | Image Acquisition | Camera frame grabbing for Area, Clip, Color, CSP, CTS, IR, TDI cameras |
| `Sources/calibration/` (~35 modules) | Calibration | System calibration algorithms, UI, gain/offset, objective, periodic |
| `Sources/UI/` (6 modules) | Frontend | Falcon WPF main UI, navigation, shared components |
| `Sources/Components/` (~45 modules) | Mid-tier | Display, WaferMap, RTP, ScanResults, Dialogs |
| `Sources/JobParts/` (~35 modules) | Recipe Engine | Job recipe parts — optics config per camera, recipe steps, zones, materials |
| `Sources/ToolManagement/` (~26 modules) | SECS/GEM | Semiconductor equipment integration: `SecsGemClient`, `SecsGemDriver`, `TAC.Net`, `TopiClient.Net` |
| `Sources/Tracing/` (8 modules) | Observability | `CamtekLogger.NET`, `Log4cpp`, `LogManager`, `SystemLogger` |
| `Sources/Automation.Mng/` (4 modules) | Automation | Batch execution, wafer loader, wafer database |
| `Sources/InspecTune/` (10 modules) | Tuning | Inspection parameter tuning system |
| `Sources/TestAutomationAPI/` (~29 modules) | Test Automation | `AOI_Main`, `Engine.FlaUI`, `RunnerGui`, `TestAutomationSDK`, `ReportGenerator` |
| `Sources/Compilation/` (19 tools) | Build Tools | AxInterop, TlbToIdl, VbAnalyzer, RegisterComponent |
| `Sources/Simulator/` (7 modules) | Simulation | Frame simulation, VCam, recording/playback |
| `Sources/Plugins/` (5 modules) | Office Integration | Excel 2003/2016, OpenOffice wrappers |

---

### 2.2 BSIHR — BSI High Resolution

| Field | Details |
|---|---|
| **Name** | BSIHR |
| **Type** | Client-Server desktop application |
| **Language & Framework** | C# (.NET 6), C++/CLI, Protobuf / ASP.NET Core (Kestrel), gRPC, WPF |
| **Responsibility** | BSI High Resolution bump/wafer inspection — image processing, calibration, hardware control |
| **Entry Point** | Server: `BSIHR.MainServer` (ASP.NET Core exe), Client: `BSIHR.UI` (WPF WinExe), DB: `BSIHR.Database` (ASP.NET Core web host) |
| **Port** | gRPC over HTTP/2 — default **`http://127.0.0.1:5678`** (configurable via `AppSettings.MainPortNumber`); ServiceControl on port **1234**; Database server on port **4578** [NEW] |
| **Database / Storage** | SQLite via EF Core 6.0 (`Microsoft.EntityFrameworkCore.Sqlite.Core` 6.0.11); `RecipeDataContext` with `Recipes`, `Optics`, `AlgoScenarios` tables; also SQL Server via `System.Data.SqlClient`. Auth: custom GUID-based ownership token over gRPC metadata — **no TLS, no JWT, no mTLS** (`ChannelCredentials.Insecure`). Client sends session GUID in `"Ownership"` metadata header; server validates via ASP.NET `IAuthorizationHandler` [NEW] |

**BSIHR Modules:**

| Module | Type | Responsibility |
|---|---|---|
| `BSIHR.MainServer` | API (gRPC server) | Central server — hosts all gRPC services |
| `BSIHR.ServerServices` | Library | Server-side service implementations |
| `BSIHR.ImageProc` | Library | Image processing engine with workflows |
| `BSIHR.ImageServices` | Library | Image acquisition/streaming |
| `Calibration.Service` | Library | Camera/system calibration |
| `CalibrationAlgoRunner` | Library | Calibration algorithm execution |
| `BSIHR.ServiceControl` | Library | Service lifecycle control |
| `BSIHR.Client` | Library | gRPC client proxy |
| `BSIHR.ClientServices` | Library | Client-side service layer |
| `BSIHR.UI` | Frontend (WPF) | Desktop application |
| `BSIHR.UI.Calibration` | Frontend Module | Calibration UI pages |
| `BSIHR.UI.Common` | Library | Shared UI controls |
| `BSIHR.UI.Themes` | Library | WPF themes/resource dictionaries |
| `BSIHR.UI.Infrastructure` | Library | UI DI, navigation |
| `BSIHR.SimDeployer` | Tool | Simulator deployment |
| `BSIHR.Common` | Library | Shared models, events, helpers |
| `BSIHR.Services.Common` | Library | **gRPC Protobuf service contracts** (`.proto` files) |
| `BSIHR.JobClient` | Library | Job scheduling client |
| `BSIHR.DataContext` | Library | EF Core `DbContext` (SQLite) |
| `BSIHR.Database` | API (web host) | Database server |
| `BSIHR.DataServices` | Library | Data service interfaces |
| `BSIHR.AppEntities` | Library | Domain entities (Job, Light, AlgoScenarios) |
| `BSIHR.Algo` suite (9 modules) | Library | Native algorithm wrappers: `BSIHR.Algo`, `BSIHR.Calib`, `BSIHR.Projections`, `BSIAlignImp`, `BSIEdgeDetect`, `NotchDetection`, `WaferMosaicCLI/Imp` |
| `BSIHR.HW` suite | Library | Hardware abstraction: drivers, chuck, scan routes, CAN bus, STIL camera |

---

### 2.3 DataServer (ScanResultsServerAPI)

| Field | Details |
|---|---|
| **Name** | DataServer / ScanResultsServerAPI |
| **Type** | Multi-service WCF host (Tier 1 architecture) |
| **Language & Framework** | C# / .NET Framework 4.8, WCF (`net.tcp`), LinqToDB, protobuf-net |
| **Responsibility** | Centralized data services for inspection results — scan results CRUD, verification, classification, wafer layout, images, CMM integration, user auth |
| **Entry Point** | `DataServer.Host` (WCF ServiceHost, `Tier1/Modules/DataServer/Host/`) |
| **Port** | See port table below |
| **Database / Storage** | SQLite via LinqToDB **2.6.4** (`SQLiteDataProvider`), file-based inspection DB (`FileDB.INFS`), file-system message queue (JSON on disk). DB path: `C:\bis\data\SWS\dataserver\DataServerDB.sqlite3`. Auth DB: `C:\bis\data\SWS\dataserver\Auth.db3` [NEW] |

**DataServer Port Map:**

| Port | Protocol | Service |
|---|---|---|
| `8002` | `net.tcp` | MainServer |
| `8012` | `net.tcp` | UserAuth |
| `8022` | `net.tcp` | Identification |
| `8032` | `net.tcp` | CMM |
| `8202` | `net.tcp` | T1.ScanResults |
| `8212` | `net.tcp` | T1.Classifiers |
| `8222` | `net.tcp` | T1.WaferLayout |
| `8232` | `net.tcp` | T1.DiceAttributes |
| `8242` | `net.tcp` | T1.Images |
| `8252` | `net.tcp` | T1.VerificationImages |
| `8262` | `net.tcp` | T1.Verification |
| `8272` | `net.tcp` | T1.InspectionResult |

Port naming convention: `8XY2` where X=priority, Y=service, 2=net.tcp.  
Default base address: `net.tcp://localhost:8000/DataServer`

**DataServer API Modules:**

| API Module | Responsibility |
|---|---|
| `Camtek.API.MainServer` | Master server coordination |
| `Camtek.API.ScanResults` | Scan results CRUD + events |
| `Camtek.API.InspectionResults` | Inspection result data |
| `Camtek.API.Verification` | Defect verification + events |
| `Camtek.API.VerificationImage` | Verification images |
| `Camtek.API.Images` | Image storage/retrieval |
| `Camtek.API.WaferLayout` | Wafer layout + models |
| `Camtek.API.Classifiers` | Defect classifiers |
| `Camtek.API.DiceAttributes` | Die-level attributes |
| `Camtek.API.Users` | User management |
| `Camtek.API.IIdentification` | Identity services |
| `Camtek.API.CMM` | CMM integration + events |
| `Camtek.API.VirtualData` | Virtual/plugin data |

---

### 2.4 MDC — Manual Defect Classification

| Field | Details |
|---|---|
| **Name** | MDC |
| **Type** | Frontend (WPF desktop client) |
| **Language & Framework** | C# / .NET Framework 4.8, WPF, Prism 6 (Unity), WCF client proxies |
| **Responsibility** | Manual defect classification — wafer/die/defect visualization, verification workflows, lot management, user auth |
| **Entry Point** | `MDC/App.xaml.cs` → Prism `UnityBootstrapper` → loads `MDC.MainModule` |
| **Port** | None (client-only) |
| **Database / Storage** | None directly — all data via WCF to DataServer; local XML/cache for snapshots |

---

### 2.5 RMS — Recipe Management System

| Field | Details |
|---|---|
| **Name** | RMS |
| **Type** | 3-tier: API Server + Tool Agent + WPF Client + Background Worker |
| **Language & Framework** | C# / .NET 6.0, ASP.NET Core, gRPC (protobuf-net code-first), SignalR, EF Core 6 (SQLite), JWT, Serilog |
| **Responsibility** | Recipe/job lifecycle — upload, deploy, archive, qualify, remove jobs across servers and tools |
| **Entry Point** | Server: `Camtek.RMS.Service/Program.cs`, Tool: `Camtek.RMS.Service4Tool/Program.cs`, Client: `Camtek.RMS/App.xaml.cs`, Worker: `Camtek.Rms.Worker/Program.cs` |
| **Port** | Server: `5001`, Tool Agent: `5020` |
| **Database / Storage** | SQLite via EF Core 6 (`Data Source={RMSPath}\RMS.sqlite`), file system storage (`C:\BIS\RMS\Server\RMSStorages` server, `C:\Job` tool) |

**RMS gRPC Services:**

| Service | Responsibility |
|---|---|
| `AuthService` | JWT authentication |
| `JobService` | Job/recipe CRUD and lifecycle |
| `ToolService` | Tool registration and management |
| `StorageService` | File storage operations |
| `NotifierService` | Real-time event notification |
| `HistoryService` | Job history and audit trail |
| `TimeService` | Server time synchronization |
| `ReportsService` | Recipe report generation |
| `SettingsService` | System settings management |

**RMS SignalR Hub:** `/notify` — pushes `ToolChangedDto`, `JobChangedDto`, `DeliveryPlanDto`

---

### 2.6 CMM — Coordinate Measuring Machine

| Field | Details |
|---|---|
| **Name** | CMM.NET |
| **Type** | Desktop application (WinForms) |
| **Language & Framework** | VB.NET (main app), C# (support modules) / .NET Framework 4.8, WinForms, NUnit |
| **Responsibility** | Inspection ticket processing — converter engine, report generation, wafer map matching, parallel export |
| **Entry Point** | `CMM.NET.Main` (VB.NET WinExe, `Sub Main` startup) |
| **Port** | None (desktop) |
| **Database / Storage** | MDB/Access via `DataAccess.dll`, file-based ticket directories, connects to DataServer via WCF (`Camtek.API.CMM`) |

**CMM Sub-modules:**

| Module | Responsibility |
|---|---|
| `CMM.NET` | Core converter engine, KLARF viewer, map parsing |
| `CMM_Parallel` | Parallel export/conversion (WinForms) |
| `CMM_Parallel_Runner` | WPF runner GUI for parallel CMM |
| `CMM_Parallel.Common` | Shared parallel execution types |
| `CMM_Utils` | Utilities (alerts, progress) |
| `CMMParamsCollection` | Parameter collection parsing |
| `CMMExecuteAssembly` | Console exe for converter execution |
| `CMM_BadTicketRestorator` | Corrupted ticket restoration |
| `GraphControls` | Wafer map / defect visualization WinForms controls |
| `LightInfrastructure` | Lightweight infrastructure shared with CmmLightConverters |
| `Plugins/` | Spreadsheet wrappers: Excel 2003/2016, LibreOffice, OpenOffice |

---

### 2.7 CmmLightConverters

| Field | Details |
|---|---|
| **Name** | CmmLightConverters |
| **Type** | Library (300+ converters) |
| **Language & Framework** | C# / .NET Framework 4.8 |
| **Responsibility** | File format conversion for semiconductor inspection results — KLARF, SEMI E142, SINF, CSV, Excel, TDX, and 300+ customer-specific formats (Samsung, TSMC, Intel, Hynix, Infineon, etc.) |
| **Entry Point** | Library: `CmmLightConverters.dll` (loaded by CMM.NET); CLI: `ExecuteMethod` console exe |
| **Port** | None |
| **Database / Storage** | File I/O (reads inspection data, writes converted reports) |

---

### 2.8 SystemCalibration

| Field | Details |
|---|---|
| **Name** | SystemCalibration |
| **Type** | Desktop application (WPF, plugin-based) |
| **Language & Framework** | C# / .NET Framework 4.6.1, WPF, Prism 5 (MEF), Telerik UI |
| **Responsibility** | Hardware calibration — cameras (2D, Clip, Clip2, Color, CTS, CSP, IRScan, TDI, CCS), optics, chuck, positions |
| **Entry Point** | `SystemCalibration.Shell/App.xaml.cs` → `NGSUIBootsrapper : MefBootstrapper` |
| **Port** | None |
| **Database / Storage** | None — interfaces with BIS hardware layer via DLL references from `c:\bis\bin\` |

**Dynamically loaded modules (from ModuleCatalog XAML):**
`System.DataContext`, `System.ModuleInits.Machine`, `System.Hardware.Chuck`, `CameraManager`, `System.Hardware.Cameras`, `Camera2D`, `CameraClip2`, `CameraClip`, `CameraColor`, `CameraCTS`, `CameraCSP`, `CameraIRScan`, `CameraTDI`, `CameraCCS`, `System.Optics.Converters`, `System.Optics.Services`, `IntegrationTests`, `CcsTools`, `HighMagCalPlugin`

---

### 2.9 ToolAnalytics

| Field | Details |
|---|---|
| **Name** | ToolAnalytics |
| **Type** | Desktop application (WPF standalone) |
| **Language & Framework** | C# / .NET 7.0, WPF, CommunityToolkit.Mvvm, LiveCharts.Wpf, MS.Extensions.DI |
| **Responsibility** | Light Channel Calibration (LCC) validation — reads machine config INI files, displays calibration parameters, color filter analysis, charting |
| **Entry Point** | `ToolAnalytics.Ui/App.xaml` → `Bootstrapper.cs` |
| **Port** | None |
| **Database / Storage** | File system — reads `c:\falcon\data\machine\{MachineName}\config.ini`, LCC files, color filter configs |

---

### 2.10 LogLens

| Field | Details |
|---|---|
| **Name** | LogLens |
| **Type** | Development tool (WPF + TCP sniffer agent) |
| **Language & Framework** | C# / .NET 7.0 (UI), .NET Standard 2.1 (Core) |
| **Responsibility** | Real-time log viewer — online TCP streaming from remote sniffers, offline file analysis, structured log parsing (log4net format), query language |
| **Entry Point** | `LogLens.Ui` (WPF WinExe) |
| **Port** | TCP (dynamic — sniffer-to-UI streaming) |
| **Database / Storage** | None — streams/reads log files |

---

### 2.11 DeployUI

| Field | Details |
|---|---|
| **Name** | DeployUI2 |
| **Type** | Developer Tool (PowerShell + WPF GUI) |
| **Language & Framework** | PowerShell |
| **Responsibility** | Developer workstation orchestrator — git pull, MSBuild compilation of all solutions (Falcon, EBI, FAR, TestAutomation, CMM, Common, SystemCalibration), COM registration, binary deployment, simulator mode |
| **Entry Point** | `DeployUI2.cmd` → `DeployUI2.ps1` |
| **Port** | None |
| **Database / Storage** | None |

---

## 3. Communication Map

### 3.1 Inter-Service Communication

| Source | Target | Protocol | Method/Endpoint | Auth | Notes |
|---|---|---|---|---|---|
| **MDC** | **DataServer** (MainServer) | WCF `net.tcp` | `net.tcp://localhost:8002/DataServer` | Auth proxy | Sync, duplex callbacks |
| **MDC** | **DataServer** (ScanResults) | WCF `net.tcp` | `net.tcp://localhost:8202/...` | Auth proxy | Duplex — `ScanResultsNotifierProxy` for push events |
| **MDC** | **DataServer** (Verification) | WCF `net.tcp` | `net.tcp://localhost:8262/...` | Auth proxy | Duplex — `VerificationNotifierProxy` |
| **MDC** | **DataServer** (CMM) | WCF `net.tcp` | `net.tcp://localhost:8032/...` | Auth proxy | Duplex — `CmmServiceNotifierProxy` |
| **MDC** | **DataServer** (InspectionResults) | WCF `net.tcp` | `net.tcp://localhost:8272/...` | Auth proxy | Sync |
| **MDC** | **DataServer** (DiceAttributes) | WCF `net.tcp` | `net.tcp://localhost:8232/...` | Auth proxy | Sync |
| **MDC** | **DataServer** (Classifiers) | WCF `net.tcp` | `net.tcp://localhost:8212/...` | Auth proxy | Sync |
| **MDC** | **DataServer** (VerificationImage) | WCF `net.tcp` | `net.tcp://localhost:8252/...` | Auth proxy | Sync |
| **MDC** | **DataServer** (Images) | WCF `net.tcp` | `net.tcp://localhost:8242/...` | Auth proxy | Sync |
| **MDC** | **DataServer** (WaferLayout) | WCF `net.tcp` | `net.tcp://localhost:8222/...` | Auth proxy | Sync |
| **MDC** | **DataServer** (Users) | WCF `net.tcp` | `net.tcp://localhost:8012/...` | Auth proxy | Sync |
| **CMM** | **DataServer** (CMM API) | WCF `net.tcp` | `Camtek.API.CMM` on port 8032 | Auth proxy | Sync — ticket operations |
| **BIS (Falcon)** | **DataServer** | WCF `net.tcp` | Various API contracts | Auth proxy | Scan result submission, image upload |
| **BSIHR.UI** | **BSIHR.MainServer** | gRPC (HTTP/2) | Protobuf services (see below) | gRPC metadata | Sync |
| **BSIHR.UI** | **BSIHR.MainServer** | gRPC | `CalibrationService` | gRPC metadata | Calibration operations |
| **BSIHR.UI** | **BSIHR.MainServer** | gRPC | `HardwareService` | gRPC metadata | Hardware control |
| **BSIHR.UI** | **BSIHR.MainServer** | gRPC | `JobService` | gRPC metadata | Job/recipe management |
| **BSIHR.UI** | **BSIHR.MainServer** | gRPC | `LoaderService` | gRPC metadata | Wafer handling |
| **BSIHR.UI** | **BSIHR.MainServer** | gRPC | `CameraServiceClient` | gRPC metadata | Camera control |
| **BSIHR.UI** | **BSIHR.MainServer** | gRPC | `NavigatorServiceClient` | gRPC metadata | Stage navigation |
| **BSIHR.UI** | **BSIHR.MainServer** | gRPC | `OwnershipServiceClient` | gRPC metadata | Resource ownership |
| **BSIHR.UI** | **BSIHR.MainServer** | gRPC | `ScenarioServiceClient` | gRPC metadata | Scan scenarios |
| **BSIHR.UI** | **BSIHR.MainServer** | gRPC | `ServiceControlClient` | gRPC metadata | Service lifecycle |
| **BSIHR.UI** | **BSIHR.MainServer** | gRPC | `AlgorithmsServiceClient` | gRPC metadata | Algorithm execution |
| **RMS Client** | **RMS Server** | gRPC-Web | `http://localhost:5001` (9 services) | JWT Bearer | Sync; all endpoints `.EnableGrpcWeb()` |
| **RMS Client** | **RMS Server** | SignalR | `http://localhost:5001/notify` | JWT Bearer | Async push — `ToolChangedDto`, `JobChangedDto`, `DeliveryPlanDto` |
| **RMS Server** | **RMS Service4Tool** | gRPC-Web | `http://localhost:5020` | JWT Bearer | Tool-to-server recipe sync |
| **LogLens.Sniffer** | **LogLens.Ui** | TCP | Raw TCP stream | None | Async — `OutputDebugString` capture |
| **BIS (PubSub)** | **RabbitMQ** | AMQP | Port 5672 | guest/guest | Async pub/sub messaging |
| **DataServer** internal | **File-system queue** | File I/O | JSON files on disk | None | Async — `FileSystemMessageProducer` / `DiskMessageConsumer` with Polly retry |
| **BIS (DDS)** | **BIS (main)** | COM IPC | DCOM local server | None | In-process / cross-process COM |
| **BIS (Grabbing)** | **BIS (DDS)** | IPC | `GrabIPC` / `AcqIPC` / `DdsIPC` | None | Named-pipe-style IPC for frame data |
| **BIS (machine)** | Hardware | CAN bus | `CanApi`, `CanSrv` | None | Hardware I/O |
| **BIS (machine)** | Hardware | EtherCAT | `EtherCATDriver` | None | Motion control |
| **BIS (machine)** | Hardware | Modbus | `ModBusIO` | None | I/O controller |
| **BIS (machine)** | Hardware | TCP/Socket | `WinSockTcp`, `CommunicationSrv` | None | Equipment comm |
| **BIS (machine)** | EFEM | E84 | `E84Driver` | None | Wafer load/unload |
| **BIS (ToolManagement)** | Factory Host | SECS/GEM (HSMS) | `SecsGemClient`, `SecsGemDriver` | None | Semiconductor equipment standard |
| **BIS** | **SystemCalibration** | DLL | Direct DLL references from `c:\bis\bin\` | None | ⚠️ **Shared binaries** — tight coupling |
| **CmmLightConverters** | **CMM** | DLL | `LightConvertersDllHandler` loads `CmmLightConverters.dll` | None | Library loading |
| **DataServer** | RabbitMQ mgmt | HTTP | `http://localhost:15672` (guest/guest) | Basic | Via `WebApiHelper` — [UNCLEAR if actively used or legacy] |

### 3.2 Code Smells / Architectural Notes

| Issue | Details |
|---|---|
| ⚠️ **Shared binary coupling** | SystemCalibration references BIS DLLs directly from `c:\bis\bin\`. `SystemCalibrationDllUpdater` syncs versions. |
| ⚠️ **COM-based IPC** | BIS relies heavily on COM inter-process communication with proxy/stub DLLs (AlgManager, Alignment, Job, PreProc) — fragile registration dependency |
| ⚠️ **File-system message queue** | DataServer uses JSON-on-disk queuing (`FileSystemMessageProducer` / `DiskMessageConsumer`) instead of a proper message broker for core messaging |
| ⚠️ **Mixed .NET versions** | .NET Framework 4.6.1 (SystemCalibration), 4.8 (BIS, DataServer, MDC, CMM), .NET 6 (BSIHR, RMS), .NET 7 (ToolAnalytics, LogLens) |
| ⚠️ **VB6 legacy** | 96 VB6 projects still in BIS, requiring COM interop bridges |

---

## 4. Data Models & Contracts

### 4.1 Core Domain Entities

| Entity | Owner Service | Notes |
|---|---|---|
| Wafer | BIS (WaferInfo), DataServer (WaferLayout) | Wafer identification, layout, die map |
| Die | BIS (objects), DataServer (DiceAttributes) | Die coordinates, classification, attributes |
| Defect | BIS (dds), DataServer (InspectionResults) | Detection results, classification, images |
| ScanResult | BIS, DataServer (ScanResults) | Full scan result sets |
| Ticket | DataServer, CMM | Inspection ticket lifecycle |
| Job / Recipe | BIS (Job.NET), BSIHR (AppEntities), RMS (JobService) | Inspection recipe definition |
| Verification | DataServer (Verification) | Manual review records |
| Classifier | DataServer (Classifiers) | Defect classification rules |
| User | DataServer (Users), RMS (AuthService) | Authentication & authorization |
| Frame | BIS (Grabbing, dds) | Acquired image frames |
| CalibrationData | BIS (calibration), BSIHR (CalibrationService) | Hardware calibration parameters |
| GoldenImage | BIS (processing) | Reference images for comparison |
| AlgoScenario | BSIHR (AppEntities) | Algorithm configuration |

### 4.2 Shared Contracts & Schemas

| Contract Type | Location | Format |
|---|---|---|
| **gRPC Protobuf** (BSIHR) | `BSIHR/Sources/BSIHR.Services.Common/` | `.proto` files: `CalibrationData.proto`, `CalibrationService.proto`, `HardwareData.proto`, `HardwareService.proto`, `JobData.proto`, `JobService.proto`, `LoaderData.proto`, `LoaderService.proto` |
| **gRPC code-first** (RMS) | `CamtekSoftwareSolutions/rms/Camtek.RMS.Contracts/` | C# interfaces with protobuf-net attributes (code-first, no `.proto` files) |
| **WCF Data Contracts** (DataServer) | `CamtekSoftwareSolutions/dataserver/API/*/Contracts/` | C# `[DataContract]` classes per service module |
| **Queue Messages** (DataServer) | `CamtekSoftwareSolutions/dataserver/Camtek.QueueMessages/` | protobuf-net serialized: `CMMBaseRequest`, `CMMExportRequest`, `CmmTicketCreationRequestMessage`, `CmmTicketState`, `ExportMapMessage`, `ScanReadyMessage`, `ScanResultChangedMessage`, `SaveWaferVerificationProcessChangesMessage`, `UpdateDiceClassChangesMessage`, `UpdateInspectionResultsMessage` |
| **COM Type Libraries** (BIS) | `BIS/bin/Win32.tlb` and various `*PS_d.dll` | COM IDL/TLB for `AlgManager`, `Alignment`, `Job`, `PreProc`, `AlgObjects`, `utils` |
| **Shared Globals** | `CamtekSoftwareSolutions/dataserver/Camtek.Shared.Global/` | Cross-cutting shared types |
| **External Verification Data Model** | `CamtekSoftwareSolutions/dataserver/Camtek.ExternalVerificationDataModel/` | Verification data exchange format |

### 4.3 Shared Libraries for Contracts

| Library | Used By | Purpose |
|---|---|---|
| `Camtek.Auth.Proxy` | DataServer, MDC | WCF auth service proxy (`PermissionServiceProxy`, `UserAuthServiceProxy`, `UserServiceProxy`) |
| `Camtek.Common.WCF.ServiceModel` | DataServer | Custom WCF bindings, duplex, utilities |
| `Camtek.QueueSharedModels` | DataServer internal | File-system queue: `FileSystemMessageProducer`, `DiskMessageConsumer` |
| `Camtek.Common.Logging` | DataServer | log4net-based logging |
| `Camtek.Common.Tools` | DataServer | HTTP helpers (`WebApiHelper`) |
| `CamtekSoftwareSolutions/Common/` | Multiple | Shared: `AssemblyInfo/`, `Coordinatesystems/`, `Extensions/`, `References/` |
| `LightInfrastructure` | CMM, CmmLightConverters | Shared lightweight infrastructure |
| `BIS/Sources/system/System.Common/` | BIS-wide | Common system types |

---

## 5. Infrastructure & Dependencies

### 5.1 External Third-Party APIs / SDKs

| Dependency | Used By | Purpose |
|---|---|---|
| **Matrox MIL** (Matrox Imaging Library) | BIS (`MilExt`, `MilWrapper`, `ManagedMil`, `RadientConnector/Controller`) | Image acquisition and processing — core imaging SDK |
| **ONNX Runtime** | BIS (`OnnxWrapper` in `Sources/dds/`) | ML model inference for defect detection |
| **Cimetrix** | BIS (`SecsGemClient`, `SecsGemDriver`) | SECS/GEM semiconductor equipment communication |
| **Telerik UI for WPF** | MDC, SystemCalibration | `RadGridView`, `RadWindow`, UI controls |
| **LiveCharts.Wpf** | ToolAnalytics | Charting |
| **Plotly.js** | Install/iTOOLS | Browser-based visualization |
| **ETEL** | BIS (`EtelDriver`) | Motion controller SDK |
| **EtherCAT** | BIS (`EtherCATDriver`) | Real-time motion bus |
| **STIL** | BIS (`StilScanner`), BSIHR (`BSIHR.HW`) | Confocal chromatic sensor |
| **Cognex** | BIS (`Scripts/CognexOCR1700`) | OCR vision system |
| **OpenCL** | BIS (~15 modules) | GPU-accelerated algorithms |
| **FiltAir** | ExternalTools | Cleanroom air filtration |

### 5.2 Auth / Identity Provider

| System | Auth Mechanism |
|---|---|
| **DataServer** | Custom WCF auth — `Camtek.Auth.Proxy` (`PermissionServiceProxy`, `UserAuthServiceProxy`, `UserServiceProxy`), WCF port 8012 |
| **RMS** | Custom JWT — `JwtMiddleware` on server, `Bearer` token via gRPC metadata; ASP.NET Identity with SQLite backend; OpenSSL X.509 certificates (CA → server/client PFX) |
| **MDC** | Proxied through DataServer auth (login dialog → `UserAuthServiceProxy`) |
| **BSIHR** | gRPC metadata-based auth [UNCLEAR — specific mechanism not determined] |

### 5.3 Observability Stack

| Component | Technology | Details |
|---|---|---|
| **Logging (BIS)** | log4net + custom | `CamtekLogger.NET`, `Log4cpp` (native), `LogManager`, `SystemLogger`, `CamtekLogAppenders` |
| **Logging (DataServer)** | log4net | `Camtek.Common.Logging` (log4net wrapper) |
| **Logging (RMS)** | Serilog | Console + File sinks; server: `c:/bis/ErrorLog/RMS/Camtek.RMS.Server_.log`, tool: `...Serice4Tool_.log`, client: `...Camtek.RMS_.log` |
| **Logging (ToolAnalytics)** | log4net 3.0.4 | Console + File (`C:\Temp\ToolAnalytics\ToolAnalytics.log`, rolling 10MB×5) + `CamtekIndexedFileAppender` (`c:\bis\errorlog`, 21 days, 1GB max) |
| **Logging (MDC)** | log4net | Via infrastructure |
| **Audit Logging** | Serilog (RMS) | Separate audit trail: `C:/Bis/SystemAudit/RMS/AuditLogServer_.log` |
| **Log Viewer** | LogLens | Real-time log viewer/analyzer with TCP sniffer agents, structured parsing, query language |
| **Performance Timing** | Custom `TimeLogger` (BIS/dds) | C++ timing instrumentation for DDS pipeline |
| **System Logger UI** | BIS (`SystemLoggerUI`) | Built-in log viewer for BIS |
| **Metrics** | [UNCLEAR] | No Prometheus, Grafana, or OpenTelemetry found |
| **Tracing** | [UNCLEAR] | No distributed tracing (Jaeger, Zipkin) found |

### 5.4 CI/CD Pipeline

| Pipeline | File | Template (from `PipelineRepo`) | Trigger | Purpose |
|---|---|---|---|---|
| CI | `xbuild/pipeline_ci.yml` | `ci_pipeline.yml` | `main` | Continuous integration build |
| PR | `xbuild/pipeline_pr.yml` | `base_pipeline.yml` | `main` | Pull request validation |
| Release | `xbuild/pipeline_rel.yml` | `rel_pipeline.yml` | `main` | Release build |
| Test | `xbuild/pipeline_test.yml` | `test_pipeline.yml` | `main` | Configurable compile testing (params: singleThreaded, compileLoops, per-solution toggles for FS_COMMON, FS_EBI_ONLY, Falcon_2022) |
| SWS | `xbuild/sws.yml` | `sws_pipeline.yml` | `main` | Software Solutions (DataServer+MDC+RMS) build |
| SWS Private | `xbuild/sws_private.yml` | `sws_pipeline_private.yml` | `main` | Private SWS build (params: MDC, DATASERVER toggles) |
| SWS Private Dev | `xbuild/sws_private_dev.yml` | [UNCLEAR] | `main` | Dev private build |

All pipelines use **Azure DevOps Pipelines** with templates from an external `Git/PipelineRepo` repository.

**Local build orchestration:** `DeployUI2.ps1` (PowerShell + WPF GUI) — builds solutions via MSBuild locally, handles COM registration, binary deployment.

**Distributed compile farm:** `BIS/comp1.cmd` through `comp5.cmd` — distribute source to 5 compilation machines (`\\comp_1` – `\\comp_5`) for parallel builds.

---

## 6. Known Patterns & Conventions

### 6.1 Naming Conventions

| Element | Convention | Examples |
|---|---|---|
| **C# projects** | `Camtek.{Area}.{Module}` or `{Area}.{Module}` | `Camtek.API.ScanResults`, `System.Hardware.Cameras.Camera2D`, `BSIHR.ImageProc` |
| **C++ projects** | Short names, sometimes with `_d` debug suffix | `AlgManager_d.dll`, `MilExt`, `DdsProcessor` |
| **COM DLLs** | `{Name}_d.dll` + `{Name}PS_d.dll` (proxy/stub) | `AlgManager_d.dll` / `AlgManagerPS_d.dll` |
| **Namespaces** | Match project name | `Camtek.API.ScanResults`, `BSIHR.Common` |
| **Solution files** | Descriptive, underscored | `Falcon_2022.sln`, `FS_COMMON.sln`, `FS_EBI_ONLY.sln` |
| **Build props** | `Camtek.{Lang}.Common.Properties.props` | `Camtek.CSharp.Common.Properties.props`, `Camtek.Cpp.Common.Properties.props` |
| **Env vars** | [UNCLEAR] | No `.env` files found; configuration via `App.config`, `appsettings.json`, INI |
| **Folders** | PascalCase | `ScanResults`, `WaferLayout`, `CameraManager` |
| **File paths (machine)** | `c:\bis\bin\`, `c:\falcon\data\`, `c:\bis\errorlog\`, `c:\Job\` | Standard deployment paths |

### 6.2 Error Handling Strategy

| Area | Strategy |
|---|---|
| **WCF (DataServer)** | `FaultException<T>` with custom fault contracts (`API/Common/Faults/`) |
| **gRPC (RMS)** | gRPC status codes; Polly retry policies for resilience |
| **gRPC (BSIHR)** | Standard gRPC error handling via Grpc.Core |
| **File-system queue** | Polly retry policies in `DiskMessageConsumer` |
| **BIS (native)** | COM `HRESULT` error codes, VB6 `On Error` handlers |
| **MDC** | Auto-reconnect with 3-second polling cycle (`ConnectionSwitchManager`) for lost service connections |
| **General** | log4net / Serilog logging at all levels |

### 6.3 Testing Approach

| Area | Framework | Type | Notes |
|---|---|---|---|
| **BIS** | NUnit, MSTest | Unit + Integration | ~100 test projects in `Sources/tests/`; `TestAutomationAPI` for E2E with FlaUI (UI automation) |
| **BSIHR** | [UNCLEAR] | Integration | `FrameToSectorIntersectionTest` in `Tests/` |
| **CMM** | NUnit | Unit | `CMM.NET.UnitTests` (`[TestFixture]`, `[Test]`) |
| **CmmLightConverters** | NUnit | Unit | 300+ converter tests, test data from FTP `10.5.0.119` |
| **DataServer** | [UNCLEAR] | [UNCLEAR] | Test projects in `CamtekSoftwareSolutionsOld` but not in current |
| **RMS** | [UNCLEAR] | [UNCLEAR] | No test project found in current RMS.sln |
| **MDC** | [UNCLEAR] | [UNCLEAR] | `Camtek.UnitTests` exists in Old version only |
| **SystemCalibration** | `IntegrationTests` | Integration | Loaded dynamically via Prism module catalog |
| **ToolAnalytics** | xUnit/NUnit | Unit | `ToolAnalytics.Tests` (scaffold) |
| **LogLens** | [UNCLEAR] | Unit | `LogLens.Tests` project |
| **Test Automation SDK** | Custom + FlaUI | E2E | `TestAutomationAPI/` with `AOI_Main`, `Engine.FlaUI`, `RunnerGui`, `ResultsComparison`, `ReportGenerator` |
| **Test Data** | Shared | Fixture | `UnitTestsData/` at repo root; NUnit test categories for test organization |

Coverage targets: [UNCLEAR] — no coverage configuration files found.

### 6.4 Branching Strategy

| Aspect | Details |
|---|---|
| **Main branch** | `main` (all pipeline triggers) |
| **PR workflow** | Azure DevOps PRs via `CreatePR.exe` tool (`xgitscripts/CreatePR/`) |
| **Pipeline-based validation** | PR pipeline (`pipeline_pr.yml`) runs on `main` pushes [UNCLEAR — may be branch-filtered in PipelineRepo template] |
| **Strategy** | Likely **trunk-based** or **gitflow-lite** — all pipelines trigger on `main`; PRs via Azure DevOps; AI-assisted workflow (`req-verify` → `coding` → `code-review`) enforces structured handoffs |

### 6.5 AI-Assisted Development Workflow (Camtek Pilot 2026)

| Command | Purpose | Tool |
|---|---|---|
| `/req-build` | Requirements building from ADO work items, text, or files | `req-build.md` |
| `/req-verify` | Requirements verification + codebase impact analysis via Sourcegraph MCP | `.windsurf/workflows/req-verify.md` |
| `/coding` | Suggest-only coding assistance (never writes to production files directly) | `.windsurf/workflows/coding.md` |
| `/code-review` | Pre-commit structured code review anchored to PR Plan | `.windsurf/workflows/code-review.md` |

Integration tools: **Sourcegraph MCP** (http://10.5.1.149) for cross-repo search, **Azure DevOps MCP** for work item management.

---

## 7. Solution Files Summary

### BIS Master Solutions (`BIS/build/`)

| Solution | Scope |
|---|---|
| `Falcon_2022.sln` | **Main Falcon system** — full build |
| `FS_COMMON.sln` | Falcon-System common modules |
| `FS_EBI_ONLY.sln` | EBI-only subset |
| `FalseAlarmReduction.sln` | FAR subsystem |
| `TestAutomationAPI.sln` | Test automation framework |
| `CMM_2023.sln` | CMM integration |
| `DieEdit.sln` | Die editing app |
| `DieReconstructWpf.sln` | Die reconstruction |
| `Calib.sln` | Calibration |
| `Camtek.Display.sln` | Display subsystem |
| `FrameAlignment.sln` | Frame alignment |
| + 42 more specialized solutions | Various subsystems |

### Other Solutions

| Solution | Location |
|---|---|
| `ScanResultsServerAPI.sln` | `CamtekSoftwareSolutions/dataserver/` |
| `MDC.sln` | `CamtekSoftwareSolutions/mdc/` |
| `RMS.sln` | `CamtekSoftwareSolutions/rms/` |
| `Server.sln` / `Client.sln` | `BSIHR/build/` |
| `CmmLightConverters.sln` | `CmmLightConverters/Sources/` |
| `ToolAnalytics.sln` | `ToolAnalytics/` |
| `LogLens.sln` | `DevelopmentTools/LogLens/` |

---

## 8. Quick Reference: Ports & Endpoints

| Service | Port | Protocol | URI |
|---|---|---|---|
| DataServer MainServer | 8002 | `net.tcp` | `net.tcp://localhost:8002/DataServer` |
| DataServer UserAuth | 8012 | `net.tcp` | `net.tcp://localhost:8012/...` |
| DataServer Identification | 8022 | `net.tcp` | `net.tcp://localhost:8022/...` |
| DataServer CMM | 8032 | `net.tcp` | `net.tcp://localhost:8032/...` |
| DataServer ScanResults | 8202 | `net.tcp` | `net.tcp://localhost:8202/...` |
| DataServer Classifiers | 8212 | `net.tcp` | `net.tcp://localhost:8212/...` |
| DataServer WaferLayout | 8222 | `net.tcp` | `net.tcp://localhost:8222/...` |
| DataServer DiceAttributes | 8232 | `net.tcp` | `net.tcp://localhost:8232/...` |
| DataServer Images | 8242 | `net.tcp` | `net.tcp://localhost:8242/...` |
| DataServer VerificationImages | 8252 | `net.tcp` | `net.tcp://localhost:8252/...` |
| DataServer Verification | 8262 | `net.tcp` | `net.tcp://localhost:8262/...` |
| DataServer InspectionResult | 8272 | `net.tcp` | `net.tcp://localhost:8272/...` |
| RMS Server | 5001 | HTTP (gRPC-Web + SignalR) | `http://localhost:5001` |
| RMS Tool Agent | 5020 | HTTP (gRPC-Web) | `http://localhost:5020` |
| RMS SignalR Hub | 5001 | WebSocket | `http://localhost:5001/notify` |
| RabbitMQ AMQP | 5672 | AMQP | `amqp://localhost:5672` |
| RabbitMQ Management | 15672 | HTTP | `http://localhost:15672` |

---

## 9. Technology Version Matrix

| Technology | Version | Used By |
|---|---|---|
| .NET Framework | 4.6.1 | SystemCalibration |
| .NET Framework | 4.8 | BIS, DataServer, MDC, CMM, CmmLightConverters |
| .NET 6.0 | 6.0 | BSIHR, RMS |
| .NET 7.0 | 7.0 | ToolAnalytics, LogLens |
| .NET Standard | 2.1 | LogLens.Core |
| .NET Core | 3.1 | RMS Worker (scaffold) |
| C++ | MSVC (VS2019/2022) | BIS native (DDS, algorithms, grabbing, machine) |
| VB6 | 6.0 | BIS legacy UI/controls (96 projects) |
| EF Core | 6.0.11 | BSIHR, RMS |
| LinqToDB | **2.6.4** | DataServer [NEW] |
| WCF | .NET 4.8 | DataServer (server+client), MDC (client) |
| gRPC | Grpc.Core + Grpc.AspNetCore | BSIHR |
| gRPC (code-first) | protobuf-net.Grpc | RMS |
| SignalR | ASP.NET Core | RMS |
| Prism | 5 (MEF) | SystemCalibration |
| Prism | 6 (Unity) | MDC |
| Serilog | Latest | RMS |
| log4net | 3.0.4 / earlier | BIS, DataServer, MDC, ToolAnalytics |
| NUnit | 3.x | CMM, CmmLightConverters, BIS |
| SQLite | via EF Core / LinqToDB | BSIHR, RMS, DataServer |
| Matrox MIL | **10.0** (header rev 10.50.0734, ©1992–2021) | BIS (core imaging) — SDK at `BIS/Externals/Mil/10.0/X64/` [NEW] |
| Telerik WPF | **2022.1.222.45** (MDC), **2016.2.613.45** (SystemCalibration), **R2 2021** (RMS) | MDC, SystemCalibration, RMS — ⚠️ SystemCalibration is 6 years behind MDC [NEW] |
| CommunityToolkit.Mvvm | 8.4.0 | ToolAnalytics |
| LiveCharts.Wpf | 0.9.7 | ToolAnalytics |
| Polly | **6.0.1** (DataServer infrastructure), **7.2.2** (DataServer queue) — ⚠️ version mismatch | DataServer (retry/circuit breaker), RMS [NEW] |
| Newtonsoft.Json | [UNCLEAR] | Multiple |
| AutoMapper | [UNCLEAR] | MDC |

---

## 10. [NEW] Risk Register — Deep Dive (2026-04-04)

### CRITICAL

**Risk: Predictable JWT signing key in RMS**
- **Location:** `CamtekSoftwareSolutions/rms/Camtek.RMS.Service/Infrastructure/Helpers/JwtMiddleware.cs`
- **Detail:** JWT signing key is derived from `Encoding.ASCII.GetBytes($"{ServerName} {ServerUri}")` — for defaults this is `"Server http://localhost:5001"`. A predictable, weak symmetric key. Issuer/audience validation is disabled. Failed JWT validation is silently swallowed (request proceeds unauthenticated).
- **Impact:** Any user who knows the server name and URI can forge valid JWT tokens with arbitrary claims, gaining admin access to all RMS operations.
- **Suggested fix:** Use a cryptographically random secret (256+ bits) stored in a protected config file. Enable issuer and audience validation. Return 401 on JWT validation failure instead of silently continuing.

**Risk: No TLS on any internal service communication**
- **Location:** DataServer WCF bindings named `"NotSecured"`, BSIHR uses `ChannelCredentials.Insecure`, RMS uses `http://localhost:5001` (no HTTPS)
- **Detail:** All 12 DataServer WCF endpoints use plain `net.tcp` with no `NetTcpSecurity`. Certificate validation is explicitly disabled (`certificateValidationMode="None"`). BSIHR gRPC channels use `ChannelCredentials.Insecure`. RMS explicitly enables `Http2UnencryptedSupport`.
- **Impact:** All service-to-service traffic (scan results, verification data, user credentials, recipes) traverses the network in plaintext. Susceptible to MITM, sniffing, and replay attacks. On a shared factory network, this exposes proprietary inspection data and credentials.
- **Suggested fix:** Enable TLS on all WCF endpoints (`NetTcpSecurity.Mode = Transport`). Use TLS for gRPC channels. Enable HTTPS on RMS.

**Risk: Hardcoded default credentials across the system**
- **Location:** `ServiceSettings.cs` (DataServer): `DefaultToolUserName = "me_admin"`, `DefaultToolPassword = "1122"`. Seeded in `AuthDbContext.cs` with `admin` role. Used for network file copy in `ParallelManager.cs`.
- **Detail:** The 4-digit password `1122` is the default for admin access, compiled into binaries, and used for network file operations (SMB credential delegation). `WebApiHelper` defaults to `guest/guest`.
- **Impact:** Any developer or decompiled binary reveals admin credentials. These credentials are used for cross-machine file copy operations, meaning compromise grants access to all DataServer instances.
- **Suggested fix:** Remove hardcoded credentials. Require secure credential injection via encrypted config or secret store. Enforce minimum password complexity. Rotate seeded admin password on first login.

### HIGH

**Risk: Silent COM registration failures**
- **Location:** `BIS/reg.cmd`, `DeployUI/helpers/reg.unreg.ps1`
- **Detail:** `regsvr32 /s` suppresses all error dialogs. No `%ERRORLEVEL%` checking. Registration order is non-deterministic (NTFS directory order). Critical inspection-path VB6 COM objects (`Connector.vbp`, `Display.vbp`) depend on correct registration.
- **Impact:** A failed COM registration goes undetected until runtime `COMException` or `ClassNotRegisteredException` crashes the inspection workflow. Debugging requires manual registration forensics.
- **Suggested fix:** Add error checking to registration scripts. Log results. Validate COM CLSID/ProgID entries in registry after registration. Adopt deterministic registration order for dependent components.

**Risk: Unbounded file-system message queue**
- **Location:** `DiskMessageConsumer.cs` in `CamtekSoftwareSolutions/dataserver/Common/Queue/Camtek.QueueSharedModels/`
- **Detail:** No max queue depth, no disk space check, no backpressure. `Directory.GetFiles()` loads all filenames into memory at once. `Failed/` subfolder also has no purge/rotation. `WaitAndRetryForever` policy means transient errors cause infinite retry with 6-second delay.
- **Impact:** Under sustained load or consumer downtime, input directory grows unbounded → `OutOfMemoryException` on `GetFiles()` or disk exhaustion. Failed messages accumulate forever.
- **Suggested fix:** Add max queue depth limit. Implement file count pagination in consumer. Add disk space monitoring. Set Failed folder TTL with automatic purge. Replace `WaitAndRetryForever` with bounded retry + circuit breaker.

**Risk: WCF message size limits set to Int32.MaxValue (2GB)**
- **Location:** DataServer Host `App.config` — all reader quotas and transport sizes at `2147483647`
- **Detail:** Both custom bindings (`NotSecured`, `NotSecuredInspection`) have all size limits maxed: `maxArrayLength`, `maxBytesPerRead`, `maxStringContentLength`, `maxBufferSize`, `maxReceivedMessageSize` all at 2GB.
- **Impact:** No protection against oversized or malicious messages. A single large message can exhaust process memory (single 2GB allocation). No DoS protection.
- **Suggested fix:** Set realistic per-operation limits based on actual data sizes. The largest expected payload (inspection result images) should cap limits. Add per-client throttling.

**Risk: RMS has zero test coverage**
- **Location:** `CamtekSoftwareSolutions/rms/` — no `*.Tests.csproj`, no NUnit/xUnit/MSTest references, no test methods
- **Detail:** 18 projects, 9 gRPC services, JWT auth, SignalR, SQLite storage — none have any automated tests.
- **Impact:** Regressions in recipe deployment, auth, or job lifecycle have no safety net. The JWT security vulnerability (see Critical section) would have been caught by basic testing.
- **Suggested fix:** Add unit tests for critical paths: JWT validation, gRPC service operations, EF Core migrations, SignalR notification flow.

**Risk: Extremely weak password policy in RMS**
- **Location:** `Camtek.RMS.Service/Startup.cs` — ASP.NET Identity configured with 1-character minimum, no complexity requirements
- **Detail:** `RequiredLength = 1`, `RequireDigit = false`, `RequireLowercase = false`, `RequireUppercase = false`, `RequireNonAlphanumeric = false`.
- **Impact:** Users can set single-character passwords. Combined with the JWT signing key vulnerability, this eliminates both authentication barriers.
- **Suggested fix:** Enforce minimum 8+ character passwords with digit + letter requirements. Add account lockout on failed attempts.

### MEDIUM

**Risk: Shared binary coupling — SystemCalibration ↔ BIS**
- **Location:** `SystemCalibration/Sources/SystemCalibrationDllUpdater/Program.cs`
- **Detail:** `SystemCalibrationDllUpdater` unconditionally copies DLLs from `c:\bis\bin\` to SystemCalibration's `lib/` folder. No version compatibility check, no hash verification. Excluded DLLs (`log4net`, `System.Buffers`, `Newtonsoft.Json`) could diverge. Missing DLLs in `c:\bis\bin` are silently skipped.
- **Impact:** SystemCalibration can run with mismatched DLL versions, causing subtle runtime failures in camera calibration routines.
- **Suggested fix:** Add assembly version compatibility checks. Compare file hashes before/after copy. Generate a DLL manifest with expected versions and validate at SystemCalibration startup.

**Risk: Mixed .NET runtimes — `netcoreapp3.1` in RMS Worker**
- **Location:** `CamtekSoftwareSolutions/rms/Camtek.Rms.Worker/Camtek.Rms.Worker.csproj` targets `netcoreapp3.1`
- **Detail:** .NET Core 3.1 reached end-of-life December 2022. No security patches since then. The RMS Worker runs alongside .NET 6.0 services.
- **Impact:** Security vulnerabilities in the .NET Core 3.1 runtime are unpatched. Potential assembly binding conflicts with .NET 6 shared contracts.
- **Suggested fix:** Upgrade the RMS Worker to `net6.0` (or `net8.0`).

**Risk: Telerik WPF version skew — SystemCalibration 6 years behind**
- **Location:** SystemCalibration uses Telerik **2016.2.613.45**; MDC uses **2022.1.222.45**; RMS uses **R2 2021**
- **Detail:** Three different Telerik versions across three products. SystemCalibration's 2016 version lacks security patches and modern control features. DLLs are resolved from `c:\bis\bin\` which may have a different version.
- **Impact:** Potential DLL hell if SystemCalibration and BIS share the same `c:\bis\bin\` at different Telerik versions. Missing security fixes in the 2016 version.
- **Suggested fix:** Unify Telerik versions across all products. Minimum: upgrade SystemCalibration to match MDC's 2022 version.

**Risk: VB6 COM components on critical inspection path**
- **Location:** `Connector.vbp`, `Display.vbp`, `FalCal.vbp` in `BIS/Sources/`
- **Detail:** 96 VB6 projects still in BIS. Critical components (Connector, Display, Calibration) are VB6 COM objects accessed via 18 interop wrapper projects. VB6 IDE hasn't been updated since 2008.
- **Impact:** No modern tooling, no unit test support, no async support, COM registration fragility. VB6 runtime end-of-support creates compliance risk.
- **Suggested fix:** Prioritize migration of critical-path VB6 components (Connector, Display) to C#/.NET. Use COM Callable Wrappers during transition.

**Risk: No distributed tracing or metrics**
- **Location:** Entire codebase — no OpenTelemetry, Prometheus, Grafana, Jaeger, or Zipkin found
- **Detail:** 12 WCF endpoints + 9 gRPC services + COM IPC + file-system queues — all diagnosed purely via log files (log4net/Serilog).
- **Impact:** Cross-service request failures require manual log correlation across multiple files and machines. Mean-time-to-diagnose is high.
- **Suggested fix:** Adopt OpenTelemetry for distributed tracing. Add structured correlation IDs to all service calls. Start with the DataServer ↔ MDC ↔ CMM flow.

### LOW

**Risk: Polly version mismatch in DataServer**
- **Location:** `Common.Infrastructure.Policies` (Polly 6.0.1) vs `Camtek.QueueSharedModels` (Polly 7.2.2)
- **Impact:** Different Polly APIs/behaviors in the same process. Assembly binding redirects may mask version conflicts.
- **Suggested fix:** Unify to Polly 8.x (current).

**Risk: `pipeline_pr.yml` misleading name**
- **Location:** `xbuild/pipeline_pr.yml` — contains only `trigger: main` (CI trigger), no `pr:` section
- **Detail:** Despite the filename, this is a CI pipeline, not a PR pipeline. PR triggering relies on external `base_pipeline.yml@PipelineRepo` template or Azure DevOps branch policies [UNCLEAR — cannot inspect PipelineRepo].
- **Impact:** Developer confusion about which pipeline validates PRs.
- **Suggested fix:** Document the actual PR trigger mechanism. Add explicit `pr:` section if PR-triggered behavior is intended.

**Risk: RabbitMQ references in DataServer are legacy/dead**
- **Location:** `WebApiHelper.cs` (zero callers), `FileDB.csproj` (unused `RabbitMQ.Client 6.2.1` reference), installer `CustomAction.cs` (RabbitMQ setup commented out)
- **Detail:** RabbitMQ is only actively used in BIS PubSub (via `RabbitMQPublisher`/`RabbitMQSubscriber`). DataServer's references are vestigial.
- **Impact:** Unused dependencies increase attack surface and confusion. The installer still packages RabbitMQ MSI unnecessarily.
- **Suggested fix:** Remove dead `RabbitMQ.Client` reference from `FileDB.csproj`. Remove `WebApiHelper.cs`. Clean installer.

---

## 11. [NEW] DataServer Deep Dive — Full WCF Interface Reference (2026-04-04)

### 11.1 Service Contracts Summary (121 operations across 21 interfaces)

| Interface | Port | # Ops | Duplex |
|---|---|---|---|
| `IMainService` | 8002 | 11 | No |
| `IScanResultsService` | 8202 | 15 | No |
| `IScanResultsServiceNotifier` | 8202 | 1 | **Yes** → `IScanResultsServiceCallbacks` (5 callbacks) |
| `IScanProcessesToolServiceNotifier` | 8202 | 1 | **Yes** → `IScanProcessesToolServiceCallbacks` (6 callbacks) |
| `IInspectionResultsService` | 8272 | 17 | No |
| `IVerificationService` | 8262 | 3 | No |
| `IVerificationServiceNotifier` | 8262 | 1 | **Yes** → `IVerificationServiceCallbacks` (1 callback) |
| `IVerificationImageService` | 8252 | 3 | No |
| `IImagesService` | 8242 | 5 | No |
| `IWaferLayoutService` | 8222 | 3 | No |
| `IClassifierService` | 8212 | 7 | No |
| `IDiceAttributesService` | 8232 | 4 | No |
| `IDiceAttributesClassesService` | 8232 | 2 | No |
| `ICmmService` | 8032 | 10 | No |
| `ICmmServiceNotifier` | 8032 | 1 | **Yes** → `ICmmServiceCallbacks` (4 callbacks) |
| `IAuthService` | 8012 | 6 | No |
| `IUsersService` | 8012 | 12 | No |
| `IPermissionService` | 8012 | 6 | No |
| `IIdentificationService` | 8022 | 5 | No |
| `IVirtualDataService` | — | 1 | No |

### 11.2 Key Operation Signatures

**IMainService** (`net.tcp://localhost:8002`):
- `bool UpdateDataServerSettings()`
- `ReloadStatus ValidateRepository(RepositorySource repository)`
- `Task<ReloadStatus> ReloadScanResultsFromRepositoryAsync(string repositoryId)`
- `Task<ReloadStatus> ReloadScanResultsFromRepositorySync(string repositoryId, SearchFilter searchFilter)`
- `Task<ReloadResponse> ReloadScanResultsFromEnabledRepositoriesAsync()`
- `List<RepositorySource> GetRepositories()` / `AddRepository()` / `EditRepository()` / `DeleteRepository()`
- `void CancelRepositoryProcessing(string repositoryId)` / `CancelAllRepositoriesProcessing()`

**IScanResultsService** (`net.tcp://localhost:8202`):
- `IList<WaferScanResult> GetWaferScanResult(IList<string> waferScanResultPaths)`
- `WaferScanResult GetWaferScanResultIncludeRecipes(string path)`
- `IList<string> GetDevicesNames/GetSetupsNames/GetLotsNames(FilterForScanResults filter)`
- `WaferScanResultResponse GetWaferScanResultsByFilter(FilterForScanResults)`
- `void UpdateYield(string path, int changeInBadDice)`
- `void UpdateDefectsCount(string path, int changeInDefects)`
- `void LockScanResults/UnLockScanResults(IList<string> paths, string userName, ...)`

**ICmmService** (`net.tcp://localhost:8032`):
- `void ExportMaps(MapExportRequest requestData)`
- `void ExportReports(IList<CmmReportAction> reports)`
- `AvailableExportMapModes GetAvailableModes()`
- `IDictionary<string, ExportStatus> GetMapExportStatuses(IEnumerable<string> scanResultsPaths)`
- `List<ServerModel> GetCMMCollection()` / `AddCMMSource()` / `EditCMMSource()` / `DeleteCMMSource()`

**IInspectionResultsService** (`net.tcp://localhost:8272`):
- `IList<InspectionResultData> ExecuteQuery(string query)` — XSql query engine
- `IList<InspectionResultData> ExecutePagingQuery(string query, int startRow, int rowsCount)`
- `void ChangeClassId(string wsrPath, IList<int> inspectionResultIds, int newClassId)`
- `HyperCreationResult GenerateScanResultsData(IList<string> paths, string userName, string twbxPath, ...)`
- `ExportAdcResult ExportADC(IList<string> paths, ExportAdcRequest)`
- `BaselineResponse CreateBaseline(BaselineRequest)` / `ImportBaselineFromCsv(BaselineImportRequest)`

**IVerificationService** (`net.tcp://localhost:8262`):
- `bool StartWaferVerificationProcess(string waferScanResultPath, string userName)`
- `void CancelWaferVerificationProcess(string waferScanResultPath, string userName)`
- `void CompleteVerificationProcess(string path, string userName, CompleteVerificationExtraData)`

**IAuthService** (`net.tcp://localhost:8012`):
- `User Login(string userName, string password)`
- `void Logout(string userName)`
- `bool Authenticate(string userName, string password)`
- `bool IsInRole(string userName, string role)`

### 11.3 Duplex Callbacks

**IScanResultsServiceCallbacks** (pushed to subscribers):
- `OnScanResultReady(WaferScanResult)` — new scan result available
- `OnLockStateChanged(IList<string> paths, string userName, bool state, string reason)`
- `OnYieldChanged(string path, double totalYield)`
- `OnNumberOfDefectsChanged(string path, int change)`
- `ScanResultsDeleted(IList<string> paths)`

**IScanProcessesToolServiceCallbacks** (tool scan lifecycle):
- `OnScanStarted/OnScanCompleted/OnScanFailed/OnScanResultsReady(ScanProcess)`
- `OnCarriersStarted/OnCarriersCompleted(IList<ToolCarrier>)`

**IVerificationServiceCallbacks**:
- `OnWaferScanResultVerificationChanged(WaferScanResult)`

**ICmmServiceCallbacks** (export status):
- `RaiseExportDataFailed(IEnumerable<string> paths, DataExportType, string extraKey, string user, string reason)`
- `RaiseExportDataStatusChanged(IEnumerable<string> paths, DataExportType, string extraKey, ExportStatus, string user)`

### 11.4 Fault Codes

18 fault codes defined in `FaultCodes.cs`: `Authentication`, `Authorization`, `NotImplemented`, `NotSupported`, `Internal`, `InvalidOperation`, `Communication`, `Arguments`, `Storage`, `FileFormatNotSupported`, `UnknownScanResult`, `StorageIsBusy`, `MessageInFailQueue`, `FailedToSaveInFS`, `DataAlreadyLocked`, `DataIsNotValid`, `NetworkIssue`, `Faulted`

### 11.5 WCF Binding Configuration

- **Binding type:** `customBinding` with `binaryMessageEncoding` + `reliableSession` + `tcpTransport`
- **Security:** **None** — bindings explicitly named `"NotSecured"`. Certificate validation disabled.
- **Message size limit:** All quotas at `Int32.MaxValue` (2,147,483,647 bytes = 2GB)
- **Timeouts:** Open/Close = 10s, Send = 3min (default) or 1hr (InspectionResults), Receive = ~25 days
- **Serialization:** Standard WCF binary for most; `InspectionResultsService` uses **protobuf-net** endpoint behavior

---

## 12. [NEW] DataServer Database Schema (2026-04-04)

### 12.1 SQLite Database (`DataServerDB.sqlite3`)

**Table: `ScanResults`** — maps to entity `WaferScanResultWrapper`

| Column | Type | PK | Nullable | Notes |
|---|---|---|---|---|
| `StartScanTimestamp` | DateTime | ✓ | No | Composite PK order 0 |
| `MachineName` | string | No | No | Order 1 |
| `ScanLogHash` | string | ✓ | No | Composite PK order 2 |
| `Path` | string(255) | ✓ | No | Composite PK order 3; unique index `Path_IX` |
| `JobName` | string(150) | No | No | |
| `SetupName` | string(150) | No | No | |
| `LotId` | string(150) | No | No | |
| `WaferId` | string(150) | No | No | |
| `InsertTimestamp` | DateTime | No | No | |
| `NumberOfDefectsAfterScan` | int | No | No | |
| `NumberOfDefects` | int | No | No | |
| `TotalScanDice` | int | No | No | |
| `GoodDice` | int | No | No | |
| `BadDice` | int | No | No | |
| `GoodDiceAfterScan` | int | No | No | |
| `BadDiceAfterScan` | int | No | No | |
| `LockedBy` | string(50) | No | ✓ | |
| `SourceId` | string | No | ✓ | |
| `VerificationState` | int | No | No | Default 0; added via ALTER TABLE |

**Table: `ExportData`**

| Column | Type | PK | Nullable | Notes |
|---|---|---|---|---|
| `ScanResultPath` | TEXT | ✓ | No | FK → `ScanResults(Path)` ON DELETE CASCADE |
| `DataExportType` | INTEGER | ✓ | No | Composite PK |
| `ExtraKey` | TEXT | ✓ | No | Composite PK |
| `RequestTime` | DATETIME | No | No | |
| `Status` | INTEGER | No | No | |
| `TicketName` | TEXT | No | ✓ | Index `IX_ticket_name` |
| `UserLogin` | TEXT | No | No | |

### 12.2 Auth Database (`Auth.db3`)

Managed via custom ORM in `AuthDbContext` — contains user/role/permission tables. Seeded with `me_admin` user (admin role, argon2 password hash).

### 12.3 File-based Inspection DB (`FileDB.INFS`)

Custom binary table format at `Tier1/Modules/InspectionResults/FileDB/`:
- `FlatDefect` — defect record structure
- `Table` — binary table container
- `ProtoViewSerializer` — protobuf-net serialization for views
- No SQL schema — direct binary read/write to files in scan result directories

---

## 13. [NEW] DataServer Configuration Reference (2026-04-04)

### 13.1 Service Settings

| Module | Key | Default | ⚠️ Hard-coded |
|---|---|---|---|
| DataServer | `BaseHost` | `net.tcp://localhost:8000/DataServer` | |
| DataServer | `DBFilePath` | `C:\bis\data\SWS\dataserver\DataServerDB.sqlite3` | ⚠️ |
| DataServer | `ClientDataPollingIntervalInSeconds` | `10` | |
| DataServer | `TasksCount` | `4` | |
| DataServer | `UseNetworkConnection` | `true` | |
| UsersAuth | `AuthDatabaseFile` | `C:\bis\data\SWS\dataserver\Auth.db3` | ⚠️ |
| CMM | `MappingsFolderRoot` | `C:\bis\data\SWS\dataserver\Mappings` | ⚠️ |
| CMM | `LocalQueueFolderPath` | `C:\bis\ScanResultServer\DataServer\CmmQueue` | ⚠️ |
| CMM | `IncomingTicketsFolderPath` | `C:\ParallelCMM\Tickets\Incoming` | ⚠️ |
| CMM | `FailedTicketsFolderPath` | `C:\ParallelCMM\Tickets\Failed` | ⚠️ |
| CMM | `UseGrpc` | `true` | |
| CMM | `TimeSpanGrpcServiceTimeout` | `5` (seconds) | |
| CMM | `TimeSpanGrpcRetry` | `10` (seconds) | |
| Classifiers | `ToolClassifiersPath` | `C:\bis\data\dds` | ⚠️ |
| InspectionResults | `PythonPath` | `C:\Dev\Python\SWS\python.exe` (fallback) | ⚠️ |
| InspectionResults | `PortNumber` (for Python workers) | `9000` | |
| InspectionResults | `NumberOfProcessToUse` | `4` | |
| WaferLayout | `LayoutCacheSize` | `60` | |
| VerificationImage | `QueryPageSize` | `50000` | |
| Base | `DefaultToolUserName` | `me_admin` | ⚠️ |
| Base | `DefaultToolPassword` | `1122` | ⚠️ |

### 13.2 DataServer Outbound Calls

| Target | Protocol | Trigger | Data |
|---|---|---|---|
| CMM Parallel Runner | gRPC (`ChannelCredentials.Insecure`) | Map/report export when `UseGrpc=true` | `CmmTicketCreationRequestMessage`, `ReportTicketCreationRequestMessage` |
| Python HyperCreator workers | gRPC (localhost:9001-900N) | `GenerateScanResultsData()` call | `ProcessChunk` with inspection data |
| Network file shares | SMB (P/Invoke `mpr.dll`) | Ticket polling, file copy | Scan result directories |
| File-system queue | File I/O (JSON) | Scan ready, export, verification | Various message types |

### 13.3 File-System Queue Retry Configuration

**Wrap pattern:** Inner=`WaitAndRetryForever`, Outer=`WaitAndRetry(2)`

| Policy | Trigger Exceptions | Count | Backoff |
|---|---|---|---|
| **WaitAndRetryForever** | `FaultException(UnknownScanResult, Communication)`, `CommunicationException`, `TimeoutException`, `ObjectDisposedException`, `SqlException(-2,-1,2,53)`, `SQLiteException(Busy,Locked,Full)` | ∞ | 2s→4s→6s forever |
| **WaitAndRetry** | Other `FaultException`s, `IOException`, `SqlException(1205)`, `SQLiteException(Error,NoMem,IoErr)`, `RobocopyException` | 2 | 5s fixed |

Dead letter: Messages move to `{inputFolder}/Failed/` subfolder. No TTL, no purge, no monitoring.

---

## 14. [NEW] Communication Flow Trace — Wafer Scan Completes (B1) (2026-04-04)

```
[1] BIS DDS ProcessingSystem
      EndScan → all processing workers finish
      → signals DDS scan completion
    → [2] BsiScanResCreator

[2] BIS BsiScanResCreator
      CreateScanResDir() writes scan result folder:
        C:\Falcon\ScanResults\{Job}\{Setup}\{Lot}\{WaferId}\
        → ScanLog.ini (all scan metadata)
        → ProductInfo.ini (equipment IDs)
        → WaferMap (die states)
        → bsiScanResult.result (binary defect data)
        → recipe files
    → [3] BIS E30Client

[3] BIS E30Client (SECS/GEM)
      WaferScanResultsAreReady() → triggers DataCollectionTH (async)
      → reads ScanLog.ini + ProductionInfo.ini
      → SECS/GEM data collection to factory host
      → DataCollectionCompleted() signals automation cycle
    → [4] Ticket creation

[4] BIS Automation
      Creates .tck ticket file on \\tool\Tickets\ network share
      → { TicketPath, ScanResultPath, MachineName, timestamp }
    → [5] DataServer PollingDirectoryService

[5] DataServer PollingDirectoryService (timer: every 10s)
      Enumerates *.tck files from configured ClientDataSources
      → ProcessTicket(): validates ticket, builds source path, hash-dedup check
      → Publishes ScanReadyMessage to in-process queue
        { Path, SourceType, RepositoryId }
    → [6] ProcessScanResultService

[6] DataServer ProcessScanResultService
      Consumes ScanReadyMessage
      → Verifies repository exists
      → Delegates to PreProcessingService.HandleScanLoading(msg)
    → [7] ScanResultsInternalService

[7] DataServer ScanResultsInternalService
      CreateScanResult():
      → WaferScanResultFSContext.BuildScanLogData() reads ScanLog.ini
      → Parses path into Job/Setup/Lot/WaferId
      → SQLite: InsertOrReplace into ScanResults table
        { Path, JobName, SetupName, LotId, WaferId, StartScanTimestamp,
          NumberOfDefects, TotalScanDice, GoodDice, BadDice, ... }
      CompleteLoading():
      → _externalSubscription.InvokeAsync()
    → [8] WCF Duplex Callbacks (async, to all subscribers)

[8] DataServer → MDC (WCF duplex net.tcp port 8202)
      OnScanResultReady(WaferScanResult waferScanResult)
      → Pushed via duplex channel to all subscribed clients
        { PathToFiles, JobName, SetupName, LotId, WaferId,
          NumberOfDefects, GoodDice, BadDice, StartScanTimestamp }
    → [9] MDC ScanResultsNotifierProxy

[9] MDC ScanResultsNotifierProxy
      Receives OnScanResultReady callback
      → Fires ScanResultReady C# event
    → [10] MDC ScanResultService

[10] MDC ScanResultService
       ScanResultsClientCallback_ScanResultReady(waferScanResult)
       → _wafersToAddOrUpdate.Enqueue(waferScanResult) [ConcurrentQueue]
     → [11] MDC DispatcherTimer

[11] MDC DispatcherTimer (UI thread)
       Drains queue: dequeues up to MaxWafersToAdd per tick
       → IsScanResultFitDateFilter() — checks date filter
       → UpsertWaferInWaferList() — creates/updates Wafer view model
       → InvokeInUiDispatcher(() => Wafers.AddRange(wafersToAdd))
       → UI grid updates with new wafer row ✓
```

**Service boundaries:**
| # | From → To | Protocol | Port/Path |
|---|---|---|---|
| 2→4 | BIS → Network share | SMB/CIFS | `\\tool\Tickets\` |
| 5 | DataServer polling | File I/O | `*.tck` files |
| 5→6 | In-process | `IMessageProducer<ScanReadyMessage>` | — |
| 7 | DataServer → SQLite | LinqToDB 2.6.4 | `DataServerDB.sqlite3` |
| 8 | DataServer → MDC | WCF duplex `net.tcp` | Port 8202, `IScanResultsServiceCallbacks` |
| 3 | BIS → Factory Host | SECS/GEM (HSMS) | Equipment integration (async branch) |

---

## 15. [NEW] Hard-Coded Deployment Paths Registry (2026-04-04)

| Path | Component | Configurable? | Used For |
|---|---|---|---|
| `C:\bis\bin\` | BIS, SystemCalibration | No | Binary deployment root |
| `C:\bis\bin\x64\` | BIS | No | 64-bit binaries |
| `C:\bis\data\SWS\dataserver\` | DataServer | Via Settings.cs | SQLite DBs, mappings, configs |
| `C:\bis\data\dds` | DataServer Classifiers | Via Settings.cs | Tool classifier data |
| `C:\bis\data\config\env\system.ini` | DataServer Tool | No | System definitions |
| `C:\bis\data\config\WaferTypes` | DataServer Tool | No | Wafer shape types |
| `C:\bis\errorlog\` | BIS, ToolAnalytics | No | Error log root |
| `C:\bis\ScanResultServer\DataServer\CmmQueue` | DataServer CMM | Via Settings.cs | CMM queue folder |
| `C:\BIS\RMS\Server\` | RMS | Via appsettings.json + fallback | RMS root |
| `C:\BIS\RMS\Server\RMSStorages` | RMS | Via config + hard-coded fallback | Recipe storage |
| `C:\BIS\RMS\Client\CodeCompare\CodeCompare.exe` | RMS Client | **No** | File comparison tool |
| `C:\falcon\data\` | ToolAnalytics, BIS | No | Machine config INI files |
| `C:\Falcon\ScanResults\` | BIS | No | Scan result output |
| `C:\Falcon\Data\ScanResultsServer\Tickets\` | DataServer Tool | No | Ticket processing |
| `C:\Job\` | RMS Tool Agent | Via config | Recipe deployment |
| `C:\ParallelCMM\Tickets\Incoming` | DataServer CMM | Via Settings.cs | Parallel CMM incoming |
| `C:\ParallelCMM\Tickets\Failed` | DataServer CMM | Via Settings.cs | Parallel CMM failed |
| `C:\ScanResultServer\Global\` | DataServer | No | Global config |
| `C:\Dev\Python\SWS\python.exe` | DataServer | Via Settings.cs (fallback) | Python runtime |
| `C:\ScanResultsServerTool\Logging.xml` | DataServer Tool | No | Logging config |
