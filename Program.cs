
var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddHostedService<TDMSWorkerService>();

var host = builder.Build();
host.Run();
