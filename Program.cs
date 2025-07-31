using FeaLogsConverter;

Console.WriteLine("Running FEA converter...");

string folderPath = args.Length > 0 ? args[0] : Directory.GetCurrentDirectory();
var converter = new LogConverter(folderPath);

converter.Run();

Console.WriteLine("Exit...");
