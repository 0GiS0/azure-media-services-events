using System.IO;
using System.Threading.Tasks;
using Microsoft.Azure.Functions.Extensions.DependencyInjection;
using Microsoft.Azure.Management.Media;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Clients.ActiveDirectory;
using Microsoft.Rest;
using Microsoft.Rest.Azure.Authentication;

[assembly: FunctionsStartup(typeof(Company.Function.Startup))]

namespace Company.Function
{
  public class Startup : FunctionsStartup
  {
    private const string _configurationSectionName = "AMS";
    public override void Configure(IFunctionsHostBuilder builder)
    {

      var configuration = builder.GetContext().Configuration;
      // Map configuration values to the settings class.
      var mediaSettings = new MediaSettings();
      configuration.Bind(_configurationSectionName, mediaSettings);

      //Configure singleton services
      builder.Services.AddSingleton<IAzureMediaServicesClient>((s) =>
        {
          return CreateMediaServicesClientAsync(mediaSettings);

        });


      builder.Services.AddOptions<MediaSettings>().Configure<IConfiguration>((settings, configuration) =>
        {
          configuration.GetSection(_configurationSectionName).Bind(settings);
        });

    }

    public override void ConfigureAppConfiguration(IFunctionsConfigurationBuilder builder)
    {
      FunctionsHostBuilderContext context = builder.GetContext();

      builder.ConfigurationBuilder
          .AddJsonFile(Path.Combine(context.ApplicationRootPath, "local.settings.json"), optional: true, reloadOnChange: false)
          .AddJsonFile(Path.Combine(context.ApplicationRootPath, "appsettings.json"), optional: true, reloadOnChange: false)
          .AddJsonFile(Path.Combine(context.ApplicationRootPath, $"appsettings.{context.EnvironmentName}.json"), optional: true, reloadOnChange: false)
          .AddEnvironmentVariables();
    }

    private static async Task<ServiceClientCredentials> GetCredentialsAsync(MediaSettings config)
    {
      var clientCredential = new ClientCredential(config.ClientId, config.ClientSecret);

      return await ApplicationTokenProvider.LoginSilentAsync(config.TenantId, clientCredential, ActiveDirectoryServiceSettings.Azure);
    }

    private static IAzureMediaServicesClient CreateMediaServicesClientAsync(MediaSettings config)
    {
      var credentials = GetCredentialsAsync(config).GetAwaiter().GetResult();

      return new AzureMediaServicesClient(credentials)
      {
        SubscriptionId = config.SubscriptionId,
      };
    }
  }
}