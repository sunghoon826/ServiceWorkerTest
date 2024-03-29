using System.Text;
using System.Text.Json;
using static TdmsDataContext;

public class TDMSWorkerService : BackgroundService
{
    readonly ILogger<TDMSWorkerService> _logger;
    private string? jsonData;
    private HashSet<string> currentDayProcessedFiles = new HashSet<string>(); // 현재 날짜에 대한 파일을 저장하는 해시셋
    private string lastProcessedDate; // 마지막으로 처리된 날짜를 저장

    public TDMSWorkerService(ILogger<TDMSWorkerService> logger)
    {
        _logger = logger;
        lastProcessedDate = GetCurrentDateString(); // 서비스 시작 시 날짜 초기화
    }

    private string GetCurrentDateString() => DateTime.Now.ToString("yyyy-MM-dd"); // 현재 날짜 가져오기

    private string TdmsFilesPath => Path.Combine(@"D:\repos\FMTDMS\", GetCurrentDateString()); // 현재 날짜 경로

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                string currentDate = GetCurrentDateString();

                // 날짜가 변경되었는지 확인하고, 변경되었다면 해시셋 초기화
                if (currentDate != lastProcessedDate)
                {
                    currentDayProcessedFiles.Clear();
                    lastProcessedDate = currentDate;
                    _logger.LogInformation("New day detected, reset processed files for {currentDate}", currentDate);
                }

                if (Directory.Exists(TdmsFilesPath))
                {
                    await ProcessDirectoryAsync(TdmsFilesPath, stoppingToken); // tdms 파일들을 처리
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred processing TDMS files.");
            }
            await Task.Delay(30000, stoppingToken); // 딜레이 설정
        }
    }

    private async Task ProcessDirectoryAsync(string directory, CancellationToken stoppingToken)
    {
        var tdmsFiles = Directory.GetFiles(directory, "*.tdms");
        foreach (var filePath in tdmsFiles)
        {
            if (!currentDayProcessedFiles.Contains(filePath))
            {
                await ProcessFileAsync(filePath);
            }
        }
    }

    private async Task ProcessFileAsync(string filePath)
    {
        try
        {
            _logger.LogInformation("Processing new TDMS file: {filePath}", filePath);

            using var fileStream = File.OpenRead(filePath);
            using var completeStream = new MemoryStream();
            await fileStream.CopyToAsync(completeStream);
            completeStream.Position = 0;

            ProcessTdmsFile(completeStream); // TDMS 파일 처리

            // 소수점 처리된 데이터를 BLOB 데이터로 변환
            var processedData = Encoding.UTF8.GetBytes(jsonData);

            using (var context = new TdmsDataContext())
            {
                var fileData = new TdmsFileData
                {
                    FileName = Path.GetFileName(filePath),
                    Data = processedData // BLOB 데이터
                };

                context.TdmsFiles.Add(fileData);
                await context.SaveChangesAsync();
            }

            currentDayProcessedFiles.Add(filePath); // 파일을 처리한 후 해시셋에 추가

            _logger.LogInformation("TDMS file processed and saved to DB successfully: {filePath}", filePath);
        }
        catch (Exception ex)
        {
            _logger.LogError("Failed to process TDMS file: {filePath}. Error: {error}", filePath, ex.ToString());
        }
        finally
        {
            // 메모리 해제
            jsonData = null; // jsonData 사용 완료 후 null 처리
        }
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
                    Data = channel.GetData<double>().Select(value => (float)(Math.Round(value, 3) )).ToList(), // 소수점 셋째자리까지 남기고 나머지 버림
                    Properties = channel.Properties.ToDictionary(p => p.Key, p => p.Value)
                };

                groupData.Channels.Add(channelData);
            }
            tdmsData.Add(groupData);
        }

        var options = new JsonSerializerOptions { WriteIndented = true };
        jsonData = JsonSerializer.Serialize(tdmsData, options);
        //_logger.LogInformation("Converted TDMS to JSON: {jsonData}", jsonData);
        tdmsData.Clear();
        GC.Collect();
    }


    private class TdmsGroupData
    {
        public string? GroupName { get; set; }
        public List<TdmsChannelData>? Channels { get; set; }
    }

    private class TdmsChannelData
    {
        public string? Name { get; set; }
        public List<float>? Data { get; set; }
        public Dictionary<string, object>? Properties { get; set; }
    }
}