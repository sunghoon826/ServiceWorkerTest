using System.Text.Json;

public class TDMSWorkerService : BackgroundService
{
    private HashSet<string> processedFiles = new HashSet<string>();
    private string? jsonData;
    readonly ILogger<TDMSWorkerService> _logger;

    public TDMSWorkerService(ILogger<TDMSWorkerService> logger)
    {
        _logger = logger;
    }

    private string GetCurrentDateString() => DateTime.Now.ToString("yyyy-MM-dd");

    private string TdmsFilesPath => Path.Combine(@"D:\repos\FMTDMS\", GetCurrentDateString());

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                string currentDate = GetCurrentDateString();
                string[] directories = Directory.GetDirectories(Path.GetDirectoryName(TdmsFilesPath));

                foreach (string dir in directories)
                {
                    if (new DirectoryInfo(dir).Name == currentDate)
                    {
                        await ProcessDirectoryAsync(dir, stoppingToken);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred processing TDMS files.");
            }
            await Task.Delay(30000, stoppingToken);
        }
    }

    private async Task ProcessDirectoryAsync(string directory, CancellationToken stoppingToken)
    {
        if (!Directory.Exists(directory))
        {
            _logger.LogInformation("Directory does not exist: {directory}", directory);
            return;
        }

        var tdmsFiles = Directory.GetFiles(directory, "*.tdms");
        foreach (var filePath in tdmsFiles)
        {
            if (!processedFiles.Contains(filePath))
            {
                await ProcessFileAsync(filePath);
            }
        }
    }

    private async Task ProcessFileAsync(string filePath)
    {
        _logger.LogInformation("Found new TDMS file: {filePath}", filePath);
        using var fileStream = File.OpenRead(filePath);
        using var completeStream = new MemoryStream();
        await fileStream.CopyToAsync(completeStream);
        completeStream.Position = 0;
        ProcessTdmsFile(completeStream);

        processedFiles.Add(filePath);
    }

    private void ProcessTdmsFile(MemoryStream completeStream)
    {
        using var tdms = new NationalInstruments.Tdms.File(completeStream);
        tdms.Open();

        var tdmsData = new List<TdmsGroupData>();

        foreach (var group in tdms)
        {
            var groupData = new TdmsGroupData
            {
                GroupName = group.Name,
                Channels = new List<TdmsChannelData>()
            };

            groupData.GroupName = string.IsNullOrWhiteSpace(groupData.GroupName) || groupData.GroupName == "제목없음" ? "Untitled" : groupData.GroupName;

            foreach (var channel in group)
            {
                var channelData = new TdmsChannelData
                {
                    Name = channel.Name,
                    Data = channel.GetData<object>().ToList(),
                    Properties = channel.Properties.ToDictionary(p => p.Key, p => p.Value)
                };

                groupData.Channels.Add(channelData);
            }
            tdmsData.Add(groupData);
        }

        var options = new JsonSerializerOptions { WriteIndented = true };
        jsonData = JsonSerializer.Serialize(tdmsData, options);
        _logger.LogInformation("Converted TDMS to JSON: {jsonData}", jsonData);
    }

    private class TdmsGroupData
    {
        public string? GroupName { get; set; }
        public List<TdmsChannelData>? Channels { get; set; }
    }

    private class TdmsChannelData
    {
        public string? Name { get; set; }
        public List<object>? Data { get; set; }
        public Dictionary<string, object>? Properties { get; set; }
    }
}
