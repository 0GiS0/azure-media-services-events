// Default URL for triggering event grid function in the local environment.
// http://localhost:7071/runtime/webhooks/EventGrid?functionName={functionname}
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.EventGrid.Models;
using Microsoft.Azure.WebJobs.Extensions.EventGrid;
using Microsoft.Extensions.Logging;
using Microsoft.Azure.WebJobs.Extensions.SignalRService;
using Microsoft.Extensions.Configuration;
using Microsoft.Azure.Management.Media;
using System.Threading.Tasks;
using Microsoft.Azure.Management.Media.Models;
using Microsoft.Rest;
using Microsoft.IdentityModel.Clients.ActiveDirectory;
using Microsoft.Rest.Azure.Authentication;
using Newtonsoft.Json;

namespace Company.Function
{
    public class ProcessingEvents
    {
        static IAzureMediaServicesClient client = null;
        static string ResourceGroup, AccountName, ClientId, ClientSecret, TenantId, SubscriptionId = "";


        [FunctionName("ProcessingEvents")]
        public async Task Run([EventGridTrigger] EventGridEvent eventGridEvent, ILogger log, [SignalR(HubName = "ams")] IAsyncCollector<SignalRMessage> signalRMessages, ExecutionContext context)
        {
            log.LogInformation("**************************************************************");
            log.LogInformation(eventGridEvent.EventType);
            log.LogInformation("**************************************************************");
            log.LogInformation(eventGridEvent.Data.ToString());

            // await signalRMessages.AddAsync(new SignalRMessage
            // {
            //     Target = "UpdateProgress",
            //     Arguments = new[] { eventGridEvent.Data.ToString() }
            // });


            dynamic data = JsonConvert.DeserializeObject(eventGridEvent.Data.ToString());


            if (eventGridEvent.EventType == "Microsoft.Media.JobOutputProgress")
            {
                log.LogDebug("Processing job output asset event");

                string assetName = data.jobCorrelationData.assetName;

                log.LogInformation($"Asset name: {assetName}");

                var video = new Video();
                video.Name = assetName;
                video.State = "Processing";

                string progress = data.progress;
                log.LogInformation($"Progress: {progress}");

                log.LogInformation("Send SignalR message...");

                //Populate video object                  
                video.Progress = int.Parse(progress);

                log.LogInformation("video asset: " + video.Name);
                log.LogInformation("video progress: " + video.Progress);

                await signalRMessages.AddAsync(new SignalRMessage
                {
                    Target = "UpdateProgress",
                    Arguments = new[] { JsonConvert.SerializeObject(video) }
                });
            }
            else if (eventGridEvent.EventType == "Microsoft.Media.JobStateChange")
            {
                string assetName = data.correlationData.assetName;
                string state = data.state;

                log.LogInformation($"Asset name: {assetName}");
                log.LogInformation($"State: {state}");

                var video = new Video();
                video.Name = assetName;
                video.State = state;

                await signalRMessages.AddAsync(new SignalRMessage
                {
                    Target = "UpdateProgress",
                    Arguments = new[] { JsonConvert.SerializeObject(video) }
                });

            }
            else if (eventGridEvent.EventType == "Microsoft.Media.JobFinished")
            {
                string assetName = data.correlationData.assetName;


                var config = new ConfigurationBuilder()
                                    .SetBasePath(context.FunctionAppDirectory)
                                    .AddJsonFile("local.settings.json", optional: true, reloadOnChange: true)
                                    .AddEnvironmentVariables()
                                    .Build();

                ResourceGroup = config["AMS:ResourceGroup"];
                AccountName = config["AMS:AccountName"];
                ClientId = config["AMS:ClientId"];
                ClientSecret = config["AMS:ClientSecret"];
                TenantId = config["AMS:TenantId"];
                SubscriptionId = config["AMS:SubscriptionId"];

                client = await CreateMediaServicesClientAsync(config);


                log.LogInformation("Job finished");
                log.LogInformation($"Create a streaming locator for {assetName}");

                //Lets create a streaming locator

                StreamingLocator locator = await client.StreamingLocators.CreateAsync(
                    ResourceGroup,
                    AccountName,
                    assetName,
                    new StreamingLocator()
                    {
                        AssetName = assetName,
                        StreamingPolicyName = PredefinedStreamingPolicy.ClearStreamingOnly
                    });

                //Send a message via SignalR to refresh
                await signalRMessages.AddAsync(new SignalRMessage
                {
                    Target = "Refresh",
                    Arguments = new object[] { }
                });
            }
        }

        private static async Task<ServiceClientCredentials> GetCredentialsAsync(IConfiguration config)
        {
            ClientCredential clientCredential = new ClientCredential(ClientId, ClientSecret);

            return await ApplicationTokenProvider.LoginSilentAsync(TenantId, clientCredential, ActiveDirectoryServiceSettings.Azure);
        }

        private static async Task<IAzureMediaServicesClient> CreateMediaServicesClientAsync(IConfiguration config)
        {
            var credentials = await GetCredentialsAsync(config);

            return new AzureMediaServicesClient(credentials)
            {
                SubscriptionId = SubscriptionId,
            };
        }

    }
}
