// Copyright(c) 2024 Jeff Hotchkiss
// Licensed under the MIT License. See License.md in the project root for license information.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using DotNet.Testcontainers.Configurations;
using DotNet.Testcontainers.Containers;
#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider adding the 'required' modifier or declaring as nullable.

namespace AspNetCore.DataProtection.Aws.IntegrationTests;

public class LocalstackContainerHealthCheck : IWaitUntil
{
    private readonly string _endpoint;

    public LocalstackContainerHealthCheck(string endpoint)
    {
        _endpoint = endpoint;
    }

    public class Script()
    {
        [property: JsonPropertyName("stage")]
        public string Stage { get; set; }
        [property: JsonPropertyName("state")]
        public string State { get; set; }
        [property: JsonPropertyName("name")]
        public string Name { get; set; }
    }

    /// <inheritdoc />
    public async Task<bool> UntilAsync(IContainer container)
    {
        // https://github.com/localstack/localstack/pull/6716
        using var httpClient = new HttpClient { BaseAddress = new Uri(_endpoint) };
        JsonNode? result;
        try
        {
            result = await httpClient.GetFromJsonAsync<JsonNode>("/_localstack/init/ready");
        }
        catch
        {
            return false;
        }

        if (result is null)
            return false;

        var scripts = result["scripts"];
        if (scripts is null)
            return false;

        foreach (var script in scripts.Deserialize<IEnumerable<Script>>() ?? Enumerable.Empty<Script>())
        {
            if (!"READY".Equals(script.Stage, StringComparison.OrdinalIgnoreCase))
                continue;

            if (!"init.sh".Equals(script.Name, StringComparison.OrdinalIgnoreCase))
                continue;

            return "SUCCESSFUL".Equals(script.State, StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }
}
