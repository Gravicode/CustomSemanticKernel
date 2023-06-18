// Copyright (c) Microsoft. All rights reserved.

using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Orchestration;
using RepoUtils;

namespace TestPalmApi;

internal class Program
{
    const string PALM_API_KEY = "[PUT YOUR PALM API KEY HERE]";
    const string Model = "text-bison-001";
    static async Task Main(string[] args)
    {
        await RunAsync();

        Console.ReadLine();
    }

    public static async Task RunAsync()
    {
        Console.WriteLine("======== PaLM QA AI ========");
        Console.WriteLine("type 'exit' to close");
        List<Chat> chatList = new List<Chat>();
        IKernel kernel = new KernelBuilder()
            .WithLogger(ConsoleLogger.Log)
            .WithPaLMTextCompletionService(Model, apiKey: PALM_API_KEY)
            .Build();

        const string FunctionDefinition = "{{$history}} Question: {{$input}}; Answer:";


        var questionAnswerFunction = kernel.CreateSemanticFunction(FunctionDefinition);
        while (true)
        {
            Console.Write("Q: ");
            var question = Console.ReadLine();
            if (string.IsNullOrEmpty(question)) continue;
            var context = new ContextVariables();
            context.Set("input", question);
            context.Set("history", GetHistory());
            try
            {
                var result = await kernel.RunAsync(context, questionAnswerFunction);
                Console.WriteLine($"A: {result.Result}");
                chatList.Add(new Chat() { Question = question, Answer = result.Result });
            }
            catch (Exception ex)
            {
                Console.WriteLine("try another question..");
            }
           
            /*
            foreach (var modelResult in result.ModelResults)
            {
                var resp = modelResult.GetPaLMResult();
                Console.WriteLine(resp.AsJson());
            }*/
            if (question == "exit")
            {
               
                break;
            }
        }
        

        string GetHistory()
        {
            var history = string.Empty;
            foreach(var chat in chatList)
            {
                history += $"Question: {chat.Question}; Answer:{chat.Answer};\n";
            }
            return history;
        }
    }
}

public class Chat
{
    public string Question { get; set; }
    public string Answer { get; set; }
}
