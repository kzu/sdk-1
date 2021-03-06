﻿using FluentAssertions;
using Microsoft.Extensions.DependencyModel;
using Microsoft.NET.TestFramework;
using Microsoft.NET.TestFramework.Assertions;
using Microsoft.NET.TestFramework.Commands;
using Microsoft.NET.TestFramework.ProjectConstruction;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Xml.Linq;
using Xunit;

namespace Microsoft.NET.Build.Tests
{
    public class GivenThatWeWantToBuildADesktopLibrary : SdkTest
    {
        [Fact]
        public void It_can_use_HttpClient_and_exchange_the_type_with_a_NETStandard_library()
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return;
            }

            var netStandardLibrary = new TestProject()
            {
                Name = "NETStandardLibrary",
                TargetFrameworks = "netstandard1.4",
                IsSdkProject = true
            };

            netStandardLibrary.SourceFiles["NETStandard.cs"] = @"
using System.Net.Http;
public class NETStandard
{
    public static HttpClient GetHttpClient()
    {
        return new HttpClient();
    }
}
";

            var netFrameworkLibrary = new TestProject()
            {
                Name = "NETFrameworkLibrary",
                TargetFrameworks = "net461",
                IsSdkProject = true
            };

            netFrameworkLibrary.ReferencedProjects.Add(netStandardLibrary);

            netFrameworkLibrary.SourceFiles["NETFramework.cs"] = @"
using System.Net.Http;
public class NETFramework
{
    public void Method1()
    {
        System.Net.Http.HttpClient client = NETStandard.GetHttpClient();
    }
}
";

            var testAsset = _testAssetsManager.CreateTestProject(netFrameworkLibrary, "ExchangeHttpClient")
                .WithProjectChanges((projectPath, project) =>
                {
                    if (Path.GetFileName(projectPath).Equals(netFrameworkLibrary.Name + ".csproj", StringComparison.OrdinalIgnoreCase))
                    {
                        var ns = project.Root.Name.Namespace;

                        var itemGroup = new XElement(ns + "ItemGroup");
                        project.Root.Add(itemGroup);

                        itemGroup.Add(new XElement(ns + "Reference", new XAttribute("Include", "System.Net.Http")));
                    }
                })
                .Restore(netFrameworkLibrary.Name);

            var buildCommand = new BuildCommand(MSBuildTest.Stage0MSBuild, Path.Combine(testAsset.TestRoot, netFrameworkLibrary.Name));

            buildCommand
                .Execute()
                .Should()
                .Pass();

        }

        [Fact]
        public void It_can_preserve_compilation_context_and_reference_netstandard_library()
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return;
            }

            var testAsset = _testAssetsManager
                .CopyTestAsset("DesktopReferencingNetStandardLibrary")
                .WithSource()
                .Restore();

            var buildCommand = new BuildCommand(MSBuildTest.Stage0MSBuild, testAsset.TestRoot);
            buildCommand
                .Execute()
                .Should().Pass();

            using (var depsJsonFileStream = File.OpenRead(Path.Combine(buildCommand.GetOutputDirectory("net46").FullName, "Library.deps.json")))
            {
                var dependencyContext = new DependencyContextJsonReader().Read(depsJsonFileStream);
                dependencyContext.CompileLibraries.Should().NotBeEmpty();
            }
        }

        [Fact]
        public void It_resolves_assembly_conflicts_with_a_NETFramework_library()
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return;
            }

            TestProject project = new TestProject()
            {
                Name = "NETFrameworkLibrary",
                TargetFrameworks = "net462",
                IsSdkProject = true
            };

            var testAsset = _testAssetsManager.CreateTestProject(project)
                .WithProjectChanges(p =>
                {
                    var ns = p.Root.Name.Namespace;

                    var itemGroup = new XElement(ns + "ItemGroup");
                    p.Root.Add(itemGroup);

                    itemGroup.Add(new XElement(ns + "PackageReference",
                        new XAttribute("Include", "NETStandard.Library"),
                        new XAttribute("Version", "$(BundledNETStandardPackageVersion)")));

                    foreach (var dependency in TestAsset.NetStandard1_3Dependencies)
                    {
                        itemGroup.Add(new XElement(ns + "PackageReference",
                            new XAttribute("Include", dependency.Item1),
                            new XAttribute("Version", dependency.Item2)));
                    }

                })
                .Restore(project.Name);

            string projectFolder = Path.Combine(testAsset.Path, project.Name);

            var buildCommand = new BuildCommand(MSBuildTest.Stage0MSBuild, projectFolder);

            buildCommand
                .CaptureStdOut()
                .Execute()
                .Should()
                .Pass()
                .And
                .NotHaveStdOutContaining("warning")
                .And
                .NotHaveStdOutContaining("MSB3243");
        }
    }
}
