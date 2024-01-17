using System.Text;
using System.Text.Json;
using static TdmsDataContext;

public class TDMSWorkerService : BackgroundService
{
    readonly ILogger<TDMSWorkerService> _logger;
    private string? jsonData;
    private HashSet<string> currentDayProcessedFiles = new HashSet<string>(); // ���� ��¥�� ���� ������ �����ϴ� �ؽü�
    private string lastProcessedDate; // ���������� ó���� ��¥�� ����

    public TDMSWorkerService(ILogger<TDMSWorkerService> logger)
    {
        _logger = logger;
        lastProcessedDate = GetCurrentDateString(); // ���� ���� �� ��¥ �ʱ�ȭ
    }

    private string GetCurrentDateString() => DateTime.Now.ToString("yyyy-MM-dd"); // ���� ��¥ ��������

    private string TdmsFilesPath => Path.Combine(@"D:\repos\FMTDMS\", GetCurrentDateString()); // ���� ��¥ ���

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                string currentDate = GetCurrentDateString();

                // ��¥�� ����Ǿ����� Ȯ���ϰ�, ����Ǿ��ٸ� �ؽü� �ʱ�ȭ
                if (currentDate != lastProcessedDate)
                {
                    currentDayProcessedFiles.Clear();
                    lastProcessedDate = currentDate;
                    _logger.LogInformation("New day detected, reset processed files for {currentDate}", currentDate);
                }

                if (Directory.Exists(TdmsFilesPath))
                {
                    await ProcessDirectoryAsync(TdmsFilesPath, stoppingToken); // tdms ���ϵ��� ó��
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred processing TDMS files.");
            }
            await Task.Delay(30000, stoppingToken); // ������ ����
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

            // ���� ó�� �� JSON ��ȯ
            using (var fileStream = File.OpenRead(filePath))
            {
                using (var completeStream = new MemoryStream())
                {
                    await fileStream.CopyToAsync(completeStream);
                    completeStream.Position = 0;

                    ProcessTdmsFile(completeStream);
                }
            }

            // JSON �����͸� ���̳ʸ��� ��ȯ
            byte[] binaryData = Encoding.UTF8.GetBytes(jsonData);

            // �����ͺ��̽��� ���̳ʸ� ������ ����
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

            // �ؽü¿� ���� ��� �߰�
            currentDayProcessedFiles.Add(filePath);

            // JSON ���Ϸ� ���� (������)
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
            // �޸� ����
            jsonData = null;
        }
    }



    private void ProcessTdmsFile(MemoryStream completeStream)
    {
        using var tdms = new NationalInstruments.Tdms.File(completeStream);
        tdms.Open();

        if (!tdms.Any()) return; // TDMS ���Ͽ� �׷��� ������ ����

        var firstGroup = tdms.First(); // ù ��° �׷�

        if (!firstGroup.Any()) return; // ù ��° �׷쿡 ä���� ������ ����

        var firstChannel = firstGroup.First(); // ù ��° ä��

        var firstChannelData = new TdmsChannelData
        {
            //Name = firstChannel.Name,
            Data = firstChannel.GetData<double>().Select(value => (float)value).ToList(),
            //Properties = firstChannel.Properties.ToDictionary(p => p.Key, p => p.Value)
        };

        // ù ��° ä���� �����͸� JSON���� ��ȯ
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