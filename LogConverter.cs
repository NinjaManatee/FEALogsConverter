using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Json.Schema;

namespace FeaLogsConverter
{
	public class LogConverter
	{
		public string FolderPath { get; }

		private List<string> allLogs = new();
		private List<string> centralLogs = new();
		private List<string> otherLogs = new();
		private List<CentralLoggerRecord> normalizedLogs = new();
		private Config Config { get; set; }

		private Dictionary<string, string> logLevelsMap;


		public LogConverter(string folderPath = null)
		{
			FolderPath = string.IsNullOrWhiteSpace(folderPath) ? Directory.GetCurrentDirectory() : folderPath;
		}

		public void Run()
		{
			if (!LoadAndValideateConfig()) return;

			ReadLogFiles();
			SplitCentralLoggerLogs();
			SaveNonCentralLogs();
			NormalizeCentralLogs();
			SaveNormalizedLogJson();
			SaveLogState();
			CreateZipArchive();
		}

		private bool LoadAndValideateConfig()
		{
			try
			{
				Console.WriteLine($"Loading config...");

				// Get the directory of the executable
				string exeDirectory = Path.GetDirectoryName(Environment.ProcessPath) ?? "";

				string schemaText = File.ReadAllText(Path.Join(exeDirectory, "config.schema.json"));
				JsonSchema schema = JsonSchema.FromText(schemaText);

				// TODO: Read config from local directory, if it exists, then default to EXE directory
				string configText = File.ReadAllText(Path.Join(exeDirectory, "config.json"));

				// Remove // comments
				configText = Regex.Replace(configText, @"//.*", "");

				JsonNode configNode = JsonNode.Parse(configText)
									  ?? throw new InvalidOperationException("Invalid JSON");

				var result = schema.Evaluate(configNode);
				if (result != null && !result.IsValid)
				{
					Console.WriteLine("❌ Configuration validation failed:");
					
					if (result?.Errors != null)
					{
						foreach (var error in result.Errors)
						{
							Console.WriteLine($" • {error.Key}: {error.Value}");
						}
					}
					return false;
				}

				Config = JsonSerializer.Deserialize<Config>(configText)
						 ?? throw new InvalidOperationException("Deserialization failed");

				logLevelsMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
				foreach (var kvp in Config.LogLevelsAlternatives)
				{
					foreach (var alias in kvp.Value)
					{
						var val = alias.ToLower();
						if (!logLevelsMap.ContainsKey(val))
						{
							logLevelsMap[val] = kvp.Key;
						}
					}
				}
			}
			catch (Exception ex) 
			{
				Console.WriteLine($"Load config exception: {ex}");
				return false;
			}

			return true;
		}

		private void MaybeSaveCurrentRecord(string currentRecord, bool isCentralLoggerRecord, string recordSplitPattern, string clientName, bool isFeaLog)
		{
			if (!string.IsNullOrEmpty(currentRecord))
			{
				var record = currentRecord.Trim();
				if (isCentralLoggerRecord)
				{
					centralLogs.Add(record);
				}
				else
				{
					if (isFeaLog)
					{
						otherLogs.Add(record);
					}
					var clRecord = NormalizeNonCentralLoggerRecord(record, recordSplitPattern, clientName);
					normalizedLogs.Add(clRecord);
				}
			}
		}

		private void ReadLogFiles()
		{
			Console.WriteLine($"Reading .log files from: {FolderPath}");
			string[] logFiles = Directory.GetFiles(FolderPath, "*.log")
				.Concat(Directory.GetFiles(FolderPath, "assimilationLogs.txt"))
				.Concat(Directory.GetFiles(FolderPath, "*.log.*")
				.Where(f => int.TryParse(Path.GetExtension(f).TrimStart('.'), out _))) // Ensures it's like ".1", ".2"
				.ToArray();

			foreach (var file in logFiles)
			{
				Console.WriteLine($"Processing: {file}");
				try
				{
					string[] lines = File.ReadAllLines(file);
					string currentRecord = "";
					bool isFeaLogs = false;
					bool isCentralLoggerRecord = false;

					string clientName = "FEA";
					string recordSplitPattern = null;
					var firstLine = lines.FirstOrDefault();
					if (firstLine != null)
					{
						if (Regex.IsMatch(firstLine, Config.FEALogRegexp))
						{
							recordSplitPattern = Config.FEALogRegexp;
							isFeaLogs = true;
						}
						else if (Regex.IsMatch(firstLine, Config.AssimilationRegexp))
						{
							recordSplitPattern = Config.AssimilationRegexp;
							clientName = "Assimilation";
						}
						else if (Regex.IsMatch(firstLine, Config.NativeLogRegexp))
						{
							recordSplitPattern = Config.NativeLogRegexp;
							clientName = "Native";
						}
					}

					if (recordSplitPattern == null)
					{
						Console.WriteLine($"Can not determine log reocrd type by first line for: {file} file. The file will be ignored");
						Console.WriteLine($"{firstLine}");
						continue;
					}

					foreach (var line in lines)
					{
						if (Regex.IsMatch(line, recordSplitPattern))
						{
							isCentralLoggerRecord = isFeaLogs && Regex.IsMatch(currentRecord, Config.FEACentralLoggerRegexp);
							MaybeSaveCurrentRecord(currentRecord, isCentralLoggerRecord, recordSplitPattern, clientName, isFeaLogs);
							currentRecord = line;
						}
						else
						{
							currentRecord += "\n" + line;
						}
					}

					// save the last record
					isCentralLoggerRecord = isFeaLogs && Regex.IsMatch(currentRecord, Config.FEACentralLoggerRegexp);
					MaybeSaveCurrentRecord(currentRecord, isCentralLoggerRecord, recordSplitPattern, clientName, isFeaLogs);

					Console.WriteLine($"Loaded file: {Path.GetFileName(file)}");
				}
				catch (Exception ex)
				{
					Console.WriteLine($"Failed to read {file}: {ex.Message}");
				}

				Console.WriteLine($"Total log records: {centralLogs.Count + otherLogs.Count + normalizedLogs.Count}");
			}
			Console.WriteLine($"Total files loaded: {logFiles.Count()}");
		}

