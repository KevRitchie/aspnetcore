// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.IO;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using System.Xml.Linq;
using Microsoft.AspNetCore.Server.IIS.FunctionalTests.Utilities;
using Microsoft.AspNetCore.Server.IntegrationTesting;
using Microsoft.AspNetCore.Server.IntegrationTesting.IIS;
using Microsoft.AspNetCore.Testing.xunit;
using Xunit;

namespace Microsoft.AspNetCore.Server.IISIntegration.FunctionalTests
{
    [Collection(PublishedSitesCollection.Name)]
    public class GlobalVersionTests : IISFunctionalTestBase
    {
        private readonly PublishedSitesFixture _fixture;

        public GlobalVersionTests(PublishedSitesFixture fixture)
        {
            _fixture = fixture;
        }

        private const string _handlerVersion20 = "2.0.0";
        private const string _helloWorldRequest = "HelloWorld";
        private const string _helloWorldResponse = "Hello World";

        [ConditionalFact]
        public async Task GlobalVersion_DefaultWorks()
        {
            var deploymentParameters = GetGlobalVersionBaseDeploymentParameters();
            deploymentParameters.PublishApplicationBeforeDeployment = false;

            deploymentParameters.AddServerConfigAction(
                element =>
                {
                    var handlerVersionElement = new XElement("handlerSetting");
                    handlerVersionElement.SetAttributeValue("name", "handlerVersion");
                    handlerVersionElement.SetAttributeValue("value", _handlerVersion20);

                    element.Descendants("aspNetCore").Single()
                        .Add(new XElement("handlerSettings", handlerVersionElement));
                });

            var deploymentResult = await DeployAsync(deploymentParameters);

            var response = await deploymentResult.HttpClient.GetAsync(_helloWorldRequest);
            var responseText = await response.Content.ReadAsStringAsync();
            Assert.Equal(_helloWorldResponse, responseText);
        }

        [ConditionalTheory]
        [InlineData("2.1.0")]
        [InlineData("2.1.0-preview")]
        public async Task GlobalVersion_NewVersionNumber_Fails(string version)
        {
            var deploymentParameters = GetGlobalVersionBaseDeploymentParameters();
            deploymentParameters.HandlerSettings["handlerVersion"] = version;

            var deploymentResult = await DeployAsync(deploymentParameters);

            var response = await deploymentResult.HttpClient.GetAsync(_helloWorldRequest);
            Assert.False(response.IsSuccessStatusCode);
            var responseString = await response.Content.ReadAsStringAsync();
            Assert.Contains("HTTP Error 500.0 - ANCM OutOfProcess Startup Failure", responseString);
        }

        [ConditionalTheory]
        [InlineData("2.1.0")]
        [InlineData("2.1.0-preview")]
        public async Task GlobalVersion_NewVersionNumber(string version)
        {
            var deploymentParameters = GetGlobalVersionBaseDeploymentParameters();
            CopyShimToOutput(deploymentParameters);
            deploymentParameters.HandlerSettings["handlerVersion"] = version;

            var deploymentResult = await DeployAsync(deploymentParameters);

            var originalANCMPath = GetANCMRequestHandlerPath(deploymentResult, _handlerVersion20);
            var newANCMPath = GetANCMRequestHandlerPath(deploymentResult, version);
            Directory.Move(originalANCMPath, newANCMPath);

            var response = await deploymentResult.HttpClient.GetAsync(_helloWorldRequest);
            var responseText = await response.Content.ReadAsStringAsync();
            Assert.Equal(_helloWorldResponse, responseText);
            AssertLoadedVersion(version);
        }

        [ConditionalTheory]
        [InlineData("2.1.0")]
        [InlineData("2.1.0-preview")]
        public async Task GlobalVersion_MultipleRequestHandlers_PicksHighestOne(string version)
        {
            var deploymentParameters = GetGlobalVersionBaseDeploymentParameters();
            CopyShimToOutput(deploymentParameters);
            var deploymentResult = await DeployAsync(deploymentParameters);

            var originalANCMPath = GetANCMRequestHandlerPath(deploymentResult, _handlerVersion20);

            var newANCMPath = GetANCMRequestHandlerPath(deploymentResult, version);

            CopyDirectory(originalANCMPath, newANCMPath);

            deploymentResult.HttpClient.DefaultRequestHeaders.Add("ANCMRHPath", newANCMPath);
            var response = await deploymentResult.HttpClient.GetAsync("CheckRequestHandlerVersion");
            var responseText = await response.Content.ReadAsStringAsync();

            Assert.Equal(_helloWorldResponse, responseText);
            AssertLoadedVersion(version);
        }

