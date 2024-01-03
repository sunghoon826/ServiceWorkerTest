using System.Text.Json;
public class TDMSWorkerService : BackgroundService
{
    private string? jsonData;
    readonly ILogger<TDMSWorkerService> _logger;
    private const string TdmsFilesPath = @"D:\\repos\\FMTDMS\\2023-11-15"; // 날짜 부분 path가 변경되야함.  date now의 스트링과 비교.  while에서 오늘 날짜를 잡는다 

    public TDMSWorkerService(ILogger<TDMSWorkerService> logger)
    {
        _logger = logger;
    }
    private HashSet<string> processedFiles = new HashSet<string>(); //하루에 한번 해시셋을 하나 만든다. 
    protected async override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var tdmsFiles = Directory.GetFiles(TdmsFilesPath, "*.tdms");
                foreach (var filePath in tdmsFiles)
                {
                    // 새로 추가된 파일만 처리
                    if (!processedFiles.Contains(filePath))
                    {
                        _logger.LogInformation("Found new TDMS file: {filePath}", filePath);
                        using var fileStream = File.OpenRead(filePath);
                        using var completeStream = new MemoryStream();
                        await fileStream.CopyToAsync(completeStream);
                        completeStream.Position = 0;
                        ProcessTdmsFile(completeStream); // 메모리 스트림으로 변환하여 처리

                        // 처리한 파일을 기록
                        processedFiles.Add(filePath);

                        // TODO: 변환된 JSON 데이터를 DB에 저장하는 로직 추가, 30초뒤에 추가된 파일이 있으면 그 파일만 DB에 추가
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred processing TDMS files.");
            }

            _logger.LogInformation("Worker running at: {time}", DateTimeOffset.Now);
            await Task.Delay(30000, stoppingToken); // 30초 간격으로 대기
        }
    }
    /// <summary>
    /// tdms를 json으로 변환
    /// </summary>
    /// <param name="completeStream"></param>
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

            if (string.IsNullOrWhiteSpace(groupData.GroupName) || groupData.GroupName == "제목없음") // 공란 또는 제목없음일 때, Untitled로 표시
            {
                groupData.GroupName = "Untitled";
            }

            foreach (var channel in group)
            {
                var channelData = new TdmsChannelData
                {
                    Name = channel.Name,
                    Data = channel.GetData<object>().ToList(),
                    Properties = channel.Properties.ToDictionary(p => p.Key, p => p.Value) // key value를 클래스화하는 방법
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
    ///


}
