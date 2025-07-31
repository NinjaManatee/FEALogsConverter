using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FeaLogsConverter
{
	internal class Config
	{
		public string FEALogRegexp { get; set; }

		public string FEACentralLoggerRegexp { get; set; }

		public string AssimilationRegexp { get; set; }

		public string NativeLogRegexp { get; set; }

		public Dictionary<string, List<string>> LogLevelsAlternatives { get; set; }
	}
}
