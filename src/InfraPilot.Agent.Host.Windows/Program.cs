using InfraPilot.Agent.Core;
using InfraPilot.Capabilities.FileTree.Windows;
using InfraPilot.Capabilities.Iis.Windows;
using InfraPilot.Capabilities.ScheduledTasks.Windows;
using InfraPilot.Capabilities.Services.Windows;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddWindowsService(options =>
{
    options.ServiceName = "InfraPilot Agent";
});

builder.Services.AddInfraPilotAgentCore(builder.Configuration);
builder.Services.AddWindowsServicesCapability(builder.Configuration);
builder.Services.AddWindowsScheduledTasksCapability(builder.Configuration);
builder.Services.AddWindowsFileTreeCapability(builder.Configuration);
builder.Services.AddWindowsIisCapability(builder.Configuration);

var host = builder.Build();
await host.RunAsync();
