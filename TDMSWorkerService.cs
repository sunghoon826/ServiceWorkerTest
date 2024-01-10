using MathNet.Numerics.IntegralTransforms;
using MathNet.Numerics;
using System.Numerics;
using System.Text.Json;

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

            ProcessTdmsFile(completeStream);
            currentDayProcessedFiles.Add(filePath); // 파일을 처리한 후 해시셋에 추가

            // JSON 파일 저장 로직
            string jsonFilePath = Path.ChangeExtension(filePath, ".json");
            await File.WriteAllTextAsync(jsonFilePath, jsonData); // jsonData를 json 파일로 저장

            _logger.LogInformation("TDMS file converted to JSON successfully: {filePath}", jsonFilePath);
        }
        catch (Exception ex)
        {
            _logger.LogError("Failed to convert TDMS file to JSON: {filePath}. Error: {error}", filePath, ex.ToString());
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

            foreach (var channel in group)
            {
                // TDMS 파일로부터 데이터 읽기 (가정: double[] 형식)
                var rawData = channel.GetData<double>().ToArray();

                // Hanning 윈도우 적용
                var hanningWindow = Window.Hann(rawData.Length);
                var windowedData = rawData.Zip(hanningWindow, (data, window) => data * window).ToArray();

                // double[]를 Complex[]로 변환
                Complex[] complexData = windowedData.Select(value => new Complex(value, 0)).ToArray();

                // FFT 처리
                Fourier.Forward(complexData, FourierOptions.Default);

                // FFT 결과의 첫 번째 절반만 사용
                var fftResultHalf = complexData.Take(complexData.Length / 2)
                                               .Select(c => (float)c.Magnitude)
                                               .ToList();

                var channelData = new TdmsChannelData
                {
                    Name = channel.Name,
                    Data = fftResultHalf,
                    Properties = channel.Properties.ToDictionary(p => p.Key, p => p.Value)
                };

                groupData.Channels.Add(channelData);
            }
            tdmsData.Add(groupData);
        }

        var options = new JsonSerializerOptions { WriteIndented = true };
        jsonData = JsonSerializer.Serialize(tdmsData, options);

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