		private void SplitCentralLoggerLogs()
		{
			Console.WriteLine($"Central Logger records: {centralLogs.Count}");
			Console.WriteLine($"Other records: {otherLogs.Count + normalizedLogs.Count}");
		}

		private void NormalizeCentralLogs()
		{
			var jsonPartRegex = new Regex(@"\{.*\}", RegexOptions.Singleline);
			var normalized = new List<CentralLoggerRecord>();

			foreach (string logRecord in centralLogs)
			{
				Match jsonMatch = jsonPartRegex.Match(logRecord);
				if (!jsonMatch.Success)
				{
					Console.WriteLine($"Skip record:\n {logRecord}");
					continue;
				}

				try
				{
					using var doc = JsonDocument.Parse(jsonMatch.Value);
					var root = doc.RootElement;

					var log = new CentralLoggerRecord
					{
						category = root.GetProperty("category").GetString(),
						logClientName = root.GetProperty("logClientName").GetString(),
						logType = root.GetProperty("logType").GetString(),
						logTimestamp = root.GetProperty("logTimestamp").GetDecimal(),
						allLogArgs = root.GetProperty("logData").GetRawText()
					};

					try
					{
						var unquoted = JsonSerializer.Deserialize<string>(log.allLogArgs);

						try
						{
							log.parsedLogArgs = JsonSerializer.Deserialize<List<string>>(unquoted);
						}
						catch
						{
							var parts = unquoted?.TrimStart('[').TrimEnd(']').Split(',')?.ToList();
							log.parsedLogArgs = parts ?? new List<string> { log.allLogArgs };
						}
					}
					catch
					{
						log.parsedLogArgs = new List<string> { log.allLogArgs };
					}

					normalized.Add(log);

					if (normalized.Count % 500 == 0)
					{
						Console.WriteLine($"Parsed Central Logger records: {normalized.Count}");
					}
				}
				catch
				{
					Console.WriteLine($"Failed to create Central Logger record for:\n {logRecord}");
				}
			}

			Console.WriteLine($"Total parsed Central Logger records: {normalized.Count}");
			normalizedLogs.AddRange(normalized);
		}

		private CentralLoggerRecord NormalizeNonCentralLoggerRecord(string record, string regexpPattern, string clientName)
		{
			try
			{
				var pattern = new Regex(regexpPattern, RegexOptions.Compiled);

				var match = pattern.Match(record);

				if (!match.Success)
				{
					throw new Exception($"Bad log record string");
				}

				string timestampStr = match.Groups["timestamp"].Value;
				string levelRaw = match.Groups["level"].Value;
				string message = match.Groups["message"].Value;

				if (!DateTime.TryParse(timestampStr, null, System.Globalization.DateTimeStyles.AdjustToUniversal, out DateTime timestamp))
				{
					Console.WriteLine($"Failed to convert {timestampStr} to DateTime");
					throw new Exception($"Bad DateTime string {timestampStr}");
				}

				string logType = logLevelsMap.TryGetValue(levelRaw.ToLower(), out var key) ? key : null;

				return new CentralLoggerRecord
				{
					category = "system",
					logClientName = clientName,
					logType = logType,
					logTimestamp = new DateTimeOffset(timestamp).ToUnixTimeMilliseconds(),
					highlightFlag = new List<string>(),
					parsedLogArgs = new List<string> { message },
					allLogArgs = message,
					previousRowTimeDelta = 0,
					timeElapsedFromStartup = 0
				};
			}
			catch
			{
				Console.WriteLine($"Failed to create Central Logger record for:\n {record}");
				return null;
			}
		}