        [ConditionalTheory]
        [InlineData("2.1.0")]
        [InlineData("2.1.0-preview")]
        public async Task GlobalVersion_MultipleRequestHandlers_UpgradeWorks(string version)
        {
            var deploymentParameters = GetGlobalVersionBaseDeploymentParameters();
            CopyShimToOutput(deploymentParameters);
            var deploymentResult = await DeployAsync(deploymentParameters);

            var originalANCMPath = GetANCMRequestHandlerPath(deploymentResult, _handlerVersion20);

            deploymentResult.HttpClient.DefaultRequestHeaders.Add("ANCMRHPath", originalANCMPath);
            var response = await deploymentResult.HttpClient.GetAsync("CheckRequestHandlerVersion");
            var responseText = await response.Content.ReadAsStringAsync();

            Assert.Equal(_helloWorldResponse, responseText);

            StopServer();

            deploymentResult = await DeployAsync(deploymentParameters);

            originalANCMPath = GetANCMRequestHandlerPath(deploymentResult, _handlerVersion20);

            var newANCMPath = GetANCMRequestHandlerPath(deploymentResult, version);

            CopyDirectory(originalANCMPath, newANCMPath);

            deploymentResult.HttpClient.DefaultRequestHeaders.Add("ANCMRHPath", newANCMPath);
            response = await deploymentResult.HttpClient.GetAsync("CheckRequestHandlerVersion");
            responseText = await response.Content.ReadAsStringAsync();

            Assert.Equal(_helloWorldResponse, responseText);
            AssertLoadedVersion(version);
        }

        [ConditionalFact]
        public async Task DoesNotCrashWhenNoVersionsAvailable()
        {
            var deploymentParameters = GetGlobalVersionBaseDeploymentParameters();
            CopyShimToOutput(deploymentParameters);
            var deploymentResult = await DeployAsync(deploymentParameters);

            var originalANCMPath = GetANCMRequestHandlerPath(deploymentResult, _handlerVersion20);
            Directory.Delete(originalANCMPath, true);
            var response = await deploymentResult.HttpClient.GetAsync("HelloWorld");

            Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        }

        private IISDeploymentParameters GetGlobalVersionBaseDeploymentParameters()
        {
            return _fixture.GetBaseDeploymentParameters(HostingModel.OutOfProcess, publish: true);
        }

        private void CopyDirectory(string from, string to)
        {
            var toInfo = new DirectoryInfo(to);
            toInfo.Create();

            foreach (var file in new DirectoryInfo(from).GetFiles())
            {
                file.CopyTo(Path.Combine(toInfo.FullName, file.Name));
            }
        }

        private string GetANCMRequestHandlerPath(IISDeploymentResult deploymentResult, string version)
        {
            return Path.Combine(deploymentResult.ContentRoot,
               deploymentResult.DeploymentParameters.RuntimeArchitecture.ToString(),
               version);
        }

        private void AssertLoadedVersion(string version)
        {
            StopServer();
            Assert.Contains(TestSink.Writes, context => context.Message.Contains(version + @"\aspnetcorev2_outofprocess.dll"));
        }

        private static void CopyShimToOutput(IISDeploymentParameters parameters)
        {
            parameters.AddServerConfigAction(
                (config, contentRoot) => {
                    var moduleNodes = config.DescendantNodesAndSelf()
                        .OfType<XElement>()
                        .Where(element =>
                            element.Name == "add" &&
                            element.Attribute("name")?.Value.StartsWith("AspNetCoreModule") == true &&
                            element.Attribute("image") != null);

                    var sourceDirectory = new DirectoryInfo(Path.GetDirectoryName(moduleNodes.First().Attribute("image").Value));
                    var destinationDirectory = new DirectoryInfo(Path.Combine(contentRoot, sourceDirectory.Name));
                    destinationDirectory.Create();
                    foreach (var element in moduleNodes)
                    {
                        var imageAttribute = element.Attribute("image");
                        imageAttribute.Value = imageAttribute.Value.Replace(sourceDirectory.FullName, destinationDirectory.FullName);
                    }
                    CopyFiles(sourceDirectory, destinationDirectory);
                });
        }

        private static void CopyFiles(DirectoryInfo source, DirectoryInfo target)
        {
            foreach (DirectoryInfo directoryInfo in source.GetDirectories())
            {
                CopyFiles(directoryInfo, target.CreateSubdirectory(directoryInfo.Name));
            }

            foreach (FileInfo fileInfo in source.GetFiles())
            {
                var destFileName = Path.Combine(target.FullName, fileInfo.Name);
                fileInfo.CopyTo(destFileName);
            }
        }

    }
}
