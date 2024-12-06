#nullable disable

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RCL.SSL.AutoRenew.Function;
using RCL.SSL.SDK;

IConfiguration configuration = null;

var host = new HostBuilder()
    .ConfigureFunctionsWorkerDefaults()
    .ConfigureAppConfiguration(builder =>
    {
        builder.AddJsonFile("local.settings.json", true, true);
        builder.AddEnvironmentVariables();
        configuration = builder.Build();
    })
    .ConfigureServices(services =>
    {
        services.AddRCLAPIService(options => configuration.Bind("RCLSSLAPI", options));
        services.AddRCLAzureAccessTokenService(options => configuration.Bind("MicrosoftEntraApp", options));
        services.Configure<CertificateOptions>(options => configuration.Bind("Certificate", options));
    })
    .Build();

host.Run();