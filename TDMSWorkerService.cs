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

            // 파일 처리 및 JSON 변환
            using (var fileStream = File.OpenRead(filePath))
            {
                using (var completeStream = new MemoryStream())
                {
                    await fileStream.CopyToAsync(completeStream);
                    completeStream.Position = 0;

                    ProcessTdmsFile(completeStream);
                }
            }

            // JSON 데이터를 바이너리로 변환
            byte[] binaryData = Encoding.UTF8.GetBytes(jsonData);

            // 데이터베이스에 바이너리 데이터 저장
            using (var context = new TdmsDataContext())
            {
                var fileData = new TdmsFileData
                {
                   FileName = Path.GetFileName(filePath),
                    Data = binaryData
                };

                context.TdmsFiles.Add(fileData);
                await context.SaveChangesAsync();
            }

            // 해시셋에 파일 경로 추가
            currentDayProcessedFiles.Add(filePath);

            // JSON 파일로 저장 (선택적)
            string jsonFilePath = Path.ChangeExtension(filePath, ".json");
            await File.WriteAllTextAsync(jsonFilePath, jsonData);

            _logger.LogInformation("TDMS file converted to JSON successfully: {filePath}", jsonFilePath);
        }
        catch (Exception ex)
        {
            _logger.LogError("Failed to convert TDMS file to JSON: {filePath}. Error: {error}", filePath, ex.ToString());
        }
        finally
        {
            // 메모리 해제
            jsonData = null;
        }
    }



    private void ProcessTdmsFile(MemoryStream completeStream)
    {
        using var tdms = new NationalInstruments.Tdms.File(completeStream);
        tdms.Open();

        if (!tdms.Any()) return; // TDMS 파일에 그룹이 없으면 리턴

        var firstGroup = tdms.First(); // 첫 번째 그룹

        if (!firstGroup.Any()) return; // 첫 번째 그룹에 채널이 없으면 리턴

        var firstChannel = firstGroup.First(); // 첫 번째 채널

        var firstChannelData = new TdmsChannelData
        {
            //Name = firstChannel.Name,
            Data = firstChannel.GetData<double>().Select(value => (float)value).ToList(),
            //Properties = firstChannel.Properties.ToDictionary(p => p.Key, p => p.Value)
        };

        // 첫 번째 채널의 데이터를 JSON으로 변환
        var options = new JsonSerializerOptions { WriteIndented = true };
        jsonData = JsonSerializer.Serialize(firstChannelData, options);
        //_logger.LogInformation("Converted first channel of TDMS to JSON: {jsonData}", jsonData);
    }




    private class TdmsGroupData
    {
        public string? GroupName { get; set; }
        public List<TdmsChannelData>? Channels { get; set; }
    }

    private class TdmsChannelData
    {
        //public string? Name { get; set; }
        public List<float>? Data { get; set; }
        //public Dictionary<string, object>? Properties { get; set; }
    }
}