using MathNet.Numerics.IntegralTransforms;
using MathNet.Numerics;
using System.Numerics;
using System.Text.Json;

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

            using var fileStream = File.OpenRead(filePath);
            using var completeStream = new MemoryStream();
            await fileStream.CopyToAsync(completeStream);
            completeStream.Position = 0;

            ProcessTdmsFile(completeStream);
            currentDayProcessedFiles.Add(filePath); // ������ ó���� �� �ؽü¿� �߰�

            // JSON ���� ���� ����
            string jsonFilePath = Path.ChangeExtension(filePath, ".json");
            await File.WriteAllTextAsync(jsonFilePath, jsonData); // jsonData�� json ���Ϸ� ����

            _logger.LogInformation("TDMS file converted to JSON successfully: {filePath}", jsonFilePath);
        }
        catch (Exception ex)
        {
            _logger.LogError("Failed to convert TDMS file to JSON: {filePath}. Error: {error}", filePath, ex.ToString());
        }
        finally
        {
            // �޸� ����
            jsonData = null; // jsonData ��� �Ϸ� �� null ó��
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
                // TDMS ���Ϸκ��� ������ �б� (����: double[] ����)
                var rawData = channel.GetData<double>().ToArray();

                // Hanning ������ ����
                var hanningWindow = Window.Hann(rawData.Length);
                var windowedData = rawData.Zip(hanningWindow, (data, window) => data * window).ToArray();

                // double[]�� Complex[]�� ��ȯ
                Complex[] complexData = windowedData.Select(value => new Complex(value, 0)).ToArray();

                // FFT ó��
                Fourier.Forward(complexData, FourierOptions.Default);

                // FFT ����� ù ��° ���ݸ� ���
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