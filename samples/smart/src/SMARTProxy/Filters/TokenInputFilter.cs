﻿// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Net;
using System.Web;
using Microsoft.AzureHealth.DataServices.Clients.Headers;
using Microsoft.AzureHealth.DataServices.Filters;
using Microsoft.AzureHealth.DataServices.Pipelines;
using Microsoft.Extensions.Logging;
using SMARTProxy.Configuration;
using SMARTProxy.Models;
using SMARTProxy.Services;

namespace SMARTProxy.Filters
{
    public sealed class TokenInputFilter : IInputFilter
    {
        private readonly ILogger _logger;
        private readonly SMARTProxyConfig _configuration;
        private readonly string _id;
        private readonly AsymmetricAuthorizationService _asymmetricAuthorizationService;

        public TokenInputFilter(ILogger<TokenInputFilter> logger, SMARTProxyConfig configuration, AsymmetricAuthorizationService asymmetricAuthorizationService)
        {
            _logger = logger;
            _configuration = configuration;
            _id = Guid.NewGuid().ToString();
            _asymmetricAuthorizationService = asymmetricAuthorizationService;
        }

#pragma warning disable CS0067 // Needed to implement interface.
        public event EventHandler<FilterErrorEventArgs>? OnFilterError;
#pragma warning restore CS0067 // Needed to implement interface.

        public string Name => nameof(TokenInputFilter);

        public StatusType ExecutionStatusType => StatusType.Normal;

        string IFilter.Id => _id;

        public async Task<OperationContext> ExecuteAsync(OperationContext context)
        {
            // Only execute for token request
            if (!context.Request.RequestUri!.LocalPath.Contains("token", StringComparison.InvariantCultureIgnoreCase))
            {
                return context;
            }

            _logger?.LogInformation("Entered {Name}", Name);

            if (!context.Request.Content!.Headers.GetValues("Content-Type").Single().Contains("application/x-www-form-urlencoded", StringComparison.CurrentCultureIgnoreCase))
            {
                context.IsFatal = true;
                context.ContentString = new PipelineError("Content Type must be application/x-www-form-urlencoded.", string.Empty, DateTime.Now, _id).ToContentString();
                context.StatusCode = HttpStatusCode.BadRequest;
                _logger.LogTrace("Content Type must be application/x-www-form-urlencoded.");
                return context;
            }

            // Parse the request body
            TokenContext? tokenContext = null;

            try
            {
                var requestData = HttpUtility.ParseQueryString(context.ContentString);
                tokenContext = TokenContext.FromRequest(requestData!, context.Request.Headers, _configuration.Audience!, _logger!);
                tokenContext.Validate();
            }
            catch (Exception ex)
            {
                context.IsFatal = true;
                context.ContentString = $"Token request invalid. Trace ID {_id}.";
                context.ContentString = new PipelineError("Token request is invalid.", string.Empty, DateTime.Now, _id).ToContentString();
                context.StatusCode = HttpStatusCode.BadRequest;
                _logger.LogError(ex, "Token request invalid. {TokenContext}", tokenContext?.ToLogString() ?? context.ContentString);
                return context;
            }

            // Setup new http client for token request
            string tokenEndpoint = "https://login.microsoftonline.com/";
            string tokenPath = $"{_configuration.TenantId}/oauth2/v2.0/token";
            context.UpdateRequestUri(context.Request.Method, tokenEndpoint, tokenPath);

            // Azure AD does not support bare JWKS auth or 384 JWKS auth. We must convert to an associated client secret flow.
            if (tokenContext.GetType() == typeof(ClientConfidentialAsyncTokenContext))
            {
                var castTokenContext = (ClientConfidentialAsyncTokenContext)tokenContext;

                // Check client id to see if it exists. Get JWKS.
                BackendClientConfiguration? clientConfig = null;

                // Fetch the jwks for this client and validate

                try
                {
                    clientConfig = await _asymmetricAuthorizationService.AuthenticateBackendAsyncClient(castTokenContext);
                }
                catch (HttpRequestException ex)
                {
                    context.IsFatal = true;
                    context.ContentString = new PipelineError("JWKS url not responding.", $"JWKS url {clientConfig?.JwksUri.ToString()} is not responding. Please check the client configuration.", DateTime.Now, _id).ToContentString();
                    context.StatusCode = HttpStatusCode.BadRequest;
                    _logger.LogError(ex, "JWT Assertion Invalid. {TokenContext}. {Content}.", castTokenContext.ToLogString(), context.ContentString);
                    return context;
                }
                catch (Microsoft.IdentityModel.Tokens.SecurityTokenValidationException ex)
                {
                    context.IsFatal = true;
                    context.ContentString = new PipelineError("JWT assestion invalid.", $"Error occured while processing you JWT assertion {ex.GetType().ToString().Split('.').Last()}", DateTime.Now, _id).ToContentString();
                    context.StatusCode = HttpStatusCode.BadRequest;
                    _logger.LogError(ex, "JWT Assertion Invalid. {TokenContext}. {Content}.", castTokenContext.ToLogString(), context.ContentString);
                    return context;
                }

                context.Request.Content = castTokenContext.ConvertToClientCredentialsFormUrlEncodedContent(clientConfig);
            }
            else
            {
                context.Request.Content = tokenContext.ToFormUrlEncodedContent();
            }

            // TODO - change to "NeedsCoors" or something similar.
            if (tokenContext.GetType() == typeof(PublicClientTokenContext))
            {
                context.Headers.Add(new HeaderNameValuePair("Origin", "http://localhost", CustomHeaderType.RequestStatic));
            }

            return context;
        }
    }
}
