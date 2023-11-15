using CommandLine;

namespace blackview
{
	internal class Program
	{
		public class Options
		{
			[Option('h', "hostname", Required = false, HelpText = "The hostname or ip of the camera.", Default = "blackvue.home")]
			public string Hostname { get; set; } = string.Empty;
			[Option('o', "output", Required = false, HelpText = "The path to the directory where you want to save the files.", Default = "c:/blackview")]
			public string Output { get; set; } = string.Empty;
		}

		static void Main(string[] args)
		{
			Parser.Default.ParseArguments<Options>(args)
			.WithParsed(o =>
			{
				if (!string.IsNullOrEmpty(o.Hostname))
				{
					var suffixes = new[]
					{
						"F.mp4",
						"R.mp4",
						"F.thm",
						"R.thm",
						".gps",
						".3gp",
					};

					Directory.CreateDirectory(o.Output);

					var client = new HttpClient();
					var tocURI = new Uri($"http://{o.Hostname}/blackvue_vod.cgi");
					var videoTableOfContentsResponse = client.GetAsync(tocURI).Result;
					if (videoTableOfContentsResponse.IsSuccessStatusCode)
					{
						var videoNames = new HashSet<string>();
						var videoTableOfContentsStream = videoTableOfContentsResponse.Content.ReadAsStreamAsync().Result;
						using var streamReader = new StreamReader(videoTableOfContentsStream);
						string? line;
						while ((line = streamReader.ReadLine()) != null)
						{
							var lineParts = line.Split(",");
							if (lineParts.Length == 2)
							{
								string videoName = lineParts.First().Replace("n:/Record/", "").Replace("F.mp4", "").Replace("R.mp4", "");
								videoNames.Add(videoName);
							}
						}

						int numTotalDownloads = suffixes.Length * videoNames.Count;
						int numCompletedDownloads = 0;
						foreach (var videoName in videoNames)
						{
							foreach (var suffix in suffixes)
							{
								string downloadStatus = $"{numCompletedDownloads+1}/{numTotalDownloads}";
								var filename = videoName + suffix;
								var videoAddress = $"http://{o.Hostname}/Record/{filename}";
								var videoURI = new Uri(videoAddress);
								Console.WriteLine($"{downloadStatus} {videoAddress}");
								var result = client.GetAsync(videoURI).Result;
								using var fs = new FileStream($"{o.Output}/{filename}", FileMode.Create);
								result.Content.CopyToAsync(fs).Wait();
								++numCompletedDownloads;
							}
						}
					}
				}
			});
		}
	}
}
