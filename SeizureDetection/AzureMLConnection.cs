using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SeizureDetection
{


    // This code requires the Nuget package Microsoft.AspNet.WebApi.Client to be installed.
    // Instructions for doing this in Visual Studio:
    // Tools -> Nuget Package Manager -> Package Manager Console
    // Install-Package Microsoft.AspNet.WebApi.Client
    //
    // Also, add a reference to Microsoft.WindowsAzure.Storage.dll for reading from and writing to the Azure blob storage

    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Globalization;
    using System.IO;
    using System.Linq;
    using System.Net.Http;
    using System.Net.Http.Formatting;
    using System.Net.Http.Headers;
    using System.Runtime.Serialization;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;

    using Sandboxable.Microsoft.WindowsAzure.Storage.Auth;
    using Sandboxable.Microsoft.WindowsAzure.Storage.Blob;
    using Sandboxable.Microsoft.WindowsAzure.Storage;
    using SeizureDetection;

    namespace CallBatchExecutionService
    {
        public class AzureBlobDataReference
        {
            // Storage connection string used for regular blobs. It has the following format:
            // DefaultEndpointsProtocol=https;AccountName=ACCOUNT_NAME;AccountKey=ACCOUNT_KEY
            // It's not used for shared access signature blobs.
            public string ConnectionString { get; set; }

            // Relative uri for the blob, used for regular blobs as well as shared access 
            // signature blobs.
            public string RelativeLocation { get; set; }

            // Base url, only used for shared access signature blobs.
            public string BaseLocation { get; set; }

            // Shared access signature, only used for shared access signature blobs.
            public string SasBlobToken { get; set; }
        }

        public enum BatchScoreStatusCode
        {
            NotStarted,
            Running,
            Failed,
            Cancelled,
            Finished
        }

        public class BatchScoreStatus
        {
            // Status code for the batch scoring job
            public BatchScoreStatusCode StatusCode { get; set; }


            // Locations for the potential multiple batch scoring outputs
            public IDictionary<string, AzureBlobDataReference> Results { get; set; }

            // Error details, if any
            public string Details { get; set; }
        }

        public class BatchExecutionRequest
        {

            public IDictionary<string, AzureBlobDataReference> Inputs { get; set; }
            public IDictionary<string, string> GlobalParameters { get; set; }

            // Locations for the potential multiple batch scoring outputs
            public IDictionary<string, AzureBlobDataReference> Outputs { get; set; }
        }
        

        public class AzureMLConnection
        {
            public static Thread ConnectionThread;

            private static string[] FilePaths = null;
            public static void Run(string[] filePaths)
            {
                FilePaths = filePaths;
                ConnectionThread = new Thread(new ThreadStart(() =>
                {
                    InvokeBatchExecutionService().ConfigureAwait(true);
                }));
                ConnectionThread.Start();
            }

            static async Task WriteFailedResponse(HttpResponseMessage response)
            {
                MainWindow.Log(string.Format("The request failed with status code: {0}", response.StatusCode));

                // Print the headers - they include the requert ID and the timestamp, which are useful for debugging the failure
                MainWindow.Log(response.Headers.ToString());

                string responseContent = await response.Content.ReadAsStringAsync();
                MainWindow.Log(responseContent);
            }


            static void SaveBlobToFile(AzureBlobDataReference blobLocation, string resultsLabel)
            {
                if (File.Exists("./myresults.csv"))
                    File.Delete("./myresults.csv");

                const string OutputFileLocation = "myresults.csv"; // Replace this with the location you would like to use for your output file
                
                var credentials = new StorageCredentials(blobLocation.SasBlobToken);
                var blobUrl = new Uri(new Uri(blobLocation.BaseLocation), blobLocation.RelativeLocation);
                var cloudBlob = new CloudBlockBlob(blobUrl, credentials);
                
                MainWindow.Log(string.Format("Reading the result from {0}", blobUrl.ToString()));
                cloudBlob.DownloadToFile(OutputFileLocation, FileMode.Create);

                string[][] lines = CsvToLines("./myresults.csv");
                int sampleCount = lines.Length;

                int trueCount = 0;
                int falseCount = 0;
                for(int i = 0; i < sampleCount; i++)
                {
                    if(lines[i][16].Equals("True"))
                    {
                        trueCount++;
                    } else if (lines[i][16].Equals("False"))
                    {
                        falseCount++;
                    }
                }

                if(falseCount > trueCount)
                {
                    MainWindow.SetString("Classified as non-seizure");
                } else
                {
                    MainWindow.SetString("Classified as siezure/post seizure");
                }

                 MainWindow.Log(string.Format("{0} have been written to the file {1}", resultsLabel, OutputFileLocation));
            }

            public static string[][] CsvToLines(string csv)
            {
                string filePath = csv;
                StreamReader sr = new StreamReader(filePath);
                var lines = new List<string[]>();
                int Row = 0;
                while (!sr.EndOfStream)
                {
                    string[] Line = sr.ReadLine().Split(',');
                    lines.Add(Line);
                    Row++;
                }

                var data = lines.ToArray();
                sr.Close();
                return data;
            }

            static void UploadFileToBlob(string inputFileLocation, string inputBlobName, string storageContainerName, string storageConnectionString)
            {
                // Make sure the file exists
                if (!File.Exists(inputFileLocation))
                {
                    throw new FileNotFoundException(
                        string.Format(
                            CultureInfo.InvariantCulture,
                            "File {0} doesn't exist on local computer.",
                            inputFileLocation));
                }

                MainWindow.Log("Uploading the input to blob storage...");

                var blobClient = CloudStorageAccount.Parse(storageConnectionString).CreateCloudBlobClient();
                var container = blobClient.GetContainerReference(storageContainerName);
                container.CreateIfNotExists();
                var blob = container.GetBlockBlobReference(inputBlobName);
                blob.UploadFromFile(inputFileLocation);
            }



            static void ProcessResults(BatchScoreStatus status)
            {

                MainWindow.Running = false;
                bool first = true;
                foreach (var output in status.Results)
                {
                    var blobLocation = output.Value;
                    MainWindow.Log(string.Format("The result '{0}' is available at the following Azure Storage location:", output.Key));
                    MainWindow.Log(string.Format("BaseLocation: {0}", blobLocation.BaseLocation));
                    MainWindow.Log(string.Format("RelativeLocation: {0}", blobLocation.RelativeLocation));
                    MainWindow.Log(string.Format("SasBlobToken: {0}", blobLocation.SasBlobToken));


                    // Save the first output to disk
                    if (first)
                    {
                        first = false;
                        SaveBlobToFile(blobLocation, string.Format("The results for {0}", output.Key));
                    }
                }
            }

            static async Task InvokeBatchExecutionService()
            {
                // How this works:
                //
                // 1. Assume the input is present in a local file (if the web service accepts input)
                // 2. Upload the file to an Azure blob - you'd need an Azure storage account
                // 3. Call the Batch Execution Service to process the data in the blob. Any output is written to Azure blobs.
                // 4. Download the output blob, if any, to local file

                const string BaseUrl = "https://ussouthcentral.services.azureml.net/workspaces/6feec1b9ce134b188a53d3982caab27f/services/26f636803a3f4cc79b20c6963ae2c058/jobs";

                const string StorageAccountName = "sp1062"; // Replace this with your Azure Storage Account name
                const string StorageAccountKey = "1BeacAnmksqzwjaSfGO630FHQTPGtBUYJ7nBqZkg5MJXMN2aBR8SGRGmX83vh1yMgy6vUaIqSkto3FE/36LvWg=="; // Replace this with your Azure Storage Key
                const string StorageContainerName = "sp1062"; // Replace this with your Azure Storage Container name
                string storageConnectionString = string.Format("DefaultEndpointsProtocol=https;AccountName={0};AccountKey={1}", StorageAccountName, StorageAccountKey);
                const string apiKey = "hiGX1YEcEX6vhmwcIzmK56LjsxQcYHJFLy1KtPcS8z09cwmCRd2cfdnnWymhhPK35w0kTtpTCiiQakH7VdE77g=="; // Replace this with the API key for the web service

                // set a time out for polling status
                const int TimeOutInMilliseconds = 480 * 1000; // Set a timeout of 8 minutes



                UploadFileToBlob(FilePaths[2] /*Replace this with the location of your input file*/,
                   "DataToClassifydatablob.csv" /*Replace this with the name you would like to use for your Azure blob; this needs to have the same extension as the input file */,
                   StorageContainerName, storageConnectionString);

                UploadFileToBlob(FilePaths[1] /*Replace this with the location of your input file*/,
                   "TrainingSetTwodatablob.csv" /*Replace this with the name you would like to use for your Azure blob; this needs to have the same extension as the input file */,
                   StorageContainerName, storageConnectionString);

                UploadFileToBlob(FilePaths[0] /*Replace this with the location of your input file*/,
                   "TrainingSetOPnepdatablob.csv" /*Replace this with the name you would like to use for your Azure blob; this needs to have the same extension as the input file */,
                   StorageContainerName, storageConnectionString);

                using (HttpClient client = new HttpClient())
                {
                    var request = new BatchExecutionRequest()
                    {

                        Inputs = new Dictionary<string, AzureBlobDataReference>()
                    {

                        {
                            "DataToClassify",
                            new AzureBlobDataReference()
                            {
                                ConnectionString = storageConnectionString,
                                RelativeLocation = string.Format("{0}/DataToClassifydatablob.csv", StorageContainerName)
                            }
                        },

                        {
                            "TrainingSetTwo",
                            new AzureBlobDataReference()
                            {
                                ConnectionString = storageConnectionString,
                                RelativeLocation = string.Format("{0}/TrainingSetTwodatablob.csv", StorageContainerName)
                            }
                        },

                        {
                            "TrainingSetOPnep",
                            new AzureBlobDataReference()
                            {
                                ConnectionString = storageConnectionString,
                                RelativeLocation = string.Format("{0}/TrainingSetOPnepdatablob.csv", StorageContainerName)
                            }
                        },
                    },

                        Outputs = new Dictionary<string, AzureBlobDataReference>()
                    {

                        {
                            "OutputData",
                            new AzureBlobDataReference()
                            {
                                ConnectionString = storageConnectionString,
                                RelativeLocation = string.Format("/{0}/OutputDataresults.csv", StorageContainerName)
                            }
                        },
                    },
                        GlobalParameters = new Dictionary<string, string>()
                        {
                        }
                    };

                    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

                    // WARNING: The 'await' statement below can result in a deadlock if you are calling this code from the UI thread of an ASP.Net application.
                    // One way to address this would be to call ConfigureAwait(false) so that the execution does not attempt to resume on the original context.
                    // For instance, replace code such as:
                    //      result = await DoSomeTask()
                    // with the following:
                    //      result = await DoSomeTask().ConfigureAwait(false)


                    MainWindow.Log("Submitting the job...");

                    // submit the job
                    var response = await client.PostAsJsonAsync(BaseUrl + "?api-version=2.0", request);
                    if (!response.IsSuccessStatusCode)
                    {
                        await WriteFailedResponse(response);
                        return;
                    }

                    string jobId = await response.Content.ReadAsAsync<string>();
                    MainWindow.Log(string.Format("Job ID: {0}", jobId));


                    // start the job
                    MainWindow.Log("Starting the job...");
                    response = await client.PostAsync(BaseUrl + "/" + jobId + "/start?api-version=2.0", null);
                    if (!response.IsSuccessStatusCode)
                    {
                        await WriteFailedResponse(response);
                        return;
                    }

                    string jobLocation = BaseUrl + "/" + jobId + "?api-version=2.0";
                    Stopwatch watch = Stopwatch.StartNew();
                    bool done = false;
                    while (!done)
                    {
                        MainWindow.Log("Checking the job status...");
                        response = await client.GetAsync(jobLocation);
                        if (!response.IsSuccessStatusCode)
                        {
                            await WriteFailedResponse(response);
                            return;
                        }

                        BatchScoreStatus status = await response.Content.ReadAsAsync<BatchScoreStatus>();
                        if (watch.ElapsedMilliseconds > TimeOutInMilliseconds)
                        {
                            done = true;
                            MainWindow.Log(string.Format("Timed out. Deleting job {0} ...", jobId));
                            await client.DeleteAsync(jobLocation);
                        }
                        switch (status.StatusCode)
                        {
                            case BatchScoreStatusCode.NotStarted:
                                MainWindow.Log(string.Format("Job {0} not yet started...", jobId));
                                break;
                            case BatchScoreStatusCode.Running:
                                MainWindow.Log(string.Format("Job {0} running...", jobId));
                                break;
                            case BatchScoreStatusCode.Failed:
                                MainWindow.Log(string.Format("Job {0} failed!", jobId));
                                MainWindow.Log(string.Format("Error details: {0}", status.Details));
                                done = true;
                                break;
                            case BatchScoreStatusCode.Cancelled:
                                MainWindow.Log(string.Format("Job {0} cancelled!", jobId));
                                done = true;
                                break;
                            case BatchScoreStatusCode.Finished:
                                done = true;
                                MainWindow.Log(string.Format("Job {0} finished!", jobId));

                                ProcessResults(status);
                                break;
                        }

                        if (!done)
                        {
                            Thread.Sleep(1000); // Wait one second
                        }
                    }
                }
            }
        }
    }
}