		private void SaveNonCentralLogs()
		{
			string path = Path.Combine(FolderPath, "Not Central Logger.log");
			File.WriteAllText(path, string.Join(Environment.NewLine + Environment.NewLine, otherLogs));
			Console.WriteLine($"Other FEA records have been saved into {path}");
		}

		private void SaveNormalizedLogJson()
		{
			var wrapper = new Dictionary<string, object>
			{
				{ "partial_log", normalizedLogs }
			};

			string outputFile = Path.Combine(FolderPath, "log0.json");
			var options = new JsonSerializerOptions { WriteIndented = true };

			File.WriteAllText(outputFile, JsonSerializer.Serialize(wrapper, options));

			Console.WriteLine($"log0.json has been created");
		}

		private void SaveLogState()
		{
			var clientNames = normalizedLogs
				.Select(n => n.logClientName)
				.Where(n => !string.IsNullOrWhiteSpace(n))
				.Distinct()
				.ToList();

			var registeredClients = clientNames.ToDictionary(
				name => name,
				name => new
				{
					name,
					viewId = (object)null,
					centralLoggerNamePrefix = "",
					displayName = name
				});

			var levelStates = new Dictionary<string, bool>
			{
				["Error"] = true,
				["Warn"] = true,
				["Info"] = true,
				["Log"] = true,
				["Debug"] = true,
				["Verbose"] = true
			};

			var initialClientState = new Dictionary<string, bool>(levelStates)
			{
				["Info"] = false,
				["LocalOnly"] = true
			};

			var clientState = clientNames.ToDictionary(name => name, name => new
			{
				console = initialClientState,
				dev = initialClientState,
				system = initialClientState,
				perf = initialClientState,
				clientChannel = name,
				showAdvancedViewFilters = false,
				wrapLog = false,
				showStackStraceInLog = false,
				filter = new { logic = "OR" },
				showTimeElapsedFromStartup = true,
				clientListVisible = true,
				colDefs = new[]
				{
				new { field = "timeElapsedFromStartup", name = "Time" },
				new { field = "previousRowTimeDelta", name = "Row Delta" },
				new { field = "category", name = "Category" }
			},
				windowName = name
			});

			var persistState = new
			{
				logState = levelStates,
				plainTextConsole = true,
				filterHighlights = true,
				highlightString = Enumerable.Range(0, 4).Select(_ => new { str = "" }).ToArray(),
				hideInactiveState = false,
				currentCategory = "system",
				devModeState = true,
				systemModeState = true,
				perfModeState = true,
				initialClientStateDefault = initialClientState,
				showAdvancedViewFilters = false,
				wrapLog = false,
				showStackStraceInLog = false,
				filter = new { logic = "OR" },
				showTimeElapsedFromStartup = true,
				colDefs = new[]
				{
				new { field = "timeElapsedFromStartup", name = "Time" },
				new { field = "previousRowTimeDelta", name = "Row Delta" },
				new { field = "category", name = "Category" }
			},
				visibleCols = new[] { "timeElapsedFromStartup" },
				clientListVisible = true,
				isPersisted = true,
				clientState,
				showClientState = clientNames.ToDictionary(name => name, name => true)
			};

			var state = new
			{
				registeredClientNames = clientNames,
				registeredClients,
				persistState
			};

			string outputFile = Path.Combine(FolderPath, "log_state.json");
			var options = new JsonSerializerOptions { WriteIndented = true };
			File.WriteAllText(outputFile, JsonSerializer.Serialize(state, options));

			Console.WriteLine($"log_state.json has been created");
		}

		void CreateZipArchive()
		{
			string zipFilePath = Path.Combine(Directory.GetCurrentDirectory(), "FEA.CentralLogger.zip");

			// Delete if it already exists to avoid errors
			if (File.Exists(zipFilePath)) File.Delete(zipFilePath);

			using (var zip = ZipFile.Open(zipFilePath, ZipArchiveMode.Create))
			{
				string[] filesToInclude = { "log0.json", "log_state.json" };

				foreach (var fileName in filesToInclude)
				{
					string fullPath = Path.Combine(Directory.GetCurrentDirectory(), fileName);
					if (File.Exists(fullPath))
					{
						zip.CreateEntryFromFile(fullPath, fileName);
						File.Delete(fullPath);
					}
					else
					{
						Console.WriteLine($"Unnable to find {fileName} file");
					}
				}
			}

			Console.WriteLine("FEA.CentralLogger.zip created");
		}
	}
}
