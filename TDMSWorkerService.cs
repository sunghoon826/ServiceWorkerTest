using System.Text.Json;
public class TDMSWorkerService : BackgroundService
{
    private string? jsonData;
    readonly ILogger<TDMSWorkerService> _logger;
    private const string TdmsFilesPath = @"D:\\repos\\FMTDMS\\2023-11-15"; // ��¥ �κ� path�� ����Ǿ���.  date now�� ��Ʈ���� ��.  while���� ���� ��¥�� ��´� 

    public TDMSWorkerService(ILogger<TDMSWorkerService> logger)
    {
        _logger = logger;
    }
    private HashSet<string> processedFiles = new HashSet<string>(); //�Ϸ翡 �ѹ� �ؽü��� �ϳ� �����. 
    protected async override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var tdmsFiles = Directory.GetFiles(TdmsFilesPath, "*.tdms");
                foreach (var filePath in tdmsFiles)
                {
                    // ���� �߰��� ���ϸ� ó��
                    if (!processedFiles.Contains(filePath))
                    {
                        _logger.LogInformation("Found new TDMS file: {filePath}", filePath);
                        using var fileStream = File.OpenRead(filePath);
                        using var completeStream = new MemoryStream();
                        await fileStream.CopyToAsync(completeStream);
                        completeStream.Position = 0;
                        ProcessTdmsFile(completeStream); // �޸� ��Ʈ������ ��ȯ�Ͽ� ó��

                        // ó���� ������ ���
                        processedFiles.Add(filePath);

                        // TODO: ��ȯ�� JSON �����͸� DB�� �����ϴ� ���� �߰�, 30�ʵڿ� �߰��� ������ ������ �� ���ϸ� DB�� �߰�
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred processing TDMS files.");
            }

            _logger.LogInformation("Worker running at: {time}", DateTimeOffset.Now);
            await Task.Delay(30000, stoppingToken); // 30�� �������� ���
        }
    }
    /// <summary>
    /// tdms�� json���� ��ȯ
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

            if (string.IsNullOrWhiteSpace(groupData.GroupName) || groupData.GroupName == "�������") // ���� �Ǵ� ��������� ��, Untitled�� ǥ��
            {
                groupData.GroupName = "Untitled";
            }

            foreach (var channel in group)
            {
                var channelData = new TdmsChannelData
                {
                    Name = channel.Name,
                    Data = channel.GetData<object>().ToList(),
                    Properties = channel.Properties.ToDictionary(p => p.Key, p => p.Value) // key value�� Ŭ����ȭ�ϴ� ���
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
