﻿using System;
using System.Net;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Primitives;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.Net.Http.Headers;
using Xunit;

namespace Microsoft.AspNetCore.Identity.ClientApplications.FunctionalTest
{
    public class TraditionalWebApplicationTests
    {
        [Fact]
        public async Task CanPerform_AuthorizationCode_Flow()
        {
            // Arrange          
            var clientId = Guid.NewGuid().ToString();
            var resourceId = Guid.NewGuid().ToString();

            var appBuilder = new CredentialsServerBuilder()
                .ConfigureReferenceData(data => data
                    .CreateIntegratedWebClientApplication(clientId)
                    .CreateResourceApplication(resourceId, "ResourceApplication", "read")
                    .CreateUser("testUser", "Pa$$w0rd"))
                .ConfigureInMemoryEntityFrameworkStorage()
                .ConfigureMvcAutomaticSignIn()
                .ConfigureOpenIdConnectClient(options =>
                {
                    options.ClientId = clientId;
                    options.ResponseType = OpenIdConnectResponseType.Code;
                    options.ResponseMode = OpenIdConnectResponseMode.Query;
                    options.Scope.Add("https://localhost/DFC7191F-FF74-42B9-A292-08FEA80F5B20/v2.0/ResourceApplication/read");
                });

            var client = appBuilder.Build();

            // Act & Assert

            // Navigate to protected resource.
            var goToAuthorizeResponse = await client.GetAsync("https://localhost/Home/About");

            // Redirected to authorize
            var location = ResponseAssert.IsRedirect(goToAuthorizeResponse);
            var oidcCookiesComparisonCriteria = CookieComparison.Strict & ~CookieComparison.NameEquals | CookieComparison.NameStartsWith;
            ResponseAssert.HasCookie(CreateExpectedSetNonceCookie(), goToAuthorizeResponse, oidcCookiesComparisonCriteria);
            ResponseAssert.HasCookie(CreateExpectedSetCorrelationIdCookie(), goToAuthorizeResponse, oidcCookiesComparisonCriteria);
            var authorizeParameters = ResponseAssert.LocationHasQueryParameters<OpenIdConnectMessage>(
                goToAuthorizeResponse,
                "state");

            // Navigate to authorize
            var goToLoginResponse = await client.GetAsync(location);

            // Redirected to login
            location = ResponseAssert.IsRedirect(goToLoginResponse);

            // Navigate to login
            var goToAuthorizeWithCookie = await client.GetAsync(location);

            // Stamp a login cookie and redirect back to authorize.
            location = ResponseAssert.IsRedirect(goToAuthorizeWithCookie);
            ResponseAssert.HasCookie(".AspNetCore.Identity.Application", goToAuthorizeWithCookie, CookieComparison.NameEquals);

            // Navigate to authorize with a login cookie.
            var goToSignInOidcCallback = await client.GetAsync(location);

            // Stamp an application session cookie and redirect to relying party callback with an authorization code on the query string.
            location = ResponseAssert.IsRedirect(goToSignInOidcCallback);
            ResponseAssert.HasCookie("Microsoft.AspNetCore.Applications.Authentication.Cookie", goToSignInOidcCallback, CookieComparison.NameEquals);
            var callBackQueryParameters = ResponseAssert.LocationHasQueryParameters(goToSignInOidcCallback, "code", "state");
            var state = callBackQueryParameters["state"];
            Assert.Equal(authorizeParameters.State, state);
            var code = callBackQueryParameters["code"];

            // Navigate to relying party callback.
            var goToProtectedResource = await client.GetAsync(location);

            // Stamp a session cookie and redirect to the protected resource.
            location = ResponseAssert.IsRedirect(goToProtectedResource);
            ResponseAssert.HasCookie(".AspNetCore.Cookies", goToProtectedResource, CookieComparison.NameEquals);
            ResponseAssert.HasCookie(CreateExpectedSetCorrelationIdCookie(DateTime.Parse("1/1/1970 12:00:00 AM +00:00")), goToProtectedResource, CookieComparison.Delete);
            ResponseAssert.HasCookie(CreateExpectedSetNonceCookie(DateTime.Parse("1/1/1970 12:00:00 AM +00:00")), goToProtectedResource, CookieComparison.Delete);

            var protectedResourceResponse = await client.GetAsync(location);
            ResponseAssert.IsOK(protectedResourceResponse);
            ResponseAssert.IsHtmlDocument(protectedResourceResponse);
        }

        private SetCookieHeaderValue CreateExpectedSetCorrelationIdCookie(DateTime expires = default(DateTime))
        {
            return new SetCookieHeaderValue(new StringSegment(".AspNetCore.Correlation.OpenIdConnect."), new StringSegment("N"))
            {
                Expires = expires == default(DateTime) ? DateTime.UtcNow.AddMinutes(15) : expires,
                Path = "/",
                Secure = true,
                HttpOnly = true
            };
        }

        private static SetCookieHeaderValue CreateExpectedSetNonceCookie(DateTime expires = default(DateTime))
        {
            return new SetCookieHeaderValue(new StringSegment(".AspNetCore.OpenIdConnect.Nonce."), new StringSegment("N"))
            {
                Expires = expires == default(DateTime) ? DateTime.UtcNow.AddMinutes(15) : expires,
                Path = "/",
                Secure = true,
                HttpOnly = true
            };
        }

        [Fact]
        public async Task CanPerform_IdToken_Flow()
        {
            // Arrange          
            var clientId = Guid.NewGuid().ToString();
            var resourceId = Guid.NewGuid().ToString();

            var appBuilder = new CredentialsServerBuilder()
                .ConfigureReferenceData(data => data
                    .CreateIntegratedWebClientApplication(clientId)
                    .CreateUser("testUser", "Pa$$w0rd"))
                .ConfigureInMemoryEntityFrameworkStorage()
                .ConfigureMvcAutomaticSignIn()
                .ConfigureOpenIdConnectClient(options =>
                {
                    options.ClientId = clientId;
                });

            var client = appBuilder.Build();

            // Act
            var goToAuthorizeResponse = await client.GetAsync("https://localhost/Home/About");

            // Assert
            Assert.Equal(HttpStatusCode.Redirect, goToAuthorizeResponse.StatusCode);

            // Act
            var goToLoginResponse = await client.GetAsync(goToAuthorizeResponse.Headers.Location);

            // Assert
            Assert.Equal(HttpStatusCode.Redirect, goToLoginResponse.StatusCode);

            // Act
            var goToAuthorizeWithCookie = await client.GetAsync(goToLoginResponse.Headers.Location);

            // Assert
            Assert.Equal(HttpStatusCode.Redirect, goToAuthorizeWithCookie.StatusCode);

            // Act
            var goToSignInOidcCallback = await client.GetAsync(goToAuthorizeWithCookie.Headers.Location);

            // Assert
            Assert.Equal(HttpStatusCode.OK, goToSignInOidcCallback.StatusCode);
            var document = ResponseAssert.IsHtmlDocument(goToSignInOidcCallback);
            var form = HtmlAssert.HasForm(document, "form");

            // Act
            var goToProtectedResource = await client.SendAsync(form);

            // Assert
            Assert.Equal(HttpStatusCode.Redirect, goToProtectedResource.StatusCode);

            // Act
            var protectedResourceResponse = await client.GetAsync(goToProtectedResource.Headers.Location);

            // Assert
            Assert.Equal(HttpStatusCode.OK, protectedResourceResponse.StatusCode);
            ResponseAssert.IsHtmlDocument(protectedResourceResponse);
        }
    }
}
