using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Covid19Radar.Models;
using Covid19Radar.DataStore;
using Microsoft.Azure.Cosmos;

namespace Covid19Radar.Api
{
    public class RegisterApi
    {
        private readonly ICosmos Cosmos;
        private readonly ILogger<RegisterApi> Logger;
        public RegisterApi(ICosmos cosmos, ILogger<RegisterApi> logger)
        {
            Cosmos = cosmos;
            Logger = logger;
        }


        [FunctionName("Register")]
        public async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", "post", Route = null)] HttpRequest req)
        {
            Logger.LogInformation("C# HTTP trigger function processed a request.");

            switch (req.Method)
            {
                case "GET":
                    return await Get(req);
                case "POST":
                    return await Post(req);
            }

            return new BadRequestObjectResult("Not Supported");
        }

        private async Task<IActionResult> Get(HttpRequest req)
        {
            // get name from query 
            var userUuid = req.Query["UserUuid"];


            // save to DB
            return await Register(userUuid);
        }

        private async Task<IActionResult> Post(HttpRequest req)
        {
            // convert Postdata to UserDataModel
            string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            var data = JsonConvert.DeserializeObject<UserDataModel>(requestBody);
            var userUuid = data.UserUuid;

            // save to DB
            return await Register(data.UserUuid);
        }

        private async Task<SequenceDataModel> GetNumber()
        {
            var id = SequenceDataModel._id.ToString();
            for (var i = 0; i < 100; i++)
            {
                var result = await Cosmos.Sequence.ReadItemAsync<SequenceDataModel>(id, PartitionKey.None);
                var model = result.Resource;
                model.Increment();
                var option = new ItemRequestOptions();
                option.IfMatchEtag = model._etag;
                try
                {
                    var resultReplace = await Cosmos.Sequence.ReplaceItemAsync(model, id, null, option);
                    return resultReplace.Resource;
                }
                catch (CosmosException ex)
                {
                    Logger.LogInformation(ex, $"GetNumber Retry {i}");
                    continue;
                }
            }
            Logger.LogWarning("GetNumber is over retry count.");
            return null;
        }

        private async Task<IActionResult> Register(string userUuid)
        {
            var number = await GetNumber();
            // 503 Error 番号の取得に失敗 
            if (number == null)
            {
                return new StatusCodeResult(503);
            }

            var newItem = new UserDataModel();
            newItem.id = Guid.NewGuid().ToString();
            newItem.UserUuid = userUuid;
            newItem.Major = number.Major.ToString();
            newItem.Minor = number.Minor.ToString();
            var result = await Cosmos.User.CreateItemAsync(newItem);
            return new StatusCodeResult((int)result.StatusCode);
        }
    }
}
