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

		static object Locker = new();

		static void Main(string[] args)
		{
			while (true)
			{
				try
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
							var tocResponse = client.GetAsync(tocURI).Result;
							if (tocResponse.IsSuccessStatusCode)
							{
								try
								{
									var rawFilenames = new HashSet<string>();
									var tocStream = tocResponse.Content.ReadAsStreamAsync().Result;
									using var streamReader = new StreamReader(tocStream);
									string? line;
									while ((line = streamReader.ReadLine()) != null)
									{
										var lineParts = line.Split(",");
										if (lineParts.Length == 2)
										{
											string rawFilename = lineParts.First().Replace("n:/Record/", "").Replace("F.mp4", "").Replace("R.mp4", "");
											rawFilenames.Add(rawFilename);
										}
									}

									int numTotalFiles = suffixes.Length * rawFilenames.Count;
									int numProcessedFiles = 0;
									Parallel.ForEach(rawFilenames.ToList().OrderByDescending(s => s), new ParallelOptions
									{
										MaxDegreeOfParallelism = 1, 
										// I've left the parallelization in here in case I want to support multiple cameras in the future.
										// But reading from a single SD card is already a bottleneck that multiple threads wont help.
									}, rawFilename =>
									{
										try
										{
											foreach (string suffix in suffixes)
											{
												var timeStarted = DateTime.Now;

												int fileIndex;
												lock (Locker)
												{
													++numProcessedFiles;
													fileIndex = numProcessedFiles;
												}

												string filename = rawFilename + suffix;
												string outputFilename = $"{o.Output}/{filename}";
												var fileUri = new Uri($"http://{o.Hostname}/Record/{filename}");
												string downloadStatus = $"{fileIndex}/{numTotalFiles} {fileUri}";
												var headerRequest = new HttpRequestMessage(HttpMethod.Head, fileUri);
												var headerResult = client.SendAsync(headerRequest).Result;
												if (headerResult.IsSuccessStatusCode)
												{
													long fileSizeOnCamera = headerResult.Content.Headers.ContentLength ?? 0;
													long fileSizeOnDisk = 0;

													try
													{
														var fileInfo = new FileInfo(outputFilename);
														fileSizeOnDisk = fileInfo.Length;
													}
													catch (FileNotFoundException) { }

													if (fileSizeOnDisk != fileSizeOnCamera)
													{
														float MB = fileSizeOnCamera / 1024f / 1024f;
														string size = $"{MB:F2} {nameof(MB)}";
														string operation = fileSizeOnDisk == 0 ? "Downloading" : "Redownloading";

														var downloadTask = client.GetAsync(fileUri);

														var timeOfLastStatusUpdate = DateTime.MinValue;

														while(!downloadTask.IsCompleted)
														{
															var timeNow = DateTime.Now;
															var currentDuration = timeNow - timeStarted;
															if (timeNow - timeOfLastStatusUpdate > TimeSpan.FromSeconds(30))
															{
																Console.WriteLine($"{downloadStatus} {operation} {size} for {currentDuration:mm\\:ss}");
																timeOfLastStatusUpdate = DateTime.Now;
															}

															Thread.Sleep(1000);
														}

														var downloadResult = downloadTask.Result;
														downloadResult.EnsureSuccessStatusCode();
														using var fs = new FileStream(outputFilename, FileMode.Create);
														downloadResult.Content.CopyToAsync(fs).Wait();
														var totalDuration = DateTime.Now - timeStarted;
														string speed = $"{MB / totalDuration.TotalSeconds:F2} MB/s";
														Console.WriteLine($"{downloadStatus} Completed {size} in {totalDuration:mm\\:ss} @ {speed}");
													}
													else
													{
														Console.WriteLine($"{downloadStatus} Skipping");
													}
												}
												else
												{
													Console.WriteLine($"{downloadStatus} Failed {headerResult.StatusCode}");
												}
											}
										}
										catch (Exception ex)
										{
											// Mostly catching task cancellations here. I'll replace with the correct exception later once I figure out what it is.
											Console.WriteLine(ex.Message);
										}
									});
								}
								catch (Exception ex)
								{
									Console.WriteLine(ex.Message);
								}
							}
						}
					});
				}
				catch(Exception ex) 
				{
					// Catching if the camera is offline. I'll replace with the correct exception later once I figure out what it is.
					Console.WriteLine(ex.Message);
				}

				// Retry in 10 seconds.
				Thread.Sleep(10000);
			}
		}
	}
}
