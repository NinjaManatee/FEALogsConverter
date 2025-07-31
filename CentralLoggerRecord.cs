namespace FeaLogsConverter
{
	public class CentralLoggerRecord
	{
		public string category { get; set; }
		public string logClientName { get; set; }
		public string logType { get; set; }
		public decimal logTimestamp { get; set; }
		public List<string> highlightFlag { get; set; } = new List<string>();
		public List<string> parsedLogArgs { get; set; }
		public string allLogArgs { get; set; }
		public int previousRowTimeDelta { get; set; } = 0;
		public int timeElapsedFromStartup { get; set; } = 0;
	}
}
