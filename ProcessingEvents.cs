// Default URL for triggering event grid function in the local environment.
// http://localhost:7071/runtime/webhooks/EventGrid?functionName={functionname}
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.EventGrid.Models;
using Microsoft.Azure.WebJobs.Extensions.EventGrid;
using Microsoft.Extensions.Logging;
using Microsoft.Azure.WebJobs.Extensions.SignalRService;
using Microsoft.Azure.Management.Media;
using System.Threading.Tasks;
using Microsoft.Azure.Management.Media.Models;
using Newtonsoft.Json;
using Microsoft.Extensions.Options;

namespace Company.Function
{
    public class ProcessingEvents
    {
        IAzureMediaServicesClient _client = null;
        MediaSettings _config = null;

        const string _UpdateClientFunction = "UpdateProgress";
        const string _RefreshClientFunction = "Refresh";

        public ProcessingEvents(IAzureMediaServicesClient client, IOptions<MediaSettings> config)
        {
            _client = client;
            _config = config.Value;
        }


        [FunctionName("ProcessingEvents")]
        public async Task Run([EventGridTrigger] EventGridEvent eventGridEvent, ILogger log, [SignalR(HubName = "ams")] IAsyncCollector<SignalRMessage> signalRMessages)
        {
            log.LogInformation("**************************************************************");
            log.LogInformation(eventGridEvent.EventType);
            log.LogInformation("**************************************************************");
            log.LogInformation(eventGridEvent.Data.ToString());


            dynamic data = JsonConvert.DeserializeObject(eventGridEvent.Data.ToString());


            if (eventGridEvent.EventType == "Microsoft.Media.JobOutputProgress")
            {                

                string assetName = data.jobCorrelationData.assetName;

                log.LogInformation($"Asset name: {assetName}");

                var video = new Video();
                video.Name = assetName;
                video.State = "Processing";

                string progress = data.progress;                

                log.LogInformation("Send SignalR message...");

                //Populate video object                  
                video.Progress = int.Parse(progress);

                await signalRMessages.AddAsync(new SignalRMessage
                {
                    Target = _UpdateClientFunction,
                    Arguments = new[] { JsonConvert.SerializeObject(video) }
                });
            }
            else if (eventGridEvent.EventType == "Microsoft.Media.JobStateChange")
            {
                string assetName = data.correlationData.assetName;
                string state = data.state;   

                var video = new Video();
                video.Name = assetName;
                video.State = state;

                await signalRMessages.AddAsync(new SignalRMessage
                {
                    Target = _UpdateClientFunction,
                    Arguments = new[] { JsonConvert.SerializeObject(video) }
                });

            }
            else if (eventGridEvent.EventType == "Microsoft.Media.JobFinished")
            {
                string assetName = data.correlationData.assetName;


                log.LogInformation("Job finished");
                log.LogInformation($"Create a streaming locator for {assetName}");

                //Let's create a streaming locator
                var locator = await _client.StreamingLocators.CreateAsync(
                    _config.ResourceGroup,
                    _config.AccountName,
                    assetName,
                    new StreamingLocator()
                    {
                        AssetName = assetName,
                        StreamingPolicyName = PredefinedStreamingPolicy.ClearStreamingOnly
                    });

                //Send a message via SignalR to refresh
                await signalRMessages.AddAsync(new SignalRMessage
                {
                    Target = _RefreshClientFunction,
                    Arguments = new object[] { }
                });
            }
        }
    }
}