# Central Logger Converter

This is a C# console application that reads `.log` files from a specified folder (or the current working directory), filters and parses logs from "Central Logger", normalizes them into structured JSON, and produces output files including a ZIP archive.
All non central logger records will be converted to CL format with original message text and fake "FEA" client name.

---

## 🔧 Features

- Reads "config.json" and validate it agains "config.schema.json".
- Reads "assimilationLogs.txt" and all `.log` files in a folder.
- Available log files formats: FEA, Assimilation, Native.
- Groups log records into:
  - Central Logger logs
  - All other logs
- Parses all logs into Central Logger structured JSON objects. For parsing are required the next aliases in Regexp: \<timestamp\>, \<level\> and \<message\>
- Outputs:
  - `Not Central Logger.log` — all non-Central Logger logs
  - `FEA.CentralLogger.zip` — ZIP archive of `log0.json` and `log_state.json`
	- `log0.json` — normalized log entries
	- `log_state.json` — state metadata for UI representation

---

## 🚀 Getting Started

### ✅ Prerequisites

- [.NET 8.0 SDK or later](https://dotnet.microsoft.com/en-us/download)

---

### 📁 Folder Structure

Place your `.log` files into a directory, for example:

C:\Logs

├── log1.log

├── log2.log

### 🏃 Running the Program

Open terminal or command prompt and run:

```bash
dotnet run --project CentralLoggerConverter.csproj "C:\Logs"
```

Or run the `FeaLogsConverter.exe`.

If no folder path is provided, the program will use the current directory.


### 📦 Output Files
After running, the following files will be created in the target directory:

- Not Central Logger.log
- FEA.CentralLogger.zip

The ZIP archive contains:
- log0.json
- log_state.json