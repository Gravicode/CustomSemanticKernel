﻿// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Orchestration;
using Microsoft.SemanticKernel.Reliability;
using Microsoft.SemanticKernel.Skills.OpenAPI.Authentication;
using Microsoft.SemanticKernel.Skills.OpenAPI.Skills;
using RepoUtils;

#pragma warning disable CA1861 // Avoid constant arrays as arguments
// ReSharper disable once InconsistentNaming
public static class Example22_OpenApiSkill_AzureKeyVault
{
    public static async Task RunAsync()
    {
        // To run this example, you must register a client application with the Microsoft identity platform.
        // Instructions here: https://learn.microsoft.com/en-us/azure/active-directory/develop/quickstart-register-app
        var authenticationProvider = new InteractiveMsalAuthenticationProvider(
            Env.Var("AZURE_KEYVAULT_CLIENTID"),
            Env.Var("AZURE_KEYVAULT_TENANTID"),
            new[] { "https://vault.azure.net/.default" },
            new Uri("http://localhost"));

        await GetSecretFromAzureKeyVaultWithRetryAsync(authenticationProvider);

        await AddSecretToAzureKeyVaultAsync(authenticationProvider);
    }

    public static async Task GetSecretFromAzureKeyVaultWithRetryAsync(InteractiveMsalAuthenticationProvider authenticationProvider)
    {
        var kernel = new KernelBuilder().WithLogger(ConsoleLogger.Log).Build();

        var retryConfig = new HttpRetryConfig() { MaxRetryCount = 3, UseExponentialBackoff = true };

        // Import a OpenApi skill using one of the following Kernel extension methods
        // kernel.ImportOpenApiSkillFromResource
        // kernel.ImportOpenApiSkillFromDirectory
        // kernel.ImportOpenApiSkillFromFile
        // kernel.ImportOpenApiSkillFromUrlAsync
        // kernel.RegisterOpenApiSkill
        var skill = await kernel.ImportOpenApiSkillFromResourceAsync(SkillResourceNames.AzureKeyVault,
            authCallback: authenticationProvider.AuthenticateRequestAsync,
            retryConfiguration: retryConfig);

        // Add arguments for required parameters, arguments for optional ones can be skipped.
        var contextVariables = new ContextVariables();
        contextVariables.Set("server-url", "https://<keyvault-name>.vault.azure.net");
        contextVariables.Set("secret-name", "<secret-name>");
        contextVariables.Set("api-version", "7.0");

        // Run
        var result = await kernel.RunAsync(contextVariables, skill["GetSecret"]);

        Console.WriteLine("GetSecret skill response: {0}", result);
    }

    public static async Task AddSecretToAzureKeyVaultAsync(InteractiveMsalAuthenticationProvider authenticationProvider)
    {
        var kernel = new KernelBuilder().WithLogger(ConsoleLogger.Log).Build();

        // Import a OpenApi skill using one of the following Kernel extension methods
        // kernel.ImportOpenApiSkillFromResource
        // kernel.ImportOpenApiSkillFromDirectory
        // kernel.ImportOpenApiSkillFromFile
        // kernel.ImportOpenApiSkillFromUrlAsync
        // kernel.RegisterOpenApiSkill
        var skill = await kernel.ImportOpenApiSkillFromResourceAsync(SkillResourceNames.AzureKeyVault,
            authenticationProvider.AuthenticateRequestAsync);

        // Add arguments for required parameters, arguments for optional ones can be skipped.
        var contextVariables = new ContextVariables();
        contextVariables.Set("server-url", "https://<keyvault-name>.vault.azure.net");
        contextVariables.Set("secret-name", "<secret-name>");
        contextVariables.Set("api-version", "7.0");
        contextVariables.Set("payload", JsonSerializer.Serialize(new { value = "<secret>", attributes = new { enabled = true } }));

        // Run
        var result = await kernel.RunAsync(contextVariables, skill["SetSecret"]);

        Console.WriteLine("SetSecret skill response: {0}", result);
    }
}
