//
// Copyright 2019 Amazon.com, Inc. or its affiliates. All Rights Reserved.
//
// Permission is hereby granted, free of charge, to any person obtaining a copy of this
// software and associated documentation files (the "Software"), to deal in the Software
// without restriction, including without limitation the rights to use, copy, modify,
// merge, publish, distribute, sublicense, and/or sell copies of the Software, and to
// permit persons to whom the Software is furnished to do so.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED,
// INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A
// PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT
// HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION
// OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE
// SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
//

using System;
using System.IO;
using System.Text;

using Newtonsoft.Json;

using Amazon.Lambda.Core;
using Amazon.Lambda.DynamoDBEvents;
using Amazon.DynamoDBv2.Model;

using Amazon.SimpleNotificationService;
using Amazon.SimpleNotificationService.Model;
using Amazon.StepFunctions;
using Amazon.StepFunctions.Model;
using Amazon.XRay.Recorder.Handlers.AwsSdk;
using Amazon.SimpleSystemsManagement;
using Amazon.SimpleSystemsManagement.Model;

// Assembly attribute to enable the Lambda function's JSON input to be converted into a .NET class.
[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.Json.JsonSerializer))]

namespace LeaveRequestProcessor
{
    public class Function
    {
        private static readonly JsonSerializer _jsonSerializer = new JsonSerializer();
        private static readonly string SNSTOPIC_ARN = "<SNS_TOPIC_ARN>";
        private static string STATEMACHINE_ARN = "<STEP_FUNCTION_STATEMACHINE_ARN>";

        public void FunctionHandler(DynamoDBEvent dynamoEvent, ILambdaContext context)
        {
            context.Logger.LogLine($"Beginning to process {dynamoEvent.Records.Count} records...");

            AWSSDKHandler.RegisterXRayForAllServices();

            var snsClient = new AmazonSimpleNotificationServiceClient();

            foreach (var record in dynamoEvent.Records)
            {
                context.Logger.LogLine($"Event ID: {record.EventID}");
                context.Logger.LogLine($"Event Name: {record.EventName}");
                if (record.EventName == "INSERT")
                {
                    string streamRecordJson = SerializeStreamRecord(record.Dynamodb);

                    context.Logger.LogLine($"DynamoDB Record:");
                    context.Logger.LogLine(streamRecordJson);
                    context.Logger.LogLine("Name   - " + record.Dynamodb.NewImage["EmployeeName"].S);
                    snsClient.PublishAsync(new PublishRequest()
                    {
                        Message = $"{record.Dynamodb.NewImage["EmployeeName"].S} has submitted a Leave Request. Please respond.",
                        TopicArn = SNSTOPIC_ARN
                    }).Wait();
                }
                else if (record.EventName == "MODIFY" && record.Dynamodb.NewImage["LeaveStatus"].S == "Approved")
                {
                    context.Logger.LogLine($"About to kickoff Step Function workflow");

                    new AmazonStepFunctionsClient().StartExecutionAsync(new StartExecutionRequest()
                    {
                        StateMachineArn = STATEMACHINE_ARN,
                        Input = $"\"{record.Dynamodb.NewImage["EmployeeName"].S}\"",
                        Name = Guid.NewGuid().ToString()
                    }).Wait();

                    context.Logger.LogLine($"Workflow complete");

                }
            }

            context.Logger.LogLine("Stream processing complete.");
        }

        private string SerializeStreamRecord(StreamRecord streamRecord)
        {
            using (var writer = new StringWriter())
            {
                _jsonSerializer.Serialize(writer, streamRecord);
                return writer.ToString();
            }
        }
    }
}