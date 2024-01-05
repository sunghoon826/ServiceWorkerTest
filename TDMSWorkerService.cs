using System.Text.Json;

public class TDMSWorkerService : BackgroundService
{
    private Dictionary<string, HashSet<string>> dailyProcessedFiles = new Dictionary<string, HashSet<string>>();
    readonly ILogger<TDMSWorkerService> _logger;
    private string? jsonData;

    public TDMSWorkerService(ILogger<TDMSWorkerService> logger)
    {
        _logger = logger;
    }

    private string GetCurrentDateString() => DateTime.Now.ToString("yyyy-MM-dd"); // 현재날짜

    private string TdmsFilesPath => Path.Combine(@"D:\repos\FMTDMS\", GetCurrentDateString()); // 현재 날짜 경로

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        string lastProcessedDate = GetCurrentDateString();

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                string currentDate = GetCurrentDateString();

                if (!dailyProcessedFiles.ContainsKey(currentDate)) // 헤시셋에 오늘 날짜명이 딕셔너리 없을 때, 새로운 해시셋을 생성
                {
                    dailyProcessedFiles[currentDate] = new HashSet<string>();
                }

                var currentDayProcessedFiles = dailyProcessedFiles[currentDate];

                string[] directories = Directory.GetDirectories(Path.GetDirectoryName(TdmsFilesPath)); // 날짜명 폴더이름을 찾아서 directories에 대입
                foreach (string dir in directories)
                {
                    if (new DirectoryInfo(dir).Name == currentDate) // 오늘 날짜인 날짜명 폴더를 찾아서
                    {
                        await ProcessDirectoryAsync(dir, currentDayProcessedFiles, stoppingToken); // tdms 파일들을 처리
                    }
                }

                if (currentDate != lastProcessedDate) // currentDate와 lastProcessedDate가 다르면(날짜가 바뀌었으면)
                {
                    _logger.LogInformation("Previous day's data is still available for date: {lastProcessedDate}", lastProcessedDate);
                    lastProcessedDate = currentDate;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred processing TDMS files.");
            }
            await Task.Delay(30000, stoppingToken); // 딜레이 설정
        }
    }

    private async Task ProcessDirectoryAsync(string directory, HashSet<string> processedFiles, CancellationToken stoppingToken)
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
                await ProcessFileAsync(filePath, processedFiles);
            }
        }
    }

    private async Task ProcessFileAsync(string filePath, HashSet<string> processedFiles)
    {
        try
        {
            _logger.LogInformation("Found new TDMS file: {filePath}", filePath);
            using var fileStream = File.OpenRead(filePath);
            using var completeStream = new MemoryStream();
            await fileStream.CopyToAsync(completeStream);
            completeStream.Position = 0;
            ProcessTdmsFile(completeStream);

            processedFiles.Add(filePath);

            // JSON 파일 저장 로직
            string jsonFilePath = Path.ChangeExtension(filePath, ".json"); // .tdms 파일과 같은 이름으로 .json 확장자를 사용
            await File.WriteAllTextAsync(jsonFilePath, jsonData); // jsonData를 json 파일로 저장

            _logger.LogInformation("TDMS file converted to JSON successfully: {filePath}", jsonFilePath); // 로그
        }
        catch (Exception ex)
        {
            _logger.LogError("Failed to convert TDMS file to JSON: {filePath}. Error: {error}", filePath, ex.ToString());
        }
    }

    private void ProcessTdmsFile(MemoryStream completeStream) //tdms 처리
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
                    Properties = channel.Properties.ToDictionary(p => p.Key, p => p.Value) //
                };

                groupData.Channels.Add(channelData);
            }
            tdmsData.Add(groupData);


        }

        var options = new JsonSerializerOptions { WriteIndented = true };
        jsonData = JsonSerializer.Serialize(tdmsData, options);
        //_logger.LogInformation("Converted TDMS to JSON: {jsonData}", jsonData);
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
