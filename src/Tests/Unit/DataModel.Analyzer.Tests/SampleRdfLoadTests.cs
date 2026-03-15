using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using DataModel.Analyzer.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DataModel.Analyzer.Tests;

public class SampleRdfLoadTests
{
    [Fact]
    public async Task AnalyzeRdfFile_SampleTurtle_LoadsModel()
    {
        var samplePath = Path.Combine(AppContext.BaseDirectory, "data", "sample.ttl");
        File.Exists(samplePath).Should().BeTrue("sample.ttl must be copied to test output");

        var service = new RdfAnalyzerService(NullLogger<RdfAnalyzerService>.Instance);
        var model = await service.AnalyzeRdfFileAsync(samplePath);

        model.Sites.Should().NotBeEmpty();
        model.Buildings.Should().NotBeEmpty();
        model.Points.Should().NotBeEmpty();

        var point = model.Points.FirstOrDefault(p => p.PointId == "PT001" || p.SchemaId == "PT001" || p.Uri.EndsWith("PT001", StringComparison.Ordinal));
        point.Should().NotBeNull("PT001 should be parsed from sample.ttl");

        point!.CustomTags.Should().ContainKey("temperature");
        point.CustomTags["temperature"].Should().BeTrue();
        point.CustomTags.Should().ContainKey("room101");
        point.CustomTags["room101"].Should().BeTrue();

        point.CustomProperties.Should().ContainKey("point_list");
        var pointList = JsonSerializer.Deserialize<Dictionary<string, string>>(point.CustomProperties["point_list"]);
        pointList.Should().NotBeNull();
        pointList!["writable"].Should().Be("false");
        pointList["interval"].Should().Be("60");
        pointList["supplier"].Should().Be("VendorA");
    }
}
